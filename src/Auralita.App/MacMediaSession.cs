using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Auralita.App;

internal static partial class MacMediaSession
{
    private const int CommandPlay = 0;
    private const int CommandPause = 1;
    private const int CommandTogglePlayPause = 2;
    private const int CommandNext = 3;
    private const int CommandPrevious = 4;

    private static Action<int>? _commandHandler;
    private static Action<double>? _seekHandler;

    public static void Enable(Action<int> commandHandler, Action<double> seekHandler)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        _commandHandler = commandHandler;
        _seekHandler = seekHandler;
        Native.SetHandlers(CommandPointer, SeekPointer);
    }

    public static void UpdateNowPlaying(
        string? title,
        string? artist,
        string? album,
        double durationSeconds,
        double elapsedSeconds,
        int state)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Native.UpdateNowPlaying(
            title,
            artist,
            album,
            durationSeconds,
            elapsedSeconds,
            state);
    }

    public static void SetArtwork(byte[]? pngBytes)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Native.SetArtwork(pngBytes, pngBytes?.Length ?? 0);
    }

    public static void ClearArtwork()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Native.ClearArtwork();
    }

    private static unsafe IntPtr CommandPointer =>
        (IntPtr)(void*)(delegate* unmanaged[Cdecl]<int, void>)&OnCommand;

    private static unsafe IntPtr SeekPointer =>
        (IntPtr)(void*)(delegate* unmanaged[Cdecl]<double, void>)&OnSeek;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnCommand(int command)
    {
        var handler = _commandHandler;
        if (handler is not null)
        {
            handler(command);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnSeek(double seconds)
    {
        var handler = _seekHandler;
        if (handler is not null)
        {
            handler(seconds);
        }
    }

    private static partial class Native
    {
        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_media_session_set_handlers")]
        internal static partial void SetHandlers(IntPtr commandHandler, IntPtr seekHandler);

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_media_session_update_now_playing", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial void UpdateNowPlaying(
            string? title,
            string? artist,
            string? album,
            double durationSeconds,
            double elapsedSeconds,
            int state);

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_media_session_set_artwork")]
        internal static partial void SetArtwork(byte[]? bytes, int length);

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_media_session_clear_artwork")]
        internal static partial void ClearArtwork();
    }
}
