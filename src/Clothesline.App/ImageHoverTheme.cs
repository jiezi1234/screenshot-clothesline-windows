using System.Windows;
using System.Windows.Markup;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Clothesline.App;

internal static class ImageHoverTheme
{
    internal static ResourceDictionary Create(bool neutral = false)
    {
        var theme = DashboardTheme.Create();
        if (!SystemParameters.HighContrast)
        {
            foreach (var (key, color) in new[] { ("CanvasBrush", "#FBFCFA"), ("SurfaceBrush", "#FBFCFA"), ("SidebarBrush", "#F0F4EE"), ("TextBrush", "#263B31"), ("MutedBrush", "#708075"), ("LineBrush", "#E3E9E2"), ("AccentBrush", "#2D6045"), ("HoverBrush", "#F0F4EE"), ("SelectedBrush", "#E1EDE3") }) theme[key] = DemoAssets.Brush(color);
        }
        if (neutral && !SystemParameters.HighContrast)
        {
            foreach (var (key, color) in new[] { ("CanvasBrush", "#FFFFFF"), ("SurfaceBrush", "#FFFFFF"), ("SidebarBrush", "#EEF0F2"), ("TextBrush", "#26272A"), ("MutedBrush", "#62656C"), ("LineBrush", "#DADDE1"), ("AccentBrush", "#393B42"), ("HoverBrush", "#ECEEF0"), ("SelectedBrush", "#E6E8EB") })
                theme[key] = DemoAssets.Brush(color);
        }
        var controls = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="EditorButton" TargetType="ButtonBase">
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderBrush" Value="Transparent"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FontSize" Value="12"/><Setter Property="MinHeight" Value="24"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ButtonBase">
              <Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/></Trigger>
                <Trigger Property="ToggleButton.IsChecked" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.7"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/><Setter Property="Cursor" Value="Arrow"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Button" BasedOn="{StaticResource EditorButton}"/>
          <Style TargetType="ToggleButton" BasedOn="{StaticResource EditorButton}"/>
          <Style x:Key="ColorSwatch" TargetType="ToggleButton">
            <Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToggleButton">
              <Grid Background="Transparent"><Ellipse x:Name="Ring" Width="21" Height="21" StrokeThickness="1" Stroke="Transparent"/><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="Ring" Property="Stroke" Value="{DynamicResource AccentBrush}"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Ring" Property="Stroke" Value="{DynamicResource AccentBrush}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Ring" Property="Stroke" Value="{DynamicResource AccentBrush}"/><Setter TargetName="Ring" Property="StrokeThickness" Value="2"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="SliderTrackButton" TargetType="RepeatButton">
            <Setter Property="Focusable" Value="False"/><Setter Property="IsTabStop" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="4" Background="{TemplateBinding Background}" CornerRadius="2"/></Grid></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Slider">
            <Setter Property="MinHeight" Value="28"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
              <Grid Background="Transparent">
                <Track x:Name="PART_Track" VerticalAlignment="Center">
                  <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource SliderTrackButton}" Command="Slider.DecreaseLarge" Background="{DynamicResource AccentBrush}"/></Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource SliderTrackButton}" Command="Slider.IncreaseLarge" Background="{DynamicResource LineBrush}"/></Track.IncreaseRepeatButton>
                  <Track.Thumb><Thumb Width="14" Height="24"><Thumb.Template><ControlTemplate TargetType="Thumb"><Grid Background="Transparent"><Ellipse Width="12" Height="12" Fill="{DynamicResource AccentBrush}"/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                </Track>
                <Border x:Name="Focus" BorderBrush="Transparent" BorderThickness="1" CornerRadius="5" IsHitTestVisible="False"/>
              </Grid>
              <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Focus" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </ResourceDictionary>
        """);
        foreach (var key in controls.Keys) theme[key] = controls[key];
        return theme;
    }

    internal static FrameworkElement EditorIcon(string symbol)
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
            "previous" => "M15,5 L8,12 L15,19",
            "next" => "M9,5 L16,12 L9,19",
            "fit" => "M3,9 V3 H9 M15,3 H21 V9 M21,15 V21 H15 M9,21 H3 V15",
            "minus" => "M5,12 H19",
            "plus" => "M5,12 H19 M12,5 V19",
            "more" => "M5,12 H6 M11.5,12 H12.5 M18,12 H19",
            "help" => "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M8.5,9 A3.5,3.5 0 1 1 13.5,12 L12,13.5 V14.5 M12,17 V17.5",
            _ => "M8,12 V5 Q8,2 10,4 V11 M10,8 V3 Q12,1 13,3 V11 M13,6 Q16,3 16,6 V12 M16,9 Q19,6 19,10 V15 Q19,21 13,21 H11 Q8,21 6,18 L3,13 Q2,10 5,11 L8,14"
        };
        var path = new Path { Data = Geometry.Parse(data), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        path.SetBinding(Shape.StrokeProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1), Path = new PropertyPath(Control.ForegroundProperty) });
        return new Viewbox { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Child = new Canvas { Width = 24, Height = 24, Children = { path } }, IsHitTestVisible = false };
    }
}
