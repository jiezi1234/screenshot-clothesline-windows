namespace Clothesline.Core;

public static class ShelfZones
{
    public static int Limit(int totalCapacity) => Math.Clamp(totalCapacity, 0, 100) / 2;
    public static PixelRect Area(PixelRect work, double scale, bool text)
    {
        var gap = Math.Min(Math.Max(0, work.Width - 2), (int)Math.Round(24 * scale));
        var width = Math.Max(1, (work.Width - gap) / 2);
        return new(text ? work.Right - width : work.X, work.Y, width, work.Height);
    }
    public static int DisplayCapacity(PixelRect work, double scale, double cardScale = 1)
    {
        var full = DesktopGeometry.Cards(work, scale, 100, cardScale).Count;
        var half = DesktopGeometry.Cards(Area(work, scale, false), scale, 100, cardScale).Count;
        return Math.Max(1, Math.Min(full / 2, half)) * 2;
    }
    public static IReadOnlyList<PixelRect> Cards(PixelRect work, double scale, int count, bool text, double cardScale = 1)
        => DesktopGeometry.Cards(Area(work, scale, text), scale, Math.Min(count, DisplayCapacity(work, scale, cardScale) / 2), cardScale);
}
