using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Clothesline.App;

// Presentation only: document history and saving remain in ImageHoverEditor.
internal sealed partial class HoverPreviewWindow
{
    private readonly TextBlock filename = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly TextBlock dimensions = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
    private readonly TextBlock optionHint = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock sizeValue = new() { FontSize = 11, Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock sizeLabel = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel palette = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel sizeOptions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid parameterOptions = new() { MinHeight = 32 };
    private readonly DockPanel textOptions = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly Slider strokeSize = new() { Minimum = 1, Maximum = 24, Value = 4, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 80, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox zoomPicker = new() { Width = 112, MinHeight = 24, Padding = new Thickness(8, 1, 25, 1), FontSize = 11, ToolTip = T("适应窗口") + " · " + T("Ctrl + 滚轮缩放，滚动条浏览图片") };
    private readonly ComboBoxItem customZoom = new();
    private readonly Path saveIndicator = new() { Width = 13, Height = 13, Stretch = Stretch.Uniform, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(TextBlock Label, double Width)> toolLabels = new();
    private readonly Dictionary<Color, ToggleButton> swatches = new();
    private bool updatingZoom;

    private void BuildEditorChrome(out Button previous, out Button next, out Button tryAgain)
    {
        editor.Resources = ImageHoverTheme.Create();
        editor.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        editor.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI, Microsoft YaHei UI"));
        editor.UseLayoutRounding = true;
        frame.Background = (Brush)editor.Resources["CanvasBrush"]; frame.BorderBrush = (Brush)editor.Resources["LineBrush"];
        var header = new StackPanel();
        var toolbar = new DockPanel { Margin = new Thickness(8, 5, 8, 4) };
        var history = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
        var historyDivider = new Border { Width = 1, Height = 16, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        historyDivider.SetResourceReference(Border.BackgroundProperty, "LineBrush"); history.Children.Add(historyDivider);
        Button History(string label, string symbol, Action action)
        {
            var button = new Button { Content = EditorIcon(symbol), Width = 30, MinHeight = 30, Padding = new Thickness(4), Margin = new Thickness(1, 0, 0, 0) };
            AutomationProperties.SetName(button, T(label)); button.Click += (_, _) => action(); history.Children.Add(button); return button;
        }
        previous = History("上一步", "undo", Undo); next = History("下一步", "redo", Redo);
        previous.ToolTip = T("上一步") + " · Ctrl+Z"; next.ToolTip = T("下一步") + " · Ctrl+Y";
        DockPanel.SetDock(history, Dock.Right); toolbar.Children.Add(history);

        tools.Margin = new Thickness(0);
        foreach (var (tool, name, symbol) in new[] { (EditorTool.Pan, "浏览", "pan"), (EditorTool.Crop, "裁剪", "crop"), (EditorTool.Pen, "画笔", "pen"), (EditorTool.Arrow, "箭头", "arrow"), (EditorTool.Rectangle, "矩形", "rectangle"), (EditorTool.Text, "文字", "text"), (EditorTool.Mosaic, "马赛克", "mosaic") })
        {
            var labels = new List<TextBlock>(); var content = IconLabel(symbol, T(name), labels);
            labels[0].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            toolLabels.Add((labels[0], labels[0].DesiredSize.Width + 38));
            var hint = tool == EditorTool.Pan ? T("Ctrl + 滚轮缩放，滚动条浏览图片") : tool == EditorTool.Crop ? T("拖动框选裁剪区域") : null;
            var button = new ToggleButton { Content = content, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(3, 3, 3, 3), ToolTip = hint is null ? T(name) : T(name) + "\n" + hint, MinHeight = 30 };
            AutomationProperties.SetName(button, T(name)); button.Click += (_, _) => SelectTool(tool);
            Grid.SetColumn(button, tools.Children.Count); tools.ColumnDefinitions.Add(new ColumnDefinition()); tools.Children.Add(button); toolButtons.Add(tool, button);
        }
        toolbar.Children.Add(tools); header.Children.Add(toolbar);
        var separator = new Border { Height = 1 }; separator.SetResourceReference(Border.BackgroundProperty, "LineBrush"); header.Children.Add(separator);

        var parameters = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
        parameterOptions.ColumnDefinitions.Add(new ColumnDefinition()); parameterOptions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var (hex, name) in new[] { ("#DA7858", "红色"), ("#E3B94D", "黄色"), ("#5D9473", "绿色"), ("#5487AE", "蓝色"), ("#FAF7EB", "白色"), ("#293930", "黑色") })
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new ToggleButton { Style = (Style)editor.Resources["ColorSwatch"], ToolTip = T(name), Content = new Ellipse { Width = 13, Height = 13, Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), StrokeThickness = 1 }, Width = 27, Height = 28 };
            AutomationProperties.SetName(swatch, T("颜色") + ": " + T(name)); swatch.Click += (_, _) => SelectInk(color); swatches.Add(color, swatch); palette.Children.Add(swatch);
        }
        SelectInk(swatches.Keys.First());
        parameterOptions.Children.Add(palette); parameterOptions.Children.Add(optionHint);
        strokeSize.ValueChanged += (_, _) => { canvas.StrokeSize = strokeSize.Value; UpdateSizeValue(); };
        sizeLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        sizeOptions.Children.Add(sizeLabel); sizeOptions.Children.Add(strokeSize); sizeOptions.Children.Add(sizeValue); Grid.SetColumn(sizeOptions, 1); parameterOptions.Children.Add(sizeOptions); parameters.Children.Add(parameterOptions);
        var textLabel = new TextBlock { Text = T("文字内容"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        textOptions.Children.Add(textLabel); annotation.MinHeight = 32; annotation.Padding = new Thickness(9, 5, 9, 5);
        AutomationProperties.SetName(annotation, T("文字内容")); annotation.TextChanged += (_, _) => canvas.Annotation = annotation.Text; textOptions.Children.Add(annotation); parameters.Children.Add(textOptions);
        header.Children.Add(parameters); DockPanel.SetDock(header, Dock.Top); editor.Children.Add(header);

        var footer = new DockPanel { Margin = new Thickness(10, 3, 8, 4), MinHeight = 24 };
        var scale = new StackPanel { Orientation = Orientation.Horizontal };
        dimensions.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); scale.Children.Add(dimensions);
        zoomPicker.Items.Add(new ComboBoxItem { Content = T("适应窗口"), Tag = 0d });
        foreach (var percent in new[] { 50d, 100d, 125d, 150d, 200d }) zoomPicker.Items.Add(new ComboBoxItem { Content = percent + "%", Tag = percent });
        zoomPicker.Items.Add(customZoom);
        AutomationProperties.SetName(zoomPicker, T("缩放比例"));
        zoomPicker.SelectionChanged += (_, _) =>
        {
            if (updatingZoom || zoomPicker.SelectedItem is not ComboBoxItem { Tag: double percent }) return;
            fit = percent == 0; if (!fit) zoom = percent / 100 / VisualTreeHelper.GetDpi(this).DpiScaleX; ResizeCanvas();
        };
        scale.Children.Add(zoomPicker); DockPanel.SetDock(scale, Dock.Right); footer.Children.Add(scale);
        tryAgain = new Button { Content = T("重试"), Visibility = Visibility.Collapsed, Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(8, 4, 8, 4) };
        tryAgain.Click += (_, _) => _ = Save(); DockPanel.SetDock(tryAgain, Dock.Right); footer.Children.Add(tryAgain);
        var statusDetails = new StackPanel { MaxWidth = 480 };
        var statusMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        statusMessage.SetBinding(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = status });
        statusDetails.Children.Add(filename); statusDetails.Children.Add(statusMessage); status.ToolTip = statusDetails;
        footer.Children.Add(saveIndicator); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); editor.Children.Add(footer);
        viewport.Content = canvas; canvas.HorizontalAlignment = HorizontalAlignment.Center; canvas.VerticalAlignment = VerticalAlignment.Center;
        var imageFrame = new Border { Margin = new Thickness(6, 3, 6, 0), CornerRadius = new CornerRadius(9), Padding = new Thickness(1), Child = viewport };
        imageFrame.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
        editor.Children.Add(imageFrame);
        tools.SizeChanged += (_, _) => AdaptToolbar();
        UpdateToolOptions(); UpdateZoomDisplay();
    }

