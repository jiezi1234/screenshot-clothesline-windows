using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class ShelfController
{
    internal enum ReorderExit { Committed, Cancelled, Export }
    private sealed class ReorderGesture(CardWindow source, PixelPoint cursor, int offset, bool poll)
    {
        public readonly CardWindow Source = source;
        public readonly int GripOffset = offset;
        public readonly bool PollInput = poll;
        public readonly TaskCompletionSource<ReorderExit> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PixelPoint Cursor = cursor;
        public int Slot = -1, Page = -1;
        public string? Before;
        public bool Ending, Committed;
    }
    private ReorderGesture? localReorder;
    private readonly HashSet<CardWindow> previewCards = new();
    private async Task<string> DragCardWithPreview(CardWindow card)
    {
        var outcome = await BeginLocalReorder(card, Native.Cursor);
        if (outcome == ReorderExit.Export && !disposed && !SessionBlocked) return DragCard(card, null);
        return outcome == ReorderExit.Committed ? T("已调整晾衣绳顺序，文件保留。") : T("拖拽未完成，原文件和卡片已保留。");
    }
    internal Task<ReorderExit> BeginLocalReorder(CardWindow source, PixelPoint cursor, bool pollInput = true)
    {
        if (localReorder is not null || !presented || !cards.Contains(source)) return Task.FromResult(ReorderExit.Cancelled);
        var slots = DesktopGeometry.Cards(activeMonitor!.WorkArea, scale, cards.Count, cardScale);
        var slot = cards.IndexOf(source) - page * slots.Count;
        if (slot < 0 || slot >= slots.Count) return Task.FromResult(ReorderExit.Cancelled);
        Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(source).Handle, out var sourceBounds);
        var grip = (int)System.Math.Round((cursor.X - sourceBounds.Left) * slots[slot].Width / (double)System.Math.Max(1, sourceBounds.Right - sourceBounds.Left));
        ResetShelfHover();
        var gesture = new ReorderGesture(source, cursor, grip, pollInput);
        localReorder = gesture; pageDirection = 0;
        try
        {
            ShowDropTarget(activeMonitor, true, T("拖动调整顺序；在左右边缘停留可翻页。"));
            UpdateLocalReorder(cursor, true, false, clock.ElapsedMilliseconds);
            if (pollInput && localReorder == gesture && !gesture.Ending)
            {
                if (!dropTarget.CaptureMouse()) { CancelLocalReorder(); return gesture.Completion.Task; }
                CompositionTarget.Rendering += ReorderFrame;
            }
        }
        catch (Exception ex) { CleanupReorder(gesture); gesture.Completion.TrySetException(ex); }
        return gesture.Completion.Task;
    }
    private void ReorderFrame(object? sender, EventArgs e) => PollLocalReorder();
    private void PollLocalReorder()
    {
        if (localReorder is not { PollInput: true, Ending: false }) return;
        UpdateLocalReorder(Native.Cursor, Native.GetAsyncKeyState(1) < 0, Native.GetAsyncKeyState(0x1B) < 0 || !dropTarget.IsMouseCaptured, clock.ElapsedMilliseconds);
    }
    internal void UpdateLocalReorder(PixelPoint cursor, bool leftDown, bool escape, long now)
    {
        if (localReorder is not { Ending: false } gesture) return;
        try
        {
            if (disposed || SessionBlocked || !presented) { CancelLocalReorder(); return; }
            gesture.Cursor = cursor;
            var monitor = activeMonitor!;
            var slots = DesktopGeometry.Cards(monitor.WorkArea, scale, cards.Count, cardScale);
            var band = new PixelRect(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Width, slots[0].Bottom - monitor.Bounds.Y + (int)(52 * scale));
            if (escape) { _ = FinishLocalReorder(ReorderExit.Cancelled); return; }
            if (!band.Contains(cursor)) { _ = FinishLocalReorder(leftDown ? ReorderExit.Export : ReorderExit.Cancelled); return; }
            UpdateDragPage(cursor, monitor, now);
            if (gesture.Page != page) { gesture.Page = page; gesture.Slot = -1; }
            var count = Math.Min(slots.Count, cards.Count - page * slots.Count);
            gesture.Slot = ShelfDropGeometry.StableSlot(slots, count, gesture.Slot, cursor.X, (int)(8 * scale));
            var before = cards.ElementAtOrDefault(page * slots.Count + gesture.Slot)?.Item;
            gesture.Before = before?.EntryId ?? before?.Path;
            if (!leftDown) { _ = FinishLocalReorder(ReorderExit.Committed); return; }
            RenderLocalReorder(gesture, cursor, false);
        }
        catch (Exception ex) { CleanupReorder(gesture); gesture.Completion.TrySetException(ex); }
    }
    private void RenderLocalReorder(ReorderGesture gesture, PixelPoint cursor, bool settling)
    {
        var monitor = activeMonitor!;
        var slots = DesktopGeometry.Cards(monitor.WorkArea, scale, cards.Count, cardScale);
        var order = cards.ToList();
        if (!settling)
        {
            var target = cards.FirstOrDefault(c => (c.Item.EntryId ?? c.Item.Path) == gesture.Before);
            if (target != gesture.Source) { order.Remove(gesture.Source); order.Insert(target is null ? order.Count : order.IndexOf(target), gesture.Source); }
        }
        var sourceIndex = order.IndexOf(gesture.Source);
        ShowDropTarget(monitor, true, T("拖动调整顺序；在左右边缘停留可翻页。"));
        dropTarget.BeginPreviewFrame();
        for (var i = 0; i < slots.Count && page * slots.Count + i < order.Count; i++)
        {
            var index = page * slots.Count + i; var card = order[index];
            var originalSlot = cards.IndexOf(card) - page * slots.Count;
            var origin = slots[Math.Clamp(originalSlot, 0, slots.Count - 1)];
            if (card == gesture.Source && !settling) continue;
            // Positive WPF rotation about the pin leans the lower edge left.
            var angle = settling || !MotionAllowed ? 0 : index == sourceIndex - 1 ? 5 : index == sourceIndex + 1 ? -5 : 0;
            dropTarget.Preview(card, origin, slots[i], dropBounds, scale, angle, MotionAllowed);
        }
        if (!settling)
        {
            var originalSlot = Math.Clamp(cards.IndexOf(gesture.Source) - page * slots.Count, 0, slots.Count - 1);
            var origin = slots[originalSlot];
            var x = Math.Clamp(cursor.X - gesture.GripOffset, dropBounds.X, Math.Max(dropBounds.X, dropBounds.Right - origin.Width));
            dropTarget.Preview(gesture.Source, origin, origin with { X = x, Y = origin.Y - (int)(4 * scale) }, dropBounds, scale, 0, false, true);
        }
        dropTarget.EndPreviewFrame(); HidePreviewCards();
    }
    private async Task FinishLocalReorder(ReorderExit result)
    {
        if (localReorder is not { Ending: false } gesture) return;
        gesture.Ending = true; CompositionTarget.Rendering -= ReorderFrame;
        try
        {
            if (result == ReorderExit.Committed)
            {
                MoveCardBefore(gesture.Source, gesture.Before, false); gesture.Committed = true;
                page = cards.IndexOf(gesture.Source) / DisplayCapacity;
                Present(); dropTarget.InvalidatePlacement();
            }
            if (result != ReorderExit.Export && presented)
            {
                RenderLocalReorder(gesture, gesture.Cursor, true);
                if (MotionAllowed) await Task.Delay(170);
            }
            CleanupReorder(gesture); gesture.Completion.TrySetResult(result);
        }
        catch (Exception ex) { CleanupReorder(gesture); gesture.Completion.TrySetException(ex); }
    }
    private void HidePreviewCards()
    {
        foreach (var card in cards.Where(c => c.IsVisible))
        {
            if (previewCards.Add(card) || card.MotionRunning) card.StopMotion();
            card.Opacity = 0;
        }
    }
    private void RestorePreviewCards()
    {
        foreach (var card in previewCards) if (cards.Contains(card)) card.Opacity = 1;
        previewCards.Clear(); dropTarget.ClearPreview();
    }
    private void CleanupReorder(ReorderGesture gesture)
    {
        if (localReorder != gesture) return;
        localReorder = null; CompositionTarget.Rendering -= ReorderFrame;
        if (dropTarget.IsMouseCaptured) dropTarget.ReleaseMouseCapture();
        RestorePreviewCards(); if (dropTarget.IsVisible) dropTarget.Dismiss();
        pageDirection = 0; dropNearSince = null;
        if (!disposed && presented) gate.Show(clock.ElapsedMilliseconds);
    }
    private void CancelLocalReorder()
    {
        if (localReorder is not { } gesture) return;
        CleanupReorder(gesture); gesture.Completion.TrySetResult(gesture.Committed ? ReorderExit.Committed : ReorderExit.Cancelled);
    }
    private void RenderIncomingGap()
    {
        if (cards.Count == 0) return;
        var slots = DesktopGeometry.Cards(activeMonitor!.WorkArea, scale, cards.Count, cardScale);
        var count = Math.Min(slots.Count, cards.Count - page * slots.Count);
        var slot = Math.Clamp(hoverSlot, 0, count);
        dropTarget.BeginPreviewFrame();
        for (var i = 0; i < count; i++)
        {
            var rect = slots[i]; var shift = (int)(24 * scale) * (i < slot ? -1 : 1);
            var x = Math.Clamp(rect.X + shift, dropBounds.X + (int)(12 * scale), Math.Max(dropBounds.X + (int)(12 * scale), dropBounds.Right - rect.Width - (int)(12 * scale)));
            var angle = !MotionAllowed ? 0 : i == slot - 1 ? 5 : i == slot ? -5 : 0;
            dropTarget.Preview(cards[page * slots.Count + i], rect, rect with { X = x }, dropBounds, scale, angle, MotionAllowed);
        }
        dropTarget.EndPreviewFrame(); HidePreviewCards();
    }
}
