using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Auralita.Core.Domain;
using Auralita.Core.Playback;
using Auralita.Core.Plex;

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
    private readonly ObservableCollection<QueueTrackViewModel> _entries = [];
    private readonly Dictionary<Guid, QueueTrackViewModel> _entryViewModels = [];
    private readonly DispatcherTimer _playbackTimer = new();
    private readonly PlexSettingsStore _plexSettingsStore = new();
    private readonly IPlexCredentialStore _credentialStore = new MacKeychainCredentialStore();
    private readonly PlexArtworkCache _artworkCache = new();
    private readonly CancellationTokenSource _windowCancellation = new();
    private readonly List<PlexClient> _plexClients = [];
    private PlexServerSettings? _plexSettings;
    private PlexClient? _plexClient;
    private Guid? _activeEntryId;
    private DateTimeOffset _streamStartTime;
    private bool _activeTrackIsRemote;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _hasActiveTrack;

    public MainWindow()
    {
        InitializeComponent();
        QueueList.ItemsSource = _entries;
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTimer.Tick += OnPlaybackTimerTick;
        Closed += OnWindowClosed;
        Opened += OnWindowOpened;
        UpdateQueueChrome();
        UpdatePlaybackControls();
    }

    private void OnMenuOpenClick(object? sender, EventArgs e) =>
        OnAddTracksClick(sender, new RoutedEventArgs());

    private void OnMenuPlayClick(object? sender, EventArgs e) =>
        OnPlayClick(sender, new RoutedEventArgs());

    private void OnMenuStopClick(object? sender, EventArgs e) =>
        OnStopClick(sender, new RoutedEventArgs());

    private void OnMenuPlexClick(object? sender, EventArgs e) =>
        OnPlexClick(sender, new RoutedEventArgs());

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            _plexSettings = await _plexSettingsStore.LoadAsync(_windowCancellation.Token);
            var token = _credentialStore.GetPlexToken();
            if (_plexSettings is null || token is null)
            {
                return;
            }

            var client = CreatePlexClient(_plexSettings, token);
            var tracks = await client.GetTracksAsync(
                _plexSettings.LibrarySectionKey,
                _windowCancellation.Token);
            AddPlexTracks(tracks);
            RefreshQueue();
            StatusText.Text = tracks.Count == 0
                ? "Connected Plex library is empty."
                : $"Loaded {tracks.Count} track(s) from Plex.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            PlexConnectionException or
            DllNotFoundException or
            EntryPointNotFoundException)
        {
            StatusText.Text = $"Plex connection unavailable: {exception.Message}";
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
        }
    }

    private void OnMenuQuitClick(object? sender, EventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private async void OnPlexClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _plexSettingsStore.LoadAsync(_windowCancellation.Token);
            var token = _credentialStore.GetPlexToken();
            var dialog = new PlexConnectionDialog(settings, token);
            var result = await dialog.ShowDialog<PlexConnectionResult?>(this);
            if (result is null)
            {
                return;
            }

            _credentialStore.SetPlexToken(result.Token);
            await _plexSettingsStore.SaveAsync(result.Settings, _windowCancellation.Token);
            _plexSettings = result.Settings;
            _ = CreatePlexClient(result.Settings, result.Token);
            AddPlexTracks(result.Tracks);
            RefreshQueue();
            StatusText.Text = result.Tracks.Count == 0
                ? "Connected Plex library is empty."
                : $"Added {result.Tracks.Count} Plex track(s) to the queue.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            PlexConnectionException or
            DllNotFoundException or
            EntryPointNotFoundException)
        {
            StatusText.Text = $"Could not connect to Plex: {exception.Message}";
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
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
        if (QueueList.SelectedItem is not QueueTrackViewModel selected)
        {
            StatusText.Text = "Select a track to play.";
            return;
        }

        PlayTrack(selected.Entry);
    }

    private void OnPauseClick(object? sender, RoutedEventArgs e)
    {
        if (!_isPlaying)
        {
            return;
        }

        _audioPlayer.Pause();
        _playbackTimer.Stop();
        _isPlaying = false;
        _isPaused = true;
        StatusText.Text = "Playback paused.";
        UpdatePlaybackControls();
    }

    private void PlayTrack(QueueEntry entry)
    {
        try
        {
            if (_isPaused && _activeEntryId == entry.Id)
            {
                _audioPlayer.Resume();
                _streamStartTime = DateTimeOffset.UtcNow;
            }
            else
            {
                _audioPlayer.Play(entry.Track);
                _activeEntryId = entry.Id;
                _activeTrackIsRemote = entry.Track.Source?.Scheme is "http" or "https";
                _streamStartTime = DateTimeOffset.UtcNow;
            }

            _isPlaying = true;
            _isPaused = false;
            _hasActiveTrack = true;
            NowPlayingText.Text = entry.Track.Title;
            NowPlayingDetailsText.Text = $"{entry.Track.Artist}  ·  {entry.Track.Album}";
            NowPlayingArtwork.Source = _entryViewModels.GetValueOrDefault(entry.Id)?.Artwork;
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
        if (QueueList.SelectedItem is QueueTrackViewModel selected)
        {
            PlayTrack(selected.Entry);
        }
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        _audioPlayer.Stop();
        _playbackTimer.Stop();
        _isPlaying = false;
        _isPaused = false;
        _hasActiveTrack = false;
        _activeEntryId = null;
        _activeTrackIsRemote = false;
        NowPlayingText.Text = "Nothing playing";
        NowPlayingDetailsText.Text = string.Empty;
        NowPlayingArtwork.Source = null;
        StatusText.Text = "Playback stopped.";
        UpdatePlaybackProgress();
        UpdatePlaybackControls();
    }

    private void OnProgressPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_hasActiveTrack || ProgressTrack.Bounds.Width <= 0 || _audioPlayer.Duration <= TimeSpan.Zero)
        {
            return;
        }

        var fraction = Math.Clamp(
            e.GetPosition(ProgressTrack).X / ProgressTrack.Bounds.Width,
            0,
            1);
        try
        {
            _audioPlayer.Seek(TimeSpan.FromTicks((long)(_audioPlayer.Duration.Ticks * fraction)));
            UpdatePlaybackProgress();
            e.Handled = true;
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = $"Could not seek in this track: {exception.Message}";
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueTrackViewModel selected ||
            !_queue.TryRemove(selected.Entry.Id))
        {
            return;
        }

        if (_activeEntryId == selected.Entry.Id)
        {
            OnStopClick(sender, e);
        }

        RefreshQueue();
        StatusText.Text = $"{_entries.Count} file(s) in queue.";
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (QueueList.SelectedItem is QueueTrackViewModel selected)
        {
            _queue.TrySelect(selected.Entry.Id);
        }

        UpdatePlaybackControls();
    }

    private void MoveSelected(int offset)
    {
        if (QueueList.SelectedItem is not QueueTrackViewModel selected)
        {
            return;
        }

        var snapshot = _queue.Snapshot;
        var currentIndex = -1;
        for (var index = 0; index < snapshot.Entries.Count; index++)
        {
            if (snapshot.Entries[index].Id == selected.Entry.Id)
            {
                currentIndex = index;
                break;
            }
        }

        var destinationIndex = currentIndex + offset;
        if (currentIndex < 0 ||
            destinationIndex < 0 ||
            destinationIndex >= snapshot.Entries.Count ||
            !_queue.TryMove(selected.Entry.Id, destinationIndex))
        {
            return;
        }

        RefreshQueue();
    }

    private void RefreshQueue()
    {
        var snapshot = _queue.Snapshot;
        var existing = _entries.ToDictionary(viewModel => viewModel.Entry.Id);
        _entries.Clear();
        foreach (var entry in snapshot.Entries)
        {
            if (!existing.TryGetValue(entry.Id, out var viewModel))
            {
                viewModel = new QueueTrackViewModel(entry);
            }

            _entryViewModels[entry.Id] = viewModel;
            _entries.Add(viewModel);
            LoadArtwork(viewModel);
        }

        foreach (var entryId in _entryViewModels.Keys
                     .Where(entryId => snapshot.Entries.All(entry => entry.Id != entryId))
                     .ToArray())
        {
            _entryViewModels.Remove(entryId);
        }

        QueueList.SelectedItem = _entries
            .FirstOrDefault(entry => entry.Entry.Id == snapshot.CurrentEntryId);
        UpdateQueueChrome();
        UpdatePlaybackControls();
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        try
        {
            if (_audioPlayer.IsPlaying)
            {
                if (_activeTrackIsRemote &&
                    _audioPlayer.Duration <= TimeSpan.Zero &&
                    DateTimeOffset.UtcNow - _streamStartTime > TimeSpan.FromSeconds(30))
                {
                    throw new InvalidOperationException("The Plex stream timed out while loading.");
                }

                UpdatePlaybackProgress();
                return;
            }
        }
        catch (InvalidOperationException exception)
        {
            _playbackTimer.Stop();
            _audioPlayer.Stop();
            _isPlaying = false;
            _isPaused = false;
            _hasActiveTrack = false;
            _activeEntryId = null;
            _activeTrackIsRemote = false;
            StatusText.Text = $"Playback failed: {exception.Message}";
            UpdatePlaybackControls();
            return;
        }

        if (_queue.TrySelectNext())
        {
            RefreshQueue();
            if (QueueList.SelectedItem is QueueTrackViewModel nextTrack)
            {
                PlayTrack(nextTrack.Entry);
                return;
            }
        }

        _playbackTimer.Stop();
        _isPlaying = false;
        _isPaused = false;
        _hasActiveTrack = false;
        _activeEntryId = null;
        _activeTrackIsRemote = false;
        StatusText.Text = "Playback finished.";
        UpdatePlaybackProgress();
        UpdatePlaybackControls();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _playbackTimer.Stop();
        _windowCancellation.Cancel();
        _audioPlayer.Dispose();
        _artworkCache.Dispose();
        foreach (var client in _plexClients)
        {
            client.Dispose();
        }

        _windowCancellation.Dispose();
    }

    private void UpdatePlaybackControls()
    {
        var selectedViewModel = QueueList.SelectedItem as QueueTrackViewModel;
        var selectedIndex = -1;
        var entries = _queue.Snapshot.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            if (selectedViewModel is not null && entries[index].Id == selectedViewModel.Entry.Id)
            {
                selectedIndex = index;
                break;
            }
        }

        PlayButton.IsEnabled = selectedIndex >= 0;
        PauseButton.IsEnabled = _isPlaying;
        StopButton.IsEnabled = _hasActiveTrack;
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
            ? "1 track"
            : $"{_entries.Count} tracks";
    }

    private PlexClient CreatePlexClient(PlexServerSettings settings, string token)
    {
        var client = new PlexClient(settings, token);
        _plexClients.Add(client);
        _plexClient = client;
        return client;
    }

    private void AddPlexTracks(IReadOnlyList<Track> tracks)
    {
        foreach (var track in tracks)
        {
            _queue.Enqueue(track);
        }
    }

    private void LoadArtwork(QueueTrackViewModel viewModel)
    {
        if (viewModel.Entry.Track.ArtworkUri is not { } artworkUri ||
            _plexClient is null ||
            viewModel.Artwork is not null)
        {
            return;
        }

        _ = LoadArtworkAsync(viewModel, _plexClient, artworkUri);
    }

    private async Task LoadArtworkAsync(
        QueueTrackViewModel viewModel,
        PlexClient client,
        Uri artworkUri)
    {
        try
        {
            var artwork = await _artworkCache.GetAsync(
                client,
                artworkUri,
                _windowCancellation.Token);
            if (artwork is null || _windowCancellation.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                viewModel.Artwork = artwork;
                if (_activeEntryId == viewModel.Entry.Id)
                {
                    NowPlayingArtwork.Source = artwork;
                }
            });
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

}
