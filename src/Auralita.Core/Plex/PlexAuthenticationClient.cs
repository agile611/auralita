using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Auralita.Core.Plex;

public sealed class PlexAuthenticationClient : IDisposable
{
    private static readonly Uri PinsUri = new("https://plex.tv/api/v2/pins");
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public PlexAuthenticationClient(string clientIdentifier, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientIdentifier);
        if (!Guid.TryParseExact(clientIdentifier, "D", out var parsedIdentifier))
        {
            throw new ArgumentException("Plex client identifier must be a UUID.", nameof(clientIdentifier));
        }

        ClientIdentifier = parsedIdentifier.ToString("D");
        _httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _ownsHttpClient = httpClient is null;
    }

    public string ClientIdentifier { get; }

    public async Task<PlexPin> RequestPinAsync(CancellationToken cancellationToken = default)
    {
        // The PIN is the user's one-time approval code; the account token is only available after polling.
        using var request = CreateRequest(
            HttpMethod.Post,
            new Uri($"{PinsUri.AbsoluteUri}?strong=true"));
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (!TryReadInt64(root, "id", out var id) ||
            !root.TryGetProperty("code", out var codeElement) ||
            codeElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(codeElement.GetString()))
        {
            throw new PlexConnectionException("Plex returned an invalid sign-in PIN.");
        }

        return new PlexPin(
            id,
            codeElement.GetString()!,
            TryReadDateTimeOffset(root, "expiresAt"));
    }

    public Uri CreateAuthorizationUri(PlexPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var clientId = Uri.EscapeDataString(ClientIdentifier);
        var code = Uri.EscapeDataString(pin.Code);
        const string product = "Auralita";
        return new Uri(
            $"https://app.plex.tv/auth#?clientID={clientId}&code={code}" +
            $"&context%5Bdevice%5D%5Bproduct%5D={Uri.EscapeDataString(product)}");
    }

    public async Task<string> PollForAuthenticationTokenAsync(
        PlexPin pin,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var deadline = DateTimeOffset.UtcNow + LoginTimeout;
        var pinUri = new Uri(
            $"{PinsUri.AbsoluteUri}/{pin.Id.ToString(CultureInfo.InvariantCulture)}");

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pin.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            {
                throw new PlexConnectionException("The Plex sign-in PIN expired. Start a new sign-in.");
            }

            using var request = CreateRequest(HttpMethod.Get, pinUri);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            if (root.TryGetProperty("authToken", out var tokenElement) &&
                tokenElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                return tokenElement.GetString()!;
            }

            if (TryReadDateTimeOffset(root, "expiresAt") is { } serverExpiry &&
                serverExpiry <= DateTimeOffset.UtcNow)
            {
                throw new PlexConnectionException("The Plex sign-in PIN expired. Start a new sign-in.");
            }

            progress?.Report("Finish signing in through Plex in your browser.");
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new PlexConnectionException(
            "Plex sign-in timed out. Start a new sign-in and approve it within five minutes.");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Auralita");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", "1.0.0");
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", ClientIdentifier);
        return request;
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
            if (statusCode == HttpStatusCode.Unauthorized)
            {
                throw new PlexAuthenticationException("Plex did not authorize the sign-in request.");
            }

            throw new PlexConnectionException(
                $"Plex sign-in returned HTTP {(int)statusCode} ({statusCode}).");
        }
        catch (HttpRequestException exception)
        {
            throw new PlexConnectionException("Could not reach Plex to sign in.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlexConnectionException("The Plex sign-in request timed out.", exception);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new PlexConnectionException("Plex returned an invalid sign-in response.", exception);
        }
    }

    private static bool TryReadInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.Number
            ? property.TryGetInt64(out value)
            : property.ValueKind == JsonValueKind.String &&
              long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static DateTimeOffset? TryReadDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var timestamp))
        {
            return timestamp;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out var unixSeconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }
}
