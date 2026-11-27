namespace Auralita.Core.Plex;

public sealed record PlexPin(long Id, string Code, DateTimeOffset? ExpiresAt);
