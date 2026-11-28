using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Auralita.Core.Domain;

namespace Auralita.App;

internal sealed class QueueTrackViewModel(QueueEntry entry) : INotifyPropertyChanged
{
    private Bitmap? _artwork;
    private int _trackNumber;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    public QueueEntry Entry { get; } = entry;

    public string Title => Entry.Track.Title;
    public int QueuePosition { get; set; } = 0;

    public string Artist => Entry.Track.Artist;

    public string Album => Entry.Track.Album;

    public string DurationText => Entry.Track.Duration is { } duration
        ? $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}"
        : "--:--";

    public string QualityLabel => GetQualityLabel();

    public int TrackNumber
    {
        get => _trackNumber;
        set
        {
            if (_trackNumber == value) return;
            _trackNumber = value;
            OnPropertyChanged();
        }
    }

    public Bitmap? Artwork
    {
        get => _artwork;
        set
        {
            if (ReferenceEquals(_artwork, value))
            {
                return;
            }

            _artwork = value;
            OnPropertyChanged();
        }
    }

    private string GetQualityLabel()
    {
        if (Entry.Track.Source is not { IsAbsoluteUri: true } source)
        {
            return string.Empty;
        }

        if (source.IsFile)
        {
            var ext = System.IO.Path.GetExtension(source.LocalPath).ToLowerInvariant();
            return ext switch
            {
                ".flac" => "FLAC",
                ".wav" => "WAV",
                ".aiff" or ".aif" => "AIFF",
                ".ape" => "APE",
                ".wv" => "WV",
                ".m4a" => "ALAC",
                ".mp3" => "MP3",
                ".ogg" => "OGG",
                ".opus" => "OPUS",
                _ => ext.TrimStart('.').ToUpperInvariant()
            };
        }

        if (source.Scheme is "http" or "https")
        {
            return "Plex";
        }

        return string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
