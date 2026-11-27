using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Auralita.Core.Domain;

namespace Auralita.Core.Plex;

public sealed class PlexClient : IDisposable
{
    private const int MaxArtworkBytes = 16 * 1024 * 1024;
    private const int TrackPageSize = 200;
    private static readonly Uri PlexResourcesUri =
        new("https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1");
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _accountToken;
    private readonly string _clientIdentifier;
    private Uri? _activeServer;
    private string? _activeServerToken;
    private IReadOnlyList<PlexLibrarySection>? _activeMusicLibraries;
    private bool _disposed;

    public PlexClient(
        PlexServerSettings settings,
        string token,
        string clientIdentifier,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientIdentifier);
        ValidateServerUrl(settings.ServerUrl);
        if (!Guid.TryParseExact(clientIdentifier, "D", out var parsedIdentifier))
        {
            throw new ArgumentException("Plex client identifier must be a UUID.", nameof(clientIdentifier));
        }

        Settings = settings;
        _accountToken = token.Trim();
        _clientIdentifier = parsedIdentifier.ToString("D");
        _httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _ownsHttpClient = httpClient is null;
    }

    public PlexServerSettings Settings { get; }

    public Uri? ResolvedServerUri => _activeServer;

    public async Task<IReadOnlyList<PlexLibrarySection>> GetMusicLibrariesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var server = await ResolveServerAsync(cancellationToken).ConfigureAwait(false);
        if (_activeMusicLibraries is not null)
        {
            return _activeMusicLibraries;
        }

        _activeMusicLibraries = await ReadMusicLibrariesAsync(
                server,
                _activeServerToken ?? _accountToken,
                cancellationToken)
            .ConfigureAwait(false);
        return _activeMusicLibraries;
    }

    private async Task<IReadOnlyList<PlexLibrarySection>> ReadMusicLibrariesAsync(
        Uri server,
        string serverToken,
        CancellationToken cancellationToken)
    {
        var container = await GetApiResponseAsync(
            new Uri(server, "library/sections"),
            serverToken,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(container.Name, "MediaContainer", StringComparison.OrdinalIgnoreCase))
        {
            throw new PlexConnectionException("Plex returned an invalid library response.");
        }

        return container.Children
            .Where(element =>
                string.Equals(element.Name, "Directory", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(
                     element.Attribute("type"),
                     "music",
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     element.Attribute("type"),
                     "artist",
                     StringComparison.OrdinalIgnoreCase)))
            .Select(element => new PlexLibrarySection(
                element.Attribute("key") ?? string.Empty,
                element.Attribute("title") ?? "Untitled music library"))
            .Where(section => section.Key.Length > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<PlexArtist>> GetArtistsAsync(
        string sectionKey,
        CancellationToken cancellationToken = default)
    {
        var (server, serverToken, items) = await GetDirectoryItemsAsync(
            sectionKey,
            "artist",
            8,
            cancellationToken).ConfigureAwait(false);
        return items
            .Select(item => new PlexArtist(
                item.Attribute("key") ?? string.Empty,
                item.Attribute("title") ?? "Unknown artist",
                BuildArtworkUri(
                    server,
                    serverToken,
                    item.Attribute("thumb"))))
            .Where(artist => artist.Key.Length > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<PlexAlbum>> GetAlbumsAsync(
        string sectionKey,
        CancellationToken cancellationToken = default)
    {
        var (server, serverToken, items) = await GetDirectoryItemsAsync(
            sectionKey,
            "album",
            9,
            cancellationToken).ConfigureAwait(false);
        return items
            .Select(item => new PlexAlbum(
                item.Attribute("key") ?? string.Empty,
                item.Attribute("title") ?? "Unknown album",
                item.Attribute("parentTitle") ?? "Unknown artist",
                BuildArtworkUri(
                    server,
                    serverToken,
                    item.Attribute("thumb") ?? item.Attribute("parentThumb"))))
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
        var serverToken = _activeServerToken ?? _accountToken;
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
            var root = await GetApiResponseAsync(
                requestUri,
                serverToken,
                cancellationToken,
                start,
                TrackPageSize)
                .ConfigureAwait(false);
            if (!string.Equals(root.Name, "MediaContainer", StringComparison.OrdinalIgnoreCase))
            {
                throw new PlexConnectionException("Plex returned an invalid music library response.");
            }

            var pageItems = root.Children
                .Where(IsTrackElement)
                .ToArray();
            var page = pageItems
                .Select(element => MapTrack(element, server, serverToken))
                .Where(track => track is not null)
                .Cast<Track>()
                .ToArray();
            tracks.AddRange(page);

            var totalSize = int.TryParse(
                root.Attribute("totalSize"),
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

        if (!IsPlexWebApp(Settings.ServerUrl) && !SameAuthority(Settings.ServerUrl, artworkUri))
        {
            throw new ArgumentException("Plex artwork must come from the configured Plex server.", nameof(artworkUri));
        }

        var server = await ResolveServerAsync(cancellationToken).ConfigureAwait(false);
        if (!SameAuthority(server, artworkUri))
        {
            throw new ArgumentException("Plex artwork must come from the configured Plex server.", nameof(artworkUri));
        }

        using var request = CreateRequest(artworkUri, _activeServerToken ?? _accountToken);
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

        // Account credentials discover servers; each server's access token authenticates its own library and media requests.
        PlexElement? resources;
        try
        {
            resources = await GetApiResponseAsync(
                PlexResourcesUri,
                _accountToken,
                cancellationToken).ConfigureAwait(false);
        }
        catch (PlexAuthenticationException)
        {
            throw;
        }
        catch (PlexConnectionException)
        {
            if (IsPlexWebApp(Settings.ServerUrl))
            {
                throw;
            }

            resources = null;
        }

        if (resources is null)
        {
            var directServer = EnsureTrailingSlash(Settings.ServerUrl);
            _activeServer = directServer;
            _activeServerToken = _accountToken;
            _activeMusicLibraries = await ReadMusicLibrariesAsync(
                directServer,
                _accountToken,
                cancellationToken).ConfigureAwait(false);
            return directServer;
        }

        var candidates = resources.DescendantsAndSelf()
            .Where(element =>
                string.Equals(element.Name, "Device", StringComparison.OrdinalIgnoreCase) &&
                element.Attribute("provides")?
                    .Split(',', StringSplitOptions.TrimEntries)
                    .Contains("server", StringComparer.OrdinalIgnoreCase) == true)
            .SelectMany(device => device.Children
                .Where(connection =>
                    string.Equals(connection.Name, "Connection", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(connection.Name, "connections", StringComparison.OrdinalIgnoreCase))
                .Select(connection => TryCreateServerCandidate(
                    connection,
                    device.Attribute("accessToken"))))
            .Where(candidate => candidate is not null)
            .Cast<PlexServerCandidate>()
            .OrderBy(candidate => candidate.Relay)
            .ThenByDescending(candidate => candidate.Local)
            .ThenByDescending(candidate =>
                string.Equals(candidate.Uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(candidate => candidate.Uri)
            .ToArray();
        if (!IsPlexWebApp(Settings.ServerUrl))
        {
            var configuredServer = EnsureTrailingSlash(Settings.ServerUrl);
            var configuredCandidates = candidates
                .Where(candidate => SameAuthority(configuredServer, candidate.Uri))
                .ToArray();
            if (candidates.Length == 0)
            {
                candidates =
                [
                    new PlexServerCandidate(configuredServer, _accountToken, false, false)
                ];
            }
            else if (configuredCandidates.Length == 0)
            {
                candidates =
                [
                    new PlexServerCandidate(configuredServer, _accountToken, false, false),
                    .. candidates
                ];
            }
            else
            {
                candidates = candidates
                    .OrderByDescending(candidate => SameAuthority(configuredServer, candidate.Uri))
                    .ToArray();
            }
        }

        PlexServerCandidate? firstAccessibleServer = null;
        IReadOnlyList<PlexLibrarySection>? firstAccessibleLibraries = null;
        PlexConnectionException? lastConnectionError = null;
        PlexAuthenticationException? lastAuthenticationError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeCancellation.CancelAfter(TimeSpan.FromSeconds(8));
                var libraries = await ReadMusicLibrariesAsync(
                        candidate.Uri,
                        candidate.AccessToken,
                        probeCancellation.Token)
                    .ConfigureAwait(false);
                firstAccessibleServer ??= candidate;
                firstAccessibleLibraries ??= libraries;

                if (string.IsNullOrWhiteSpace(Settings.LibrarySectionKey))
                {
                    if (libraries.Count == 0)
                    {
                        continue;
                    }
                }
                else if (!libraries.Any(library =>
                             string.Equals(
                                 library.Key,
                                 Settings.LibrarySectionKey,
                                 StringComparison.Ordinal)))
                {
                    continue;
                }

                _activeServer = candidate.Uri;
                _activeServerToken = candidate.AccessToken;
                _activeMusicLibraries = libraries;
                return candidate.Uri;
            }
            catch (PlexAuthenticationException exception)
            {
                lastAuthenticationError = exception;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastConnectionError = new PlexConnectionException(
                    $"Plex server {candidate.Uri} did not answer quickly enough.");
            }
            catch (PlexConnectionException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastConnectionError = exception;
            }
        }

        if (firstAccessibleServer is not null)
        {
            if (lastAuthenticationError is not null)
            {
                throw lastAuthenticationError;
            }

            if (!string.IsNullOrWhiteSpace(Settings.LibrarySectionKey))
            {
                throw new PlexConnectionException(
                    "The saved Plex music library was not found on an accessible server. Reconnect and select a library again.");
            }

            _activeServer = firstAccessibleServer.Uri;
            _activeServerToken = firstAccessibleServer.AccessToken;
            _activeMusicLibraries = firstAccessibleLibraries ?? [];
            return firstAccessibleServer.Uri;
        }

        if (lastAuthenticationError is not null)
        {
            throw lastAuthenticationError;
        }

        if (lastConnectionError is not null)
        {
            throw new PlexConnectionException(
                lastConnectionError.Message,
                lastConnectionError);
        }

        throw new PlexConnectionException(
            candidates.Length == 0
                ? "No accessible Plex Media Server was found for this account."
                : "Plex servers were discovered, but none could be reached.");
    }

    private async Task<(Uri Server, string ServerToken, IReadOnlyList<PlexElement> Items)> GetDirectoryItemsAsync(
        string sectionKey,
        string mediaType,
        int typeId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

        var server = await ResolveServerAsync(cancellationToken).ConfigureAwait(false);
        var serverToken = _activeServerToken ?? _accountToken;
        var normalizedKey = sectionKey.Trim('/');
        if (normalizedKey.StartsWith("library/sections/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedKey = normalizedKey["library/sections/".Length..].TrimEnd('/');
        }

        var requestUri = new Uri(
            server,
            $"library/sections/{Uri.EscapeDataString(normalizedKey)}/all?type={typeId}");
        var items = new List<PlexElement>();
        var start = 0;
        while (true)
        {
            var root = await GetApiResponseAsync(
                    requestUri,
                    serverToken,
                    cancellationToken,
                    start,
                    TrackPageSize)
                .ConfigureAwait(false);
            if (!string.Equals(root.Name, "MediaContainer", StringComparison.OrdinalIgnoreCase))
            {
                throw new PlexConnectionException($"Plex returned an invalid {mediaType} library response.");
            }

            var page = root.Children
                .Where(element =>
                    (string.Equals(element.Name, "Directory", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(element.Name, "Metadata", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(
                        element.Attribute("type"),
                        mediaType,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            items.AddRange(page);
            start += page.Length;

            var totalSize = int.TryParse(
                root.Attribute("totalSize"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedTotal)
                ? parsedTotal
                : (int?)null;
            if (page.Length < TrackPageSize || totalSize is null || start >= totalSize.Value)
            {
                return (server, serverToken, items);
            }
        }
    }

    private async Task<PlexElement> GetApiResponseAsync(
        Uri uri,
        string token,
        CancellationToken cancellationToken,
        int? containerStart = null,
        int? containerSize = null)
    {
        using var request = CreateRequest(uri, token);
        if (containerStart is not null && containerSize is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Plex-Container-Start", containerStart.Value.ToString(CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("X-Plex-Container-Size", containerSize.Value.ToString(CultureInfo.InvariantCulture));
        }

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            return PlexResponseParser.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new PlexConnectionException("Plex returned a malformed API response.", exception);
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
                HttpStatusCode.Unauthorized =>
                    "Plex rejected the authentication token. Sign in again to continue.",
                HttpStatusCode.Forbidden =>
                    "Plex denied access to the requested resource.",
                HttpStatusCode.NotFound =>
                    "The Plex server did not find the requested music library.",
                _ => $"Plex returned HTTP {(int)statusCode} ({statusCode})."
            };
            if (statusCode == HttpStatusCode.Unauthorized)
            {
                throw new PlexAuthenticationException(message);
            }

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

    private HttpRequestMessage CreateRequest(Uri uri, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("X-Plex-Token", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Auralita");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", "1.0.0");
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", _clientIdentifier);
        return request;
    }

    private Track? MapTrack(PlexElement element, Uri server, string serverToken)
    {
        var title = element.Attribute("title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artist = FirstNonEmpty(
            element.Attribute("grandparentTitle"),
            element.Attribute("artistTitle")) ?? "Unknown artist";
        var album = FirstNonEmpty(element.Attribute("parentTitle")) ?? "Unknown album";
        var duration = ParseDuration(element.Attribute("duration"));
        var artworkKey = FirstNonEmpty(
            element.Attribute("thumb"),
            element.Attribute("parentThumb"));
        var artworkUri = BuildArtworkUri(server, serverToken, artworkKey);
        var partKey = element.DescendantsAndSelf()
            .FirstOrDefault(part => string.Equals(part.Name, "Part", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("key");
        var streamKey = FirstNonEmpty(partKey, element.Attribute("key"));
        var streamUri = BuildAuthenticatedUri(server, serverToken, streamKey);
        var ratingKey = FirstNonEmpty(element.Attribute("ratingKey"), element.Attribute("key"));

        return streamUri is null
            ? null
            : new Track(title, artist, streamUri, album, duration, artworkUri, ratingKey);
    }

    public async Task ReportTimelineAsync(
        string ratingKey,
        string state,
        TimeSpan position,
        TimeSpan? duration,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(ratingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        if (position < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var server = await ResolveServerAsync(cancellationToken).ConfigureAwait(false);
        var token = _activeServerToken ?? _accountToken;
        var query = new List<string>
        {
            $"key={Uri.EscapeDataString(ratingKey)}",
            $"ratingKey={Uri.EscapeDataString(ratingKey)}",
            $"state={Uri.EscapeDataString(state)}",
            $"time={Math.Max(0, (long)position.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)}",
            $"duration={Math.Max(0, (long)(duration ?? TimeSpan.Zero).TotalMilliseconds).ToString(CultureInfo.InvariantCulture)}"
        };
        var timelineUri = new Uri(
            $"{server.AbsoluteUri.TrimEnd('/')}/:/timeline?{string.Join("&", query)}");
        using var request = CreateRequest(timelineUri, token);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private Uri? BuildAuthenticatedUri(Uri server, string serverToken, string? key)
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
        var tokenQuery = $"X-Plex-Token={Uri.EscapeDataString(serverToken)}";
        builder.Query = string.Join("&", existingParameters.Append(tokenQuery));
        return builder.Uri;
    }

    private Uri? BuildArtworkUri(Uri server, string serverToken, string? key)
    {
        var imageUri = ResolveResourceUri(server, key);
        if (imageUri is null)
        {
            return null;
        }

        var transcodeUri = new Uri(
            server,
            $"photo/:/transcode?url={Uri.EscapeDataString(imageUri.PathAndQuery)}&width=160&height=160&minSize=1&upscale=1");
        return BuildAuthenticatedUri(server, serverToken, transcodeUri.AbsoluteUri);
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

    private static bool IsTrackElement(PlexElement element) =>
        string.Equals(element.Name, "Track", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(element.Name, "Metadata", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(element.Attribute("type"), "track", StringComparison.OrdinalIgnoreCase));

    private static PlexServerCandidate? TryCreateServerCandidate(
        PlexElement connection,
        string? accessToken)
    {
        var uriValue = connection.Attribute("uri");
        if (string.IsNullOrWhiteSpace(accessToken) ||
            !Uri.TryCreate(uriValue, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return new PlexServerCandidate(
            EnsureTrailingSlash(uri),
            accessToken,
            IsTruthy(connection.Attribute("local")),
            IsTruthy(connection.Attribute("relay")));
    }

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

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

    private sealed record PlexServerCandidate(
        Uri Uri,
        string AccessToken,
        bool Local,
        bool Relay);
}
