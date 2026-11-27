using System.Diagnostics;
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
    private readonly string _clientIdentifier;
    private readonly IPlexCredentialStore _credentialStore;
    private readonly PlexServerSettings? _savedSettings;
    private readonly string? _savedToken;
    private readonly ListBox _libraries = new()
    {
        MinHeight = 150,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0)
    };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 24
    };
    private readonly Button _signInButton = new()
    {
        Content = "Sign in with Plex",
        Classes = { "accent" }
    };
    private readonly Button _loadButton = new()
    {
        Content = "Load selected library",
        IsEnabled = false
    };
    private readonly Button _cancelButton = new()
    {
        Content = "Cancel",
        Classes = { "quiet" }
    };
    private readonly List<PlexLibrarySection> _sections = [];
    private PlexClient? _client;
    private CancellationTokenSource? _operationCancellation;
    private string? _connectedToken;
    private bool _isBusy;
    private bool _isSigningIn;

    public PlexConnectionDialog(
        PlexServerSettings? settings,
        string? savedToken,
        string clientIdentifier,
        IPlexCredentialStore credentialStore)
    {
        _savedSettings = settings;
        _savedToken = savedToken;
        _clientIdentifier = clientIdentifier;
        _credentialStore = credentialStore;
        Title = "Connect to Plex";
        Width = 560;
        Height = 600;
        MinWidth = 460;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _signInButton.Click += async (_, _) => await SignInAsync();
        _loadButton.Click += async (_, _) => await LoadSelectedLibraryAsync();
        _cancelButton.Click += (_, _) => Close();
        Opened += OnOpened;
        Closed += (_, _) =>
        {
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _client?.Dispose();
        };
        Content = CreateContent();
    }

    private Control CreateContent()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(26),
            Spacing = 14
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Connect to Plex",
            FontSize = 24,
            FontWeight = FontWeight.Bold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Sign in securely with your Plex account. A browser window will open to approve access.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(_signInButton);
        panel.Children.Add(new TextBlock
        {
            Text = "Music library",
            Classes = { "eyebrow" }
        });
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#17151B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#403A48")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Child = _libraries
        });
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _cancelButton, _loadButton }
        });
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2B2731")),
            BorderBrush = new SolidColorBrush(Color.Parse("#403A48")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            Child = _status
        });

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#17151B")),
            Child = panel
        };
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_savedToken))
        {
            await FindLibrariesWithTokenAsync(_savedToken);
        }
    }

    private async Task SignInAsync()
    {
        if (_isSigningIn)
        {
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation = new CancellationTokenSource();
        var cancellationToken = _operationCancellation.Token;
        _isSigningIn = true;
        SetBusy(true, "Requesting a secure Plex sign-in code…");
        try
        {
            using var authenticationClient = new PlexAuthenticationClient(_clientIdentifier);
            var pin = await authenticationClient.RequestPinAsync(cancellationToken);
            var authorizationUri = authenticationClient.CreateAuthorizationUri(pin);
            using var browser = Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri)
            {
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("Could not open the Plex sign-in page.");
            SetBusy(true, "Approve Auralita in the Plex browser window…");
            var token = await authenticationClient.PollForAuthenticationTokenAsync(
                pin,
                cancellationToken: cancellationToken);
            _credentialStore.SetPlexToken(token);
            _isSigningIn = false;
            SetBusy(false);
            await FindLibrariesWithTokenAsync(token, cancellationToken);
        }
        catch (PlexAuthenticationException exception)
        {
            _connectedToken = null;
            _status.Text = ClearSavedToken(exception);
        }
        catch (PlexConnectionException exception)
        {
            _status.Text = exception.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _status.Text = "Plex sign-in was cancelled.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                           System.ComponentModel.Win32Exception or
                                           IOException or
                                           UnauthorizedAccessException)
        {
            _status.Text = $"Could not complete Plex sign-in: {exception.Message}";
        }
        finally
        {
            _isSigningIn = false;
            SetBusy(false);
        }
    }

    private async Task FindLibrariesWithTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            _operationCancellation?.Dispose();
            _operationCancellation = new CancellationTokenSource();
        }
        else if (_operationCancellation is null)
        {
            _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        cancellationToken = _operationCancellation.Token;
        SetBusy(true, "Finding Plex servers and music libraries…");
        try
        {
            _client?.Dispose();
            _sections.Clear();
            _libraries.ItemsSource = null;
            _loadButton.IsEnabled = false;
            var initialSettings = _savedSettings is null
                ? new PlexServerSettings(new Uri("https://app.plex.tv"), string.Empty)
                : _savedSettings with { LibrarySectionKey = string.Empty };
            _client = new PlexClient(initialSettings, token, _clientIdentifier);
            _connectedToken = token;
            _sections.AddRange(await _client.GetMusicLibrariesAsync(cancellationToken));
            _libraries.ItemsSource = _sections.Select(section => $"{section.Title}  [{section.Key}]").ToArray();
            _libraries.SelectedIndex = _sections.Count == 1 ? 0 : -1;
            _loadButton.IsEnabled = _sections.Count > 0;
            _status.Text = _sections.Count == 0
                ? "Connected, but no music libraries were found on the available Plex servers."
                : _sections.Count == 1
                    ? "Music library found. Load it to browse tracks."
                    : $"Found {_sections.Count} music libraries. Select one to continue.";
        }
        catch (PlexAuthenticationException exception)
        {
            _connectedToken = null;
            _status.Text = ClearSavedToken(exception);
        }
        catch (PlexConnectionException exception)
        {
            _status.Text = exception.Message;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Plex discovery was cancelled.";
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
            _libraries.SelectedIndex >= _sections.Count ||
            string.IsNullOrWhiteSpace(_connectedToken))
        {
            _status.Text = "Sign in to Plex and select a music library first.";
            return;
        }

        SetBusy(true, "Loading tracks…");
        var cancellationToken = _operationCancellation?.Token ?? CancellationToken.None;
        try
        {
            var section = _sections[_libraries.SelectedIndex];
            var tracks = await _client.GetTracksAsync(section.Key, cancellationToken);
            var serverUrl = _client.ResolvedServerUri
                ?? throw new PlexConnectionException("Plex did not resolve an accessible server.");
            var settings = new PlexServerSettings(serverUrl, section.Key);
            var result = new PlexConnectionResult(settings, _connectedToken, tracks);
            _status.Text = tracks.Count == 0
                ? "The selected music library is empty."
                : $"Loaded {tracks.Count} track(s) from {section.Title}.";
            Close(result);
        }
        catch (PlexAuthenticationException exception)
        {
            _connectedToken = null;
            _status.Text = ClearSavedToken(exception);
        }
        catch (PlexConnectionException exception)
        {
            _status.Text = exception.Message;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Loading the Plex library was cancelled.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _isBusy = busy || _isSigningIn;
        _signInButton.IsEnabled = !_isSigningIn;
        _loadButton.IsEnabled =
            !_isBusy &&
            _sections.Count > 0 &&
            !string.IsNullOrWhiteSpace(_connectedToken);
        _cancelButton.IsEnabled = true;
        if (status is not null)
        {
            _status.Text = status;
        }
    }

    private string ClearSavedToken(PlexAuthenticationException exception)
    {
        try
        {
            _credentialStore.DeletePlexToken();
            return $"{exception.Message} Sign in with Plex again.";
        }
        catch (InvalidOperationException deletionException)
        {
            return $"{exception.Message} Could not clear the saved Plex sign-in: {deletionException.Message}";
        }
    }
}