    private static FrameworkElement EditorIcon(string symbol)
    {
        // Small original line drawings; no icon font or external asset dependency.
        var data = symbol switch
        {
            "undo" => "M8,5 L3,10 L8,15 M3,10 H14 C22,10 22,20 14,20 H10",
            "redo" => "M16,5 L21,10 L16,15 M21,10 H10 C2,10 2,20 10,20 H14",
            "crop" => "M7,2 V17 H22 M2,7 H17 V22 M11,7 H17 V13",
            "pen" => "M4,16 L16,4 L20,8 L8,20 L3,21 Z M14,6 L18,10",
            "arrow" => "M4,20 L20,4 M11,4 H20 V13",
            "rectangle" => "M4,4 H20 V20 H4 Z",
            "text" => "M4,6 V4 H20 V6 M12,4 V21 M8,21 H16",
            "mosaic" => "M4,4 H20 V20 H4 Z M4,12 H20 M12,4 V20",
            _ => "M8,12 V5 Q8,2 10,4 V11 M10,8 V3 Q12,1 13,3 V11 M13,6 Q16,3 16,6 V12 M16,9 Q19,6 19,10 V15 Q19,21 13,21 H11 Q8,21 6,18 L3,13 Q2,10 5,11 L8,14"
        };
        var path = new Path { Data = Geometry.Parse(data), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        path.SetBinding(Shape.StrokeProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1), Path = new PropertyPath(Control.ForegroundProperty) });
        return new Viewbox { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Child = new Canvas { Width = 24, Height = 24, Children = { path } }, IsHitTestVisible = false };
    }
    private static StackPanel IconLabel(string symbol, string text, List<TextBlock> labels)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(EditorIcon(symbol));
        var label = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        labels.Add(label); row.Children.Add(label); return row;
    }
    private void AdaptToolbar()
    {
        var compact = tools.ActualWidth < toolLabels.Sum(x => x.Width);
        for (var i = 0; i < toolLabels.Count; i++)
        {
            toolLabels[i].Label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            tools.ColumnDefinitions[i].Width = new GridLength(compact ? 1 : toolLabels[i].Width, GridUnitType.Star);
        }
        dimensions.Visibility = tools.ActualWidth < 420 ? Visibility.Collapsed : Visibility.Visible;
        strokeSize.Width = tools.ActualWidth < 360 ? 55 : 80;
        sizeLabel.Visibility = tools.ActualWidth < 420 || canvas.Tool == EditorTool.Mosaic ? Visibility.Collapsed : Visibility.Visible;
    }
    private void SelectInk(Color color)
    {
        canvas.Ink = color;
        foreach (var pair in swatches) pair.Value.IsChecked = pair.Key == color;
    }
    private void UpdateToolOptions()
    {
        var tool = canvas.Tool;
        var drawing = tool is not (EditorTool.Pan or EditorTool.Crop);
        parameterOptions.Visibility = drawing ? Visibility.Visible : Visibility.Collapsed;
        palette.Visibility = drawing && tool != EditorTool.Mosaic ? Visibility.Visible : Visibility.Collapsed;
        sizeOptions.Visibility = drawing ? Visibility.Visible : Visibility.Collapsed;
        optionHint.Visibility = tool == EditorTool.Mosaic ? Visibility.Visible : Visibility.Collapsed;
        optionHint.Text = T("马赛克大小");
        optionHint.ToolTip = optionHint.Text;
        textOptions.Visibility = tool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
        var label = tool == EditorTool.Text ? T("字号") : tool == EditorTool.Mosaic ? T("马赛克大小") : T("笔画粗细");
        sizeLabel.Text = tool == EditorTool.Text ? T("字号") : T("粗细");
        sizeLabel.Visibility = tools.ActualWidth < 420 || tool == EditorTool.Mosaic ? Visibility.Collapsed : Visibility.Visible;
        strokeSize.ToolTip = label; AutomationProperties.SetName(strokeSize, label);
        canvas.Cursor = drawing || tool == EditorTool.Crop ? System.Windows.Input.Cursors.Cross : System.Windows.Input.Cursors.Arrow;
        UpdateSizeValue();
    }
    private void UpdateSizeValue()
    {
        var value = canvas.Tool == EditorTool.Text ? Math.Max(14, strokeSize.Value * 4) : canvas.Tool == EditorTool.Mosaic ? Math.Max(12, strokeSize.Value * 4) : strokeSize.Value;
        sizeValue.Text = value.ToString("0") + " px";
    }
    private void UpdateZoomDisplay()
    {
        if (Document is not { } document) return;
        dimensions.Text = $"{document.State.Crop.Width} × {document.State.Crop.Height}";
        dimensions.ToolTip = dimensions.Text;
        updatingZoom = true;
        try
        {
            var percent = zoom * VisualTreeHelper.GetDpi(this).DpiScaleX * 100;
            var match = zoomPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item != customZoom && item.Tag is double amount && (fit ? amount == 0 : Math.Abs(amount - percent) < .01));
            customZoom.Tag = percent; customZoom.Content = percent.ToString("0.#") + "%";
            customZoom.Visibility = match is null ? Visibility.Visible : Visibility.Collapsed;
            zoomPicker.SelectedItem = match ?? customZoom;
        }
        finally { updatingZoom = false; }
    }
    private void UpdateSaveIndicator()
    {
        saveIndicator.Data = Geometry.Parse(SaveFailed ? "M7,1 V8 M7,11 V13" : saving || Dirty ? "M7,1 A6,6 0 1 0 13,7 M7,3 V7 L10,9" : "M1,7 L5,11 L13,2");
        saveIndicator.Stroke = SaveFailed && !SystemParameters.HighContrast ? DemoAssets.Brush("#AC433D") : (Brush)editor.Resources["AccentBrush"];
        status.SetResourceReference(TextBlock.ForegroundProperty, SaveFailed ? "TextBrush" : "MutedBrush");
    }
}
