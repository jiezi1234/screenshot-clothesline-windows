using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class ShelfController
{
    private readonly ShelfHoverPreview hoverPreview;
    private readonly ShelfTextHover textHover = new();
    private CardWindow? emphasizedCard;
    private PixelRect emphasizedHome;
    internal CardWindow? EmphasizedCard => emphasizedCard;

    private void ResetShelfHover() { hoverPreview.Reset(); textHover.Reset(); ResetStackEmphasis(); }
    private void ResetStackEmphasis()
    {
        if (emphasizedCard is not { } previous) return;
        emphasizedCard = null;
        if (!previous.IsVisible || previous.Retiring) return;
        previous.SetStackEmphasis(emphasizedHome, false, false);
        // Restore just this card's original layer. Do not raise the row above another app.
        var index = cards.IndexOf(previous);
        var next = index < 0 ? null : cards.Skip(index + 1).FirstOrDefault(c => c.IsVisible && !c.Retiring);
        if (next is not null) Native.PutBehind(previous, next);
    }
    internal void TrackHoverPreview(long now, PixelPoint cursor, bool buttonsDown, bool escape)
    {
        if (disposed || !presented || activeMonitor is null || SessionBlocked || dropTarget.IsVisible)
        { ResetShelfHover(); return; }
        var hit = Native.WindowFromPoint(new Native.Point { X = cursor.X, Y = cursor.Y });
        var card = cards.FirstOrDefault(c => c.IsVisible && !c.Retiring && c.Opacity > 0 && new WindowInteropHelper(c).Handle == hit);
        if (!CardInteractionBusy && textHover.Hold(now, cursor, escape, card)) { hoverPreview.Reset(); return; }
        if (!CardInteractionBusy && (!buttonsDown || card is null) && hoverPreview.Hold(now, cursor, escape, card)) { textHover.Reset(); return; }
        if (Busy || buttonsDown || Mouse.Captured is not null)
        {
            // Keep the pressed card still until its click/menu/long press finishes.
            hoverPreview.Reset(); textHover.Reset(); return;
        }
        var row = VisibleRow().ToArray();
        var home = row.FirstOrDefault(pair => pair.Card == card).Rect;
        var stacked = card is not null && row.Any(pair => pair.Card != card && pair.Rect.X < home.Right && pair.Rect.Right > home.X);
        if (!stacked) ResetStackEmphasis();
        else if (card != emphasizedCard)
        {
            ResetStackEmphasis();
            if (card is not null)
            {
                emphasizedHome = home; emphasizedCard = card;
                card.SetStackEmphasis(DesktopGeometry.EmphasizedCard(emphasizedHome, InteractionArea), true, MotionAllowed);
            }
        }
        // The second-stage preview also avoids the enlarged card.
        var occupied = cards.Where(c => c.IsVisible).Select(c =>
        {
            Native.GetWindowRect(new WindowInteropHelper(c).Handle, out var rect); return rect.Pixels;
        }).ToArray();
        textHover.Track(card, activeMonitor, occupied, now, escape, MotionAllowed);
        hoverPreview.Track(card is { Item.IsVideo: false, Item.IsText: false } ? card : null, activeMonitor, occupied, now, escape, MotionAllowed);
    }
}
