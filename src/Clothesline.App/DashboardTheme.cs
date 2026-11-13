using System.Windows;
using System.Windows.Markup;

namespace Clothesline.App;

internal static class DashboardTheme
{
    public static ResourceDictionary Create()
    {
        var resources = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="CanvasBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="GlassBorderBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="SidebarBrush" Color="#FFFFFF"/>
          <SolidColorBrush x:Key="SurfaceBrush" Color="#FFFFFF"/>
          <SolidColorBrush x:Key="SoftBrush" Color="#FFFFFF"/>
          <SolidColorBrush x:Key="TextBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="Ink2Brush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="MutedBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="LineBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="LineStrongBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="AccentBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="SidebarTextBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="SidebarMutedBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="SidebarHoverBrush" Color="#EAF4FC"/>
          <SolidColorBrush x:Key="SidebarSelectedBrush" Color="#EAF4FC"/>
          <SolidColorBrush x:Key="AccentTextBrush" Color="#FFFFFF"/>
          <SolidColorBrush x:Key="AccentSoftBrush" Color="#EAF4FC"/>
          <SolidColorBrush x:Key="AccentRingBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="LiveBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="HoverBrush" Color="#EAF4FC"/>
          <SolidColorBrush x:Key="SelectedBrush" Color="#EAF4FC"/>
          <SolidColorBrush x:Key="CordBrush" Color="#5EA6E5"/>
          <SolidColorBrush x:Key="CordPegBrush" Color="#5EA6E5"/>
          <FontFamily x:Key="DisplayFont">SimSun, Microsoft YaHei UI</FontFamily>
          <Style x:Key="FocusRing" TargetType="Control"><Setter Property="Template"><Setter.Value><ControlTemplate><Border Margin="-3" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="2" CornerRadius="0"/></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="Button">
            <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource LineBrush}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="16,9"/><Setter Property="MinHeight" Value="40"/><Setter Property="FontSize" Value="13"/>
            <Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Frame" CornerRadius="0" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}"><Border.Effect><DropShadowEffect Color="#5EA6E5" BlurRadius="0" ShadowDepth="2" Direction="315" Opacity="1"/></Border.Effect>
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentRingBrush}"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="RenderTransform"><Setter.Value><TranslateTransform X="2" Y="2"/></Setter.Value></Setter><Setter TargetName="Frame" Property="Effect" Value="{x:Null}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.48"/><Setter Property="Cursor" Value="Arrow"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="{DynamicResource AccentBrush}"/><Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/><Setter Property="FontWeight" Value="SemiBold"/></Style>
          <Style x:Key="QuietButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="Transparent"/><Setter Property="BorderBrush" Value="Transparent"/><Setter Property="Padding" Value="10,8"/></Style>
          <Style x:Key="RailButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Background" Value="Transparent"/><Setter Property="BorderBrush" Value="Transparent"/><Setter Property="Padding" Value="10,0"/><Setter Property="MinHeight" Value="36"/>
            <Setter Property="HorizontalContentAlignment" Value="Left"/><Setter Property="FontSize" Value="12.5"/><Setter Property="Foreground" Value="{DynamicResource MutedBrush}"/>
            <Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Frame" CornerRadius="0" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="RenderTransform"><Setter.Value><TranslateTransform X="2" Y="2"/></Setter.Value></Setter><Setter TargetName="Frame" Property="Effect" Value="{x:Null}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.48"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="DockButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#5EA6E5"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="10,0"/><Setter Property="MinHeight" Value="36"/><Setter Property="FontSize" Value="12"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Frame" Background="{TemplateBinding Background}" CornerRadius="0" Padding="{TemplateBinding Padding}"><StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center"><TextBlock Text="{TemplateBinding Tag}" FontFamily="Segoe MDL2 Assets" FontSize="15" VerticalAlignment="Center" Margin="0,0,6,0"/><ContentPresenter VerticalAlignment="Center" RecognizesAccessKey="True"/></StackPanel></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="#EAF4FC"/><Setter Property="Foreground" Value="#5EA6E5"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.7"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="DockActiveButton" TargetType="Button" BasedOn="{StaticResource DockButton}"><Setter Property="Background" Value="#EAF4FC"/><Setter Property="Foreground" Value="#5EA6E5"/></Style>
          <Style TargetType="TextBox">
            <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="12,9"/><Setter Property="MinHeight" Value="40"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FontSize" Value="13"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="0"><ScrollViewer x:Name="PART_ContentHost"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentRingBrush}"/></Trigger><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/><Setter TargetName="Frame" Property="BorderThickness" Value="2"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="FontSize" Value="13"/><Setter Property="Padding" Value="7,3,0,3"/><Setter Property="MinHeight" Value="30"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/></Style>
          <Style TargetType="ComboBox">
            <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/><Setter Property="MinHeight" Value="40"/><Setter Property="Padding" Value="12,9,32,9"/><Setter Property="MaxDropDownHeight" Value="300"/><Setter Property="ScrollViewer.CanContentScroll" Value="True"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid>
              <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" ClickMode="Press"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="Frame" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource LineStrongBrush}" BorderThickness="1" CornerRadius="0"><Path Margin="0,0,14,0" HorizontalAlignment="Right" VerticalAlignment="Center" Data="M0,0 L4,4 L8,0" Stroke="{DynamicResource MutedBrush}" StrokeThickness="1.5"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton>
              <ContentPresenter IsHitTestVisible="False" Margin="{TemplateBinding Padding}" VerticalAlignment="Center" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
              <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource LineBrush}" BorderThickness="1" CornerRadius="0" Padding="5" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="{TemplateBinding MaxDropDownHeight}"><ScrollViewer CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border></Popup>
            </Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBoxItem"><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Frame" Padding="{TemplateBinding Padding}" CornerRadius="0"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ListBox"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/><Setter Property="VirtualizingPanel.IsVirtualizing" Value="True"/><Setter Property="VirtualizingPanel.VirtualizationMode" Value="Recycling"/></Style>
          <Style TargetType="ListBoxItem"><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Padding" Value="13,11"/><Setter Property="Margin" Value="0,2"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="0" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentRingBrush}"/></Trigger><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style x:Key="NavigationItem" TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="Transparent" CornerRadius="0" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SidebarHoverBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SidebarSelectedBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style x:Key="ShelfItem" TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}"><Setter Property="Padding" Value="12,12"/><Setter Property="Margin" Value="0,0,14,14"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource GlassBorderBrush}" BorderThickness="1" CornerRadius="0" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentRingBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="Expander"><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="Padding" Value="0,10,0,0"/><Setter Property="Margin" Value="0,12,0,0"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/></Style>
        </ResourceDictionary>
        """);
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "CanvasBrush", "SidebarBrush", "SurfaceBrush", "SoftBrush" }) resources[key] = SystemColors.WindowBrush;
            resources["TextBrush"] = resources["Ink2Brush"] = resources["MutedBrush"] = SystemColors.WindowTextBrush;
            resources["LineBrush"] = resources["LineStrongBrush"] = resources["GlassBorderBrush"] = SystemColors.WindowTextBrush;
            resources["CordBrush"] = resources["CordPegBrush"] = SystemColors.WindowTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush; resources["AccentTextBrush"] = SystemColors.HighlightTextBrush;
            resources["HoverBrush"] = resources["SelectedBrush"] = resources["AccentSoftBrush"] = SystemColors.ControlBrush;
            resources["AccentRingBrush"] = SystemColors.HighlightBrush;
            resources["SidebarTextBrush"] = resources["SidebarMutedBrush"] = SystemColors.WindowTextBrush;
            resources["SidebarHoverBrush"] = resources["SidebarSelectedBrush"] = SystemColors.ControlBrush;
        }
        return resources;
    }
}
