using System.Security.Cryptography;
using Clothesline.Core;

namespace Clothesline.Storage;

public sealed partial class LibraryStore
{
    public const string ImageEditSource = "HoverImageEdit";
    public static string FileDigest(string path)
    { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }

    // The first save changes only this card's reference. Later saves replace its managed copy.
    // A flushed PNG and journal make both cases recoverable across process interruption.
    public LibraryEntry SaveImageEdit(string id, byte[] png, string pixelHash, string expectedDigest, string originalId, long limit, long now)
    {
        lock (gate)
        {
            var entry = Find(id) ?? throw new IOException(T("这张卡片不能直接编辑。"));
            if (!MediaFiles.IsImage(entry.Path) || entry.State == EntryState.Pending || HasOriginalDependents(id) || entry.Source == OriginalSource)
                throw new IOException(T("这张卡片不能直接编辑。"));
            if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM ImageEdits WHERE EntryId=$id", "$id", id)) != 0)
                throw new IOException(T("上次图片保存尚未完成，请重启后检查。"));
            var original = Find(originalId);
            if (original?.Source != OriginalSource || original.Ownership != Ownership.Managed || original.Missing || !File.Exists(original.Path))
                throw new IOException(T("原版快照尚不可用，未建立编辑关联。"));
            if (OriginalFor(id) is { } existing && existing.Id != originalId)
                throw new IOException(T("这份副本已经关联另一份原版。"));
            if (FileDigest(entry.Path) != expectedDigest) throw new IOException(T("图片已在其他窗口或程序中更改，请重新打开后编辑。"));
            if (Directory.EnumerateFiles(ManagedFolder).Sum(p => new FileInfo(p).Length) + png.Length > limit)
                throw new IOException(T("自管内容空间不足，请增加上限或清理历史后重试。"));
            var destination = entry.Source == ImageEditSource ? entry.Path : Path.Combine(ManagedFolder, "image-edit-" + Guid.NewGuid().ToString("N") + ".png");
            if (!OwnedEditPath(destination)) throw new IOException(T("这张卡片不能直接编辑。"));
            var stage = destination + ".edit-" + Guid.NewGuid().ToString("N") + ".part";
            using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(png); file.Flush(true); }
            var digest = Convert.ToHexString(SHA256.HashData(png));
            Execute("INSERT INTO ImageEdits VALUES($id,$stage,$dest,$old,$new,$hash,$original,$now)", "$id", id, "$stage", stage, "$dest", destination,
                "$old", expectedDigest, "$new", digest, "$hash", pixelHash, "$original", originalId, "$now", now);
            File.Move(stage, destination, entry.Source == ImageEditSource);
            CompleteImageEdit(entry, destination, pixelHash, originalId, now);
            return Find(id)!;
        }
    }
    private bool OwnedEditPath(string path) => string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), ManagedFolder, StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(path).StartsWith("image-edit-", StringComparison.Ordinal) && Path.GetExtension(path) == ".png";
    private void CompleteImageEdit(LibraryEntry entry, string destination, string hash, string originalId, long now)
    {
        using var transaction = db.BeginTransaction();
        if (entry.Source != ImageEditSource)
        {
            // Keep the source record in history, including externally owned paths; never overwrite it.
            var sourceId = Guid.NewGuid().ToString("N");
            Execute("INSERT INTO Items SELECT $copy,Path,Title,Ownership,Source,Hash,1,AddedAt,$now,Missing,Error FROM Items WHERE Id=$id", "$copy", sourceId, "$now", now, "$id", entry.Id);
            Execute("INSERT INTO EditOrigins SELECT $copy,OriginalId FROM EditOrigins WHERE EntryId=$id", "$copy", sourceId, "$id", entry.Id);
        }
        Execute("UPDATE Items SET Path=$path,Ownership=1,Source=$source,Hash=$hash,Missing=0,Error='' WHERE Id=$id", "$path", destination, "$source", ImageEditSource, "$hash", hash, "$id", entry.Id);
        Execute("INSERT INTO EditOrigins VALUES($id,$original) ON CONFLICT(EntryId) DO NOTHING", "$id", entry.Id, "$original", originalId);
        Execute("DELETE FROM ImageEdits WHERE EntryId=$id", "$id", entry.Id);
        transaction.Commit();
    }
    private void RecoverImageEdits()
    {
        var edits = new List<(string Id, string Stage, string Destination, string OldDigest, string NewDigest, string Hash, string Original, long Now)>();
        using (var command = Command("SELECT EntryId,StagePath,Destination,OldDigest,NewDigest,PixelHash,OriginalId,SavedAt FROM ImageEdits"))
        using (var reader = command.ExecuteReader()) while (reader.Read()) edits.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt64(7)));
        foreach (var edit in edits)
        {
            var entry = Find(edit.Id); if (entry is null) continue;
            try
            {
                if (!OwnedEditPath(edit.Destination) || !string.Equals(Path.GetDirectoryName(edit.Stage), ManagedFolder, StringComparison.OrdinalIgnoreCase)
                    || !edit.Stage.StartsWith(edit.Destination + ".edit-", StringComparison.OrdinalIgnoreCase)) throw new IOException(T("这张卡片不能直接编辑。"));
                if (!File.Exists(edit.Destination) || FileDigest(edit.Destination) != edit.NewDigest)
                {
                    if (FileDigest(entry.Path) != edit.OldDigest || FileDigest(edit.Stage) != edit.NewDigest
                        || (entry.Path != edit.Destination && File.Exists(edit.Destination))) throw new IOException(T("图片已在其他窗口或程序中更改，请重新打开后编辑。"));
                    File.Move(edit.Stage, edit.Destination, entry.Source == ImageEditSource);
                }
                CompleteImageEdit(entry, edit.Destination, edit.Hash, edit.Original, edit.Now);
            }
            catch (Exception ex) { SetError(edit.Id, T("上次图片保存尚未完成，请重启后检查。") + " " + ex.Message); }
        }
    }
}
