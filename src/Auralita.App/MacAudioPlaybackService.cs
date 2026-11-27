using System.Runtime.InteropServices;
using Auralita.Core.Domain;

namespace Auralita.App;

internal sealed partial class MacAudioPlaybackService : IDisposable
{
    private bool _disposed;

    public bool IsPlaying
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return NativeAudio.IsPlaying() != 0;
        }
    }

    public TimeSpan CurrentTime => GetTime(NativeAudio.CurrentTime());

    public TimeSpan Duration => GetTime(NativeAudio.Duration());

    public void Play(Track track)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(track);

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Local playback currently uses the macOS audio framework.");
        }

        if (track.Source is not { IsFile: true } source)
        {
            throw new NotSupportedException("Playback requires a local audio file.");
        }

        var path = source.LocalPath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected audio file could not be found.", path);
        }

        if (NativeAudio.PlayFile(path) == 0)
        {
            var message = Marshal.PtrToStringUTF8(NativeAudio.LastError());
            throw new InvalidOperationException(message ?? "macOS could not play the selected audio file.");
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeAudio.Stop();
    }

    private static TimeSpan GetTime(double seconds) =>
        double.IsFinite(seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NativeAudio.Stop();
        _disposed = true;
    }

    private static partial class NativeAudio
    {
        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_play_file", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int PlayFile(string filePath);

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_stop")]
        internal static partial void Stop();

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_is_playing")]
        internal static partial int IsPlaying();

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_current_time")]
        internal static partial double CurrentTime();

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_duration")]
        internal static partial double Duration();

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_last_error")]
        internal static partial nint LastError();
    }
}
