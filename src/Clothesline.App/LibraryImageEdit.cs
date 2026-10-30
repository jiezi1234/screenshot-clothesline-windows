using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clothesline.Storage;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class LibraryRuntime
{
    private readonly ImageEditHistory imageEditHistory = new();
    internal async Task<ImageEditSession?> CreateImageEditSession(DemoItem item, CancellationToken cancellation)
    {
        if (item.EntryId is null || Store.Find(item.EntryId) is not { } entry || !MediaFiles.IsImage(entry.Path) || entry.Source == LibraryStore.OriginalSource) return null;
        var held = new List<IDisposable>();
        var lease = new FileLease(() => { foreach (var value in held) value.Dispose(); held.Clear(); });
        held.Add(AcquireLease(entry.Path));
        try
        {
            var digest = await Task.Run(() => LibraryStore.FileDigest(entry.Path), cancellation);
            var retained = imageEditHistory.Restore(entry.Id, entry.Path, digest, entry.Hash);
            var bitmap = retained?.Original ?? await ImageDecoder.Read(entry.Path, cancellation: cancellation);
            if (digest != await Task.Run(() => LibraryStore.FileDigest(entry.Path), cancellation)) throw new IOException(T("图片已在其他窗口或程序中更改，请重新打开后编辑。"));
            cancellation.ThrowIfCancellationRequested();
            var hash = retained is not null ? entry.Hash : await Task.Run(() => ImageFiles.Hash(bitmap), cancellation);
            var original = ResolveOriginal(new(entry.Id, entry.Path, entry.Title, entry.Hash), hash);
            if (original is not null) held.Add(AcquireLease(original.Path));
            var currentPath = entry.Path; var currentHash = entry.Hash;
            return new(retained ?? new ImageDocument(bitmap), async rendered =>
            {
                if (disposed) throw new ObjectDisposedException(nameof(LibraryRuntime));
                fileOperation++;
                try
                {
                    if (original is null)
                    {
                        var png = await Task.Run(() => ImageFiles.Png(bitmap));
                        var originalHash = await Task.Run(() => ImageFiles.Hash(bitmap));
                        var root = await Collector.PreserveOriginal(png, originalHash, Settings.MaxManagedBytes, closing.Token);
                        original = new(root.Id, root.Path, root.Hash); held.Add(AcquireLease(root.Path));
                    }
                    if (Store.Find(original.Id)?.Hash != original.Hash || await Task.Run(() => ImageFiles.Hash(original.Path)) != original.Hash)
                        throw new IOException(T("关联原版已失效或改变，请先检查历史中的原版快照。"));
                    var output = await Task.Run(() => (Png: ImageFiles.Png(rendered), Hash: ImageFiles.Hash(rendered)));
                    var updated = await Collector.SaveImageEdit(entry.Id, output.Png, output.Hash, digest, original.Id, Settings.MaxManagedBytes, closing.Token);
                    digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(output.Png));
                    currentHash = output.Hash;
                    if (currentPath != updated.Path) { held.Add(AcquireLease(updated.Path)); currentPath = updated.Path; }
                    cache.Remove(entry.Id); ScheduleRefresh(false);
                }
                finally { fileOperation--; }
            }, lease, checkpoint => imageEditHistory.Remember(entry.Id, currentPath, digest, currentHash, checkpoint));
        }
        catch { lease.Dispose(); throw; }
    }
}
