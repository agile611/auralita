using System.Runtime.InteropServices;
using Auralita.Core.Plex;

namespace Auralita.App;

internal sealed partial class MacKeychainCredentialStore : IPlexCredentialStore
{
    private const string PlexTokenAccount = "plex-auth-token";

    public string? GetPlexToken()
    {
        EnsureMacOS();
        var credential = NativeKeychain.Get(PlexTokenAccount, out var status);
        if (credential == 0)
        {
            if (status == NativeKeychain.ItemNotFoundStatus())
            {
                return null;
            }

            throw new InvalidOperationException($"Could not read the Plex token from Keychain (status {status}).");
        }

        return Marshal.PtrToStringUTF8(credential);
    }

    public void SetPlexToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        EnsureMacOS();
        var status = NativeKeychain.Set(PlexTokenAccount, token);
        if (status != 0)
        {
            throw new InvalidOperationException($"Could not save the Plex token to Keychain (status {status}).");
        }
    }

    public void DeletePlexToken()
    {
        EnsureMacOS();
        var status = NativeKeychain.Delete(PlexTokenAccount);
        if (status != 0 && status != NativeKeychain.ItemNotFoundStatus())
        {
            throw new InvalidOperationException($"Could not remove the Plex token from Keychain (status {status}).");
        }
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Plex credentials are stored in the macOS Keychain.");
        }
    }

    private static partial class NativeKeychain
    {
        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_keychain_get", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint Get(string account, out int status);

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_keychain_set", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Set(string account, string secret);

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_keychain_delete", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Delete(string account);

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_keychain_item_not_found_status")]
        internal static partial int ItemNotFoundStatus();
    }
}
