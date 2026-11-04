namespace Clothesline.Storage;

public enum ClipboardContentKind { None, Image, Text }
public static class ClipboardPolicy
{
    private static readonly string[] ImageFormats = ["PNG", "Bitmap", "DeviceIndependentBitmap", "Format17"];
    private static readonly string[] WeChatFormats = ["WeChat_RichEdit_Format", "QQ_Unicode_RichEdit_Format", "x-application-mimetype-xwechat"];

    public static bool Accept(IEnumerable<string> formats, Func<string, bool?> flag)
        => Select(formats, flag) == ClipboardContentKind.Image;
    public static ClipboardContentKind Select(IEnumerable<string> formats, Func<string, bool?> flag)
    {
        var names = formats.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (new[] { "ScreenshotClothesline.OwnCopy", "ExcludeClipboardContentFromMonitorProcessing", "ClipboardViewerIgnore" }.Any(names.Contains)) return ClipboardContentKind.None;
        if (flag("CanIncludeInClipboardHistory") == false || flag("CanUploadToCloudClipboard") == false) return ClipboardContentKind.None;
        var hasImage = ImageFormats.Any(names.Contains);
        // WeChat screenshots expose a bitmap together with FileDrop and rich-text formats.
        // Some releases omit the product-specific formats, so the bitmap + temporary-file
        // combination is the stable signal. A normal Explorer file copy has no bitmap.
        if (hasImage && (names.Contains("FileDrop") || WeChatFormats.Any(names.Contains))) return ClipboardContentKind.Image;
        if (names.Contains("FileDrop")) return ClipboardContentKind.None;
        if (names.Contains("UnicodeText") || names.Contains("Text")) return ClipboardContentKind.Text;
        if (names.Contains("HTML Format") || names.Contains("Rich Text Format")) return ClipboardContentKind.None;
        return hasImage ? ClipboardContentKind.Image : ClipboardContentKind.None;
    }
}
