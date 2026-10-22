using System.Security.Cryptography;
using Clothesline.Storage;
using Microsoft.Data.Sqlite;

var count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
var root = Path.GetFullPath(Path.Combine("artifacts", "storage-tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
string Make(string name, string contents = "image") { var path = Path.Combine(root, name); File.WriteAllText(path, contents); return path; }
var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var day = (long)TimeSpan.FromDays(1).TotalMilliseconds;
var external = Make("external.png");
string restoreId;
using (var store = new LibraryStore(Path.Combine(root, "library")))
{
    Check(store.LoadSettings().RetentionDays == 0 && !store.LoadSettings().ClipboardEnabled, "conservative initial settings");
    store.SaveSettings(new LibrarySettings { Capacity = 25, RetentionDays = 7, ExtraFolders = [root] });
    for (var i = 0; i < 100; i++) store.Add(Make($"image-{i}.png", i.ToString()), Ownership.External, "test", i.ToString(), now + i, 3);
    Check(store.Entries(EntryState.Shelf).Count == 3 && store.Entries(EntryState.History).Count == 97, "100 captures spill into history without loss");
    Check(Directory.GetFiles(root, "image-*.png").Length == 100, "capacity does not delete external files");
    var oldest = store.Entries(EntryState.History).Last(); restoreId = oldest.Id;
    Check(store.Restore(oldest.Id, 3, now + 1000) && store.Find(oldest.Id)!.ArchivedAt is null, "restore clears history timer and enforces capacity");
    Check(store.Entries(EntryState.Shelf).Count == 3, "restore maintains shelf limit");
    var cleanCalls = 0;
    var removed = store.Cleanup(7, now + 10 * day, _ => { cleanCalls++; return true; });
    Check(removed == 97 && cleanCalls == 0 && Directory.GetFiles(root, "image-*.png").Length == 100, "expired external history never invokes deletion");
    var managedPath = Path.Combine(store.ManagedFolder, "managed.png"); File.WriteAllText(managedPath, "managed");
    var managed = store.Add(managedPath, Ownership.Managed, "Clipboard", "managed", now, 100);
    store.Archive(managed.Id, now);
    Check(!store.Recycle(managed.Id, _ => throw new IOException("test recycle failure")) && store.Find(managed.Id)!.Error.Length > 0 && File.Exists(managedPath), "recycle failure preserves record and file");
    store.Cleanup(7, now + 10 * day, _ => throw new Exception("must not retry failed deletion"));
    Check(store.Find(managed.Id) is not null, "background cleanup leaves failed records for manual review");
    Check(store.Recycle(managed.Id, path => { File.Move(path, path + ".test-recycled"); return true; }) && store.Find(managed.Id) is null, "successful recycle commits record removal");
    var item = store.Add(external, Ownership.External, "Folder:A", "same", now, 100);
    Check(store.IsDuplicate(external, "same", "Folder:A", now + 999999), "same-path repeated events deduplicate");
    Check(store.IsDuplicate("other.png", "same", "Clipboard", now + 3000), "short cross-source duplicate detected");
    Check(!store.IsDuplicate("other.png", "same", "Clipboard", now + 6000), "dedup window expires");
    Check(!store.IsDuplicate("another.png", "same", "Folder:A", now + 100), "distinct same-source files are not globally discarded");
    File.Move(external, external + ".moved"); store.RefreshMissing();
    Check(store.Find(item.Id)!.Missing && !store.Restore(item.Id, 3, now), "missing external file is marked rather than forgotten");
    var recovered = Path.Combine(store.ManagedFolder, "pending.png"); File.WriteAllText(recovered, "complete");
    store.Add(recovered, Ownership.Managed, "Clipboard", "pending", now, 3, EntryState.Pending, "pending-complete");
    var partial = Path.Combine(store.ManagedFolder, "partial.png"); File.WriteAllText(partial + ".part", "partial");
    store.Add(partial, Ownership.Managed, "Clipboard", "partial", now, 3, EntryState.Pending, "pending-partial");
    var kept = Path.Combine(store.ManagedFolder, "interrupted-recycle.png"); File.WriteAllText(kept, "keep me");
    store.Add(kept, Ownership.Managed, "Clipboard", "recycle-keep", now, 100, id: "recycle-kept");
    store.Add(Path.Combine(store.ManagedFolder, "already-recycled.png"), Ownership.Managed, "Clipboard", "recycle-gone", now, 100, id: "recycle-gone");
    using var faultConnection = new SqliteConnection($"Data Source={Path.Combine(store.Root, "library.db")}"); faultConnection.Open();
    using var fault = faultConnection.CreateCommand(); fault.CommandText = "INSERT INTO Operations VALUES('recycle-kept','recycle'),('recycle-gone','recycle')"; fault.ExecuteNonQuery();
}
using (var store = new LibraryStore(Path.Combine(root, "library")))
{
    Check(store.LoadSettings().Capacity == 25 && store.LoadSettings().ExtraFolders.Single() == root, "settings survive restart");
    Check(store.Find(restoreId) is not null, "shelf state survives restart");
    Check(store.Find("pending-complete")!.State == EntryState.History && !store.Find("pending-complete")!.Missing, "completed orphan write recovered into history");
    Check(store.Find("pending-partial")!.Path.EndsWith(".part") && store.Find("pending-partial")!.Error.Length > 0, "interrupted partial write retained for inspection");
    Check(store.Find("recycle-kept")!.Error.Length > 0 && !store.Find("recycle-kept")!.Missing, "interrupted recycle preserves existing source for manual review");
    Check(store.Find("recycle-gone") is null, "completed recycle journal reconciles missing source");
}
Check(ClipboardPolicy.Accept(["PNG"], _ => null), "pure PNG accepted");
foreach (var blocked in new[] { "UnicodeText", "Text", "FileDrop", "HTML Format", "Rich Text Format", "ScreenshotClothesline.OwnCopy", "ExcludeClipboardContentFromMonitorProcessing", "ClipboardViewerIgnore" })
    Check(!ClipboardPolicy.Accept(["PNG", blocked], _ => null), "clipboard excludes " + blocked);
Check(!ClipboardPolicy.Accept(["PNG"], name => name == "CanIncludeInClipboardHistory" ? false : null), "clipboard respects privacy DWORD");
Check(!ClipboardPolicy.Accept(["Text"], _ => null), "text-only clipboard rejected");
Check(!ClipboardPolicy.Accept(["PNG"], name => name == "CanUploadToCloudClipboard" ? false : null), "clipboard respects no-cloud flag conservatively");
Check(ClipboardPolicy.Accept(["PNG"], _ => true), "explicit allowed flags accept image");

var watched = Path.Combine(root, "watched"); Directory.CreateDirectory(watched); File.WriteAllText(Path.Combine(watched, "old.png"), "old");
using (var store = new LibraryStore(Path.Combine(root, "watch-library")))
using (var engine = new CollectionEngine(store, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), () => 3))
{
    var settings = new LibrarySettings { ScreenshotFolder = watched };
    engine.Configure(settings); await Task.Delay(400);
    Check(store.Entries().Count == 0, "initial directory scan does not import old files");
    for (var i = 0; i < 100; i++) File.WriteAllText(Path.Combine(watched, $"new-{i}.png"), "capture-" + i);
    var deadline = DateTime.UtcNow.AddSeconds(45);
    while (store.Entries().Count < 100 && DateTime.UtcNow < deadline) await Task.Delay(200);
    Check(store.Entries().Count == 100, "watcher ingests 100 burst file events");
    engine.RequestRescan(); await Task.Delay(500);
    Check(store.Entries().Count == 100, "rescan and repeated events do not duplicate");
    engine.Configure(settings with { Paused = true }); File.WriteAllText(Path.Combine(watched, "paused.png"), "paused"); await Task.Delay(400);
    Check(store.Entries().Count == 100, "pause stops collection");
    engine.Configure(settings); deadline = DateTime.UtcNow.AddSeconds(5);
    while (store.Entries().Count < 101 && DateTime.UtcNow < deadline) await Task.Delay(100);
    Check(store.Entries().Count == 101, "resume reconciles pending new files");
    var stableHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("paused")));
    Check(!await engine.ImportClipboard([1, 2, 3], stableHash, 1024 * 1024), "file and clipboard duplicates merge");
    Check(await engine.ImportClipboard([1, 2, 3], "clipboard-unique", 1024 * 1024), "managed import atomically commits file and record");
    Check(store.Entries().Any(e => e.Source == "Clipboard" && File.Exists(e.Path)) && !Directory.GetFiles(store.ManagedFolder, "*.part").Any(), "managed image has no unfinished part after success");
    Check(!await engine.ImportClipboard([1, 2, 3], "over-budget", 1), "storage ceiling refuses new managed content");
}
using (var store = new LibraryStore(Path.Combine(root, "editor-library")))
{
    var paths = Enumerable.Range(0, 3).Select(i => Make($"batch-{i}.png")).ToArray();
    var entries = paths.Select((p, i) => store.Add(p, Ownership.External, "test", "batch-" + i, now + i, 100)).ToArray();
    var changed = store.ArchiveMany([entries[0].Id, entries[0].Id, entries[1].Id, "unknown"], now + 100);
    Check(changed == 2 && store.Entries(EntryState.Shelf).Single().Id == entries[2].Id && paths.All(File.Exists), "batch archive deduplicates IDs and retains original files");
    Check(store.ArchiveMany([entries[0].Id], now + 900) == 0 && store.Find(entries[0].Id)!.ArchivedAt == now + 100, "repeated batch archive preserves retention start");
    var managedPath = Path.Combine(store.ManagedFolder, "in-editor.png"); File.WriteAllText(managedPath, "protected");
    var managed = store.Add(managedPath, Ownership.Managed, "Clipboard", "protected", now, 100); store.Archive(managed.Id, now);
    var calls = 0;
    store.Cleanup(7, now + 10 * day, _ => { calls++; return false; }, e => e.Id == managed.Id || e.Id == entries[0].Id);
    Check(calls == 0 && store.Find(managed.Id) is not null && File.Exists(managedPath) && store.Find(entries[0].Id) is not null && store.Find(entries[1].Id) is null, "active editor protects managed file and external record while unrelated history expires");
    store.Cleanup(7, now + 10 * day, p => { calls++; File.Move(p, p + ".test-recycled"); return true; });
    Check(calls == 1 && store.Find(managed.Id) is null && store.Find(entries[0].Id) is null, "released editor lease permits normal retention cleanup");
    var copy = Make("edited-copy.png", "copy");
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copy)));
    store.Add(Make("copy-original.png", "copy"), Ownership.External, "different-folder", hash, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100);
    using var engine = new CollectionEngine(store, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), () => 100);
    await engine.ImportExternal(copy, preserveCopy: true); await engine.ImportExternal(copy, preserveCopy: true);
    Check(store.Entries().Count(e => e.Hash == hash) == 2 && store.Entries().Count(e => e.Path == copy) == 1, "explicit edited copy survives cross-source dedup but repeats do not create extra cards");
}
var versions = Path.Combine(root, "original-versions"); string originalId, firstCopyId, secondCopyId, originalPath;
using (var store = new LibraryStore(versions))
using (var engine = new CollectionEngine(store, _ => "fixture", () => 25))
{
    var original = await engine.PreserveOriginal([1, 2, 3], "root-hash", 1024 * 1024); originalId = original.Id; originalPath = original.Path;
    Check(original.State == EntryState.History && original.Source == LibraryStore.OriginalSource && File.Exists(originalPath) && store.Entries(EntryState.Shelf).Count == 0, "original snapshot commits atomically to history without occupying shelf");
    Check(!store.IsDuplicate("new-capture.png", "root-hash", "Folder:New", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), "original backup cannot suppress a new external capture");
    Check(await engine.ImportClipboard([1, 2, 3], "root-hash", 1024 * 1024), "original backup cannot suppress a new clipboard capture");
    var first = store.Add(Make("derived-one.png"), Ownership.External, "Folder", "one", now + day, 25); firstCopyId = first.Id;
    var second = store.Add(Make("derived-two.png"), Ownership.External, "Folder", "two", now + day, 25); secondCopyId = second.Id;
    store.BindOriginal(first.Id, original.Id); store.BindOriginal(second.Id, original.Id); store.BindOriginal(first.Id, original.Id);
    Check(store.OriginalFor(first.Id)!.Id == store.OriginalFor(second.Id)!.Id && store.HasOriginalDependents(original.Id), "multiple generations share one original snapshot");
    var recycleCalls = 0;
    Check(!store.Recycle(original.Id, _ => { recycleCalls++; return true; }) && recycleCalls == 0, "referenced original refuses explicit recycle before filesystem side effect");
    store.Cleanup(7, now + 20 * day, _ => { recycleCalls++; return false; });
    Check(File.Exists(originalPath) && recycleCalls == 0, "referenced original does not expire with time");
    var full = false; try { await engine.PreserveOriginal([4, 5], "quota", 3); } catch (IOException) { full = true; }
    Check(full && store.Entries().Count(e => e.Source == LibraryStore.OriginalSource) == 1, "original snapshots share managed quota and rejection adds no orphan record");
    Check(!await engine.ImportClipboard([8, 9], "clip-budget", 4), "clipboard quota includes retained originals");
    store.SaveSettings(new LibrarySettings { Shortcut = "Ctrl+Shift+F8", RunAtLogin = true });
}
using (var store = new LibraryStore(versions))
{
    Check(store.OriginalFor(firstCopyId)!.Id == originalId && store.OriginalFor(secondCopyId)!.Id == originalId, "original lineage survives restart");
    Check(store.LoadSettings().Shortcut == "Ctrl+Shift+F8" && store.LoadSettings().RunAtLogin, "desktop preferences survive restart");
    store.ForgetExternalOrMissing(firstCopyId, now + 25 * day);
    Check(store.HasOriginalDependents(originalId), "removing one sibling retains original for remaining sibling");
    store.Archive(secondCopyId, now - 8 * day);
    store.CompleteOriginal(originalId, now - 20 * day);
    var calls = 0; store.Cleanup(7, now, _ => { calls++; return false; });
    Check(store.Find(secondCopyId) is null && store.Find(originalId)!.ArchivedAt == now && calls == 0, "last dependent expiry restarts original retention and prevents same-pass deletion");
    store.Cleanup(7, now + 8 * day, _ => { calls++; return false; });
    Check(calls == 1 && store.Find(originalId)!.Error.Length > 0 && File.Exists(originalPath), "original recycle failure preserves file and record");
    Check(store.Recycle(originalId, p => { File.Move(p, p + ".test-recycled"); return true; }) && store.Find(originalId) is null, "unreferenced original can be explicitly recycled");
}
var migration = Path.Combine(root, "v1-migration"); string migrationId;
using (var store = new LibraryStore(migration)) { migrationId = store.Add(Make("v1-image.png"), Ownership.External, "v1", "v1", now, 25).Id; }
using (var db = new SqliteConnection($"Data Source={Path.Combine(migration, "library.db")};Pooling=False"))
{
    db.Open(); using var command = db.CreateCommand();
    command.CommandText = "DROP TABLE EditOrigins; PRAGMA user_version=1; INSERT INTO Settings VALUES(1,'{\"Capacity\":25}')"; command.ExecuteNonQuery();
}
using (var store = new LibraryStore(migration))
{
    Check(store.Find(migrationId) is not null && store.LoadSettings().Capacity == 25 && store.LoadSettings().Shortcut == "Ctrl+Alt+T" && !store.LoadSettings().RunAtLogin, "v1 migration preserves entries and supplies conservative preference defaults");
    Check(Directory.GetFiles(migration, "library-v1-backup-*.db").Length == 1, "v1 migration produces database backup before schema change");
    using var db = new SqliteConnection($"Data Source={Directory.GetFiles(migration, "library-v1-backup-*.db").Single()};Mode=ReadOnly;Pooling=False"); db.Open();
    using var version = db.CreateCommand(); version.CommandText = "PRAGMA user_version";
    Check(Convert.ToInt32(version.ExecuteScalar()) == 1, "migration backup remains readable as v1");
}
using (var store = new LibraryStore(migration)) Check(Directory.GetFiles(migration, "library-v1-backup-*.db").Length == 1, "v2 reopening does not repeat migration or create extra backups");
Console.WriteLine($"{count} storage and collection checks passed. Fixtures: {root}");
