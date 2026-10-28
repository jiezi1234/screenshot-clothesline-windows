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
    public static int StableSlot(IReadOnlyList<PixelRect> slots, int visibleCount, int previous, int cursorX, int hysteresis)
    {
        var count = Math.Clamp(visibleCount, 0, slots.Count);
        if (previous < 0) return InsertionSlot(slots, count, cursorX);
        var slot = Math.Clamp(previous, 0, count);
        while (slot < count && cursorX > slots[slot].X + slots[slot].Width / 2 + hysteresis) slot++;
        while (slot > 0 && cursorX < slots[slot - 1].X + slots[slot - 1].Width / 2 - hysteresis) slot--;
        return slot;
    }
}
