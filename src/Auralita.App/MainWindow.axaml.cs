using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        // Wire up ViewModel events
        _viewModel.FilterChanged += OnFilterChanged;
        _viewModel.VolumeChanged += OnVolumeChanged;
        _viewModel.MuteChanged += OnMuteChanged;
        _viewModel.ShuffleChanged += OnShuffleChanged;
        _viewModel.RepeatModeChanged += OnRepeatModeChanged;
        _viewModel.PlaySelectedRequested += OnPlaySelectedRequested;
        _viewModel.PauseRequested += OnPauseRequested;
        _viewModel.StopRequested += OnStopRequested;
        _viewModel.PreviousRequested += OnPreviousRequested;
        _viewModel.NextRequested += OnNextRequested;
        _viewModel.AddFilesRequested += OnAddFilesRequested;
        _viewModel.ConnectPlexRequested += OnConnectPlexRequested;
        _viewModel.MoveUpRequested += OnMoveUpRequested;
        _viewModel.MoveDownRequested += OnMoveDownRequested;
        _viewModel.RemoveRequested += OnRemoveRequested;
        _viewModel.SeekRequested += OnSeekRequested;
        _viewModel.SidebarSectionChanged += OnSidebarSectionChanged;
        _viewModel.SourceFilterChanged += OnSourceFilterChanged;
        _viewModel.SelectionChanged += OnViewModelSelectionChanged;

        MacMediaSession.Enable(HandleMediaCommand, HandleMediaSeek);
        TrackList.ItemsSource = _entries;
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(500);
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

    private void OnToggleQueueClick(object? sender, EventArgs e) =>
        _viewModel?.ToggleQueueCommand.Execute(null);

    private void OnToggleLyricsClick(object? sender, EventArgs e) =>
        _viewModel?.ToggleLyricsCommand.Execute(null);

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnZoomClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void HandleMediaCommand(int command)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var args = new RoutedEventArgs();
            switch (command)
            {
                case 0:
                    OnPlayClick(this, args);
                    break;
                case 1:
                    OnPauseClick(this, args);
                    break;
                case 2:
                    if (_isPlaying)
                    {
                        OnPauseClick(this, args);
                    }
                    else
                    {
                        OnPlayClick(this, args);
                    }
                    break;
                case 3:
                    OnNextClick(this, args);
                    break;
                case 4:
                    OnPreviousClick(this, args);
                    break;
            }
        });
    }

    private void HandleMediaSeek(double seconds)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_hasActiveTrack)
            {
                return;
            }

            try
            {
                _audioPlayer.Seek(TimeSpan.FromSeconds(seconds));
                UpdatePlaybackProgress();
                UpdateMediaSession(_isPlaying ? 1 : 2);
            }
            catch (InvalidOperationException exception)
            {
                _viewModel!.StatusText = $"Could not seek in this track: {exception.Message}";
            }
        });
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            _plexSettings = await _plexSettingsStore.LoadAsync(_windowCancellation.Token);
            _viewModel!.StatusText = _plexSettings is null
                ? "Open audio files or connect to Plex to add music."
                : "Open audio files or connect to Plex to refresh your music library.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            _viewModel!.StatusText = $"Saved Plex settings unavailable: {exception.Message}";
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
            var clientIdentifier = await _plexSettingsStore.GetOrCreateClientIdentifierAsync(
                _windowCancellation.Token);
            var dialog = new PlexConnectionDialog(
                settings,
                null,
                clientIdentifier,
                _credentialStore);
            var result = await dialog.ShowDialog<PlexConnectionResult?>(this);
            if (result is null)
            {
                return;
            }

            _credentialStore.SetPlexToken(result.Token);
            await _plexSettingsStore.SaveAsync(result.Settings, _windowCancellation.Token);
            _plexSettings = result.Settings;
            _ = CreatePlexClient(result.Settings, result.Token, clientIdentifier);
            AddPlexTracks(result.Tracks);
            RefreshQueue();
            _viewModel!.StatusText = result.Tracks.Count == 0
                ? "Connected Plex library is empty."
                : $"Added {result.Tracks.Count} Plex track(s) to the queue.";
        }
        catch (PlexAuthenticationException exception)
        {
            _viewModel!.StatusText = ClearPlexCredentials(exception);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            PlexConnectionException or
            DllNotFoundException or
            EntryPointNotFoundException)
        {
            _viewModel!.StatusText = $"Could not connect to Plex: {exception.Message}";
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
                    _viewModel!.StatusText = "A selected item is not available as a local file.";
                    continue;
                }

                var fullPath = Path.GetFullPath(path);
                var title = Path.GetFileNameWithoutExtension(fullPath);
                var track = new Track(title, "Unknown artist", new Uri(fullPath));
                _queue.Enqueue(track);
                addedCount++;
            }

            RefreshQueue();
            _viewModel!.StatusText = addedCount == 0
                ? "No local audio files were added."
                : $"Added {addedCount} file(s). Select a track and press Play.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException)
        {
            _viewModel!.StatusText = $"Could not add audio files: {exception.Message}";
        }
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e) =>
        MoveSelected(-1);

    private void OnMoveDownClick(object? sender, RoutedEventArgs e) =>
        MoveSelected(1);

    private void OnPreviousClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.PreviousCommand.CanExecute(null) == true)
        {
            _viewModel.PreviousCommand.Execute(null);
        }
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.NextCommand.CanExecute(null) == true)
        {
            _viewModel.NextCommand.Execute(null);
        }
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedTrack is not QueueTrackViewModel selected)
        {
            _viewModel!.StatusText = "Select a track to play.";
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
        if (_activeEntryId is { } pausedEntryId)
        {
            ReportPlexTimeline(pausedEntryId, "paused");
        }

        UpdateMediaSession(2);
        _viewModel!.StatusText = "Playback paused.";
        UpdatePlaybackControls();
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        if (_activeEntryId is { } stoppedEntryId)
        {
            ReportPlexTimeline(stoppedEntryId, "stopped");
        }

        UpdateMediaSession(0);
        MacMediaSession.ClearArtwork();
        _audioPlayer.Stop();
        _playbackTimer.Stop();
        _isPlaying = false;
        _isPaused = false;
        _hasActiveTrack = false;
        _activeEntryId = null;
        _activeTrackIsRemote = false;
        _viewModel!.ActiveTrack = null;
        _viewModel!.IsPlaying = false;
        _viewModel!.IsPaused = false;
        _viewModel!.HasActiveTrack = false;
        _viewModel!.StatusText = "Playback stopped.";
        UpdatePlaybackProgress();
        UpdatePlaybackControls();
    }

    private void OnProgressPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_hasActiveTrack || _audioPlayer.Duration <= TimeSpan.Zero)
        {
            return;
        }

        var progressTrack = this.FindControl<Grid>("ProgressTrack");
        if (progressTrack == null || progressTrack.Bounds.Width <= 0)
        {
            return;
        }

        var fraction = Math.Clamp(
            e.GetPosition(progressTrack).X / progressTrack.Bounds.Width,
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
            _viewModel!.StatusText = $"Could not seek in this track: {exception.Message}";
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedTrack is not QueueTrackViewModel selected ||
            !_queue.TryRemove(selected.Entry.Id))
        {
            return;
        }

        if (_activeEntryId == selected.Entry.Id)
        {
            OnStopClick(sender, e);
        }

        RefreshQueue();
        _viewModel!.StatusText = $"{_entries.Count} file(s) in queue.";
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TrackList.SelectedItem is QueueTrackViewModel selected)
        {
            _queue.TrySelect(selected.Entry.Id);
            LoadArtwork(selected);
        }

        UpdatePlaybackControls();
    }

    private void MoveSelected(int offset)
    {
        if (_viewModel?.SelectedTrack is not QueueTrackViewModel selected)
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
            QueueTrackViewModel viewModel;
            if (!existing.TryGetValue(entry.Id, out viewModel!))
            {
                viewModel = new QueueTrackViewModel(entry);
            }

            _entryViewModels[entry.Id] = viewModel;
            _entries.Add(viewModel);
        }

        foreach (var entryId in _entryViewModels.Keys
                     .Where(entryId => snapshot.Entries.All(entry => entry.Id != entryId))
                     .ToArray())
        {
            _entryViewModels.Remove(entryId);
        }

        TrackList.SelectedItem = _entries
            .FirstOrDefault(entry => entry.Entry.Id == snapshot.CurrentEntryId);
        if (TrackList.SelectedItem is QueueTrackViewModel selectedViewModel)
        {
            LoadArtwork(selectedViewModel);
        }

        UpdateQueueChrome();
        UpdatePlaybackControls();
    }

    private void SelectQueueEntry(Guid entryId)
    {
        _queue.TrySelect(entryId);
        TrackList.SelectedItem = _entryViewModels.GetValueOrDefault(entryId);
        if (TrackList.SelectedItem is QueueTrackViewModel selectedViewModel)
        {
            LoadArtwork(selectedViewModel);
        }

        UpdateQueueChrome();
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
                if (_hasActiveTrack &&
                    _activeEntryId is { } previousEntryId &&
                    previousEntryId != entry.Id)
                {
                    ReportPlexTimeline(previousEntryId, "stopped");
                }

                _audioPlayer.Play(entry.Track);
                _activeEntryId = entry.Id;
                _activeTrackIsRemote = entry.Track.Source?.Scheme is "http" or "https";
                _streamStartTime = DateTimeOffset.UtcNow;
            }

            _isPlaying = true;
            _isPaused = false;
            _hasActiveTrack = true;
            ReportPlexTimeline(entry.Id, "playing");
            _viewModel!.ActiveTrack = _entryViewModels.GetValueOrDefault(entry.Id);
            _viewModel!.IsPlaying = true;
            _viewModel!.IsPaused = false;
            _viewModel!.HasActiveTrack = true;
            if (_entryViewModels.TryGetValue(entry.Id, out var activeViewModel))
            {
                LoadArtwork(activeViewModel);
            }

            _viewModel!.StatusText = $"Playing {entry.Track.Title}.";
            UpdateMediaSession(1);
            UpdateMediaSessionArtwork();
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
            _viewModel!.StatusText = $"Could not play {entry.Track.Title}: {exception.Message}";
            UpdatePlaybackControls();
        }
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
                UpdateMediaSession(1);
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
            _viewModel!.StatusText = $"Playback failed: {exception.Message}";
            UpdatePlaybackControls();
            return;
        }

        if (_queue.TrySelectNext())
        {
            if (_queue.Snapshot.CurrentEntryId is { } entryId)
            {
                SelectQueueEntry(entryId);
            }

            if (TrackList.SelectedItem is QueueTrackViewModel nextTrack)
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
        _viewModel!.StatusText = "Playback finished.";
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

    // ViewModel Event Handlers
    private void OnFilterChanged(object? sender, EventArgs e)
    {
        // Filter logic would go here
    }

    private void OnVolumeChanged(object? sender, double volume)
    {
        // Volume control would be implemented in audio player
    }

    private void OnMuteChanged(object? sender, bool isMuted)
    {
        // Mute control would be implemented in audio player
    }

    private void OnShuffleChanged(object? sender, bool isShuffle)
    {
        // Shuffle mode would be implemented in queue
    }

    private void OnRepeatModeChanged(object? sender, RepeatMode repeatMode)
    {
        // Repeat mode would be implemented in queue
    }

    private void OnPlaySelectedRequested(object? sender, EventArgs e)
    {
        if (_viewModel?.SelectedTrack is QueueTrackViewModel selected)
        {
            PlayTrack(selected.Entry);
        }
    }

    private void OnPauseRequested(object? sender, EventArgs e)
    {
        OnPauseClick(this, new RoutedEventArgs());
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        OnStopClick(this, new RoutedEventArgs());
    }

    private void OnPreviousRequested(object? sender, EventArgs e)
    {
        OnPreviousClick(this, new RoutedEventArgs());
    }

    private void OnNextRequested(object? sender, EventArgs e)
    {
        OnNextClick(this, new RoutedEventArgs());
    }

    private void OnAddFilesRequested(object? sender, EventArgs e)
    {
        OnAddTracksClick(this, new RoutedEventArgs());
    }

    private void OnConnectPlexRequested(object? sender, EventArgs e)
    {
        OnPlexClick(this, new RoutedEventArgs());
    }

    private void OnMoveUpRequested(object? sender, EventArgs e)
    {
        MoveSelected(-1);
    }

    private void OnMoveDownRequested(object? sender, EventArgs e)
    {
        MoveSelected(1);
    }

    private void OnRemoveRequested(object? sender, EventArgs e)
    {
        OnRemoveClick(this, new RoutedEventArgs());
    }

    private void OnSeekRequested(object? sender, double fraction)
    {
        if (!_hasActiveTrack || _audioPlayer.Duration <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            _audioPlayer.Seek(TimeSpan.FromTicks((long)(_audioPlayer.Duration.Ticks * fraction)));
            UpdatePlaybackProgress();
        }
        catch (InvalidOperationException exception)
        {
            _viewModel!.StatusText = $"Could not seek in this track: {exception.Message}";
        }
    }

    private void OnSidebarSectionChanged(object? sender, SidebarSection section)
    {
        // Update section title
        if (this.FindControl<TextBlock>("SectionTitle") is { } sectionTitle)
        {
            sectionTitle.Text = section switch
            {
                SidebarSection.Queue => "Queue",
                SidebarSection.Tracks => "Tracks",
                SidebarSection.Albums => "Albums",
                SidebarSection.Artists => "Artists",
                _ => "Queue"
            };
        }
    }

    private void OnSourceFilterChanged(object? sender, SourceFilter filter)
    {
        // Filter logic would go here
    }

    private void OnViewModelSelectionChanged(object? sender, EventArgs e)
    {
        UpdatePlaybackControls();
    }

    private void UpdatePlaybackControls()
    {
        var selectedViewModel = TrackList.SelectedItem as QueueTrackViewModel;
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

        _viewModel!.UpdateQueueCount(entries.Count);
        _viewModel!.UpdateSelectedTrackCapabilities();

        // Update command states
        ((RelayCommand)_viewModel.PlaySelectedCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.PauseCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.StopCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.PreviousCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.NextCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.MoveDownCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.RemoveCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.ShuffleCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.RepeatCommand).RaiseCanExecuteChanged();
        ((RelayCommand)_viewModel.ToggleQueueCommand).RaiseCanExecuteChanged();
    }

    private void UpdatePlaybackProgress()
    {
        var duration = _audioPlayer.Duration;
        if (duration <= TimeSpan.Zero)
        {
            _viewModel!.ElapsedTime = TimeSpan.Zero;
            _viewModel!.Duration = TimeSpan.Zero;
            return;
        }

        var position = _audioPlayer.CurrentTime;
        _viewModel!.ElapsedTime = position;
        _viewModel!.Duration = duration;
    }

    private void UpdateQueueChrome()
    {
        if (this.FindControl<StackPanel>("EmptyStatePanel") is { } emptyPanel)
        {
            emptyPanel.IsVisible = _entries.Count == 0;
        }
        TrackList.IsVisible = _entries.Count > 0;
        _viewModel!.UpdateQueueCount(_entries.Count);
    }

    private PlexClient CreatePlexClient(
        PlexServerSettings settings,
        string token,
        string clientIdentifier)
    {
        var client = new PlexClient(settings, token, clientIdentifier);
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
                    UpdateMediaSessionArtwork();
                }
            });
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (PlexAuthenticationException exception)
        {
            var message = ClearPlexCredentials(exception);
            await Dispatcher.UIThread.InvokeAsync(() => _viewModel!.StatusText = message);
        }
        catch (PlexConnectionException exception)
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => _viewModel!.StatusText = $"Could not load Plex artwork: {exception.Message}");
        }
    }

    private void UpdateMediaSessionArtwork()
    {
        if (_activeEntryId is null ||
            !_entryViewModels.TryGetValue(_activeEntryId.Value, out var viewModel) ||
            viewModel.Artwork is not { } artwork)
        {
            MacMediaSession.ClearArtwork();
            return;
        }

        using var stream = new MemoryStream();
        artwork.Save(stream, PngBitmapEncoderOptions.Default);
        MacMediaSession.SetArtwork(stream.ToArray());
    }

    private void UpdateMediaSession(int state)
    {
        if (_activeEntryId is null ||
            !_entryViewModels.TryGetValue(_activeEntryId.Value, out var viewModel))
        {
            return;
        }

        var track = viewModel.Entry.Track;
        var duration = (track.Duration ?? _audioPlayer.Duration).TotalSeconds;
        MacMediaSession.UpdateNowPlaying(
            track.Title,
            track.Artist,
            track.Album,
            duration,
            _audioPlayer.CurrentTime.TotalSeconds,
            state);
    }

    private void ReportPlexTimeline(Guid entryId, string state)
    {
        if (_plexClient is null ||
            !_entryViewModels.TryGetValue(entryId, out var viewModel) ||
            string.IsNullOrWhiteSpace(viewModel.Entry.Track.PlexRatingKey))
        {
            return;
        }

        var duration = viewModel.Entry.Track.Duration ?? _audioPlayer.Duration;
        _ = ReportPlexTimelineAsync(
            _plexClient,
            viewModel.Entry.Track.PlexRatingKey,
            state,
            _audioPlayer.CurrentTime,
            duration);
    }

    private async Task ReportPlexTimelineAsync(
        PlexClient client,
        string ratingKey,
        string state,
        TimeSpan position,
        TimeSpan duration)
    {
        try
        {
            await client.ReportTimelineAsync(
                ratingKey,
                state,
                position,
                duration,
                _windowCancellation.Token);
        }
        catch (PlexAuthenticationException exception)
        {
            var message = ClearPlexCredentials(exception);
            await Dispatcher.UIThread.InvokeAsync(() => _viewModel!.StatusText = message);
        }
        catch (PlexConnectionException)
        {
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private string ClearPlexCredentials(PlexAuthenticationException exception)
    {
        try
        {
            _credentialStore.DeletePlexToken();
            return $"{exception.Message} Sign in to Plex again.";
        }
        catch (InvalidOperationException deletionException)
        {
            return $"{exception.Message} Could not clear the saved Plex sign-in: {deletionException.Message}";
        }
    }
}
