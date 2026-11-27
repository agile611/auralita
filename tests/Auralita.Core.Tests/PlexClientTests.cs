using System.Net;
using Auralita.Core.Plex;
using Xunit;

namespace Auralita.Core.Tests;

public sealed class PlexClientTests
{
    [Fact]
    public async Task DirectServerLoadsOnlyMusicLibraries()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/library/sections", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-token", request.Headers.GetValues("X-Plex-Token").Single());
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
    public async Task PlexWebAppDiscoversAndUsesAnAccessibleServer()
    {
        var requests = new List<string>();
        var handler = new StubHandler((request, _) =>
        {
            requests.Add(request.RequestUri!.ToString());
            if (request.RequestUri.Host == "plex.tv")
            {
                Assert.Equal("test-token", request.Headers.GetValues("X-Plex-Token").Single());
                return XmlResponse("""
                    <MediaContainer>
                      <Device provides="server">
                        <Connection uri="https://plex.example:32400" local="0" />
                        <Connection uri="http://plex.local:32400" local="1" />
                      </Device>
                    </MediaContainer>
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/identity")
            {
                return XmlResponse("<MediaContainer machineIdentifier=\"test\" />");
            }

            return XmlResponse("""
                <MediaContainer>
                  <Directory key="2" title="Music" type="music" />
                </MediaContainer>
                """);
        });
        using var client = CreateClient("https://app.plex.tv", handler);

        var libraries = await client.GetMusicLibrariesAsync();

        Assert.Single(libraries);
        Assert.Equal("http://plex.local:32400/library/sections", requests[^1]);
    }

    [Fact]
    public async Task TrackMappingIncludesMetadataAndAuthenticatedMediaUrls()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/library/sections/5/all", request.RequestUri!.AbsolutePath);
            return XmlResponse("""
                <MediaContainer>
                  <Track title="Song" grandparentTitle="Artist" parentTitle="Album" duration="183000" thumb="/library/metadata/11/thumb/1">
                    <Media><Part key="/library/parts/99/file?download=1" /></Media>
                  </Track>
                </MediaContainer>
                """);
        });
        using var client = CreateClient("https://plex.local:32400", handler);

        var tracks = await client.GetTracksAsync("5");

        var track = Assert.Single(tracks);
        Assert.Equal("Song", track.Title);
        Assert.Equal("Artist", track.Artist);
        Assert.Equal("Album", track.Album);
        Assert.Equal(TimeSpan.FromSeconds(183), track.Duration);
        Assert.Equal(
            "https://plex.local:32400/photo/:/transcode?url=%2Flibrary%2Fmetadata%2F11%2Fthumb%2F1&width=160&height=160&minSize=1&upscale=1&X-Plex-Token=test-token",
            track.ArtworkUri!.AbsoluteUri);
        Assert.Equal(
            "https://plex.local:32400/library/parts/99/file?download=1&X-Plex-Token=test-token",
            track.Source!.AbsoluteUri);
    }

    [Fact]
    public async Task ArtistAndAlbumEndpointsMapNamesAndArtwork()
    {
        var handler = new StubHandler((request, _) =>
        {
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
        using var client = CreateClient("https://plex.local:32400", handler);

        var artist = Assert.Single(await client.GetArtistsAsync("5"));
        var album = Assert.Single(await client.GetAlbumsAsync("5"));

        Assert.Equal("Artist", artist.Name);
        Assert.Equal(
            "https://plex.local:32400/photo/:/transcode?url=%2Fart%2Fartist&width=160&height=160&minSize=1&upscale=1&X-Plex-Token=test-token",
            artist.ArtworkUri!.AbsoluteUri);
        Assert.Equal("Album", album.Title);
        Assert.Equal("Artist", album.Artist);
        Assert.Equal(
            "https://plex.local:32400/photo/:/transcode?url=%2Fart%2Falbum&width=160&height=160&minSize=1&upscale=1&X-Plex-Token=test-token",
            album.ArtworkUri!.AbsoluteUri);
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
        using var client = CreateClient("https://plex.local:32400", handler);

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

        var error = await Assert.ThrowsAsync<PlexConnectionException>(
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
                new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)))));
    }

    private static PlexClient CreateClient(string serverUrl, HttpMessageHandler handler) =>
        new(
            new PlexServerSettings(new Uri(serverUrl), "1"),
            "test-token",
            new HttpClient(handler));

    private static HttpResponseMessage XmlResponse(string xml) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(xml)
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
