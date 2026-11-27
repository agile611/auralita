using System.Net;
using System.Text;
using Auralita.Core.Plex;
using Xunit;

namespace Auralita.Core.Tests;

public sealed class PlexClientTests
{
    private const string ClientIdentifier = "11111111-2222-4333-8444-555555555555";

    [Fact]
    public async Task DirectServerLoadsOnlyMusicLibrariesWithServerAccessToken()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/library/sections", request.RequestUri!.AbsolutePath);
            AssertPlexHeaders(request, "server-token");
            return XmlResponse("""
                <MediaContainer>
                  <Directory key="1" title="Movies" type="movie" />
                  <Directory key="3" title="Music" type="artist" />
                  <Directory key="4" title="Other music" type="music" />
                </MediaContainer>
                """);
        });
        using var client = CreateClient("http://plex.local:32400", handler);

        var libraries = await client.GetMusicLibrariesAsync();

        Assert.Equal(
            [
                new PlexLibrarySection("3", "Music"),
                new PlexLibrarySection("4", "Other music")
            ],
            libraries);
    }

    [Fact]
    public async Task PlexWebAppFindsMusicLibraryOnLaterServerAndUsesItsAccessToken()
    {
        var requests = new List<string>();
        var handler = CreateMultiServerHandler(requests);
        using var client = new PlexClient(
            new PlexServerSettings(new Uri("https://app.plex.tv"), string.Empty),
            "test-token",
            ClientIdentifier,
            new HttpClient(handler));

        var libraries = await client.GetMusicLibrariesAsync();

        var library = Assert.Single(libraries);
        Assert.Equal(new PlexLibrarySection("2", "Remote Music"), library);
        Assert.Contains("https://plex.local:32400/library/sections", requests);
        Assert.Equal("https://plex.example:32400/library/sections", requests[^1]);

        var track = Assert.Single(await client.GetTracksAsync(library.Key));
        Assert.Equal("Remote song", track.Title);
        Assert.Equal(
            "https://plex.example:32400/library/parts/10/file?X-Plex-Token=remote-token",
            track.Source!.AbsoluteUri);
        Assert.Equal(
            "https://plex.example:32400/library/sections/2/all?type=10",
            requests[^1]);
    }

    [Fact]
    public async Task PlexWebAppRestoresTheSelectedLibraryFromMatchingServer()
    {
        var requests = new List<string>();
        var handler = CreateMultiServerHandler(requests);
        using var client = new PlexClient(
            new PlexServerSettings(new Uri("https://app.plex.tv"), "2"),
            "test-token",
            ClientIdentifier,
            new HttpClient(handler));

        var libraries = await client.GetMusicLibrariesAsync();

        var library = Assert.Single(libraries);
        Assert.Equal(new PlexLibrarySection("2", "Remote Music"), library);
        Assert.Equal("https://plex.example:32400/library/sections", requests[^1]);
    }

    [Fact]
    public async Task ServerDiscoverySkipsUnreachableConnectionsAndUsesSecureHttps()
    {
        var requests = new List<string>();
        var handler = new StubHandler((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.ToString());
            if (uri.Host == "plex.tv")
            {
                Assert.Equal(
                    "?includeHttps=1&includeRelay=1",
                    uri.Query);
                return JsonResponse("""
                    [
                      {"provides":"server","accessToken":"local-token","connections":[{"uri":"http://plex.local:32400","local":true,"relay":false}]},
                      {"provides":"server","accessToken":"relay-token","connections":[{"uri":"https://plex.relay:32400","local":false,"relay":true}]},
                      {"provides":"server","accessToken":"secure-token","connections":[{"uri":"https://plex.secure:32400","local":false,"relay":false}]}
                    ]
                    """);
            }

            if (uri.Host is "plex.local" or "plex.relay")
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            AssertPlexHeaders(request, "secure-token");
            return XmlResponse("""
                <MediaContainer>
                  <Directory key="7" title="Secure music" type="artist" />
                </MediaContainer>
                """);
        });
        using var client = new PlexClient(
            new PlexServerSettings(new Uri("https://app.plex.tv"), string.Empty),
            "test-token",
            ClientIdentifier,
            new HttpClient(handler));

        var libraries = await client.GetMusicLibrariesAsync();

        Assert.Equal(new PlexLibrarySection("7", "Secure music"), Assert.Single(libraries));
        Assert.Contains("https://plex.secure:32400/library/sections", requests);
        Assert.DoesNotContain(requests, request => request.Contains("plex.relay"));
    }

    [Fact]
    public async Task SavedLocalServerFallsBackToRemoteServerWhenUnreachable()
    {
        var requests = new List<string>();
        var handler = new StubHandler((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.ToString());
            if (uri.Host == "plex.tv")
            {
                return JsonResponse("""
                    [
                      {"provides":"server","accessToken":"local-token","connections":[{"uri":"https://plex.local:32400","local":true,"relay":false}]},
                      {"provides":"server","accessToken":"remote-token","connections":[{"uri":"https://plex.example:32400","local":false,"relay":false}]}
                    ]
                    """);
            }

            if (uri.Host == "plex.local")
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            AssertPlexHeaders(request, "remote-token");
            return XmlResponse("""
                <MediaContainer>
                  <Directory key="2" title="Remote music" type="artist" />
                </MediaContainer>
                """);
        });
        using var client = new PlexClient(
            new PlexServerSettings(new Uri("https://plex.local:32400"), "2"),
            "test-token",
            ClientIdentifier,
            new HttpClient(handler));

        var libraries = await client.GetMusicLibrariesAsync();

        Assert.Equal(new PlexLibrarySection("2", "Remote music"), Assert.Single(libraries));
        Assert.Equal("https://plex.example:32400/", client.ResolvedServerUri!.AbsoluteUri);
        Assert.Contains("https://plex.local:32400/library/sections", requests);
        Assert.Contains("https://plex.example:32400/library/sections", requests);
    }

    [Fact]
    public async Task TrackMappingIncludesMetadataAndAuthenticatedMediaUrls()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/library/sections/5/all", request.RequestUri!.AbsolutePath);
            AssertPlexHeaders(request, "server-token");
            return XmlResponse("""
                <MediaContainer>
                  <Track ratingKey="99" key="/library/metadata/99" title="Song" grandparentTitle="Artist" parentTitle="Album" duration="183000" thumb="/library/metadata/11/thumb/1">
                    <Media><Part key="/library/parts/99/file?download=1" /></Media>
                  </Track>
                </MediaContainer>
                """);
        });
        using var client = CreateClient("https://plex.local:32400", handler, includeDefaultLibrary: true);

        var tracks = await client.GetTracksAsync("5");

        var track = Assert.Single(tracks);
        Assert.Equal("Song", track.Title);
        Assert.Equal("Artist", track.Artist);
        Assert.Equal("Album", track.Album);
        Assert.Equal(TimeSpan.FromSeconds(183), track.Duration);
        Assert.Equal(
            "https://plex.local:32400/photo/:/transcode?url=%2Flibrary%2Fmetadata%2F11%2Fthumb%2F1&width=160&height=160&minSize=1&upscale=1&X-Plex-Token=server-token",
            track.ArtworkUri!.AbsoluteUri);
        Assert.Equal(
            "https://plex.local:32400/library/parts/99/file?download=1&X-Plex-Token=server-token",
            track.Source!.AbsoluteUri);
        Assert.Equal("99", track.PlexRatingKey);
    }

    [Fact]
    public async Task ReportTimelineUsesResolvedServerTokenAndTimelineParameters()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/:/timeline", request.RequestUri!.AbsolutePath);
            AssertPlexHeaders(request, "server-token");
            var query = request.RequestUri!.Query;
            Assert.Contains("key=123", query);
            Assert.Contains("ratingKey=123", query);
            Assert.Contains("state=playing", query);
            Assert.Contains("time=5000", query);
            Assert.Contains("duration=180000", query);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var client = CreateClient(
            "https://plex.local:32400",
            handler,
            includeDefaultLibrary: true);

        _ = await client.GetMusicLibrariesAsync();
        await client.ReportTimelineAsync(
            "123",
            "playing",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task ArtistAndAlbumEndpointsMapNamesAndArtwork()
    {
        var handler = new StubHandler((request, _) =>
        {
            AssertPlexHeaders(request, "server-token");
            if (request.RequestUri!.Query.Contains("type=8", StringComparison.Ordinal))
            {
                return XmlResponse("""
                    <MediaContainer>
                      <Directory key="/library/metadata/7" title="Artist" type="artist" thumb="/art/artist" />
                    </MediaContainer>
                    """);
            }

            return XmlResponse("""
                <MediaContainer>
                  <Directory key="/library/metadata/9" title="Album" type="album" parentTitle="Artist" thumb="/art/album" />
                </MediaContainer>
                """);
        });
        using var client = CreateClient("https://plex.local:32400", handler, includeDefaultLibrary: true);

        var artist = Assert.Single(await client.GetArtistsAsync("5"));
        var album = Assert.Single(await client.GetAlbumsAsync("5"));

        Assert.Equal("Artist", artist.Name);
        Assert.Equal(
            "https://plex.local:32400/photo/:/transcode?url=%2Fart%2Fartist&width=160&height=160&minSize=1&upscale=1&X-Plex-Token=server-token",
            artist.ArtworkUri!.AbsoluteUri);
        Assert.Equal("Album", album.Title);
        Assert.Equal("Artist", album.Artist);
        Assert.Equal(
            "https://plex.local:32400/photo/:/transcode?url=%2Fart%2Falbum&width=160&height=160&minSize=1&upscale=1&X-Plex-Token=server-token",
            album.ArtworkUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ParsesJsonMetadataForArtistsAlbumsAndTracks()
    {
        var handler = new StubHandler((request, _) =>
        {
            var query = request.RequestUri!.Query;
            if (query.Contains("type=8", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {"MediaContainer":{"Metadata":[{"type":"artist","key":"/library/metadata/artist","title":"JSON artist"}]}}
                    """);
            }

            if (query.Contains("type=9", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {"MediaContainer":{"Metadata":[{"type":"album","key":"/library/metadata/album","title":"JSON album","parentTitle":"JSON artist"}]}}
                    """);
            }

            return JsonResponse("""
                {
                  "MediaContainer": {
                    "totalSize": 1,
                    "Metadata": [{
                      "type": "track",
                      "key": "/library/metadata/track",
                      "title": "JSON song",
                      "grandparentTitle": "JSON artist",
                      "parentTitle": "JSON album",
                      "duration": 120000,
                      "Media": [{"Part": [{"key": "/library/parts/json"}]}]
                    }]
                  }
                }
                """);
        });
        using var client = CreateClient(
            "https://plex.local:32400",
            handler,
            includeDefaultLibrary: true);

        var artist = Assert.Single(await client.GetArtistsAsync("5"));
        var album = Assert.Single(await client.GetAlbumsAsync("5"));
        var track = Assert.Single(await client.GetTracksAsync("5"));

        Assert.Equal("JSON artist", artist.Name);
        Assert.Equal("JSON album", album.Title);
        Assert.Equal("JSON song", track.Title);
        Assert.Equal("JSON artist", track.Artist);
        Assert.Equal("JSON album", track.Album);
        Assert.Equal(TimeSpan.FromMinutes(2), track.Duration);
        Assert.Equal(
            "https://plex.local:32400/library/parts/json?X-Plex-Token=server-token",
            track.Source!.AbsoluteUri);
    }

    [Fact]
    public async Task TrackMappingRejectsMediaFromAnotherServer()
    {
        var handler = new StubHandler((_, _) => XmlResponse("""
            <MediaContainer>
              <Track title="Untrusted stream">
                <Media><Part key="https://untrusted.example/audio.mp3" /></Media>
              </Track>
            </MediaContainer>
            """));
        using var client = CreateClient("https://plex.local:32400", handler, includeDefaultLibrary: true);

        var tracks = await client.GetTracksAsync("5");

        Assert.Empty(tracks);
    }

    [Fact]
    public async Task ArtworkMustStayOnTheResolvedPlexServer()
    {
        var handler = new StubHandler((_, _) =>
            throw new InvalidOperationException("An external artwork host must not be requested."));
        using var client = CreateClient("https://plex.local:32400", handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetArtworkAsync(new Uri("https://untrusted.example/cover.jpg")));
    }

    [Fact]
    public async Task InvalidTokenReturnsClearAuthenticationError()
    {
        var handler = new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient("http://plex.local:32400", handler);

        var error = await Assert.ThrowsAsync<PlexAuthenticationException>(
            () => client.GetMusicLibrariesAsync());

        Assert.Contains("authentication token", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServerTimeoutReturnsClearConnectionError()
    {
        var handler = new StubHandler((_, _) =>
            throw new TaskCanceledException("request timed out"));
        using var client = CreateClient("https://plex.local:32400", handler);

        var error = await Assert.ThrowsAsync<PlexConnectionException>(
            () => client.GetMusicLibrariesAsync());

        Assert.Contains("timed out", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsNonHttpServerUrls()
    {
        Assert.Throws<ArgumentException>(() =>
            new PlexClient(
                new PlexServerSettings(new Uri("file:///tmp/plex"), "1"),
                "test-token",
                ClientIdentifier,
                new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)))));
    }

    private static PlexClient CreateClient(
        string serverUrl,
        StubHandler handler,
        bool includeDefaultLibrary = false)
    {
        var client = new HttpClient(new StubHandler((request, cancellationToken) =>
        {
            if (request.RequestUri!.Host == "plex.tv")
            {
                AssertPlexHeaders(request, "test-token");
                return JsonResponse($$"""
                    [{
                      "provides": "server",
                      "accessToken": "server-token",
                      "connections": [{"uri": "{{serverUrl}}", "local": true, "relay": false}]
                    }]
                    """);
            }

            if (includeDefaultLibrary &&
                request.RequestUri.AbsolutePath == "/library/sections")
            {
                AssertPlexHeaders(request, "server-token");
                return XmlResponse("""
                    <MediaContainer>
                      <Directory key="1" title="Test music" type="artist" />
                    </MediaContainer>
                    """);
            }

            return handler.Respond(request, cancellationToken);
        }));
        return new PlexClient(
            new PlexServerSettings(new Uri(serverUrl), string.Empty),
            "test-token",
            ClientIdentifier,
            client);
    }

    private static HttpMessageHandler CreateMultiServerHandler(List<string> requests) =>
        new StubHandler((request, _) =>
        {
            var requestUri = request.RequestUri!;
            requests.Add(requestUri.ToString());
            if (requestUri.Host == "plex.tv")
            {
                AssertPlexHeaders(request, "test-token");
                return JsonResponse("""
                    [
                      {
                        "provides": "server",
                        "accessToken": "local-token",
                        "connections": [{"uri": "https://plex.local:32400", "local": true, "relay": false}]
                      },
                      {
                        "provides": "server",
                        "accessToken": "remote-token",
                        "connections": [{"uri": "https://plex.example:32400", "local": false, "relay": false}]
                      }
                    ]
                    """);
            }

            AssertPlexHeaders(
                request,
                requestUri.Host == "plex.local" ? "local-token" : "remote-token");
            if (requestUri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal))
            {
                return XmlResponse("""
                    <MediaContainer>
                      <Track title="Remote song">
                        <Media><Part key="/library/parts/10/file" /></Media>
                      </Track>
                    </MediaContainer>
                    """);
            }

            if (requestUri.Host == "plex.local")
            {
                return XmlResponse("""
                    <MediaContainer>
                      <Directory key="1" title="Movies" type="movie" />
                    </MediaContainer>
                    """);
            }

            return XmlResponse("""
                <MediaContainer>
                  <Directory key="2" title="Remote Music" type="artist" />
                </MediaContainer>
                """);
        });

    private static HttpResponseMessage XmlResponse(string xml) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(xml)
        };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static void AssertPlexHeaders(HttpRequestMessage request, string token)
    {
        Assert.Equal(token, request.Headers.GetValues("X-Plex-Token").Single());
        Assert.Equal("Auralita", request.Headers.GetValues("X-Plex-Product").Single());
        Assert.Equal("1.0.0", request.Headers.GetValues("X-Plex-Version").Single());
        Assert.Equal(ClientIdentifier, request.Headers.GetValues("X-Plex-Client-Identifier").Single());
        Assert.Contains(request.Headers.Accept, header => header.MediaType == "application/json");
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public HttpResponseMessage Respond(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Respond(request, cancellationToken));
    }
}
