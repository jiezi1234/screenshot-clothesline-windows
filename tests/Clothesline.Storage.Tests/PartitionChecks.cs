using Clothesline.Core;
using Clothesline.Storage;

internal static class PartitionChecks
{
    internal static async Task Run(string parent, Action<bool, string> check)
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
            check(shelf.Count == 24 && shelf.Count(e => MediaFiles.IsText(e.Path)) == 12, "odd screen capacity gives both categories the same rounded-down quota");
            check(store.Entries(EntryState.History).Count == 6 && store.Entries().All(e => File.Exists(e.Path)), "separate overflow archives oldest arrivals without deleting files");
            check(shelf.Select(e => e.Id).SequenceEqual(Enumerable.Range(3, 12).Reverse().SelectMany(i => new[] { "text-" + i, "media-" + i })), "new items use arrival order without grouping by type");
            check(shelf.Any(e => MediaFiles.IsVideo(e.Path)), "video shares the image quota");
            check(store.MoveShelfItems(["media-3"], "media-14") && store.MoveShelfItems(["text-3"], null), "any retained item can move to another position or the row end");
            check(store.MoveShelfItems(["media-3"], "text-3") && store.MoveShelfItems(["media-3", "text-3"], "text-14"), "storage accepts cross-type sorting and mixed moving groups");
            var ordered = store.Entries(EntryState.Shelf).Select(e => e.Id).ToArray();
            check(ordered.Take(3).SequenceEqual(new[] { "media-3", "text-3", "text-14" }), "mixed group retains requested order before a text anchor");
            using (var snapshot = new LibraryStore(root)) check(snapshot.Entries(EntryState.Shelf).Select(e => e.Id).SequenceEqual(ordered), "mixed positions survive reopening the database");
            var textOrder = store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path)).Select(e => e.Id).ToArray();
            store.Restore("media-0", 25, 1000);
            check(store.Find("media-3")!.State == EntryState.History && store.Find("media-0")!.State == EntryState.Shelf, "restoring an image evicts the oldest image even when reordered");
            check(store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path)).Select(e => e.Id).SequenceEqual(textOrder), "image restore leaves text membership and order intact");
            foreach (var e in store.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path))) store.Archive(e.Id, 2000);
            store.Restore("media-1", 25, 2001); store.Restore("media-2", 25, 2002);
            check(store.Entries(EntryState.Shelf).Count == 12, "empty text category does not increase image quota");
            for (var i = 15; i < 25; i++)
            {
                var image = Path.Combine(root, "image-" + i + ".png"); File.WriteAllText(image, "fixture");
                store.Add(image, Ownership.External, "fixture", "media-" + i, 2100 + i, 25, id: "media-" + i);
            }
            check(store.Entries(EntryState.Shelf).Count == 12 && store.Entries(EntryState.Shelf).All(e => !MediaFiles.IsText(e.Path)), "single category remains capped at half the screen slots");
            store.EnforceCapacity(6, 3000);
            check(store.Entries(EntryState.Shelf).Count == 3 && store.Entries().All(e => File.Exists(e.Path)), "smaller screen reapplies the per-category quota and preserves files");
            store.EnforceCapacity(100, 3001);
            check(store.Entries(EntryState.Shelf).Count == 3, "larger screen does not automatically restore archived content");
        }
        using var reopened = new LibraryStore(root);
        check(reopened.Entries(EntryState.Shelf).Count == 3 && reopened.Entries(EntryState.History).Count == 37, "separate shelf quotas and history survive restart");
        using var example = new LibraryStore(Path.Combine(parent, "twenty-slot-example"));
        using var engine = new CollectionEngine(example, _ => "fixture", () => 20);
        check(await engine.ImportClipboard([1, 2, 3], "retained-image", 1024 * 1024), "twenty-slot example collects the first image");
        var photo = example.Entries(EntryState.Shelf).Single();
        for (var i = 0; i < 10; i++) check(await engine.ImportText("Copied text " + i, 1024 * 1024), "twenty-slot example collects note " + i);
        var oldest = example.Entries(EntryState.Shelf).Last(e => MediaFiles.IsText(e.Path));
        example.MoveShelfItems([oldest.Id], example.Entries(EntryState.Shelf).First(e => MediaFiles.IsText(e.Path)).Id);
        await engine.ImportText("Copied text 10", 1024 * 1024);
        check(example.Entries(EntryState.Shelf).Count == 11 && example.Entries(EntryState.Shelf).Count(e => MediaFiles.IsText(e.Path)) == 10,
            "one image plus ten notes stays at eleven cards after another text copy");
        check(example.Find(photo.Id)!.State == EntryState.Shelf && File.Exists(photo.Path), "new text never evicts the older image");
        check(TextContent.Read(example.Entries(EntryState.Shelf).First().Path) == "Copied text 10", "new text arrives at the global front after custom mixed ordering");
        check(example.Find(oldest.Id)!.State == EntryState.History && TextContent.Read(oldest.Path) == "Copied text 0", "eleventh note archives first copied note regardless of visual order");
        var remaining = example.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path)).Select(e => e.Id).ToArray();
        for (var i = 0; i < 10; i++) await engine.ImportClipboard([1, 2, 3], "new-image-" + i, 1024 * 1024);
        check(example.Find(photo.Id)!.State == EntryState.History && example.Entries(EntryState.Shelf).Count == 20,
            "eleventh image replaces the first image within its own quota");
        check(example.Entries(EntryState.Shelf).Where(e => MediaFiles.IsText(e.Path)).Select(e => e.Id).SequenceEqual(remaining), "image overflow never changes text membership or order");
        example.Restore(oldest.Id, 20, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000);
        check(example.Find(oldest.Id)!.State == EntryState.Shelf && example.Entries(EntryState.Shelf).Count(e => !MediaFiles.IsText(e.Path)) == 10,
            "restoring archived text consumes only a text slot");
        example.EnforceCapacity(8, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        check(example.Entries(EntryState.Shelf).Count == 8 && example.Entries(EntryState.Shelf).Count(e => MediaFiles.IsText(e.Path)) == 4
            && example.Entries().All(e => File.Exists(e.Path)), "screen shrink independently keeps newest four per category with every file intact");
        var upper = Path.Combine(example.ManagedFolder, "uppercase.TXT"); File.WriteAllText(upper, "Uppercase note");
        example.Add(upper, Ownership.Managed, LibraryStore.TextSource, "upper", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2000, 8);
        check(example.Entries(EntryState.Shelf).Count(e => MediaFiles.IsText(e.Path)) == 4 && example.Entries(EntryState.Shelf).Count(e => !MediaFiles.IsText(e.Path)) == 4,
            "uppercase text extension uses the same quota as lowercase");
    }
}
