namespace Auralita.Core.Domain;

public sealed record Track
{
    public Track(string title, string artist, Uri? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(artist);

        Id = Guid.NewGuid();
        Title = title.Trim();
        Artist = artist.Trim();
        Source = source;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string Artist { get; }

    public Uri? Source { get; }
}
