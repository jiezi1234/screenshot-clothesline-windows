using Clothesline.Core;
using Clothesline.Storage;
using Microsoft.Data.Sqlite;

internal static class TextChecks
{
    internal static async Task Run(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "text-" + Guid.NewGuid().ToString("N"));
        const string content = "  你好 🧺\r\nSecond line\n\tconst value = '<b>literal</b>';  ";
        check(ClipboardPolicy.Select(["UnicodeText", "HTML Format"], _ => null) == ClipboardContentKind.Text, "rich clipboard uses its plain Unicode text representation");
        check(ClipboardPolicy.Select(["Text"], _ => null) == ClipboardContentKind.Text, "legacy plain text has a text route");
        check(ClipboardPolicy.Select(["PNG", "UnicodeText"], _ => null) == ClipboardContentKind.Text, "mixed image and text clipboard makes one text card");
        foreach (var flag in new[] { "ScreenshotClothesline.OwnCopy", "ExcludeClipboardContentFromMonitorProcessing", "ClipboardViewerIgnore", "FileDrop" })
            check(ClipboardPolicy.Select(["UnicodeText", flag], _ => null) == ClipboardContentKind.None, "text rejects own copy privacy or file-drop marker: " + flag);
        foreach (var flag in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
            check(ClipboardPolicy.Select(["UnicodeText"], key => key == flag ? false : null) == ClipboardContentKind.None, "text respects privacy flag: " + flag);
        check(ClipboardPolicy.Select(["HTML Format", "Rich Text Format"], _ => null) == ClipboardContentKind.None, "format-only content is never parsed or executed");
        check(TextContent.Encoding.GetString(TextContent.Encode(content)) == content, "text encoding preserves Unicode whitespace and mixed line endings");
        foreach (var invalid in new[] { "", " \r\n\t", "bad\0text", new string('x', TextContent.MaxCharacters + 1), "\ud800" })
        {
            var rejected = false; try { TextContent.Encode(invalid); } catch (ArgumentException) { rejected = true; }
            check(rejected, "invalid empty oversized or malformed text is rejected");
        }
        check(TextContent.Title("\n  标题 🧺 \n后续") == "标题 🧺", "title uses first nonempty line without changing content");
        check(!char.IsHighSurrogate(TextContent.Title(string.Concat(Enumerable.Repeat("🧺", 50)))[^2]), "title never splits emoji pairs");
        string id, path, hash; TextPaperStyle paperStyle;
        using (var store = new LibraryStore(root))
        using (var engine = new CollectionEngine(store, _ => "fixture-image", () => 25))
        {
            check(await engine.ImportText(content, 1024 * 1024), "clipboard text commits managed file and record");
            var entry = store.Entries(EntryState.Shelf).Single(); id = entry.Id; path = entry.Path; hash = entry.Hash;
            paperStyle = TextPaperStyles.ForEntry(id);
            check(entry.Ownership == Ownership.Managed && entry.Source == LibraryStore.TextSource && MediaFiles.IsText(path) && entry.Title == "你好 🧺", "text card has managed ownership type and readable title");
            check(TextContent.Read(path) == content && !Directory.GetFiles(store.ManagedFolder, "*.part").Any(), "committed text is complete without unfinished file");
            check(!await engine.ImportText(content, 1024 * 1024), "rapid duplicate text is suppressed");
            check(!await engine.ImportText("over quota", 1), "text import enforces managed storage quota");
            check(!await engine.ImportClipboard([1, 2], "image-budget", 1), "image import shares quota with stored text");
            await engine.ImportText("second", 1024 * 1024); store.MoveShelfItems([id], null);
            var before = store.Entries(EntryState.Shelf).Select(e => e.Id).ToArray(); var added = entry.AddedAt;
            hash = await engine.SaveText(id, "edited\r\n新的文字", hash, 1024 * 1024);
            check(TextContent.Read(path) == "edited\r\n新的文字" && store.Find(id)!.Hash == hash && store.Find(id)!.Title == "edited", "save updates the same text file and metadata");
            check(store.Entries(EntryState.Shelf).Select(e => e.Id).SequenceEqual(before) && store.Find(id)!.AddedAt == added, "edit preserves ordering capacity age and entry identity");
            check(TextPaperStyles.ForEntry(store.Find(id)!.Id) == paperStyle, "editing text keeps its assigned paper style");
            var conflict = false; try { await engine.SaveText(id, "stale overwrite", entry.Hash, 1024 * 1024); } catch (IOException) { conflict = true; }
            check(conflict && TextContent.Read(path) == "edited\r\n新的文字", "stale editor cannot overwrite newer text");
            var quota = false; try { await engine.SaveText(id, "too large", hash, 1); } catch (IOException) { quota = true; }
            check(quota && store.Find(id)!.Hash == hash, "save quota failure preserves original file and metadata");
            store.Archive(id, 1234); hash = await engine.SaveText(id, "history edit", hash, 1024 * 1024);
            check(store.Find(id) is { State: EntryState.History, ArchivedAt: 1234 }, "editing history does not rehang or restart retention");
            check(store.Restore(id, 25, 2345) && TextContent.Read(path) == "history edit", "edited text restores from history");
            check(TextPaperStyles.ForEntry(store.Find(id)!.Id) == paperStyle, "history restoration keeps the same paper style");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var denied = false; try { await engine.SaveText(id, "locked", hash, 1024 * 1024); } catch (IOException) { denied = true; }
                check(denied, "locked text refuses save without truncating source");
            }
            check(TextContent.Read(path) == "history edit", "failed write leaves readable original");
            File.WriteAllText(path, "external edit", TextContent.Encoding);
            var externalConflict = false; try { await engine.SaveText(id, "overwrite external", hash, 1024 * 1024); } catch (IOException) { externalConflict = true; }
            check(externalConflict && TextContent.Read(path) == "external edit", "external edits are not silently overwritten");
        }
        using (var store = new LibraryStore(root))
        {
            check(store.Find(id)!.State == EntryState.Shelf && TextContent.Read(path) == "external edit", "text content and shelf state survive restart");
            check(TextPaperStyles.ForEntry(store.Find(id)!.Id) == paperStyle, "assigned paper style survives library restart");
        }
        foreach (var replaced in new[] { false, true })
        {
            var saved = "recovered " + replaced; var stage = path + ".edit-fixture-" + replaced + ".part";
            var oldHash = TextContent.Hash(TextContent.Read(path)); File.WriteAllText(stage, saved, TextContent.Encoding);
            using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "library.db")};Pooling=False"))
            {
                db.Open(); using var command = db.CreateCommand(); command.CommandText = "INSERT INTO TextEdits VALUES($id,$path,$old,$new,$title)";
                command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$path", stage); command.Parameters.AddWithValue("$old", oldHash); command.Parameters.AddWithValue("$new", TextContent.Hash(saved)); command.Parameters.AddWithValue("$title", saved); command.ExecuteNonQuery();
            }
            if (replaced) File.Move(stage, path, true);
            using var recovered = new LibraryStore(root);
            check(TextContent.Read(path) == saved && recovered.Find(id)!.Hash == TextContent.Hash(saved) && recovered.Find(id)!.Title == saved && !File.Exists(stage), "interrupted text save recovers file and metadata: " + replaced);
        }
        var v3 = Path.Combine(parent, "v3-text-upgrade");
        using (var store = new LibraryStore(v3)) store.SaveSettings(new LibrarySettings { ClipboardEnabled = true, Capacity = 25 });
        using (var db = new SqliteConnection($"Data Source={Path.Combine(v3, "library.db")};Pooling=False")) { db.Open(); using var command = db.CreateCommand(); command.CommandText = "DROP TABLE TextEdits; PRAGMA user_version=3"; command.ExecuteNonQuery(); }
        using (var migrated = new LibraryStore(v3)) check(migrated.LoadSettings().ClipboardEnabled && migrated.LoadSettings().Capacity == 25 && Directory.GetFiles(v3, "library-v3-backup-*.db").Length == 1, "v3 to v4 migration backs up and preserves clipboard settings");
        using (var migrated = new LibraryStore(v3)) check(Directory.GetFiles(v3, "library-v3-backup-*.db").Length == 1, "v4 reopening does not create extra backups");
    }
}
