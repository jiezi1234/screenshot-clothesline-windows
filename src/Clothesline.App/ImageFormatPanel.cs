using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Clothesline.App;

internal sealed class ImageFormatPanel : StackPanel
{
    private readonly TextBlock results = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 14), FontSize = 15 };
    private readonly Button refresh = new() { Content = T("重新检测"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 8, 14, 8) };
    private bool running;
    internal ImageFormatCapability[]? Capabilities { get; private set; }
    internal ImageFormatPanel()
    {
        Margin = new Thickness(16);
        Children.Add(new TextBlock { Text = T("图片格式支持"), FontSize = 20 });
        Children.Add(new TextBlock { Text = T("PNG、JPEG、BMP、TIFF、GIF 首帧沿用现有支持。WebP、HEIC/HEIF 使用 Windows 图像组件，以下结果来自内置小样本的实际解码。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        results.Text = T("尚未检测。"); AutomationProperties.SetName(results, T("图片格式检测结果"));
        Children.Add(results); Children.Add(refresh);
        Children.Add(new TextBlock { Text = T("样本通过不代表所有编码变体均支持。样本无法解码时，请在 Windows 中安装或更新对应图像扩展，再重启应用检测；HEIC 可能还需要 HEVC 解码组件。应用不会自动安装扩展。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        Children.Add(new TextBlock { Text = T("新增格式沿用截图目录和额外目录。升级时已有 WebP/HEIC 不自动导入，可手动选择。只处理首帧或主图；复制提供 PNG 与原文件，编辑另存 PNG，拖出和另存原文件保留原格式。上限为 64 MiB、4000 万像素。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
        refresh.Click += async (_, _) => await Refresh();
        IsVisibleChanged += async (_, _) => { if (IsVisible && Capabilities is null) await Refresh(); };
    }
    internal async Task Refresh(Func<ImageFormatCapability[]>? probe = null)
    {
        if (running) return; running = true; refresh.IsEnabled = false; results.Text = T("正在检测图片格式…");
        try { Capabilities = await Task.Run(probe ?? ImageFormatSupport.ProbeAll); results.Text = string.Join("\n\n", Capabilities.Select(c => c.Summary)); }
        catch (Exception ex) { results.Text = T("格式检测未完成：") + ex.Message; }
        finally { running = false; refresh.IsEnabled = true; }
    }
}
