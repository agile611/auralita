using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Auralita.Core.Domain;

namespace Auralita.App;

internal sealed class QueueTrackViewModel(QueueEntry entry) : INotifyPropertyChanged
{
    private Bitmap? _artwork;

    public event PropertyChangedEventHandler? PropertyChanged;

    public QueueEntry Entry { get; } = entry;

    public string Title => Entry.Track.Title;

    public string Artist => Entry.Track.Artist;

    public string Album => Entry.Track.Album;

    public string DurationText => Entry.Track.Duration is { } duration
        ? $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}"
        : "--:--";

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
