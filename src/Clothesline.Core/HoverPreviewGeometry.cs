namespace Clothesline.Core;

public static class HoverPreviewGeometry
{
    public const int PaddingDips = 12;
    public static bool Overlaps(PixelRect a, PixelRect b) => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;
    public static PixelRect? Place(PixelRect anchor, PixelRect work, double scale, int imageWidth, int imageHeight, IReadOnlyList<PixelRect> occupied)
    {
        if (!double.IsFinite(scale) || scale <= 0 || imageWidth < 1 || imageHeight < 1 || work.Width < 1 || work.Height < 1) throw new ArgumentOutOfRangeException();
        var inset = (int)Math.Ceiling(8 * scale); var gap = (int)Math.Ceiling(12 * scale); var padding = (int)Math.Ceiling(PaddingDips * scale);
        var area = new PixelRect(work.X + inset, work.Y + inset, Math.Max(0, work.Width - 2 * inset), Math.Max(0, work.Height - 2 * inset));
        PixelRect? Fit(PixelRect region, bool center)
        {
            var maxWidth = Math.Min(640 * scale, region.Width - 2 * padding); var maxHeight = Math.Min(480 * scale, region.Height - 2 * padding);
            if (maxWidth < 1 || maxHeight < 1) return null;
            var ratio = Math.Min(maxWidth / imageWidth, maxHeight / imageHeight);
            var width = Math.Max(1, (int)Math.Floor(imageWidth * ratio)) + 2 * padding;
            var height = Math.Max(1, (int)Math.Floor(imageHeight * ratio)) + 2 * padding;
            var x = center ? Math.Clamp(anchor.X + (anchor.Width - width) / 2, region.X, region.Right - width) : region.X;
            return new PixelRect(x, region.Y, width, height);
        }
        bool Clear(PixelRect rect) => rect.X >= area.X && rect.Right <= area.Right && rect.Y >= area.Y && rect.Bottom <= area.Bottom
            && !Overlaps(rect, anchor) && !occupied.Any(card => Overlaps(rect, card));
        if (Fit(area, false) is not PixelRect desired) return null;
        var y = Math.Clamp(anchor.Y, area.Y, area.Bottom - desired.Height);
        var right = desired with { X = anchor.Right + gap, Y = y };
        if (Clear(right)) return right;
        var left = desired with { X = anchor.X - gap - desired.Width, Y = y };
        if (Clear(left)) return left;
        // Keep every card accessible, including neighbors in a full row.
        var bottom = Math.Max(anchor.Bottom, occupied.Count == 0 ? anchor.Bottom : occupied.Max(r => r.Bottom));
        var below = area with { Y = Math.Max(area.Y, bottom + gap), Height = area.Bottom - Math.Max(area.Y, bottom + gap) };
        if (Fit(below, true) is PixelRect down && Clear(down)) return down;
        var top = Math.Min(anchor.Y, occupied.Count == 0 ? anchor.Y : occupied.Min(r => r.Y));
        var above = area with { Height = Math.Min(area.Bottom, top - gap) - area.Y };
        if (Fit(above, true) is PixelRect up && Clear(up)) return up with { Y = above.Bottom - up.Height };
        return null;
    }
}
