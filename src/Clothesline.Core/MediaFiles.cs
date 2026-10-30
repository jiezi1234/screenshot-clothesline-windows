namespace Clothesline.Core;

public static class MediaFiles
{
    public static bool IsText(string path) => Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);
    public static bool IsVideo(string path) => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
    public static bool IsExtendedImage(string path) => new[] { ".webp", ".heic", ".heif" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsImage(string path) => IsExtendedImage(path) || new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif" }.Contains(Path.GetExtension(path).ToLowerInvariant());
}
