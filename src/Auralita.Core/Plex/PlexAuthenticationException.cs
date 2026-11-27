namespace Auralita.Core.Plex;

public sealed class PlexAuthenticationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
