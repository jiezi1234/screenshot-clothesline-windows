using System.Security.Cryptography;
using System.Text;

namespace Clothesline.Core;

public enum TextPaperStyle { Sticky = 0, Journal = 1, Notebook = 2, Index = 3 }

public static class TextPaperStyles
{
    // Entry IDs are random UUIDs persisted with each capture. This stable mapping makes the
    // four styles equally likely without changing the style on edit, restore, reorder or restart.
    // Keep the mapping and enum values stable: existing libraries rely on them.
    public static TextPaperStyle ForEntry(string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        return (TextPaperStyle)(SHA256.HashData(Encoding.UTF8.GetBytes(identity))[0] & 3);
    }
}
