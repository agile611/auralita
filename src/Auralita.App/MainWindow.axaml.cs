using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Auralita.Core.Domain;
using Auralita.Core.Playback;

namespace Auralita.App;

public partial class MainWindow : Window
{
    private static readonly string[] AudioPatterns =
    [
        "*.mp3", "*.wav", "*.flac", "*.ogg", "*.wma", "*.ape", "*.m4a",
        "*.aac", "*.mp2", "*.amr", "*.mid", "*.midi", "*.m4r", "*.mmf",
        "*.wv", "*.aif", "*.aiff", "*.au", "*.ra", "*.mka", "*.ac3", "*.caf"
    ];

    private readonly PlaybackQueue _queue = new();
    private readonly MacAudioPlaybackService _audioPlayer = new();
    private readonly ObservableCollection<QueueEntry> _entries = [];
    private readonly DispatcherTimer _playbackTimer = new();
    private bool _isPlaying;

    public MainWindow()
    {
        InitializeComponent();
        QueueList.ItemsSource = _entries;
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTimer.Tick += OnPlaybackTimerTick;
        Closed += OnWindowClosed;
        UpdateQueueChrome();
        UpdatePlaybackControls();
    }

    private void OnMenuOpenClick(object? sender, EventArgs e) =>
        OnAddTracksClick(sender, new RoutedEventArgs());

    private void OnMenuPlayClick(object? sender, EventArgs e) =>
        OnPlayClick(sender, new RoutedEventArgs());

    private void OnMenuStopClick(object? sender, EventArgs e) =>
        OnStopClick(sender, new RoutedEventArgs());

