using System.Text.Json;
using System.Text.Json.Serialization;
using Auralita.Core.Plex;

namespace Auralita.App;

internal sealed class PlexSettingsStore
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Auralita",
        "plex-settings.json");

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
