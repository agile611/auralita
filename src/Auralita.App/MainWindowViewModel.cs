using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Auralita.Core.Domain;
using Auralita.Core.Playback;

namespace Auralita.App;

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _filterText = string.Empty;
    private double _volume = 0.8;
    private bool _isMuted;
    private bool _isShuffle;
    private RepeatMode _repeatMode = RepeatMode.Off;
    private bool _isLyricsVisible;
    private bool _isQueueVisible = true;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _hasActiveTrack;
    private bool _plexConnected;
    private QueueTrackViewModel? _activeTrack;
    private QueueTrackViewModel? _selectedTrack;
    private TimeSpan _elapsedTime;
    private TimeSpan _duration;
    private string _statusText = "Ready when you are";
    private Color _ambientPrimaryColor = Color.Parse("#3D2B5A");
    private Color _ambientSecondaryColor = Color.Parse("#2A1F42");
    private SidebarSection _activeSidebarSection = SidebarSection.Queue;
    private SourceFilter _activeSourceFilter = SourceFilter.All;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value;
            OnPropertyChanged();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(_volume - clamped) < 0.001) return;
            _volume = clamped;
            OnPropertyChanged();
            VolumeChanged?.Invoke(this, _volume);
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            OnPropertyChanged();
            MuteChanged?.Invoke(this, _isMuted);
        }
    }

    public bool IsShuffle
    {
        get => _isShuffle;
        set
        {
            if (_isShuffle == value) return;
            _isShuffle = value;
            OnPropertyChanged();
            ShuffleChanged?.Invoke(this, _isShuffle);
        }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode == value) return;
            _repeatMode = value;
            OnPropertyChanged();
            RepeatModeChanged?.Invoke(this, _repeatMode);
        }
    }

    public bool IsLyricsVisible
    {
        get => _isLyricsVisible;
        set
        {
            if (_isLyricsVisible == value) return;
            _isLyricsVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsQueueVisible
    {
        get => _isQueueVisible;
        set
        {
            if (_isQueueVisible == value) return;
            _isQueueVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotPlaying));
        }
    }

    public bool IsNotPlaying => !_isPlaying;

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused == value) return;
            _isPaused = value;
            OnPropertyChanged();
        }
    }

    public bool HasActiveTrack
    {
        get => _hasActiveTrack;
        set
        {
            if (_hasActiveTrack == value) return;
            _hasActiveTrack = value;
            OnPropertyChanged();
        }
    }

    public bool PlexConnected
    {
        get => _plexConnected;
        set
        {
            _plexConnected = value;
            OnPropertyChanged();
        }
    }

    public QueueTrackViewModel? ActiveTrack
    {
        get => _activeTrack;
        set
        {
            if (_activeTrack == value) return;
            _activeTrack = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveTrackTitle));
            OnPropertyChanged(nameof(ActiveTrackArtist));
            OnPropertyChanged(nameof(ActiveTrackAlbum));
            OnPropertyChanged(nameof(ActiveTrackArtwork));
            OnPropertyChanged(nameof(ActiveTrackQuality));
        }
    }

    public string ActiveTrackTitle => _activeTrack?.Title ?? "Nothing playing";
    public string ActiveTrackArtist => _activeTrack?.Artist ?? string.Empty;
    public string ActiveTrackAlbum => _activeTrack?.Album ?? string.Empty;
    public Bitmap? ActiveTrackArtwork => _activeTrack?.Artwork;
    public string ActiveTrackQuality => _activeTrack?.QualityLabel ?? string.Empty;

    public QueueTrackViewModel? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (_selectedTrack == value) return;
            _selectedTrack = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPlaySelected));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanNavigatePrevious));
            OnPropertyChanged(nameof(CanNavigateNext));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TimeSpan ElapsedTime
    {
        get => _elapsedTime;
        set
        {
            if (_elapsedTime == value) return;
            _elapsedTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ElapsedTimeText));
            OnPropertyChanged(nameof(ProgressFraction));
        }
    }

    public string ElapsedTimeText => FormatTime(_elapsedTime);

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (_duration == value) return;
            _duration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(RemainingTimeText));
            OnPropertyChanged(nameof(ProgressFraction));
        }
    }

    public string DurationText => _duration > TimeSpan.Zero ? FormatTime(_duration) : "--:--";

    public string RemainingTimeText => _duration > TimeSpan.Zero
        ? $"-{FormatTime(_duration - _elapsedTime)}"
        : "--:--";

    public double ProgressFraction => _duration > TimeSpan.Zero
        ? Math.Clamp(_elapsedTime.TotalSeconds / _duration.TotalSeconds, 0, 1)
        : 0;

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public Color AmbientPrimaryColor
    {
        get => _ambientPrimaryColor;
        set
        {
            if (_ambientPrimaryColor == value) return;
            _ambientPrimaryColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AmbientPrimaryBrush));
        }
    }

    public SolidColorBrush AmbientPrimaryBrush => new SolidColorBrush(_ambientPrimaryColor);

    public Color AmbientSecondaryColor
    {
        get => _ambientSecondaryColor;
        set
        {
            if (_ambientSecondaryColor == value) return;
            _ambientSecondaryColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AmbientSecondaryBrush));
        }
    }

    public SolidColorBrush AmbientSecondaryBrush => new SolidColorBrush(_ambientSecondaryColor);

    public SidebarSection ActiveSidebarSection
    {
        get => _activeSidebarSection;
        set
        {
            if (_activeSidebarSection == value) return;
            _activeSidebarSection = value;
            OnPropertyChanged();
            SidebarSectionChanged?.Invoke(this, _activeSidebarSection);
        }
    }

    public SourceFilter ActiveSourceFilter
    {
        get => _activeSourceFilter;
        set
        {
            if (_activeSourceFilter == value) return;
            _activeSourceFilter = value;
            OnPropertyChanged();
            SourceFilterChanged?.Invoke(this, _activeSourceFilter);
        }
    }

    public bool CanPlaySelected => _selectedTrack != null;
    public bool CanMoveUp => _selectedTrack != null && GetSelectedIndex() > 0;
    public bool CanMoveDown => _selectedTrack != null && GetSelectedIndex() >= 0 && GetSelectedIndex() < QueueCount - 1;
    public bool CanRemove => _selectedTrack != null;
    public bool CanNavigatePrevious => _selectedTrack != null && GetSelectedIndex() > 0;
    public bool CanNavigateNext => _selectedTrack != null && GetSelectedIndex() >= 0 && GetSelectedIndex() < QueueCount - 1;
    public int QueueCount { get; private set; }

    // Commands
    public ICommand TogglePlayPauseCommand { get; }
    public ICommand PlaySelectedCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand ShuffleCommand { get; }
    public ICommand RepeatCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand ToggleLyricsCommand { get; }
    public ICommand ToggleQueueCommand { get; }
    public ICommand AddFilesCommand { get; }
    public ICommand ConnectPlexCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand SeekCommand { get; }
    public ICommand SidebarSectionCommand { get; }
    public ICommand SourceFilterCommand { get; }

    // Events for code-behind to handle
    public event EventHandler? FilterChanged;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<bool>? MuteChanged;
    public event EventHandler<bool>? ShuffleChanged;
    public event EventHandler<RepeatMode>? RepeatModeChanged;
    public event EventHandler? PlaySelectedRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? AddFilesRequested;
    public event EventHandler? ConnectPlexRequested;
    public event EventHandler? MoveUpRequested;
    public event EventHandler? MoveDownRequested;
    public event EventHandler? RemoveRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler<SidebarSection>? SidebarSectionChanged;
    public event EventHandler<SourceFilter>? SourceFilterChanged;
    public event EventHandler? SelectionChanged;

    public MainWindowViewModel()
    {
        TogglePlayPauseCommand = new RelayCommand(_ => OnTogglePlayPause());
        PlaySelectedCommand = new RelayCommand(_ => OnPlaySelected(), _ => CanPlaySelected);
        PauseCommand = new RelayCommand(_ => OnPause(), _ => IsPlaying);
        StopCommand = new RelayCommand(_ => OnStop(), _ => HasActiveTrack);
        PreviousCommand = new RelayCommand(_ => OnPrevious(), _ => CanNavigatePrevious);
        NextCommand = new RelayCommand(_ => OnNext(), _ => CanNavigateNext);
        ShuffleCommand = new RelayCommand(_ => IsShuffle = !IsShuffle);
        RepeatCommand = new RelayCommand(_ => CycleRepeatMode());
        MuteCommand = new RelayCommand(_ => IsMuted = !IsMuted);
        ToggleLyricsCommand = new RelayCommand(_ => IsLyricsVisible = !IsLyricsVisible);
        ToggleQueueCommand = new RelayCommand(_ => IsQueueVisible = !IsQueueVisible);
        AddFilesCommand = new RelayCommand(_ => AddFilesRequested?.Invoke(this, EventArgs.Empty));
        ConnectPlexCommand = new RelayCommand(_ => ConnectPlexRequested?.Invoke(this, EventArgs.Empty));
        MoveUpCommand = new RelayCommand(_ => MoveUpRequested?.Invoke(this, EventArgs.Empty), _ => CanMoveUp);
        MoveDownCommand = new RelayCommand(_ => MoveDownRequested?.Invoke(this, EventArgs.Empty), _ => CanMoveDown);
        RemoveCommand = new RelayCommand(_ => RemoveRequested?.Invoke(this, EventArgs.Empty), _ => CanRemove);
        SeekCommand = new RelayCommand<double>(fraction => SeekRequested?.Invoke(this, fraction));
        SidebarSectionCommand = new RelayCommand<SidebarSection>(s => ActiveSidebarSection = s);
        SourceFilterCommand = new RelayCommand<SourceFilter>(f => ActiveSourceFilter = f);
    }

    private void OnTogglePlayPause()
    {
        if (IsPlaying)
        {
            PauseRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (HasActiveTrack && IsPaused)
        {
            PlaySelectedRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (SelectedTrack != null)
        {
            PlaySelectedRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPlaySelected() => PlaySelectedRequested?.Invoke(this, EventArgs.Empty);
    private void OnPause() => PauseRequested?.Invoke(this, EventArgs.Empty);
    private void OnStop() => StopRequested?.Invoke(this, EventArgs.Empty);
    private void OnPrevious() => PreviousRequested?.Invoke(this, EventArgs.Empty);
    private void OnNext() => NextRequested?.Invoke(this, EventArgs.Empty);

    private void CycleRepeatMode()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
    }

    public void UpdateQueueCount(int count)
    {
        QueueCount = count;
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanNavigateNext));
    }

    public void UpdateSelectedTrackCapabilities()
    {
        OnPropertyChanged(nameof(CanPlaySelected));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanNavigatePrevious));
        OnPropertyChanged(nameof(CanNavigateNext));
    }

    private int GetSelectedIndex()
    {
        // This would need to be implemented based on actual queue state
        // For now return -1 to indicate not found
        return -1;
    }

    public void SetAmbientColors(Color primary, Color secondary)
    {
        AmbientPrimaryColor = primary;
        AmbientSecondaryColor = secondary;
    }

    public void ResetAmbientColors()
    {
        AmbientPrimaryColor = Color.Parse("#3D2B5A");
        AmbientSecondaryColor = Color.Parse("#2A1F42");
    }

    private static string FormatTime(TimeSpan time)
    {
        var totalSeconds = Math.Max(0, (int)time.TotalSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal enum SidebarSection
{
    Queue,
    Tracks,
    Albums,
    Artists
}

internal enum SourceFilter
{
    All,
    Plex,
    Local
}

internal enum RepeatMode
{
    Off,
    All,
    One
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T)parameter!) ?? true;

    public void Execute(object? parameter) => _execute((T)parameter!);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
