using System.Text.Json;
using System.Text.Json.Serialization;
using Auralita.Core.Plex;

namespace Auralita.App;

internal sealed class PlexSettingsStore
{
    private readonly SemaphoreSlim _clientIdentifierLock = new(1, 1);
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Auralita",
        "plex-settings.json");
    private readonly string _clientIdentifierPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Auralita",
        "plex-client-id.txt");

    public async Task<string> GetOrCreateClientIdentifierAsync(
        CancellationToken cancellationToken = default)
    {
        await _clientIdentifierLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_clientIdentifierPath))
            {
                var existing = (await File.ReadAllTextAsync(_clientIdentifierPath, cancellationToken)).Trim();
                if (Guid.TryParseExact(existing, "D", out var identifier))
                {
                    return identifier.ToString("D");
                }

                throw new IOException("The saved Plex client identifier is invalid.");
            }

            var directory = Path.GetDirectoryName(_clientIdentifierPath)
                ?? throw new InvalidOperationException("The Plex settings directory could not be determined.");
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var newIdentifier = Guid.NewGuid().ToString("D");
            var temporaryPath = $"{_clientIdentifierPath}.new";
            await File.WriteAllTextAsync(temporaryPath, newIdentifier, cancellationToken);
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, _clientIdentifierPath, overwrite: false);
            return newIdentifier;
        }
        finally
        {
            _clientIdentifierLock.Release();
        }
    }

    public async Task<PlexServerSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<PlexServerSettings>(
                stream,
                PlexSettingsJsonContext.Default.PlexServerSettings,
                cancellationToken);
            if (settings is not null &&
                (settings.ServerUrl is null || string.IsNullOrWhiteSpace(settings.LibrarySectionKey)))
            {
                throw new IOException("Saved Plex settings are incomplete. Reconfigure the Plex connection.");
            }

            return settings;
        }
        catch (JsonException exception)
        {
            throw new IOException("Saved Plex settings are invalid. Reconfigure the Plex connection.", exception);
        }
    }

    public async Task SaveAsync(
        PlexServerSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The Plex settings directory could not be determined.");
        Directory.CreateDirectory(directory);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var temporaryPath = $"{_settingsPath}.new";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                PlexSettingsJsonContext.Default.PlexServerSettings,
                cancellationToken);
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                temporaryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PlexServerSettings))]
internal partial class PlexSettingsJsonContext : JsonSerializerContext;
