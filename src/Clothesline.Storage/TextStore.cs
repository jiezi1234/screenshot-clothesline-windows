using Clothesline.Core;

namespace Clothesline.Storage;

public sealed partial class LibraryStore
{
    public const string TextSource = "ClipboardText";
    public void UpdateTextMetadata(string id, string text)
    {
        lock (gate) Execute("UPDATE Items SET Hash=$hash,Title=$title,Missing=0,Error='' WHERE Id=$id AND Source=$source AND NOT EXISTS(SELECT 1 FROM TextEdits WHERE EntryId=$id)", "$id", id, "$source", TextSource, "$hash", TextContent.Hash(text), "$title", TextContent.Title(text));
    }
    public string SaveText(string id, string text, string expectedHash, long limit)
    {
        var bytes = TextContent.Encode(text); var hash = TextContent.Hash(text);
        lock (gate)
        {
            var entry = Find(id) ?? throw new IOException(T("文字卡片已不存在。"));
            if (entry.Ownership != Ownership.Managed || entry.Source != TextSource || !MediaFiles.IsText(entry.Path) || !Path.GetDirectoryName(entry.Path)!.Equals(ManagedFolder, StringComparison.OrdinalIgnoreCase))
                throw new IOException(T("这张卡片不能直接编辑。"));
            if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM TextEdits WHERE EntryId=$id", "$id", id)) != 0)
                throw new IOException(T("上次文字保存尚未完成，请重启后检查。"));
            if (TextContent.Hash(TextContent.Read(entry.Path)) != expectedHash) throw new IOException(T("文字已在其他窗口或程序中更改，请重新打开后编辑。"));
            var used = Directory.EnumerateFiles(ManagedFolder).Sum(p => new FileInfo(p).Length);
            if (used + bytes.Length > limit) throw new IOException(T("自管内容空间不足，请增加上限或清理历史后重试。"));
            var stage = entry.Path + ".edit-" + Guid.NewGuid().ToString("N") + ".part";
            using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            // Journal only a fully flushed file. The old text remains intact until the replacement.
            Execute("INSERT INTO TextEdits VALUES($id,$stage,$old,$new,$title)", "$id", id, "$stage", stage, "$old", expectedHash, "$new", hash, "$title", TextContent.Title(text));
            File.Move(stage, entry.Path, true);
            CompleteTextEdit(id, hash, TextContent.Title(text)); return hash;
        }
    }
    private void CompleteTextEdit(string id, string hash, string title)
    {
        using var transaction = db.BeginTransaction();
        Execute("UPDATE Items SET Hash=$hash,Title=$title,Missing=0,Error='' WHERE Id=$id", "$id", id, "$hash", hash, "$title", title);
        Execute("DELETE FROM TextEdits WHERE EntryId=$id", "$id", id); transaction.Commit();
    }
    private void RecoverTextEdits()
    {
        lock (gate)
        {
            var edits = new List<(string Id, string Stage, string OldHash, string NewHash, string Title)>();
            using (var command = Command("SELECT EntryId,StagePath,OldHash,NewHash,Title FROM TextEdits"))
            using (var reader = command.ExecuteReader()) while (reader.Read()) edits.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            foreach (var edit in edits)
            {
                var entry = Find(edit.Id); if (entry is null) continue;
                try
                {
                    if (entry.Ownership != Ownership.Managed || entry.Source != TextSource || !Path.GetDirectoryName(entry.Path)!.Equals(ManagedFolder, StringComparison.OrdinalIgnoreCase)
                        || !Path.GetDirectoryName(edit.Stage)!.Equals(ManagedFolder, StringComparison.OrdinalIgnoreCase) || !edit.Stage.StartsWith(entry.Path + ".edit-", StringComparison.OrdinalIgnoreCase)) throw new IOException(T("这张卡片不能直接编辑。"));
                    var current = TextContent.Hash(TextContent.Read(entry.Path));
                    if (current != edit.NewHash)
                    {
                        if (current != edit.OldHash || TextContent.Hash(TextContent.Read(edit.Stage)) != edit.NewHash) throw new IOException(T("文字已在其他窗口或程序中更改，请重新打开后编辑。"));
                        File.Move(edit.Stage, entry.Path, true);
                    }
                    CompleteTextEdit(edit.Id, edit.NewHash, edit.Title);
                }
                catch (Exception ex) { SetError(edit.Id, T("上次文字保存尚未完成，请重启后检查。") + " " + ex.Message); }
            }
        }
    }
}
