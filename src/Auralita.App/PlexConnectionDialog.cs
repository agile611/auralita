using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Auralita.Core.Domain;
using Auralita.Core.Plex;

namespace Auralita.App;

internal sealed record PlexConnectionResult(
    PlexServerSettings Settings,
    string Token,
    IReadOnlyList<Track> Tracks);

internal sealed class PlexConnectionDialog : Window
{
    private readonly TextBox _serverUrl = new()
    {
        PlaceholderText = "https://app.plex.tv or http://plex-host:32400",
        MinHeight = 38,
        Padding = new Thickness(10, 7)
    };
    private readonly TextBox _token = new()
    {
        PlaceholderText = "Plex authentication token",
        PasswordChar = '•',
        MinHeight = 38,
        Padding = new Thickness(10, 7)
    };
    private readonly ListBox _libraries = new()
    {
        MinHeight = 130,
        Background = new SolidColorBrush(Color.Parse("#141414")),
        BorderBrush = new SolidColorBrush(Color.Parse("#565656")),
        BorderThickness = new Thickness(1)
    };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 24
    };
    private readonly Button _connectButton = new()
    {
        Content = "FIND MUSIC LIBRARIES",
        Classes = { "accent" }
    };
    private readonly Button _loadButton = new()
    {
        Content = "LOAD SELECTED LIBRARY",
        IsEnabled = false
    };
    private readonly Button _cancelButton = new()
    {
        Content = "CANCEL"
    };
    private readonly List<PlexLibrarySection> _sections = [];
    private PlexClient? _client;
    private string? _connectedToken;
    private bool _isBusy;

    public PlexConnectionDialog(PlexServerSettings? settings, string? savedToken)
    {
        Title = "Connect to Plex";
        Width = 560;
        Height = 580;
        MinWidth = 480;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _serverUrl.Text = settings?.ServerUrl.AbsoluteUri ?? "https://app.plex.tv";
        _token.Text = savedToken ?? string.Empty;
        _connectButton.Click += async (_, _) => await FindLibrariesAsync();
        _loadButton.Click += async (_, _) => await LoadSelectedLibraryAsync();
        _cancelButton.Click += (_, _) => Close();
        Closed += (_, _) => _client?.Dispose();
        Content = CreateContent();
    }

    private Control CreateContent()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12
        };
        panel.Children.Add(new TextBlock
        {
            Text = "PLEX MUSIC",
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#B8EC60"))
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Connect to your server, then choose a music library.",
            Opacity = 0.75
        });
        panel.Children.Add(new TextBlock { Text = "SERVER URL", FontSize = 11 });
        panel.Children.Add(_serverUrl);
        panel.Children.Add(new TextBlock { Text = "PLEX TOKEN", FontSize = 11 });
        panel.Children.Add(_token);
        panel.Children.Add(_connectButton);
        panel.Children.Add(new TextBlock { Text = "MUSIC LIBRARY", FontSize = 11 });
        panel.Children.Add(_libraries);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _cancelButton, _loadButton }
        });
        panel.Children.Add(_status);

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#202020")),
            Child = panel
        };
    }

    private async Task FindLibrariesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!Uri.TryCreate(_serverUrl.Text?.Trim(), UriKind.Absolute, out var serverUrl) ||
            serverUrl.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(serverUrl.UserInfo))
        {
            _status.Text = "Enter an HTTP or HTTPS Plex server URL without embedded credentials.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_token.Text))
        {
            _status.Text = "Enter a Plex authentication token.";
            return;
        }

        SetBusy(true, "Connecting to Plex…");
        try
        {
            _client?.Dispose();
            _sections.Clear();
            _libraries.ItemsSource = null;
            _loadButton.IsEnabled = false;
            _client = new PlexClient(
                new PlexServerSettings(serverUrl, string.Empty),
                _token.Text);
            _connectedToken = _token.Text;
            _sections.AddRange(await _client.GetMusicLibrariesAsync());
            _libraries.ItemsSource = _sections.Select(section => $"{section.Title}  [{section.Key}]").ToArray();
            _libraries.SelectedIndex = _sections.Count == 1 ? 0 : -1;
            _loadButton.IsEnabled = _sections.Count > 0;
            _status.Text = _sections.Count == 0
                ? "Connected, but no music libraries were found on this Plex server."
                : _sections.Count == 1
                    ? "Music library found. Load it to browse tracks."
                    : $"Found {_sections.Count} music libraries. Select one to continue.";
        }
        catch (PlexConnectionException exception)
        {
            _status.Text = exception.Message;
            _loadButton.IsEnabled = false;
        }
        catch (HttpRequestException exception)
        {
            _status.Text = $"Could not connect to Plex: {exception.Message}";
            _loadButton.IsEnabled = false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadSelectedLibraryAsync()
    {
        if (_isBusy || _client is null ||
            _libraries.SelectedIndex < 0 ||
            _libraries.SelectedIndex >= _sections.Count)
        {
            _status.Text = "Connect to Plex and select a music library first.";
            return;
        }

        if (!Uri.TryCreate(_serverUrl.Text?.Trim(), UriKind.Absolute, out var serverUrl) ||
            serverUrl != _client.Settings.ServerUrl ||
            !string.Equals(_token.Text, _connectedToken, StringComparison.Ordinal))
        {
            _status.Text = "The connection details changed. Find music libraries again before loading.";
            _loadButton.IsEnabled = false;
            return;
        }

        SetBusy(true, "Loading tracks…");
        try
        {
            var section = _sections[_libraries.SelectedIndex];
            var tracks = await _client.GetTracksAsync(section.Key);
            var settings = new PlexServerSettings(serverUrl, section.Key);
            var result = new PlexConnectionResult(settings, _connectedToken!, tracks);
            _status.Text = tracks.Count == 0
                ? "The selected music library is empty."
                : $"Loaded {tracks.Count} track(s) from {section.Title}.";
            Close(result);
        }
        catch (PlexConnectionException exception)
        {
            _status.Text = exception.Message;
        }
        catch (HttpRequestException exception)
        {
            _status.Text = $"Could not load the Plex library: {exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _isBusy = busy;
        _connectButton.IsEnabled = !busy;
        _loadButton.IsEnabled = !busy && _sections.Count > 0;
        _cancelButton.IsEnabled = !busy;
        if (status is not null)
        {
            _status.Text = status;
        }
    }
}
