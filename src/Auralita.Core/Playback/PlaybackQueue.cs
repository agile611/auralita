using Auralita.Core.Domain;

namespace Auralita.Core.Playback;

public sealed class PlaybackQueue
{
    private readonly object _gate = new();
    private readonly List<QueueEntry> _entries = [];
    private Guid? _currentEntryId;

    public QueueSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new QueueSnapshot(
                    Array.AsReadOnly(_entries.ToArray()),
                    _currentEntryId);
            }
        }
    }

    public QueueEntry Enqueue(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        lock (_gate)
        {
            var entry = new QueueEntry(Guid.NewGuid(), track);
            _entries.Add(entry);
            _currentEntryId ??= entry.Id;
            return entry;
        }
    }

    public bool TrySelect(Guid entryId)
    {
        lock (_gate)
        {
            if (_entries.All(entry => entry.Id != entryId))
            {
                return false;
            }

            _currentEntryId = entryId;
            return true;
        }
    }

    public bool TrySelectNext()
    {
        lock (_gate)
        {
            var currentIndex = FindCurrentIndex();
            if (currentIndex < 0 || currentIndex + 1 >= _entries.Count)
            {
                return false;
            }

            _currentEntryId = _entries[currentIndex + 1].Id;
            return true;
        }
    }

    public bool TrySelectPrevious()
    {
        lock (_gate)
        {
            var currentIndex = FindCurrentIndex();
            if (currentIndex <= 0)
            {
                return false;
            }

            _currentEntryId = _entries[currentIndex - 1].Id;
            return true;
        }
    }

    public bool TryMove(Guid entryId, int destinationIndex)
    {
        lock (_gate)
        {
            if (destinationIndex < 0 || destinationIndex >= _entries.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            }

            var sourceIndex = _entries.FindIndex(entry => entry.Id == entryId);
            if (sourceIndex < 0)
            {
                return false;
            }

            var entry = _entries[sourceIndex];
            _entries.RemoveAt(sourceIndex);
            _entries.Insert(destinationIndex, entry);
            return true;
        }
    }

    public bool TryRemove(Guid entryId)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(entry => entry.Id == entryId);
            if (index < 0)
            {
                return false;
            }

            var wasCurrent = _currentEntryId == entryId;
            _entries.RemoveAt(index);

            if (wasCurrent)
            {
                _currentEntryId = _entries.Count == 0
                    ? null
                    : _entries[Math.Min(index, _entries.Count - 1)].Id;
            }

            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _currentEntryId = null;
        }
    }

    private int FindCurrentIndex() =>
        _currentEntryId is Guid currentEntryId
            ? _entries.FindIndex(entry => entry.Id == currentEntryId)
            : -1;
}
