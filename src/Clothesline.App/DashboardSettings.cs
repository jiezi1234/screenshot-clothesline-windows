using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class Dashboard
{
    // Both the coss form and the offline native fallback edit the same draft controls.
    private sealed record Setting(string Id, string Label, FrameworkElement Control, string Help = "", bool Folder = false)
    {
        public string Kind => Control switch { CheckBox => "switch", ComboBox => "choice", TextBox => Folder ? "folder" : "text", System.Windows.Controls.Button => "button", _ => "info" };
        public object? Value => Control switch { CheckBox c => c.IsChecked == true, ComboBox c => c.SelectedIndex, TextBox t => t.Text, TextBlock t => t.Text, _ => null };
        public string[]? Choices => Control is ComboBox combo ? combo.Items.Cast<object>().Select(item => item is UiText.Locale locale ? locale.Name : item.ToString()!).ToArray() : null;
    }
    private sealed record SettingGroup(string Id, string Title, Setting[] Fields);
    private readonly SettingGroup[] settingsGroups;
    private readonly Dictionary<string, Setting> settingsById;
    private readonly Viewbox nativeSettings = new() { StretchDirection = StretchDirection.DownOnly };

    private SettingGroup[] CreateSettingGroups()
    {
        Setting Field(string id, string label, FrameworkElement control, string help = "", bool folder = false)
            => new(id, T(label), control, T(help), folder);
        Setting Toggle(string id, CheckBox box, string help = "") => new(id, (string)box.Content, box, T(help));
        shortcutEnabled.Checked += (_, _) => showShortcut.IsEnabled = hideShortcut.IsEnabled = true;
        shortcutEnabled.Unchecked += (_, _) => showShortcut.IsEnabled = hideShortcut.IsEnabled = false;
        var retry = Button(T("重试当前注册"), () => { library?.Desktop?.RetryShortcut(); UpdateShortcutHint(); SendSettingInfo(); });
        return [
            new("sources", T("来源与设置"), [
                Field("screenshot", "截图目录", screenshot, "首次选择只监听新增文件；已有文件可手动导入。", true),
                Field("video", "录像目录", videoFolder, "支持 H.264 视频与 AAC 音频或无音轨，最高 4K、单文件 2 GiB。首次选择不导入旧录像；留空关闭。", true),
                .. extras.Select((box, i) => new Setting("extra" + i, T("额外目录 {0}", i + 1), box,
                    i == 2 ? T("额外目录：最多 3 个，不包含子目录") : "", true))]),
            new("collection", T("收集行为"), [
                Toggle("clipboard", clipboard), Toggle("popup", popup), Toggle("full", full),
                Field("retention", "历史期限", retention, "取下或超出容量会进入历史。到期时外部原文件保留；应用保存的内容仅尝试移入回收站，失败保留记录。"),
                Field("storage", "自管内容上限", storage),
                Field("capacity", "晾衣绳容量", capacity, "两类各限当前屏幕容量的一半，超额仅将同类最早加入的项目移入历史。图片与文字共用整条绳子，可自由混排和拖动排序。")]),
            new("shortcuts", T("快捷键与启动"), [
                Toggle("shortcutEnabled", shortcutEnabled),
                Field("showShortcut", "显示快捷键", showShortcut, "默认 Ctrl+Down（↓）显示，Ctrl+Up（↑）收回；重复按不会反向切换。留空可单独关闭，冲突时保留原设置。"),
                Field("hideShortcut", "收回快捷键", hideShortcut, "组合至少包含 Ctrl 或 Alt，可增加 Shift；支持字母、数字、Up/Down（↑/↓）和 F1–F24（不含 F12）。关闭后仍可使用顶部悬停或托盘。"),
                Field("shortcutStatus", "全局快捷键", interactionHint), Field("retry", "重试当前注册", retry),
                Toggle("login", login, "登录时启动到托盘，不弹出控制面板。移动便携版目录后，请从新位置打开应用并重新保存设置以更新启动路径。Windows 的启动应用设置可能另外禁用此项。")]),
            new("appearance", T("外观与语言"), [
                Field("cardSize", "卡片大小", cardSize, "大小立即生效；每类名额随屏幕和卡片大小调整，超额的同类最旧项目进入历史。"),
                Field("protection", "展开保护期", protection, "给鼠标移入晾衣绳预留时间；保存后立即生效，已展开时重新计时。"),
                Toggle("motion", motion, "关闭卡片入场、摆动、取下及绳线淡入；Windows 关闭动画时也会自动停用。鼠标进入卡片时立即停稳。"),
                Toggle("sound", sound, "连续收集最多每 2 秒播放一次；暂停或当前屏幕全屏时静音。提示音遵循 Windows 声音方案和音量设置。"),
                Field("language", "界面语言（重启后生效）", language, "保存后，请先保存编辑内容，再从托盘退出并重新打开应用。")])
        ];
    }

    private void BuildNativeSettings()
    {
        var columns = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Width = 1110 };
        nativeSettings.Child = columns;
        foreach (var group in settingsGroups)
        {
            var fields = Stack();
            foreach (var field in group.Fields)
            {
                AutomationProperties.SetName(field.Control, field.Label);
                var row = field.Control is CheckBox or System.Windows.Controls.Button ? field.Control :
                    Field(field.Label, field.Folder ? FolderRow((TextBox)field.Control) : field.Control);
                fields.Children.Add(row);
                if (field.Help.Length > 0) { row.ToolTip = field.Help; AutomationProperties.SetHelpText(field.Control, field.Help); }
            }
            var card = Card(group.Title, fields);
            columns.Children.Add(card);
        }
    }
}