    private void OnMenuQuitClick(object? sender, EventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private async void OnAddTracksClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Add audio files",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Audio files")
                        {
                            Patterns = AudioPatterns
                        }
                    ]
                });

            var addedCount = 0;
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (path is null)
                {
                    StatusText.Text = "A selected item is not available as a local file.";
                    continue;
                }

                var fullPath = Path.GetFullPath(path);
                var title = Path.GetFileNameWithoutExtension(fullPath);
                var track = new Track(title, "Unknown artist", new Uri(fullPath));
                _queue.Enqueue(track);
                addedCount++;
            }

            RefreshQueue();
            StatusText.Text = addedCount == 0
                ? "No local audio files were added."
                : $"Added {addedCount} file(s). Select a track and press Play.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException)
        {
            StatusText.Text = $"Could not add audio files: {exception.Message}";
        }
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e) =>
        MoveSelected(-1);

    private void OnMoveDownClick(object? sender, RoutedEventArgs e) =>
        MoveSelected(1);

    private void OnPreviousClick(object? sender, RoutedEventArgs e) =>
        NavigatePlayback(-1);

    private void OnNextClick(object? sender, RoutedEventArgs e) =>
        NavigatePlayback(1);

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueEntry selected)
        {
            StatusText.Text = "Select a track to play.";
            return;
        }

        PlayTrack(selected);
    }

    private void PlayTrack(QueueEntry entry)
    {
        try
        {
            _audioPlayer.Play(entry.Track);
            _isPlaying = true;
            NowPlayingText.Text = entry.Track.Title.ToUpperInvariant();
            StatusText.Text = $"Playing {entry.Track.Title}.";
            _playbackTimer.Start();
            UpdatePlaybackProgress();
            UpdatePlaybackControls();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException or
            DllNotFoundException or
            EntryPointNotFoundException)
        {
            StatusText.Text = $"Could not play {entry.Track.Title}: {exception.Message}";
            UpdatePlaybackControls();
        }
    }

    private void NavigatePlayback(int direction)
    {
        var moved = direction < 0
            ? _queue.TrySelectPrevious()
            : _queue.TrySelectNext();
        if (!moved)
        {
            return;
        }

        RefreshQueue();
        if (QueueList.SelectedItem is QueueEntry selected)
        {
            PlayTrack(selected);
        }
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        _audioPlayer.Stop();
        _playbackTimer.Stop();
        _isPlaying = false;
        NowPlayingText.Text = "-- NOTHING PLAYING --";
        StatusText.Text = "Playback stopped.";
        UpdatePlaybackProgress();
        UpdatePlaybackControls();
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueEntry selected ||
            !_queue.TryRemove(selected.Id))
        {
            return;
        }

        RefreshQueue();
        StatusText.Text = $"{_entries.Count} file(s) in queue.";
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (QueueList.SelectedItem is QueueEntry selected)
        {
            _queue.TrySelect(selected.Id);
        }

        UpdatePlaybackControls();
    }

    private void MoveSelected(int offset)
    {
        if (QueueList.SelectedItem is not QueueEntry selected)
        {
            return;
        }

        var snapshot = _queue.Snapshot;
        var currentIndex = -1;
        for (var index = 0; index < snapshot.Entries.Count; index++)
        {
            if (snapshot.Entries[index].Id == selected.Id)
            {
                currentIndex = index;
                break;
            }
        }

        var destinationIndex = currentIndex + offset;
        if (currentIndex < 0 ||
            destinationIndex < 0 ||
            destinationIndex >= snapshot.Entries.Count ||
            !_queue.TryMove(selected.Id, destinationIndex))
        {
            return;
        }

        RefreshQueue();
    }

    private void RefreshQueue()
    {
        var snapshot = _queue.Snapshot;
        _entries.Clear();
        foreach (var entry in snapshot.Entries)
        {
            _entries.Add(entry);
        }

        QueueList.SelectedItem = snapshot.Entries
            .FirstOrDefault(entry => entry.Id == snapshot.CurrentEntryId);
        UpdateQueueChrome();
        UpdatePlaybackControls();
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (_audioPlayer.IsPlaying)
        {
            UpdatePlaybackProgress();
            return;
        }

        if (_queue.TrySelectNext())
        {
            RefreshQueue();
            if (QueueList.SelectedItem is QueueEntry nextTrack)
            {
                PlayTrack(nextTrack);
                return;
            }
        }

        _playbackTimer.Stop();
        _isPlaying = false;
        StatusText.Text = "Playback finished.";
        UpdatePlaybackProgress();
        UpdatePlaybackControls();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _playbackTimer.Stop();
        _audioPlayer.Dispose();
    }

    private void UpdatePlaybackControls()
    {
        var selectedEntry = QueueList.SelectedItem as QueueEntry;
        var selectedIndex = -1;
        var entries = _queue.Snapshot.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            if (selectedEntry is not null && entries[index].Id == selectedEntry.Id)
            {
                selectedIndex = index;
                break;
            }
        }

        PlayButton.IsEnabled = selectedIndex >= 0;
        StopButton.IsEnabled = _isPlaying;
        PreviousButton.IsEnabled = selectedIndex > 0;
        NextButton.IsEnabled = selectedIndex >= 0 && selectedIndex < entries.Count - 1;
        MoveUpButton.IsEnabled = selectedIndex > 0;
        MoveDownButton.IsEnabled = selectedIndex >= 0 && selectedIndex < entries.Count - 1;
        RemoveButton.IsEnabled = selectedIndex >= 0;
    }

    private void UpdatePlaybackProgress()
    {
        var duration = _audioPlayer.Duration;
        if (duration <= TimeSpan.Zero)
        {
            ProgressFill.Width = 0;
            ElapsedTimeText.Text = "00:00";
            RemainingTimeText.Text = "--:--";
            return;
        }

        var position = _audioPlayer.CurrentTime;
        var progress = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
        ProgressFill.Width = ProgressTrack.Bounds.Width * progress;
        ElapsedTimeText.Text = FormatTime(position);
        RemainingTimeText.Text = $"-{FormatTime(duration - position)}";
    }

    private static string FormatTime(TimeSpan time)
    {
        var totalSeconds = Math.Max(0, (int)time.TotalSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void UpdateQueueChrome()
    {
        EmptyQueuePanel.IsVisible = _entries.Count == 0;
        QueueCountText.Text = _entries.Count == 1
            ? "01 TRACK"
            : $"{_entries.Count:00} TRACKS";
    }
}
