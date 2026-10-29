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
            check(shelf.Count == 24 && shelf.Count(e => MediaFiles.IsText(e.Path)) == 12, "odd total capacity reserves equal rounded-down quotas");
            check(store.Entries(EntryState.History).Count == 6 && store.Entries().All(e => File.Exists(e.Path)), "each group spills only its own oldest records without deleting files");
            check(shelf.Take(12).All(e => !MediaFiles.IsText(e.Path)) && shelf.Skip(12).All(e => MediaFiles.IsText(e.Path)), "library shelf orders media left and text right");
            check(shelf.Any(e => MediaFiles.IsVideo(e.Path)), "video shares media quota instead of consuming text slots");
            check(store.MoveShelfItems(["media-3"], "media-14") && store.MoveShelfItems(["text-3"], null), "each zone supports independent persistent ordering");
            check(!store.MoveShelfItems(["media-3"], "text-3") && !store.MoveShelfItems(["media-3", "text-3"], null), "storage rejects cross-zone sorting and mixed moving groups");
            var textOrder = store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path)).Select(e => e.Id).ToArray();
            store.Restore("media-0", 25, 1000);
            check(store.Find("media-3")!.State == EntryState.History && store.Find("media-0")!.State == EntryState.Shelf, "restore evicts oldest media arrival regardless of custom order");
            check(store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path)).Select(e => e.Id).SequenceEqual(textOrder), "media restore leaves text order and quota untouched");
            foreach (var e in store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path))) store.Archive(e.Id, 2000);
            store.Restore("media-1", 25, 2001); store.Restore("media-2", 25, 2002);
            check(store.Entries(EntryState.Shelf).Count == 12, "empty text side cannot lend capacity to media");
            store.EnforceCapacity(6, 3000);
            check(store.Entries(EntryState.Shelf).Count == 3 && store.Entries().All(e => File.Exists(e.Path)), "shrinking total capacity respects half quota and retains all files");
        }
        using var reopened = new LibraryStore(root);
        check(reopened.Entries(EntryState.Shelf).Count == 3 && reopened.Entries(EntryState.History).Count == 27, "partitioned shelf and history survive restart");
    }
}
