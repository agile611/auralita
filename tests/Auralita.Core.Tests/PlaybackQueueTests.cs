using Auralita.Core.Domain;
using Auralita.Core.Playback;
using Xunit;

namespace Auralita.Core.Tests;

public sealed class PlaybackQueueTests
{
    [Fact]
    public void EnqueueSelectsFirstEntryAndKeepsItsIdentity()
    {
        var queue = new PlaybackQueue();

        var first = queue.Enqueue(CreateTrack("First"));
        queue.Enqueue(CreateTrack("Second"));

        Assert.Equal(first.Id, queue.Snapshot.CurrentEntryId);
        Assert.Equal(2, queue.Snapshot.Entries.Count);
    }

    [Fact]
    public void MovePreservesCurrentEntry()
    {
        var queue = new PlaybackQueue();
        var first = queue.Enqueue(CreateTrack("First"));
        var second = queue.Enqueue(CreateTrack("Second"));
        var third = queue.Enqueue(CreateTrack("Third"));

        Assert.True(queue.TryMove(third.Id, 0));

        var snapshot = queue.Snapshot;
        Assert.Equal(third.Id, snapshot.Entries[0].Id);
        Assert.Equal(first.Id, snapshot.CurrentEntryId);
        Assert.Equal(second.Id, snapshot.Entries[2].Id);
    }

    [Fact]
    public void RemovingCurrentEntrySelectsTheNextEntry()
    {
        var queue = new PlaybackQueue();
        var first = queue.Enqueue(CreateTrack("First"));
        var second = queue.Enqueue(CreateTrack("Second"));
        queue.Enqueue(CreateTrack("Third"));

        Assert.True(queue.TryRemove(first.Id));

        Assert.Equal(second.Id, queue.Snapshot.CurrentEntryId);
    }

    [Fact]
    public void RemovingLastCurrentEntrySelectsPreviousEntry()
    {
        var queue = new PlaybackQueue();
        queue.Enqueue(CreateTrack("First"));
        var second = queue.Enqueue(CreateTrack("Second"));
        Assert.True(queue.TrySelect(second.Id));

        Assert.True(queue.TryRemove(second.Id));

        Assert.Equal(queue.Snapshot.Entries[0].Id, queue.Snapshot.CurrentEntryId);
    }

    [Fact]
    public void NavigationStopsAtQueueBoundaries()
    {
        var queue = new PlaybackQueue();
        var first = queue.Enqueue(CreateTrack("First"));
        var second = queue.Enqueue(CreateTrack("Second"));

        Assert.False(queue.TrySelectPrevious());
        Assert.True(queue.TrySelectNext());
        Assert.Equal(second.Id, queue.Snapshot.CurrentEntryId);
        Assert.False(queue.TrySelectNext());
        Assert.True(queue.TrySelect(first.Id));
        Assert.False(queue.TrySelectPrevious());
    }

    [Fact]
    public void MoveRejectsAnIndexOutsideTheQueue()
    {
        var queue = new PlaybackQueue();
        var entry = queue.Enqueue(CreateTrack("First"));

        Assert.Throws<ArgumentOutOfRangeException>(() => queue.TryMove(entry.Id, 1));
    }

    [Fact]
    public void TrackRequiresTitleAndArtist()
    {
        Assert.Throws<ArgumentException>(() => new Track(" ", "Artist"));
        Assert.Throws<ArgumentException>(() => new Track("Title", ""));
    }

    private static Track CreateTrack(string title) => new(title, "Test Artist");
}
