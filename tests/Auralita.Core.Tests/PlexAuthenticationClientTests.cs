using System.Net;
using System.Text;
using Auralita.Core.Plex;
using Xunit;

namespace Auralita.Core.Tests;

public sealed class PlexAuthenticationClientTests
{
    private const string ClientIdentifier = "11111111-2222-4333-8444-555555555555";

    [Fact]
    public async Task RequestsStrongPinAndBuildsEncodedAuthorizationUrl()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v2/pins", request.RequestUri!.AbsolutePath);
            Assert.Equal("strong=true", request.RequestUri.Query.TrimStart('?'));
            AssertPlexHeaders(request);
            return JsonResponse("""{"id":123,"code":"A+B&="}""");
        });
        using var client = new PlexAuthenticationClient(ClientIdentifier, new HttpClient(handler));

        var pin = await client.RequestPinAsync();
        var authorizationUri = client.CreateAuthorizationUri(pin);

        Assert.Equal(123, pin.Id);
        Assert.Equal("A+B&=", pin.Code);
        Assert.Contains("clientID=" + ClientIdentifier, authorizationUri.AbsoluteUri);
        Assert.Contains("code=A%2BB%26%3D", authorizationUri.AbsoluteUri);
        Assert.Contains("context%5Bdevice%5D%5Bproduct%5D=Auralita", authorizationUri.AbsoluteUri);
    }

    [Fact]
    public async Task PollsPinAndReturnsAuthenticationToken()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v2/pins/456", request.RequestUri!.AbsolutePath);
            AssertPlexHeaders(request);
            return JsonResponse("""{"authToken":"account-secret"}""");
        });
        using var client = new PlexAuthenticationClient(ClientIdentifier, new HttpClient(handler));

        var token = await client.PollForAuthenticationTokenAsync(new PlexPin(456, "code", null));

        Assert.Equal("account-secret", token);
    }

    [Fact]
    public async Task ExpiredPinFailsWithoutPolling()
    {
        var handler = new StubHandler((_, _) =>
            throw new InvalidOperationException("An expired PIN must not be polled."));
        using var client = new PlexAuthenticationClient(ClientIdentifier, new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<PlexConnectionException>(() =>
            client.PollForAuthenticationTokenAsync(
                new PlexPin(456, "code", DateTimeOffset.UtcNow.AddSeconds(-1))));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnauthorizedPinRequestRaisesAuthenticationException()
    {
        var handler = new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new PlexAuthenticationClient(ClientIdentifier, new HttpClient(handler));

        await Assert.ThrowsAsync<PlexAuthenticationException>(() => client.RequestPinAsync());
    }

    private static void AssertPlexHeaders(HttpRequestMessage request)
    {
        Assert.Equal("Auralita", request.Headers.GetValues("X-Plex-Product").Single());
        Assert.Equal("1.0.0", request.Headers.GetValues("X-Plex-Version").Single());
        Assert.Equal(ClientIdentifier, request.Headers.GetValues("X-Plex-Client-Identifier").Single());
        Assert.Contains(request.Headers.Accept, header => header.MediaType == "application/json");
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request, cancellationToken));
    }
}
