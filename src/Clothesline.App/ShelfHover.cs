using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class ShelfController
{
    private readonly ShelfHoverPreview hoverPreview;
    internal void TrackHoverPreview(long now, PixelPoint cursor, bool buttonsDown, bool escape)
    {
        if (disposed || !presented || SessionBlocked || Busy || dropTarget.IsVisible || buttonsDown || Mouse.Captured is not null)
        { hoverPreview.Reset(); return; }
        var hit = Native.WindowFromPoint(new Native.Point { X = cursor.X, Y = cursor.Y });
        var card = cards.FirstOrDefault(c => c.IsVisible && !c.Item.IsVideo && c.Opacity > 0 && new WindowInteropHelper(c).Handle == hit);
        var slots = DesktopGeometry.Cards(activeMonitor!.WorkArea, scale, cards.Count, cardScale);
        hoverPreview.Track(card, activeMonitor, slots, now, escape, MotionAllowed);
    }
}
