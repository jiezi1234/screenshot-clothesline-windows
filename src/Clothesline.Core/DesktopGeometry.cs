namespace Clothesline.Core;

public readonly record struct PixelPoint(int X, int Y);
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(PixelPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;
}

public static class DesktopGeometry
{
    public const double CardWidth = 212;
    public const double CardHeight = 190;

    public static PixelRect Trigger(PixelRect monitor, double scale)
    {
        var width = Math.Min(monitor.Width, (int)Math.Round(360 * scale));
        return new(monitor.X + (monitor.Width - width) / 2, monitor.Y, width,
            Math.Max(2, (int)Math.Round(3 * scale)));
    }

    public static IReadOnlyList<PixelRect> Cards(PixelRect workArea, double scale, int count, double cardScale = 1)
    {
        if (!double.IsFinite(scale) || scale <= 0 || !double.IsFinite(cardScale) || cardScale <= 0 || count < 0 || workArea.Width < 1 || workArea.Height < 1) throw new ArgumentOutOfRangeException();
        var top = Math.Min((int)(20 * scale), Math.Max(0, workArea.Height - 1));
        // User size and monitor DPI are independent. Tiny work areas still contain a complete card.
        var effective = Math.Min(scale * cardScale, Math.Min(workArea.Width / CardWidth, (workArea.Height - top) / CardHeight));
        var width = Math.Max(1, (int)Math.Round(CardWidth * effective));
        var height = Math.Max(1, (int)Math.Round(CardHeight * effective));
        var step = width + Math.Max(1, (int)Math.Round(28 * scale * cardScale));
        var visible = Math.Min(count, Math.Max(1, (workArea.Width - (int)(32 * scale)) / step));
        var start = workArea.X + (workArea.Width - (visible * step - (step - width))) / 2;
        return Enumerable.Range(0, visible)
            .Select(i => new PixelRect(start + i * step, workArea.Y + top, width, height)).ToArray();
    }

    public static bool CoversMonitor(PixelRect window, PixelRect monitor, bool hasStandardFrame)
        => !hasStandardFrame && window.X <= monitor.X + 2 && window.Y <= monitor.Y + 2
           && window.Right >= monitor.Right - 2 && window.Bottom >= monitor.Bottom - 2;
}

/// Uses monotonic elapsed time, independent of UI timers and wall-clock changes.
public sealed class RevealGate
{
    private long? entered;
    private long? left;
    private long graceUntil;
    private bool mustLeaveTrigger;
    public bool Visible { get; private set; }
    public void Show(long now) { Visible = true; graceUntil = now + 2500; entered = left = null; }
    public void Hide() { Visible = false; entered = left = null; mustLeaveTrigger = true; }
    public void Tick(long now, bool inTrigger, bool inShelf, bool busy, bool blocked)
    {
        if (!inTrigger) mustLeaveTrigger = false;
        if (blocked && !busy) { Hide(); return; }
        if (!Visible)
        {
            if (!inTrigger || mustLeaveTrigger || busy) { entered = null; return; }
            entered ??= now;
            if (now - entered >= 350) Show(now);
        }
        else if (busy || inShelf || inTrigger || now < graceUntil) left = null;
        else
        {
            left ??= now;
            if (now - left >= 550) Hide();
        }
    }
}

public enum TransferDisposition { Keep, RemoveMissing, VerifyMove }
public static class TransferPolicy
{
    public const uint AllowedEffects = 1; // DROPEFFECT_COPY; saving a copy never offers source movement.
    public static TransferDisposition Decide(bool completed, bool exists, uint effect)
    {
        if (!completed) return TransferDisposition.Keep;
        if (!exists) return TransferDisposition.RemoveMissing;
        return (effect & 2) != 0 ? TransferDisposition.VerifyMove : TransferDisposition.Keep;
    }
}
