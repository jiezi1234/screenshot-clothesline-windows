using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;

namespace Clothesline.App;

internal static class ImageHoverChromeDiagnostics
{
    internal static void Check(HoverPreviewWindow window, string output, Action<string, bool> check)
    {
        var root = (FrameworkElement)window.Content;
        void Layout(int width, int height, string? screenshot = null)
        {
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            if (screenshot is null) return;
            var imageViewport = UiAccessibility.Descendants(root).OfType<ScrollViewer>().Single(x => x.Content == window.Canvas);
            File.AppendAllText(Path.Combine(output, "layout-metrics.txt"), $"{screenshot}: root {root.ActualWidth} x {root.ActualHeight}; viewport {imageViewport.ActualWidth} x {imageViewport.ActualHeight}; image scale {((ScaleTransform)window.Canvas.LayoutTransform).ScaleX}\n");
            var capture = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); capture.Render(root); DemoAssets.Save(capture, Path.Combine(output, screenshot + ".png"));
        }
        TElement Find<TElement>(string name) where TElement : FrameworkElement => UiAccessibility.Descendants(root).OfType<TElement>().Single(x => AutomationProperties.GetName(x) == T(name));
        void Tool(string name) => Find<ToggleButton>(name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Layout(620, 560);
        var tools = new[] { "浏览", "裁剪", "画笔", "箭头", "矩形", "文字", "马赛克" }.Select(Find<ToggleButton>).ToArray();
        var annotation = Find<TextBox>("文字内容"); var slider = UiAccessibility.Descendants(root).OfType<Slider>().Single();
        var viewport = UiAccessibility.Descendants(root).OfType<ScrollViewer>().Single(x => x.Content == window.Canvas);
        check("browse-hides-irrelevant-annotation-and-size-controls", slider.Parent is FrameworkElement { Visibility: Visibility.Collapsed } && annotation.Parent is FrameworkElement { Visibility: Visibility.Collapsed });
        Layout(596, 504, "compact-chrome-browse");
        check("browse-gives-image-at-least-eighty-percent-of-popup-height", viewport.ActualHeight >= 504 * .8);
        Tool("箭头"); Layout(620, 560, "light-toolbar-arrow");
        Layout(596, 504, "compact-chrome-arrow");
        check("drawing-keeps-at-least-three-quarters-of-popup-height-for-image", viewport.ActualHeight >= 504 * .75);
        var swatches = UiAccessibility.Descendants(root).OfType<ToggleButton>().Where(x => AutomationProperties.GetName(x).StartsWith(T("颜色") + ": ")).ToArray();
        swatches[3].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); slider.Value = 8;
        check("color-and-width-controls-change-active-drawing-settings", swatches.Count(x => x.IsChecked == true) == 1 && window.Canvas.Ink == (Color)ColorConverter.ConvertFromString("#5487AE") && window.Canvas.StrokeSize == 8);
        foreach (var width in new[] { 620, 352 })
        {
            Layout(width, 520, width == 352 ? "light-toolbar-compact" : null);
            var rectangles = tools.Select(x => x.TransformToAncestor(root).TransformBounds(new Rect(x.RenderSize))).ToArray();
            check($"toolbar-single-row-within-{width}-dip-popup", rectangles.All(x => x.Width >= 30 && x.Left >= 0 && x.Right <= width) && rectangles.Select(x => x.Top).Distinct().Count() == 1);
            check($"palette-and-slider-do-not-overlap-at-{width}-dips", swatches[^1].TransformToAncestor(root).Transform(new Point(swatches[^1].ActualWidth, 0)).X <= slider.TransformToAncestor(root).Transform(new Point()).X);
        }
        Tool("文字"); Layout(352, 520, "light-toolbar-text-compact");
        annotation.Text = "中文标注 / Note";
        check("text-tool-exposes-editable-annotation-without-overlapping-tools", annotation.ActualWidth > 100 && annotation.ActualHeight >= 30 && window.Canvas.Annotation == annotation.Text && AutomationProperties.GetName(slider) == T("字号"));
        Tool("马赛克"); Layout(620, 560, "light-toolbar-mosaic");
        check("mosaic-shows-size-without-color-or-annotation", swatches[0].Parent is FrameworkElement { Visibility: Visibility.Collapsed } && AutomationProperties.GetName(slider) == T("马赛克大小") && annotation.Parent is FrameworkElement { Visibility: Visibility.Collapsed });
        Tool("裁剪"); Layout(596, 504);
        check("crop-instruction-is-in-tooltip-without-consuming-image-height", Find<ToggleButton>("裁剪").ToolTip.ToString()!.Contains(T("拖动框选裁剪区域")) && viewport.ActualHeight >= 504 * .8);
        var previous = Find<Button>("上一步"); var next = Find<Button>("下一步");
        check("history-buttons-share-tool-row-and-keep-accessible-targets", new[] { previous, next }.All(x => x.ActualWidth >= 30 && Math.Abs(x.TransformToAncestor(root).Transform(new Point()).Y - tools[0].TransformToAncestor(root).Transform(new Point()).Y) < 1));
        var scale = Find<ComboBox>("缩放比例"); scale.SelectedIndex = 2; Layout(620, 560);
        check("one-hundred-percent-uses-physical-pixels", Math.Abs(((ScaleTransform)window.Canvas.LayoutTransform).ScaleX * VisualTreeHelper.GetDpi(window).DpiScaleX - 1) < .001);
        scale.SelectedIndex = 0; Tool("浏览"); Layout(620, 560, "light-toolbar-browse");
        check("fit-keeps-entire-image-within-viewport", window.Canvas.TransformToAncestor(root).TransformBounds(new Rect(window.Canvas.RenderSize)) is var bounds && bounds.Left >= 0 && bounds.Right <= 620 && bounds.Top >= 0 && bounds.Bottom <= 560);
        foreach (var name in new[] { "画笔", "箭头", "矩形", "文字", "马赛克", "裁剪", "浏览" })
        {
            Tool(name); Layout(596, 504);
            var imageBounds = window.Canvas.TransformToAncestor(viewport).TransformBounds(new Rect(window.Canvas.RenderSize));
            check("tool-switch-keeps-full-image-inside-viewport-" + window.Canvas.Tool, imageBounds.Left >= 0 && imageBounds.Top >= 0 && imageBounds.Right <= viewport.ActualWidth + 1 && imageBounds.Bottom <= viewport.ActualHeight + 1);
        }
        // Translation lengths can switch the row to icons while accessible names stay intact.
        var language = UiText.Language;
        try
        {
            foreach (var locale in UiText.Languages.Where(x => x.Code != "zh-CN"))
            {
                UiText.Language = locale.Code;
                var translated = new HoverPreviewWindow();
                try
                {
                    translated.SetSession(new(new(window.Document!.Original), _ => System.Threading.Tasks.Task.CompletedTask, new EmptyLease()), "Fixture.png");
                    var content = (FrameworkElement)translated.Content;
                    content.Measure(new Size(352, 520)); content.Arrange(new Rect(0, 0, 352, 520)); content.UpdateLayout();
                    var localized = UiAccessibility.Descendants(content).OfType<ToggleButton>().Where(x => new[] { T("浏览"), T("裁剪"), T("画笔"), T("箭头"), T("矩形"), T("文字"), T("马赛克") }.Contains(AutomationProperties.GetName(x))).ToArray();
                    check("compact-toolbar-accessible-and-contained-" + locale.Code, localized.Length == 7 && localized.All(x => x.ActualWidth >= 30 && x.TransformToAncestor(content).Transform(new Point(x.ActualWidth, 0)).X <= 352));
                }
                finally { translated.TryEndEditing(() => true); translated.ClosePermanently(); }
            }
        }
        finally { UiText.Language = language; }
    }
    private sealed class EmptyLease : IDisposable { public void Dispose() { } }
}
