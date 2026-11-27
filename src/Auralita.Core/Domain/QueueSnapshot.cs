namespace Auralita.Core.Domain;

public sealed record QueueSnapshot(
    IReadOnlyList<QueueEntry> Entries,
    Guid? CurrentEntryId);
