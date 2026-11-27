namespace Auralita.Core.Plex;

public sealed class PlexConnectionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
