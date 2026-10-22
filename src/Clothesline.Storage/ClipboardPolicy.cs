namespace Clothesline.Storage;

public static class ClipboardPolicy
{
    public static bool Accept(IEnumerable<string> formats, Func<string, bool?> flag)
    {
        var names = formats.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (new[] { "ScreenshotClothesline.OwnCopy", "ExcludeClipboardContentFromMonitorProcessing", "ClipboardViewerIgnore", "FileDrop", "Text", "UnicodeText", "HTML Format", "Rich Text Format" }.Any(names.Contains)) return false;
        if (flag("CanIncludeInClipboardHistory") == false || flag("CanUploadToCloudClipboard") == false) return false;
        return new[] { "PNG", "Bitmap", "DeviceIndependentBitmap", "Format17" }.Any(names.Contains);
    }
}
