namespace Clothesline.Storage;

public sealed partial class LibraryStore
{
    /// Persist display order separately from arrival time, deduplication and overflow policy.
    public bool MoveShelfItems(IReadOnlyList<string> ids, string? beforeId)
    {
        lock (gate)
        {
            var moving = ids.Distinct().ToArray();
            var current = Entries(EntryState.Shelf).Select(e => e.Id).ToList();
            if (moving.Length == 0 || moving.Any(id => !current.Contains(id)) || (beforeId is not null && !current.Contains(beforeId))) return false;
            if (beforeId is not null && moving.Contains(beforeId))
                beforeId = current.Skip(current.IndexOf(beforeId)).FirstOrDefault(id => !moving.Contains(id));
            var ordered = current.Where(id => !moving.Contains(id)).ToList();
            ordered.InsertRange(beforeId is null ? ordered.Count : ordered.IndexOf(beforeId), moving);
            if (current.SequenceEqual(ordered)) return true;
            using var transaction = db.BeginTransaction();
            Execute("DELETE FROM ShelfOrder");
            for (var index = 0; index < ordered.Count; index++)
                Execute("INSERT INTO ShelfOrder(EntryId,Position) VALUES($id,$position)", "$id", ordered[index], "$position", index);
            transaction.Commit(); return true;
        }
    }
}
