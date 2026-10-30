namespace Clothesline.Storage;

public enum ClipboardContentKind { None, Image, Text }
public static class ClipboardPolicy
{
    public static bool Accept(IEnumerable<string> formats, Func<string, bool?> flag)
        => Select(formats, flag) == ClipboardContentKind.Image;
    public static ClipboardContentKind Select(IEnumerable<string> formats, Func<string, bool?> flag)
    {
        var names = formats.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (new[] { "ScreenshotClothesline.OwnCopy", "ExcludeClipboardContentFromMonitorProcessing", "ClipboardViewerIgnore", "FileDrop" }.Any(names.Contains)) return ClipboardContentKind.None;
        if (flag("CanIncludeInClipboardHistory") == false || flag("CanUploadToCloudClipboard") == false) return ClipboardContentKind.None;
        if (names.Contains("UnicodeText") || names.Contains("Text")) return ClipboardContentKind.Text;
        if (names.Contains("HTML Format") || names.Contains("Rich Text Format")) return ClipboardContentKind.None;
        return new[] { "PNG", "Bitmap", "DeviceIndependentBitmap", "Format17" }.Any(names.Contains) ? ClipboardContentKind.Image : ClipboardContentKind.None;
    }
}
