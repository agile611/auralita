using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Auralita.Core.Plex;

namespace Auralita.App;

internal sealed class PlexArtworkCache : IDisposable
{
    private const int MaxConcurrentDownloads = 4;
    private readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> _artwork = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _downloadSlots = new(MaxConcurrentDownloads);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private volatile bool _disposed;

    public Task<Bitmap?> GetAsync(
        PlexClient client,
        Uri artworkUri,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(artworkUri);
        return _artwork.GetOrAdd(
            artworkUri.AbsoluteUri,
            _ => new Lazy<Task<Bitmap?>>(
                () => LoadAsync(client, artworkUri, _disposeCancellation.Token),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCancellation.Cancel();
        foreach (var lazyTask in _artwork.Values)
        {
            if (!lazyTask.IsValueCreated)
            {
                continue;
            }

            var task = lazyTask.Value;
            if (task.IsCompletedSuccessfully)
            {
                task.Result?.Dispose();
            }
        }

        _disposeCancellation.Dispose();
    }

    private async Task<Bitmap?> LoadAsync(
        PlexClient client,
        Uri artworkUri,
        CancellationToken cancellationToken)
    {
        await _downloadSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = await client.GetArtworkAsync(artworkUri, cancellationToken).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new Bitmap(stream);
            if (_disposed)
            {
                bitmap.Dispose();
                return null;
            }

            return bitmap;
        }
        catch (Exception exception) when (
            exception is PlexConnectionException or
            HttpRequestException or
            IOException or
            ArgumentException or
            OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (!_disposed)
            {
                _downloadSlots.Release();
            }
        }
    }
}
