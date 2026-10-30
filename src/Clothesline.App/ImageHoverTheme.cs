using System.Windows;
using System.Windows.Markup;

namespace Clothesline.App;

internal static class ImageHoverTheme
{
    internal static ResourceDictionary Create()
    {
        var theme = DashboardTheme.Create();
        if (!SystemParameters.HighContrast)
        {
            foreach (var (key, color) in new[] { ("CanvasBrush", "#FBFCFA"), ("SurfaceBrush", "#FBFCFA"), ("SidebarBrush", "#F0F4EE"), ("TextBrush", "#263B31"), ("MutedBrush", "#708075"), ("LineBrush", "#E3E9E2"), ("AccentBrush", "#2D6045"), ("HoverBrush", "#F0F4EE"), ("SelectedBrush", "#E1EDE3") }) theme[key] = DemoAssets.Brush(color);
        }
        var controls = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="EditorButton" TargetType="ButtonBase">
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderBrush" Value="Transparent"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FontSize" Value="12"/><Setter Property="MinHeight" Value="30"/>
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
}
