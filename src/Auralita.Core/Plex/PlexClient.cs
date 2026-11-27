using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Auralita.Core.Domain;

namespace Auralita.Core.Plex;

public sealed class PlexClient : IDisposable
{
    private const int MaxArtworkBytes = 16 * 1024 * 1024;
    private const int TrackPageSize = 200;
    private static readonly Uri PlexResourcesUri =
        new("https://plex.tv/api/resources?includeHttps=1&includeRelay=1");
    private static readonly XName MediaContainerName = "MediaContainer";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _token;
    private Uri? _activeServer;
    private bool _disposed;

    public PlexClient(PlexServerSettings settings, string token, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ValidateServerUrl(settings.ServerUrl);

        Settings = settings;
        _token = token.Trim();
        _httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _ownsHttpClient = httpClient is null;
    }

    public PlexServerSettings Settings { get; }

    public async Task<IReadOnlyList<PlexLibrarySection>> GetMusicLibrariesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var server = await ResolveServerAsync(cancellationToken).ConfigureAwait(false);
        var xml = await GetXmlAsync(new Uri(server, "library/sections"), cancellationToken)
            .ConfigureAwait(false);
        var container = xml.Root;
        if (container?.Name.LocalName != MediaContainerName.LocalName)
        {
            throw new PlexConnectionException("Plex returned an invalid library response.");
        }

