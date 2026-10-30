using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Clothesline.Storage;

namespace Clothesline.App;

internal sealed partial class Dashboard
{
    private async System.Threading.Tasks.Task CopySelectedShelf()
    {
        if (shelfList.SelectedItem is not LibraryEntry entry) return;
        using var lease = library?.AcquireLease(entry.Path);
        try { var data = await ShellTransfer.CopyDataAsync(entry.Path); Clipboard.SetDataObject(data, true); SetStatus(T("已复制：") + entry.Title); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }
    private sealed record HistoryRow(LibraryEntry Entry)
    {
        public string Label => $"{(Entry.Missing ? T("文件失效") : Entry.Source == LibraryStore.OriginalSource ? T("编辑原版快照") : Entry.Ownership == Ownership.Managed ? T("应用保存") : T("外部引用"))}\n{(Entry.ArchivedAt is long time ? DateTimeOffset.FromUnixTimeMilliseconds(time).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "")}  {Entry.Error}";
    }
    private void Selected(Action<LibraryEntry> action)
    { if (history.SelectedItem is HistoryRow row) { try { action(row.Entry); } catch (Exception ex) { SetStatus(ex.Message); } } else SetStatus(T("请先选择一条历史记录。")); }
    private void Preview(LibraryEntry entry)
    {
        library?.OpenWorkspace(entry.Id, false);
    }
    private void UpdateHistory()
    {
        var id = (history.SelectedItem as HistoryRow)?.Entry.Id;
        var query = historySearch.Text.Trim();
        var rows = library?.Store.Entries(EntryState.History).Where(e => query.Length == 0 || e.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || e.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Select(e => new HistoryRow(e)).ToArray() ?? [];
        history.ItemsSource = rows; history.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == id) ?? rows.FirstOrDefault();
        var shelfId = (shelfList.SelectedItem as LibraryEntry)?.Id; var selectedIndex = shelfList.SelectedIndex;
        var entries = library?.Store.Entries(EntryState.Shelf).ToArray() ?? [];
        if (!(shelfList.ItemsSource is LibraryEntry[] old && old.SequenceEqual(entries)))
        {
            shelfList.ItemsSource = entries;
            shelfList.SelectedItem = entries.FirstOrDefault(e => e.Id == shelfId) ?? entries.ElementAtOrDefault(Math.Clamp(selectedIndex, 0, Math.Max(0, entries.Length - 1)));
        }
        shelfEmpty.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        historyEmpty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePageDescription(); UpdateCollectionState(); UpdateCapacity();
    }
    private void UpdateCapacity()
    {
        if (library is not null) capacity.Text = T("当前屏幕最多 {0} 项：图片与录像最多 {1} 项，文字最多 {1} 项。", library.Capacity, library.TypeCapacity);
    }
    private void FillSettings()
    {
        if (library is null) return; fillingSettings = true; var settings = library.Settings; screenshot.Text = settings.ScreenshotFolder; videoFolder.Text = settings.VideoFolder;
        for (var i = 0; i < 3; i++) extras[i].Text = i < settings.ExtraFolders.Length ? settings.ExtraFolders[i] : "";
        clipboard.IsChecked = settings.ClipboardEnabled; popup.IsChecked = settings.ShowNew; full.IsChecked = settings.SuppressFullscreen; motion.IsChecked = settings.ReduceMotion;
        UpdateCapacity(); retention.SelectedIndex = Array.IndexOf(new[] { 0, 7, 30 }, settings.RetentionDays);
        storage.SelectedIndex = settings.MaxManagedBytes >= 2L * 1024 * 1024 * 1024 ? 2 : settings.MaxManagedBytes >= 1024L * 1024 * 1024 ? 1 : 0;
        cardSize.SelectedIndex = Array.IndexOf(new[] { 80, 100, 120 }, settings.CardSizePercent); sound.IsChecked = settings.CaptureSound;
        protection.SelectedIndex = Array.IndexOf(protectionDurations, settings.RevealProtectionSeconds);
        language.SelectedValue = settings.Language;
        shortcutEnabled.IsChecked = settings.ShowShortcut.Length > 0 || settings.HideShortcut.Length > 0;
        showShortcut.Text = shortcutEnabled.IsChecked == true ? settings.ShowShortcut : LibrarySettings.DefaultShowShortcut;
        hideShortcut.Text = shortcutEnabled.IsChecked == true ? settings.HideShortcut : LibrarySettings.DefaultHideShortcut;
        showShortcut.IsEnabled = hideShortcut.IsEnabled = shortcutEnabled.IsChecked == true; login.IsChecked = library.Desktop?.LoginEnabled ?? settings.RunAtLogin; UpdateShortcutHint();
        fillingSettings = false; savedFingerprint = DraftFingerprint(); UpdateDraftState();
    }
    private void UpdateShortcutHint() => interactionHint.Text = library?.Desktop?.ShortcutStatus ?? (library is null ? T("诊断模式") : T("显示 {0}；收回 {1}", library.Settings.ShowShortcut, library.Settings.HideShortcut));
    private void SaveSettings()
    {
        if (library is null) return;
        library.ApplySettings(library.Settings with { ScreenshotFolder = screenshot.Text.Trim(), VideoFolder = videoFolder.Text.Trim(), ExtraFolders = extras.Select(e => e.Text.Trim()).Where(p => p.Length > 0).ToArray(), ClipboardEnabled = clipboard.IsChecked == true, Paused = library.Settings.Paused, ShowNew = popup.IsChecked == true, SuppressFullscreen = full.IsChecked == true, ReduceMotion = motion.IsChecked == true,
            Capacity = 0, RetentionDays = new[] { 0, 7, 30 }[Math.Max(0, retention.SelectedIndex)], MaxManagedBytes = new[] { 512L, 1024L, 2048L }[Math.Max(0, storage.SelectedIndex)] * 1024 * 1024,
            ShowShortcut = shortcutEnabled.IsChecked == true ? showShortcut.Text : "", HideShortcut = shortcutEnabled.IsChecked == true ? hideShortcut.Text : "", RunAtLogin = login.IsChecked == true,
            CardSizePercent = new[] { 80, 100, 120 }[Math.Max(0, cardSize.SelectedIndex)], RevealProtectionSeconds = protection.SelectedIndex >= 0 ? protectionDurations[protection.SelectedIndex] : Clothesline.Core.RevealGate.DefaultProtectionSeconds,
            CaptureSound = sound.IsChecked == true, Language = language.SelectedValue as string ?? "zh-CN" });
        FillSettings();
    }
    private async void Import()
    {
        if (library is null) return; var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = T("图片与 MP4 录像|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp;*.heic;*.heif;*.mp4|图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp;*.heic;*.heif|MP4 录像|*.mp4") };
        if (dialog.ShowDialog(this) == true) await library.ImportFiles(dialog.FileNames);
    }
    private string DraftFingerprint() => System.Text.Json.JsonSerializer.Serialize(new
    {
        screenshot.Text, Video = videoFolder.Text, Extras = extras.Select(e => e.Text).ToArray(),
        Clipboard = clipboard.IsChecked, Popup = popup.IsChecked, Full = full.IsChecked, Motion = motion.IsChecked,
        Retention = retention.SelectedIndex, Storage = storage.SelectedIndex,
        ShortcutEnabled = shortcutEnabled.IsChecked, Show = showShortcut.Text, Hide = hideShortcut.Text, Login = login.IsChecked,
        Size = cardSize.SelectedIndex, Protection = protection.SelectedIndex, Sound = sound.IsChecked, Language = language.SelectedValue
    });
    private void AttachDraftTracking()
    {
        foreach (var text in new[] { screenshot, videoFolder, showShortcut, hideShortcut }.Concat(extras)) text.TextChanged += (_, _) => UpdateDraftState();
        foreach (var check in new[] { clipboard, popup, full, motion, shortcutEnabled, login, sound }) { check.Checked += (_, _) => UpdateDraftState(); check.Unchecked += (_, _) => UpdateDraftState(); }
        foreach (var choice in new[] { retention, storage, cardSize, protection, language }) choice.SelectionChanged += (_, _) => UpdateDraftState();
    }
    private void UpdateDraftState()
    {
        if (fillingSettings) return;
        var dirty = library is not null && HasUnsavedChanges;
        draftStatus.Text = dirty ? T("有未保存的更改") : T("设置已保存"); save.IsEnabled = discard.IsEnabled = dirty;
    }
    private void UpdateCollectionState()
    {
        var paused = library?.Settings.Paused == true;
        collectionState.Text = library is null ? T("诊断模式") : paused ? T("自动收集已暂停。") : T("正在收集");
        pauseCollection.Content = paused ? T("恢复收集") : T("暂停收集"); pauseCollection.IsEnabled = library is not null;
        System.Windows.Automation.AutomationProperties.SetName(pauseCollection, (string)pauseCollection.Content);
    }
    public void SetStatus(string text)
    {
        status.Text = text + (library is not null && library.Settings.Language != Clothesline.Core.UiText.Language ? "\n" + T("语言设置已保存，重启应用后生效。") : "");
        status.ToolTip = status.Text;
    }
}
