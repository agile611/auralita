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
            var state = NativeAudio.IsPlaying();
            if (state < 0)
            {
                throw GetNativePlaybackError();
            }

            return state > 0;
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

        if (track.Source is not { IsAbsoluteUri: true } source)
        {
            throw new NotSupportedException("Playback requires a local file or an HTTP(S) media URL.");
        }

        if (source.IsFile)
        {
            var path = source.LocalPath;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The selected audio file could not be found.", path);
            }

            if (NativeAudio.PlayFile(path) == 0)
            {
                throw GetNativePlaybackError();
            }

            return;
        }

        if (source.Scheme is not ("http" or "https"))
        {
            throw new NotSupportedException("Streaming playback requires an HTTP or HTTPS URL.");
        }

        if (NativeAudio.PlayUrl(source.AbsoluteUri) == 0)
        {
            throw GetNativePlaybackError();
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeAudio.Pause();
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeAudio.Resume();
    }

    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (position < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (NativeAudio.Seek(position.TotalSeconds) == 0)
        {
            throw new InvalidOperationException("The current track cannot seek to that position.");
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

    private static InvalidOperationException GetNativePlaybackError()
    {
        var message = Marshal.PtrToStringUTF8(NativeAudio.LastError());
        return new InvalidOperationException(message ?? "macOS could not play the selected audio.");
    }

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

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_play_url", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int PlayUrl(string mediaUrl);

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_stop")]
        internal static partial void Stop();

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_pause")]
        internal static partial void Pause();

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_resume")]
        internal static partial void Resume();

        [LibraryImport("AuralitaAudio", EntryPoint = "auralita_seek")]
        internal static partial int Seek(double seconds);

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
