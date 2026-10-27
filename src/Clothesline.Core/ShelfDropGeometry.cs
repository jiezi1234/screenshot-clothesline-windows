namespace Clothesline.Core;

public static class ShelfDropGeometry
{
    public static PixelRect Activation(PixelRect bounds, PixelRect workArea, double scale)
    {
        var width = Math.Min(workArea.Width, (int)Math.Round(640 * scale));
        return new(workArea.X + (workArea.Width - width) / 2, bounds.Y, width,
            Math.Min(bounds.Height, Math.Max(0, workArea.Y - bounds.Y) + (int)Math.Round(80 * scale)));
    }
    public static int InsertionSlot(IReadOnlyList<PixelRect> slots, int visibleCount, int cursorX)
    {
        var count = Math.Clamp(visibleCount, 0, slots.Count);
        for (var i = 0; i < count; i++) if (cursorX < slots[i].X + slots[i].Width / 2) return i;
        return count;
    }
    public static int MarkerX(IReadOnlyList<PixelRect> slots, int visibleCount, int slot, double scale)
    {
        var count = Math.Clamp(visibleCount, 0, slots.Count);
        if (count == 0) throw new ArgumentException("A marker requires a visible slot.");
        if (slot <= 0) return slots[0].X - (int)(8 * scale);
        if (slot >= count) return slots[count - 1].Right + (int)(8 * scale);
        return (slots[slot - 1].Right + slots[slot].X) / 2;
    }
}
