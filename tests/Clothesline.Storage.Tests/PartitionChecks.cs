using Clothesline.Core;
using Clothesline.Storage;

internal static class PartitionChecks
{
    internal static void Run(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "partition-library");
        using (var store = new LibraryStore(root))
        {
            for (var i = 0; i < 15; i++)
            {
                var image = Path.Combine(root, "image-" + i + (i == 14 ? ".mp4" : ".png")); File.WriteAllText(image, "fixture");
                store.Add(image, Ownership.External, "fixture", "media-" + i, i, 25, id: "media-" + i);
                var text = Path.Combine(store.ManagedFolder, "text-" + i + ".txt"); File.WriteAllText(text, "Note " + i);
                store.Add(text, Ownership.Managed, LibraryStore.TextSource, "text-" + i, i, 25, id: "text-" + i);
            }
            var shelf = store.Entries(EntryState.Shelf);
            check(shelf.Count == 25 && shelf.Count(e => MediaFiles.IsText(e.Path)) == 13, "odd shared capacity uses every slot without half quotas");
            check(store.Entries(EntryState.History).Count == 5 && store.Entries().All(e => File.Exists(e.Path)), "shared overflow archives oldest arrivals without deleting files");
            check(shelf.Take(12).All(e => !MediaFiles.IsText(e.Path)) && shelf.Skip(12).All(e => MediaFiles.IsText(e.Path)), "library shelf orders media left and text right");
            check(shelf.Any(e => MediaFiles.IsVideo(e.Path)), "video participates in the same shared total capacity");
            check(store.MoveShelfItems(["media-3"], "media-14") && store.MoveShelfItems(["text-3"], null), "each zone supports independent persistent ordering");
            check(!store.MoveShelfItems(["media-3"], "text-3") && !store.MoveShelfItems(["media-3", "text-3"], null), "storage rejects cross-zone sorting and mixed moving groups");
            var textOrder = store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path)).Select(e => e.Id).ToArray();
            store.Restore("media-0", 25, 1000);
            check(store.Find("text-2")!.State == EntryState.History && store.Find("media-0")!.State == EntryState.Shelf, "media restore may evict older text under shared total quota");
            check(store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path)).Select(e => e.Id).SequenceEqual(textOrder.Where(id => id != "text-2")), "overflow retains relative order of remaining text cards");
            foreach (var e in store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path))) store.Archive(e.Id, 2000);
            store.Restore("media-1", 25, 2001); store.Restore("media-2", 25, 2002);
            check(store.Entries(EntryState.Shelf).Count == 15, "media can use more than half when text is absent");
            for (var i = 15; i < 25; i++)
            {
                var image = Path.Combine(root, "image-" + i + ".png"); File.WriteAllText(image, "fixture");
                store.Add(image, Ownership.External, "fixture", "media-" + i, 2100 + i, 25, id: "media-" + i);
            }
            check(store.Entries(EntryState.Shelf).Count == 25 && store.Entries(EntryState.Shelf).All(e => !MediaFiles.IsText(e.Path)), "single media category can fill all 25 slots");
            store.EnforceCapacity(6, 3000);
            check(store.Entries(EntryState.Shelf).Count == 6 && store.Entries().All(e => File.Exists(e.Path)), "shrinking shared capacity retains all original files");
        }
        using var reopened = new LibraryStore(root);
        check(reopened.Entries(EntryState.Shelf).Count == 6 && reopened.Entries(EntryState.History).Count == 34, "shared shelf and history survive restart");
    }
}
