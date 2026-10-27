using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal sealed partial class LibraryRuntime
{
    private volatile bool droppingImages;
    internal async Task ImportDroppedImages(string[] paths, string? beforeId)
    {
        if (disposed || droppingImages) return;
        droppingImages = true; fileOperation++;
        var accepted = new List<string>(); var failed = 0;
        try
        {
            foreach (var path in paths.Where(MediaFiles.IsImage).Distinct(StringComparer.OrdinalIgnoreCase).Take(100))
            {
                if (disposed) break;
                try
                {
                    if (!await Collector.ImportExternal(path, cancellation: closing.Token, preserveCopy: true)) { failed++; continue; }
                    // Match the decoded content too: a path may have several historical revisions.
                    var hash = await ImageDecoder.Hash(path, closing.Token);
                    if (disposed) break;
                    var entry = Store.Entries().FirstOrDefault(e => e.Source != LibraryStore.OriginalSource && e.Hash == hash && e.Path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
                    if (entry is null || (entry.State != EntryState.Shelf && !Store.Restore(entry.Id, Capacity, Now))) { failed++; continue; }
                    accepted.Add(entry.Id);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { failed++; Report(T("未导入 ") + Path.GetFileName(path) + "：" + ex.Message); }
            }
            if (disposed) return;
            var remaining = Store.Entries(EntryState.Shelf).Select(e => e.Id).ToHashSet();
            var hung = accepted.Where(remaining.Contains).ToArray();
            if (hung.Length > 0) Store.MoveShelfItems(hung, beforeId is not null && remaining.Contains(beforeId) ? beforeId : null);
            revealPending = false;
            RefreshShelf(false);
            Report(T("已挂上 {0} 张图片，原文件保留。", hung.Length)
                + (accepted.Count > hung.Length ? " " + T("超出容量的 {0} 张已进入历史。", accepted.Count - hung.Length) : "")
                + (failed > 0 ? " " + T("失败 {0} 个文件。", failed) : ""));
        }
        finally { fileOperation--; droppingImages = false; }
    }
}