        return container.Elements()
            .Where(element =>
                element.Name.LocalName == "Directory" &&
                (string.Equals(
                     (string?)element.Attribute("type"),
                     "music",
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     (string?)element.Attribute("type"),
                     "artist",
                     StringComparison.OrdinalIgnoreCase)))
            .Select(element => new PlexLibrarySection(
                (string?)element.Attribute("key") ?? string.Empty,
                (string?)element.Attribute("title") ?? "Untitled music library"))
            .Where(section => section.Key.Length > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<PlexArtist>> GetArtistsAsync(
        string sectionKey,
        CancellationToken cancellationToken = default)
    {
        var (server, items) = await GetDirectoryItemsAsync(
            sectionKey,
            "artist",
            8,
            cancellationToken).ConfigureAwait(false);
        return items
            .Select(item => new PlexArtist(
                (string?)item.Attribute("key") ?? string.Empty,
                (string?)item.Attribute("title") ?? "Unknown artist",
                BuildArtworkUri(
                    server,
                    (string?)item.Attribute("thumb"))))
            .Where(artist => artist.Key.Length > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<PlexAlbum>> GetAlbumsAsync(
        string sectionKey,
        CancellationToken cancellationToken = default)
    {
        var (server, items) = await GetDirectoryItemsAsync(
            sectionKey,
            "album",
            9,
            cancellationToken).ConfigureAwait(false);
        return items
            .Select(item => new PlexAlbum(
                (string?)item.Attribute("key") ?? string.Empty,
                (string?)item.Attribute("title") ?? "Unknown album",
                (string?)item.Attribute("parentTitle") ?? "Unknown artist",
                BuildArtworkUri(
                    server,
                    (string?)item.Attribute("thumb")
                    ?? (string?)item.Attribute("parentThumb"))))
            .Where(album => album.Key.Length > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<Track>> GetTracksAsync(
        string sectionKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

        var server = await ResolveServerAsync(cancellationToken).ConfigureAwait(false);
        var normalizedKey = sectionKey.Trim('/');
        if (normalizedKey.StartsWith("library/sections/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedKey = normalizedKey["library/sections/".Length..].TrimEnd('/');
        }

        var requestUri = new Uri(
            server,
            $"library/sections/{Uri.EscapeDataString(normalizedKey)}/all?type=10");
        var tracks = new List<Track>();
        var start = 0;
        while (true)
        {
            var xml = await GetXmlAsync(requestUri, cancellationToken, start, TrackPageSize)
                .ConfigureAwait(false);
            var root = xml.Root;
            if (root?.Name.LocalName != MediaContainerName.LocalName)
            {
                throw new PlexConnectionException("Plex returned an invalid music library response.");
            }

            var pageItems = root.Elements()
                .Where(element => element.Name.LocalName == "Track")
                .ToArray();
            var page = pageItems
                .Select(element => MapTrack(element, server))
                .Where(track => track is not null)
                .Cast<Track>()
                .ToArray();
            tracks.AddRange(page);

            var totalSize = int.TryParse(
                (string?)root.Attribute("totalSize"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedTotal)
                ? parsedTotal
                : (int?)null;
            start += pageItems.Length;
            if (pageItems.Length < TrackPageSize ||
                totalSize is null ||
                start >= totalSize.Value)
            {
                return tracks;
            }
        }
    }

    public async Task<byte[]> GetArtworkAsync(
        Uri artworkUri,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(artworkUri);
        if (artworkUri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(artworkUri.UserInfo))
        {
            throw new ArgumentException("Plex artwork must use HTTP or HTTPS.", nameof(artworkUri));
        }

        var server = await ResolveServerAsync(cancellationToken).ConfigureAwait(false);
        if (!SameAuthority(server, artworkUri))
        {
            throw new ArgumentException("Plex artwork must come from the configured Plex server.", nameof(artworkUri));
        }

        using var request = CreateRequest(artworkUri);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is > MaxArtworkBytes)
        {
            throw new PlexConnectionException("Plex artwork is larger than the allowed 16 MB limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (destination.Length + read > MaxArtworkBytes)
            {
                throw new PlexConnectionException("Plex artwork is larger than the allowed 16 MB limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return destination.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        _disposed = true;
    }

    private async Task<Uri> ResolveServerAsync(CancellationToken cancellationToken)
    {
        if (_activeServer is not null)
        {
            return _activeServer;
        }

        if (!IsPlexWebApp(Settings.ServerUrl))
        {
            _activeServer = EnsureTrailingSlash(Settings.ServerUrl);
            return _activeServer;
        }

        var resources = await GetXmlAsync(PlexResourcesUri, cancellationToken).ConfigureAwait(false);
        var candidates = resources.Descendants()
            .Where(element =>
                element.Name.LocalName == "Device" &&
                ((string?)element.Attribute("provides"))?.Contains("server", StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(device => device.Elements()
                .Where(connection => connection.Name.LocalName == "Connection")
                .Select(connection => new
                {
                    Uri = (string?)connection.Attribute("uri"),
                    Local = string.Equals((string?)connection.Attribute("local"), "1", StringComparison.Ordinal),
                    Relay = string.Equals((string?)connection.Attribute("relay"), "1", StringComparison.Ordinal)
                }))
            .Where(connection => Uri.TryCreate(connection.Uri, UriKind.Absolute, out var uri) &&
                                 (uri.Scheme is "http" or "https") &&
                                 string.IsNullOrEmpty(uri.UserInfo))
            .OrderByDescending(connection => connection.Local)
            .ThenBy(connection => connection.Relay)
            .Select(connection => EnsureTrailingSlash(new Uri(connection.Uri!, UriKind.Absolute)))
            .Distinct()
            .ToArray();

        foreach (var candidate in candidates)
        {
            try
            {
                var sections = await GetXmlAsync(
                    new Uri(candidate, "library/sections"),
                    cancellationToken).ConfigureAwait(false);
                if (sections.Root?.Name.LocalName != MediaContainerName.LocalName)
                {
                    continue;
                }

                _activeServer = candidate;
                return candidate;
            }
            catch (PlexConnectionException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        throw new PlexConnectionException(
            candidates.Length == 0
                ? "No accessible Plex Media Server was found for this account."
                : "Plex servers were discovered, but none could be reached.");
    }

    private async Task<(Uri Server, IReadOnlyList<XElement> Items)> GetDirectoryItemsAsync(
        string sectionKey,
        string mediaType,
        int typeId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

        var server = await ResolveServerAsync(cancellationToken).ConfigureAwait(false);
        var normalizedKey = sectionKey.Trim('/');
        if (normalizedKey.StartsWith("library/sections/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedKey = normalizedKey["library/sections/".Length..].TrimEnd('/');
        }

        var requestUri = new Uri(
            server,
            $"library/sections/{Uri.EscapeDataString(normalizedKey)}/all?type={typeId}");
        var items = new List<XElement>();
        var start = 0;
        while (true)
        {
            var xml = await GetXmlAsync(requestUri, cancellationToken, start, TrackPageSize)
                .ConfigureAwait(false);
            var root = xml.Root;
            if (root?.Name.LocalName != MediaContainerName.LocalName)
            {
                throw new PlexConnectionException($"Plex returned an invalid {mediaType} library response.");
            }

            var page = root.Elements()
                .Where(element =>
                    element.Name.LocalName == "Directory" &&
                    string.Equals(
                        (string?)element.Attribute("type"),
                        mediaType,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            items.AddRange(page);
            start += page.Length;

            var totalSize = int.TryParse(
                (string?)root.Attribute("totalSize"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedTotal)
                ? parsedTotal
                : (int?)null;
            if (page.Length < TrackPageSize || totalSize is null || start >= totalSize.Value)
            {
                return (server, items);
            }
        }
    }

    private async Task<XDocument> GetXmlAsync(
        Uri uri,
        CancellationToken cancellationToken,
        int? containerStart = null,
        int? containerSize = null)
    {
        using var request = CreateRequest(uri);
        if (containerStart is not null && containerSize is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Plex-Container-Start", containerStart.Value.ToString(CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("X-Plex-Container-Size", containerSize.Value.ToString(CultureInfo.InvariantCulture));
        }

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new PlexConnectionException("Plex returned malformed XML.", exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = response.StatusCode;
            response.Dispose();
            var message = statusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    "Plex rejected the authentication token. Check the token and try again.",
                HttpStatusCode.NotFound =>
                    "The Plex server did not find the requested music library.",
                _ => $"Plex returned HTTP {(int)statusCode} ({statusCode})."
            };
            throw new PlexConnectionException(message);
        }
        catch (HttpRequestException exception)
        {
            throw new PlexConnectionException("Could not reach Plex. Check the server URL and network.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlexConnectionException("The Plex request timed out.", exception);
        }
    }

    private HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("X-Plex-Token", _token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Auralita");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", "1.0");
        return request;
    }

    private Track? MapTrack(XElement element, Uri server)
    {
        var title = (string?)element.Attribute("title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artist = FirstNonEmpty(
            (string?)element.Attribute("grandparentTitle"),
            (string?)element.Attribute("artistTitle")) ?? "Unknown artist";
        var album = FirstNonEmpty((string?)element.Attribute("parentTitle")) ?? "Unknown album";
        var duration = ParseDuration((string?)element.Attribute("duration"));
        var artworkKey = FirstNonEmpty(
            (string?)element.Attribute("thumb"),
            (string?)element.Attribute("parentThumb"));
        var artworkUri = BuildArtworkUri(server, artworkKey);
        var partKey = element.Descendants()
            .FirstOrDefault(part => part.Name.LocalName == "Part")
            ?.Attribute("key")?.Value;
        var streamKey = FirstNonEmpty(partKey, (string?)element.Attribute("key"));
        var streamUri = BuildAuthenticatedUri(server, streamKey);

        return streamUri is null
            ? null
            : new Track(title, artist, streamUri, album, duration, artworkUri);
    }

    private Uri? BuildAuthenticatedUri(Uri server, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var uri = ResolveResourceUri(server, key);
        if (uri is null)
        {
            return null;
        }

        var builder = new UriBuilder(uri);
        var existingParameters = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter =>
            {
                var separator = parameter.IndexOf('=');
                var name = separator >= 0 ? parameter[..separator] : parameter;
                return !string.Equals(name, "X-Plex-Token", StringComparison.OrdinalIgnoreCase);
            });
        var tokenQuery = $"X-Plex-Token={Uri.EscapeDataString(_token)}";
        builder.Query = string.Join("&", existingParameters.Append(tokenQuery));
        return builder.Uri;
    }

    private Uri? BuildArtworkUri(Uri server, string? key)
    {
        var imageUri = ResolveResourceUri(server, key);
        if (imageUri is null)
        {
            return null;
        }

        var transcodeUri = new Uri(
            server,
            $"photo/:/transcode?url={Uri.EscapeDataString(imageUri.PathAndQuery)}&width=160&height=160&minSize=1&upscale=1");
        return BuildAuthenticatedUri(server, transcodeUri.AbsoluteUri);
    }

    private static Uri? ResolveResourceUri(Uri server, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        Uri? uri;
        if (Uri.TryCreate(key, UriKind.RelativeOrAbsolute, out var candidate) &&
            candidate.IsAbsoluteUri)
        {
            uri = candidate;
        }
        else if (!Uri.TryCreate(server, key.TrimStart('/'), out uri))
        {
            return null;
        }

        if (uri is null)
        {
            return null;
        }

        if (uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !SameAuthority(server, uri))
        {
            return null;
        }

        return uri;
    }

    private static TimeSpan? ParseDuration(string? milliseconds) =>
        long.TryParse(milliseconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
        value >= 0 &&
        value <= TimeSpan.MaxValue.TotalMilliseconds
            ? TimeSpan.FromMilliseconds(value)
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void ValidateServerUrl(Uri serverUrl)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        if (!serverUrl.IsAbsoluteUri ||
            serverUrl.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(serverUrl.UserInfo))
        {
            throw new ArgumentException("Plex server URL must be an absolute HTTP or HTTPS URL.", nameof(serverUrl));
        }
    }

    private static bool IsPlexWebApp(Uri uri) =>
        string.Equals(uri.Host, "app.plex.tv", StringComparison.OrdinalIgnoreCase);

    private static bool SameAuthority(Uri first, Uri second) =>
        string.Equals(first.Authority, second.Authority, StringComparison.OrdinalIgnoreCase);

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri($"{uri.AbsoluteUri}/");

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
