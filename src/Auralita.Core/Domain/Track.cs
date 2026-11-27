namespace Auralita.Core.Domain;

public sealed record Track
{
    public Track(
        string title,
        string artist,
        Uri? source = null,
        string? album = null,
        TimeSpan? duration = null,
        Uri? artworkUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(artist);
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        Id = Guid.NewGuid();
        Title = title.Trim();
        Artist = artist.Trim();
        Source = source;
        Album = string.IsNullOrWhiteSpace(album) ? "Unknown album" : album.Trim();
        Duration = duration;
        ArtworkUri = artworkUri;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string Artist { get; }

    public Uri? Source { get; }

    public string Album { get; }

    public TimeSpan? Duration { get; }

    public Uri? ArtworkUri { get; }
}
