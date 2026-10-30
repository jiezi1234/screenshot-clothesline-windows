using System.Security.Cryptography;
using Clothesline.Storage;
using Microsoft.Data.Sqlite;

internal static class ImageEditChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "image-edit");
        const long limit = 1024 * 1024;
        var source = Path.Combine(root, "untouched.png"); File.WriteAllBytes(source, [1, 2, 3]);
        string copy;
        using (var store = new LibraryStore(directory))
        {
            var original = Path.Combine(store.ManagedFolder, "original.png"); File.Copy(source, original);
            store.Add(original, Ownership.Managed, LibraryStore.OriginalSource, "original", 1, 20, EntryState.History, "original");
            store.Add(source, Ownership.External, "fixture", "old", 2, 20, id: "card");
            store.Add(source, Ownership.External, "fixture", "other", 3, 20, id: "neighbor");
            store.MoveShelfItems(["card"], "neighbor");
            var order = string.Join(",", store.Entries(EntryState.Shelf).Select(e => e.Id));
            var digest = LibraryStore.FileDigest(source);
            var edited = store.SaveImageEdit("card", [4, 5, 6], "edited", digest, "original", limit, 10); copy = edited.Path;
            check(edited.Id == "card" && edited.Ownership == Ownership.Managed && edited.Source == LibraryStore.ImageEditSource && edited.Hash == "edited", "image edit updates the same card to a managed copy");
            check(edited.AddedAt == 2 && string.Join(",", store.Entries(EntryState.Shelf).Select(e => e.Id)) == order, "image autosave preserves arrival time, order, and quota");
            check(File.ReadAllBytes(source).SequenceEqual(new byte[] { 1, 2, 3 }) && File.ReadAllBytes(original).SequenceEqual(new byte[] { 1, 2, 3 }), "external source and recovery original remain byte-identical");
            check(store.Entries(EntryState.History).Any(e => e.Path == source && e.Ownership == Ownership.External), "source remains discoverable in history");
            check(store.OriginalFor("card")?.Id == "original", "edited card links to preserved original");
            var count = store.Entries().Count;
            edited = store.SaveImageEdit("card", [7, 8, 9], "second", LibraryStore.FileDigest(copy), "original", limit, 11);
            check(edited.Path == copy && store.Entries().Count == count && File.ReadAllBytes(copy).SequenceEqual(new byte[] { 7, 8, 9 }), "later autosaves reuse the managed file without accumulating cards or histories");
            bool Rejected(Action action) { try { action(); return false; } catch (IOException) { return true; } }
            check(Rejected(() => store.SaveImageEdit("card", [9], "bad", digest, "original", limit, 12)), "stale save is rejected");
            check(Rejected(() => store.SaveImageEdit("card", [9], "bad", LibraryStore.FileDigest(copy), "missing", limit, 12)), "missing original prevents replacement");
            check(Rejected(() => store.SaveImageEdit("card", [9], "bad", LibraryStore.FileDigest(copy), "original", 1, 12)), "storage limit preserves existing edit");
            check(File.ReadAllBytes(copy).SequenceEqual(new byte[] { 7, 8, 9 }), "failed saves preserve latest good bytes");
            var managed = Path.Combine(store.ManagedFolder, "clipboard.png"); File.WriteAllBytes(managed, [11, 12]);
            store.Add(managed, Ownership.Managed, "Clipboard", "clip", 4, 20, id: "clipboard");
            store.SaveImageEdit("clipboard", [13, 14], "clip-edit", LibraryStore.FileDigest(managed), "original", limit, 13);
            check(File.Exists(managed) && store.Entries(EntryState.History).Any(e => e.Path == managed), "managed clipboard source stays tracked in history");
        }
        using (var store = new LibraryStore(directory))
            check(store.Find("card")?.Path == copy && store.Find("card")?.Hash == "second", "saved image survives library reopen");
        foreach (var moved in new[] { false, true })
        {
            var currentDigest = LibraryStore.FileDigest(copy); var bytes = new byte[] { (byte)(moved ? 23 : 21), 22 };
            var stage = copy + ".edit-fixture.part"; File.WriteAllBytes(stage, bytes);
            using (var db = new SqliteConnection($"Data Source={Path.Combine(directory, "library.db")};Pooling=False"))
            {
                db.Open(); using var command = db.CreateCommand();
                command.CommandText = "INSERT INTO ImageEdits VALUES('card',$stage,$dest,$old,$new,$hash,'original',20)";
                command.Parameters.AddWithValue("$stage", stage); command.Parameters.AddWithValue("$dest", copy);
                command.Parameters.AddWithValue("$old", currentDigest); command.Parameters.AddWithValue("$new", Convert.ToHexString(SHA256.HashData(bytes)));
                command.Parameters.AddWithValue("$hash", "recovered-" + moved); command.ExecuteNonQuery();
            }
            if (moved) File.Move(stage, copy, true);
            using var recovered = new LibraryStore(directory);
            check(recovered.Find("card")?.Hash == "recovered-" + moved && File.ReadAllBytes(copy).SequenceEqual(bytes), "interrupted image save recovers " + (moved ? "after" : "before") + " atomic file replacement");
        }
        var v4 = Path.Combine(root, "image-v4");
        using (var store = new LibraryStore(v4)) store.Add(source, Ownership.External, "fixture", "hash", 1, 20, id: "existing");
        using (var db = new SqliteConnection($"Data Source={Path.Combine(v4, "library.db")};Pooling=False"))
        { db.Open(); using var command = db.CreateCommand(); command.CommandText = "DROP TABLE ImageEdits; PRAGMA user_version=4"; command.ExecuteNonQuery(); }
        using (var store = new LibraryStore(v4))
            check(store.Find("existing")?.Path == source && Directory.GetFiles(v4, "library-v4-backup-*.db").Length == 1, "v4 upgrade backs up database and preserves cards");
        foreach (var phase in new[] { "staged", "moved", "conflict" })
        {
            var firstRoot = Path.Combine(root, "first-image-" + phase);
            var firstSource = Path.Combine(root, "first-source-" + phase + ".png"); File.WriteAllBytes(firstSource, [31, 32]);
            string destination;
            using (var store = new LibraryStore(firstRoot))
            {
                var original = Path.Combine(store.ManagedFolder, "original.png"); File.Copy(firstSource, original);
                store.Add(original, Ownership.Managed, LibraryStore.OriginalSource, "original", 1, 20, EntryState.History, "original");
                store.Add(firstSource, Ownership.External, "fixture", "old", 2, 20, id: "first");
                destination = Path.Combine(store.ManagedFolder, "image-edit-first.png");
            }
            var stage = destination + ".edit-fixture.part"; File.WriteAllBytes(stage, [33, 34]);
            using (var db = new SqliteConnection($"Data Source={Path.Combine(firstRoot, "library.db")};Pooling=False"))
            {
                db.Open(); using var command = db.CreateCommand();
                command.CommandText = "INSERT INTO ImageEdits VALUES('first',$stage,$dest,$old,$new,'new-pixels','original',20)";
                command.Parameters.AddWithValue("$stage", stage); command.Parameters.AddWithValue("$dest", destination);
                command.Parameters.AddWithValue("$old", LibraryStore.FileDigest(firstSource)); command.Parameters.AddWithValue("$new", LibraryStore.FileDigest(stage)); command.ExecuteNonQuery();
            }
            if (phase == "moved") File.Move(stage, destination);
            if (phase == "conflict") File.WriteAllBytes(firstSource, [41, 42]);
            using var recovered = new LibraryStore(firstRoot);
            if (phase == "conflict")
                check(recovered.Find("first")!.Path == firstSource && recovered.Find("first")!.Error.Length > 0 && File.Exists(stage) && File.ReadAllBytes(firstSource).SequenceEqual(new byte[] { 41, 42 }), "interrupted first-save conflict preserves changed source and staged edit");
            else
                check(recovered.Find("first")!.Path == destination && recovered.OriginalFor("first")!.Id == "original" && recovered.Entries(EntryState.History).Any(e => e.Path == firstSource) && File.ReadAllBytes(firstSource).SequenceEqual(new byte[] { 31, 32 }), "first save recovers source history, copy and original link from " + phase + " phase");
        }
    }
}
