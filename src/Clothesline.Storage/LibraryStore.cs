using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Clothesline.Storage;

public enum Ownership { External, Managed }
public enum EntryState { Shelf, History, Pending }
public sealed record LibraryEntry(string Id, string Path, string Title, Ownership Ownership, string Source,
    string Hash, EntryState State, long AddedAt, long? ArchivedAt, bool Missing, string Error);
public sealed record LibrarySettings
{
    public string ScreenshotFolder { get; init; } = "";
    public string[] ExtraFolders { get; init; } = [];
    public bool ClipboardEnabled { get; init; }
    public bool Paused { get; init; }
    public bool ShowNew { get; init; } = true;
    public bool SuppressFullscreen { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public int Capacity { get; init; }
    public int RetentionDays { get; init; }
    public long MaxManagedBytes { get; init; } = 512L * 1024 * 1024;
}

/// A single serialized connection. File side effects are journaled before execution.
public sealed class LibraryStore : IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();
    public string Root { get; }
    public string ManagedFolder => System.IO.Path.Combine(Root, "Images");
    public LibraryStore(string root)
    {
        Root = System.IO.Path.GetFullPath(root); Directory.CreateDirectory(Root); Directory.CreateDirectory(ManagedFolder);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(Root, "library.db"), Pooling = false }.ToString());
        db.Open(); Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
        var version = Convert.ToInt32(Scalar("PRAGMA user_version;"));
        if (version > 1) { db.Dispose(); throw new InvalidOperationException("数据库来自更新版本，请使用更新的应用。"); }
        if (version == 0)
            Execute("""
                BEGIN IMMEDIATE;
                CREATE TABLE Items(Id TEXT PRIMARY KEY, Path TEXT NOT NULL, Title TEXT NOT NULL, Ownership INTEGER NOT NULL,
                    Source TEXT NOT NULL, Hash TEXT NOT NULL, State INTEGER NOT NULL, AddedAt INTEGER NOT NULL,
                    ArchivedAt INTEGER, Missing INTEGER NOT NULL DEFAULT 0, Error TEXT NOT NULL DEFAULT '');
                CREATE INDEX ItemsState ON Items(State,AddedAt);
                CREATE INDEX ItemsHash ON Items(Hash,AddedAt);
                CREATE TABLE Settings(Id INTEGER PRIMARY KEY CHECK(Id=1), Json TEXT NOT NULL);
                CREATE TABLE Observed(Path TEXT PRIMARY KEY COLLATE NOCASE, Signature TEXT NOT NULL);
                CREATE TABLE Sources(Path TEXT PRIMARY KEY COLLATE NOCASE);
                CREATE TABLE Operations(EntryId TEXT PRIMARY KEY, Kind TEXT NOT NULL);
                PRAGMA user_version=1;
                COMMIT;
                """);
        Recover();
    }
    private SqliteCommand Command(string sql, params object?[] pairs)
    {
        var command = db.CreateCommand(); command.CommandText = sql;
        for (var i = 0; i < pairs.Length; i += 2) command.Parameters.AddWithValue((string)pairs[i]!, pairs[i + 1] ?? DBNull.Value);
        return command;
    }
    private int Execute(string sql, params object?[] pairs) { using var command = Command(sql, pairs); return command.ExecuteNonQuery(); }
    private object? Scalar(string sql, params object?[] pairs) { using var command = Command(sql, pairs); return command.ExecuteScalar(); }
    public LibrarySettings LoadSettings()
    {
        lock (gate)
        {
            var json = Scalar("SELECT Json FROM Settings WHERE Id=1") as string;
            return json is null ? new() : JsonSerializer.Deserialize<LibrarySettings>(json) ?? new();
        }
    }
    public bool HasSettings { get { lock (gate) return Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Settings")) != 0; } }
    public void FailManaged(string id, string error)
    {
        lock (gate)
        {
            var entry = Find(id); if (entry is null) return;
            Execute("UPDATE Items SET State=1,ArchivedAt=$now,Path=$path,Missing=$missing,Error=$error WHERE Id=$id", "$id", id, "$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                "$path", File.Exists(entry.Path) ? entry.Path : entry.Path + ".part", "$missing", !File.Exists(entry.Path) && !File.Exists(entry.Path + ".part"), "$error", error);
        }
    }
    public void SaveSettings(LibrarySettings settings)
    {
        if (settings.ExtraFolders.Length > 3 || settings.Capacity is not (0 or 25 or 50 or 100) || settings.RetentionDays is not (0 or 7 or 30) || settings.MaxManagedBytes < 1024 * 1024)
            throw new ArgumentException("设置值超出支持范围。");
        lock (gate) Execute("INSERT INTO Settings VALUES(1,$json) ON CONFLICT(Id) DO UPDATE SET Json=$json", "$json", JsonSerializer.Serialize(settings));
    }
    public IReadOnlyList<LibraryEntry> Entries(EntryState? state = null)
    {
        lock (gate)
        {
            using var command = Command("SELECT Id,Path,Title,Ownership,Source,Hash,State,AddedAt,ArchivedAt,Missing,Error FROM Items" +
                (state is null ? "" : " WHERE State=$state") + " ORDER BY AddedAt DESC, rowid DESC", state is null ? [] : ["$state", (int)state]);
            using var reader = command.ExecuteReader(); var rows = new List<LibraryEntry>();
            while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), (Ownership)reader.GetInt32(3), reader.GetString(4), reader.GetString(5), (EntryState)reader.GetInt32(6), reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetInt64(8), reader.GetBoolean(9), reader.GetString(10)));
            return rows;
        }
    }
    public LibraryEntry? Find(string id) => Entries().FirstOrDefault(e => e.Id == id);
    public bool IsDuplicate(string path, string hash, string source, long now)
    {
        lock (gate)
        {
            // Same-path watcher repeats are not new captures. Cross-source dedup has a short window.
            return Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Items WHERE Missing=0 AND Hash=$hash AND (Path=$path COLLATE NOCASE OR (Source<>$source AND AddedAt >= $since))", "$hash", hash, "$path", path, "$source", source, "$since", now - 5000)) > 0;
        }
    }
    public LibraryEntry Add(string path, Ownership ownership, string source, string hash, long now, int capacity, EntryState state = EntryState.Shelf, string? id = null)
    {
        lock (gate)
        {
            id ??= Guid.NewGuid().ToString("N");
            using var transaction = db.BeginTransaction();
            Execute("INSERT INTO Items(Id,Path,Title,Ownership,Source,Hash,State,AddedAt) VALUES($id,$path,$title,$owner,$source,$hash,$state,$time)", "$id", id, "$path", path, "$title", System.IO.Path.GetFileName(path), "$owner", (int)ownership, "$source", source, "$hash", hash, "$state", (int)state, "$time", now);
            EnforceCapacity(capacity, now); transaction.Commit(); return Find(id)!;
        }
    }
    public void CompleteManaged(string id, int capacity, long now)
    {
        lock (gate)
        {
            using var transaction = db.BeginTransaction();
            Execute("UPDATE Items SET State=0,Missing=0,Error='' WHERE Id=$id", "$id", id);
            EnforceCapacity(capacity, now); transaction.Commit();
        }
    }
    public void EnforceCapacity(int capacity, long now)
    {
        lock (gate) Execute("UPDATE Items SET State=1,ArchivedAt=$now WHERE Id IN (SELECT Id FROM Items WHERE State=0 ORDER BY AddedAt DESC,rowid DESC LIMIT -1 OFFSET $capacity)", "$now", now, "$capacity", Math.Clamp(capacity, 1, 100));
    }
    public void Archive(string id, long now)
    { lock (gate) Execute("UPDATE Items SET State=1,ArchivedAt=$time WHERE Id=$id AND State=0", "$id", id, "$time", now); }
    public bool Restore(string id, int capacity, long now)
    {
        lock (gate)
        {
            var entry = Find(id); if (entry is null || !File.Exists(entry.Path)) { RefreshMissing(); return false; }
            using var transaction = db.BeginTransaction();
            Execute("UPDATE Items SET State=0,ArchivedAt=NULL,AddedAt=$now,Missing=0,Error='' WHERE Id=$id", "$id", id, "$now", now);
            EnforceCapacity(capacity, now); transaction.Commit(); return true;
        }
    }
    public void RefreshMissing()
    {
        lock (gate) foreach (var entry in Entries()) Execute("UPDATE Items SET Missing=$missing WHERE Id=$id", "$missing", !File.Exists(entry.Path), "$id", entry.Id);
    }
    public void SetError(string id, string message) { lock (gate) Execute("UPDATE Items SET Error=$error WHERE Id=$id", "$id", id, "$error", message); }
    public void ForgetExternalOrMissing(string id)
    {
        lock (gate)
        {
            var entry = Find(id); if (entry is null) return;
            if (entry.Ownership == Ownership.Managed && File.Exists(entry.Path)) throw new InvalidOperationException("应用保存的文件仍存在，请使用移入回收站。");
            Execute("DELETE FROM Items WHERE Id=$id", "$id", id);
        }
    }
    public bool Recycle(string id, Func<string, bool> recycle)
    {
        lock (gate)
        {
            var entry = Find(id); if (entry is null) return false;
            Execute("INSERT OR REPLACE INTO Operations VALUES($id,'recycle')", "$id", id);
            try
            {
                if (File.Exists(entry.Path) && (!recycle(entry.Path) || File.Exists(entry.Path))) throw new IOException("回收未完成，文件与记录已保留。");
                using var transaction = db.BeginTransaction();
                Execute("DELETE FROM Items WHERE Id=$id; DELETE FROM Operations WHERE EntryId=$id", "$id", id); transaction.Commit(); return true;
            }
            catch (Exception ex)
            {
                SetError(id, ex.Message); Execute("DELETE FROM Operations WHERE EntryId=$id", "$id", id); return false;
            }
        }
    }
    public int Cleanup(int days, long now, Func<string, bool> recycle)
    {
        if (days <= 0) return 0;
        lock (gate)
        {
            var count = 0;
            foreach (var entry in Entries(EntryState.History).Where(e => e.ArchivedAt <= now - TimeSpan.FromDays(days).TotalMilliseconds && e.Error.Length == 0))
            {
                if (entry.Ownership == Ownership.External || !File.Exists(entry.Path)) { ForgetExternalOrMissing(entry.Id); count++; }
                else if (Recycle(entry.Id, recycle)) count++;
            }
            return count;
        }
    }
    public void InitializeSource(string folder, IEnumerable<(string Path, string Signature)> existing)
    {
        lock (gate)
        {
            if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Sources WHERE Path=$path", "$path", folder)) > 0) return;
            using var transaction = db.BeginTransaction();
            foreach (var file in existing) Observe(file.Path, file.Signature);
            Execute("INSERT INTO Sources VALUES($path)", "$path", folder); transaction.Commit();
        }
    }
    public bool HasObserved(string path, string signature)
    { lock (gate) return string.Equals(Scalar("SELECT Signature FROM Observed WHERE Path=$path", "$path", path) as string, signature, StringComparison.Ordinal); }
    public void Observe(string path, string signature)
    { lock (gate) Execute("INSERT INTO Observed VALUES($path,$signature) ON CONFLICT(Path) DO UPDATE SET Signature=$signature", "$path", path, "$signature", signature); }
    private void Recover()
    {
        lock (gate)
        {
            var pending = Entries(EntryState.Pending);
            foreach (var entry in pending)
            {
                var path = File.Exists(entry.Path) ? entry.Path : entry.Path + ".part";
                Execute("UPDATE Items SET State=1,ArchivedAt=$now,Path=$path,Error=$error WHERE Id=$id", "$id", entry.Id, "$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "$path", path,
                    "$error", File.Exists(entry.Path) ? "" : "上次写入被中断，保留待检查文件。");
            }
            using var command = Command("SELECT EntryId FROM Operations");
            var ids = new List<string>(); using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetString(0));
            foreach (var id in ids)
            {
                var entry = Find(id);
                if (entry is not null && !File.Exists(entry.Path)) Execute("DELETE FROM Items WHERE Id=$id", "$id", id);
                else if (entry is not null) SetError(id, "上次文件操作被中断，文件仍在；请检查后手动重试。");
                Execute("DELETE FROM Operations WHERE EntryId=$id", "$id", id);
            }
            RefreshMissing();
        }
    }
    public void Dispose() { lock (gate) db.Dispose(); }
}
