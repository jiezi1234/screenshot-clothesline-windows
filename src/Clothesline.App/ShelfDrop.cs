using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class ShelfController
{
    private readonly ShelfDropWindow dropTarget = new();
    private CardWindow? draggedCard;
    private bool internalDropHandled, importingDrop, suppressDropUntilRelease;
    private long? dropNearSince;
    private int pageDirection;
    private long pageEdgeSince;
    private PixelRect dropBounds;
    private double dropScale = 1;
    private nint dropMonitor;
    private int hoverSlot = -1, hoverPage = -1;
    public Action<string, string?>? ReorderRequested { get; set; }
    public Func<string[], string?, Task>? IncomingDropRequested { get; set; }
    internal string[] OrderedIds => cards.Select(c => c.Item.EntryId ?? c.Item.Path).ToArray();
    private void InitializeDropTarget() => AttachDropTarget(dropTarget);
    private void AttachDropTarget(Window target)
    {
        target.AllowDrop = true;
        target.PreviewDragEnter += DragOverShelf;
        target.PreviewDragOver += DragOverShelf;
        target.PreviewDrop += DropOnShelf;
    }
    internal static string[] ImageDropPaths(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent(DataFormats.FileDrop, false) || data.GetData(DataFormats.FileDrop, false) is not string[] paths || paths.Length > 100) return [];
            return paths.Where(path => Path.IsPathFullyQualified(path) && MediaFiles.IsImage(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch { return []; } // External OLE data may be unavailable or malformed; never interpret it as commands.
    }
    private bool CanReceive(DragEventArgs e) => !disposed && !SessionBlocked && !textHover.IsEditing && !hoverPreview.IsEditing && !importingDrop && (e.AllowedEffects & DragDropEffects.Copy) != 0
        && (!SuppressFullscreen || !Native.IsFullscreen(Native.MonitorAt(Native.Cursor)))
        && (draggedCard is not null || (IncomingDropRequested is not null && ImageDropPaths(e.Data).Length > 0));
    internal string DragCard(CardWindow card, ShellTransfer.DragOperation? perform)
    {
        ResetShelfHover();
        draggedCard = card; internalDropHandled = false; suppressDropUntilRelease = false;
        try
        {
            var result = perform is null ? ShellTransfer.Drag(card, card.Item) : ShellTransfer.Drag(new System.Windows.Interop.WindowInteropHelper(card).Handle, card.Item, perform);
            return internalDropHandled ? T("已调整晾衣绳顺序，文件保留。") : result;
        }
        finally { draggedCard = null; HideDropTarget(); }
    }
    internal void TrackDropTarget(long now, PixelPoint cursor, MonitorInfo monitor, bool leftDown)
    {
        if (localReorder is not null) return;
        if (!leftDown) { suppressDropUntilRelease = false; HideDropTarget(); return; }
        if (Native.GetAsyncKeyState(0x1B) < 0) suppressDropUntilRelease = true;
        if (textHover.IsEditing || textHover.Contains(cursor) || hoverPreview.IsEditing || hoverPreview.Contains(cursor) || suppressDropUntilRelease || importingDrop || SessionBlocked || (SuppressFullscreen && Native.IsFullscreen(monitor))) { HideDropTarget(); return; }
        // Do not show a target for a click, long press or menu on one of our cards.
        if (draggedCard is null && cards.Any(c => c.InteractionActive)) { HideDropTarget(); return; }
        if (IncomingDropRequested is null && draggedCard is null) return;
        var dpi = dropMonitor == monitor.Handle ? dropScale : Math.Max(1, Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(rope).EnsureHandle()) / 96.0);
        var near = ShelfDropGeometry.Activation(monitor.Bounds, monitor.WorkArea, dpi).Contains(cursor)
            || (presented && activeMonitor?.Handle == monitor.Handle && shelfArea.Contains(cursor))
            || (dropTarget.IsVisible && dropMonitor == monitor.Handle && dropBounds.Contains(cursor));
        if (!near) { HideDropTarget(); return; }
        dropNearSince ??= now;
        if (now - dropNearSince >= 180 && (!dropTarget.IsVisible || dropMonitor != monitor.Handle))
            ShowDropTarget(monitor, false, T("拖入图片，松手挂上（最多 100 张）"));
    }
    private void ShowDropTarget(MonitorInfo monitor, bool expanded, string text)
    {
        ResetShelfHover();
        if (dropMonitor != monitor.Handle || !dropTarget.IsVisible) { dropScale = Native.MoveAndGetScale(dropTarget, monitor); dropMonitor = monitor.Handle; }
        if (expanded)
        {
            var slots = ShelfZones.Row(monitor.WorkArea, dropScale, 1, cardScale);
            var height = Math.Min(monitor.WorkArea.Height, slots[0].Bottom - monitor.WorkArea.Y + (int)(24 * dropScale));
            dropBounds = new(monitor.WorkArea.X + (int)(8 * dropScale), monitor.WorkArea.Y, Math.Max(1, monitor.WorkArea.Width - (int)(16 * dropScale)), height);
        }
        else dropBounds = ShelfDropGeometry.Activation(monitor.Bounds, monitor.WorkArea, dropScale);
        dropTarget.Present(dropBounds, false); dropTarget.Feedback(text);
    }
    private void HideDropTarget() { CancelLocalReorder(); RestorePreviewCards(); if (dropTarget.IsVisible) dropTarget.Dismiss(); dropNearSince = null; pageDirection = 0; hoverSlot = hoverPage = -1; }
    private string? DropPosition(PixelPoint cursor)
    {
        var monitor = activeMonitor ?? Native.MonitorAt(cursor);
        if (DragCards.Count == 0) { hoverSlot = 0; return null; }
        var slots = DragSlots;
        var offset = page * DragCapacity; var count = Math.Min(slots.Count, DragCards.Count - offset);
        if (hoverPage != page) { hoverPage = page; hoverSlot = -1; }
        var slot = hoverSlot = ShelfDropGeometry.StableSlot(slots, count, hoverSlot, cursor.X, (int)(8 * scale));
        var next = DragCards.ElementAtOrDefault(offset + slot)?.Item;
        return next?.EntryId ?? next?.Path;
    }
    private void DragOverShelf(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (!CanReceive(e)) { RestorePreviewCards(); dropTarget.Feedback(T("此处只接受本地图片文件。")); return; }
        UpdateDropFeedback(Native.Cursor, clock.ElapsedMilliseconds);
        e.Effects = DragDropEffects.Copy;
    }
    internal void UpdateDropFeedback(PixelPoint cursor, long now)
    {
        var monitor = Native.MonitorAt(cursor);
        if (!presented || activeMonitor?.Handle != monitor.Handle) { RestorePreviewCards(); Prepare(monitor); gate.Show(clock.ElapsedMilliseconds); Present(); }
        UpdateDragPage(cursor, monitor, now);
        DropPosition(cursor);
        ShowDropTarget(monitor, true, draggedCard is null ? T("松手挂上图片，原文件保留。") : T("拖动调整顺序；在左右边缘停留可翻页。"));
        RenderIncomingGap();
    }
    private void UpdateDragPage(PixelPoint cursor, MonitorInfo monitor, long now)
    {
        var zone = InteractionArea;
        var direction = cursor.X < zone.X || cursor.X >= zone.Right ? 0 : cursor.X < zone.X + 48 * scale ? -1 : cursor.X > zone.Right - 48 * scale ? 1 : 0;
        if (direction != pageDirection) { pageDirection = direction; pageEdgeSince = now; }
        else if (direction != 0 && now - pageEdgeSince >= 650)
        {
            var next = Math.Clamp(page + direction, 0, Math.Max(0, (DragCards.Count - 1) / DragCapacity));
            if (next != page) { page = next; Present(); dropTarget.InvalidatePlacement(); }
            pageEdgeSince = now;
        }
    }
    private async void DropOnShelf(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (!CanReceive(e)) { HideDropTarget(); return; }
        var position = DropPosition(Native.Cursor);
        var paths = draggedCard is null ? ImageDropPaths(e.Data) : [];
        HideDropTarget(); suppressDropUntilRelease = true;
        try
        {
            if (draggedCard is CardWindow source)
            {
                MoveCardBefore(source, position); internalDropHandled = true;
                e.Effects = DragDropEffects.Copy;
            }
            else if (IncomingDropRequested is not null && paths.Length > 0)
            {
                e.Effects = DragDropEffects.Copy; importingDrop = true;
                await IncomingDropRequested(paths, position);
            }
        }
        catch (Exception ex) { Status?.Invoke(T("操作未完成：") + ex.Message); }
        finally { importingDrop = false; if (!disposed && presented) gate.Show(clock.ElapsedMilliseconds); }
    }
    internal void MoveCardBefore(CardWindow source, string? beforeId, bool present = true)
    {
        if (!cards.Contains(source)) return;
        if ((source.Item.EntryId ?? source.Item.Path) == beforeId) return;
        var target = beforeId is null ? null : cards.FirstOrDefault(c => (c.Item.EntryId ?? c.Item.Path) == beforeId);
        beforeId = target?.Item.EntryId ?? target?.Item.Path;
        if (source.Item.EntryId is string id) ReorderRequested?.Invoke(id, beforeId);
        cards.Remove(source); cards.Insert(target is null ? cards.Count : cards.IndexOf(target), source);
        if (present && presented) Present();
    }
}
