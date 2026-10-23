namespace Clothesline.Core;

public static class MediaFiles
{
    public static bool IsVideo(string path) => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
    public static bool IsImage(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif" }.Contains(Path.GetExtension(path).ToLowerInvariant());
}
