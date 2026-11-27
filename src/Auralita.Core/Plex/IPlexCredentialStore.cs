namespace Auralita.Core.Plex;

public interface IPlexCredentialStore
{
    string? GetPlexToken();

    void SetPlexToken(string token);
}
