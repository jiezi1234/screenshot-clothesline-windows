using System;
using System.Collections.Generic;
using System.Linq;

namespace Clothesline.App;

// Runtime-only, recently edited cards. Closed previews retain neither windows nor file leases.
internal sealed class ImageEditHistory(int maxEntries = 16, long maxBytes = 256L * 1024 * 1024)
{
    private sealed record Entry(string Id, string Path, string Digest, string PixelHash, ImageDocument Document, long Bytes);
    private readonly LinkedList<Entry> recent = new();
    internal int Count => recent.Count;
    internal long RetainedBytes => recent.Sum(entry => entry.Bytes);
    internal ImageDocument? Restore(string id, string path, string digest, string pixelHash)
    {
        var node = Find(id); if (node is null) return null;
        recent.Remove(node); var entry = node.Value;
        if (!string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase) || entry.Digest != digest || entry.PixelHash != pixelHash) return null;
        recent.AddFirst(node);
        // An unsaved live edit, or an explicit discard, must not mutate the saved checkpoint.
        return entry.Document.SavedCheckpoint();
    }
    internal void Remember(string id, string path, string digest, string pixelHash, ImageDocument checkpoint)
    {
        var previous = Find(id); if (previous is not null) recent.Remove(previous);
        if (!checkpoint.CanUndo && !checkpoint.CanRedo) return;
        recent.AddFirst(new Entry(id, path, digest, pixelHash, checkpoint, checkpoint.RetainedBytes));
        // Always retain the just-edited card, even if its single document exceeds the estimate.
        while (recent.Count > 1 && (recent.Count > maxEntries || RetainedBytes > maxBytes)) recent.RemoveLast();
    }
    private LinkedListNode<Entry>? Find(string id)
    {
        for (var node = recent.First; node is not null; node = node.Next) if (node.Value.Id == id) return node;
        return null;
    }
    internal void Clear() => recent.Clear();
}
