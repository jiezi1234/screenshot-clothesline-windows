using System.Windows;
using System.Windows.Markup;

namespace Clothesline.App;

internal static class DashboardTheme
{
    public static ResourceDictionary Create()
    {
        var resources = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <LinearGradientBrush x:Key="CanvasBrush" StartPoint="0,0" EndPoint="1,1"><GradientStop Color="#9BAFAA" Offset="0"/><GradientStop Color="#A9BDB7" Offset="0.56"/><GradientStop Color="#8FA7A2" Offset="1"/></LinearGradientBrush>
          <LinearGradientBrush x:Key="GlassBorderBrush" StartPoint="0,0" EndPoint="1,1"><GradientStop Color="#648DA6A0" Offset="0"/><GradientStop Color="#323D5550" Offset="0.55"/><GradientStop Color="#507D9590" Offset="1"/></LinearGradientBrush>
          <SolidColorBrush x:Key="SidebarBrush" Color="#EFF3EF"/>
          <SolidColorBrush x:Key="SurfaceBrush" Color="#5925312F"/>
          <SolidColorBrush x:Key="SoftBrush" Color="#5925312F"/>
          <SolidColorBrush x:Key="TextBrush" Color="#F7FAF8"/>
          <SolidColorBrush x:Key="Ink2Brush" Color="#E1EBE7"/>
          <SolidColorBrush x:Key="MutedBrush" Color="#CBD9D4"/>
          <SolidColorBrush x:Key="LineBrush" Color="#64889B94"/>
          <SolidColorBrush x:Key="LineStrongBrush" Color="#A8AFC3BB"/>
          <SolidColorBrush x:Key="AccentBrush" Color="#5AEDAF"/>
          <SolidColorBrush x:Key="SidebarTextBrush" Color="#1A2C25"/>
          <SolidColorBrush x:Key="SidebarMutedBrush" Color="#61746A"/>
          <SolidColorBrush x:Key="SidebarHoverBrush" Color="#DFE9E0"/>
          <SolidColorBrush x:Key="SidebarSelectedBrush" Color="#D1E4D6"/>
          <SolidColorBrush x:Key="AccentTextBrush" Color="#09201B"/>
          <SolidColorBrush x:Key="AccentSoftBrush" Color="#306DC5AC"/>
          <SolidColorBrush x:Key="AccentRingBrush" Color="#9078D5BC"/>
          <SolidColorBrush x:Key="LiveBrush" Color="#64D9A5"/>
          <SolidColorBrush x:Key="HoverBrush" Color="#555F827A"/>
          <SolidColorBrush x:Key="SelectedBrush" Color="#8879958B"/>
          <SolidColorBrush x:Key="CordBrush" Color="#9AC8B9"/>
          <SolidColorBrush x:Key="CordPegBrush" Color="#C2E1D6"/>
          <FontFamily x:Key="DisplayFont">Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI</FontFamily>
          <Style x:Key="FocusRing" TargetType="Control"><Setter Property="Template"><Setter.Value><ControlTemplate><Border Margin="-3" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="2" CornerRadius="11"/></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="Button">
            <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource LineBrush}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="16,9"/><Setter Property="MinHeight" Value="40"/><Setter Property="FontSize" Value="13"/>
            <Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Frame" CornerRadius="10" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentRingBrush}"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.8"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.48"/><Setter Property="Cursor" Value="Arrow"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="{DynamicResource AccentBrush}"/><Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/><Setter Property="FontWeight" Value="SemiBold"/></Style>
          <Style x:Key="QuietButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="Transparent"/><Setter Property="BorderBrush" Value="Transparent"/><Setter Property="Padding" Value="10,8"/></Style>
          <Style x:Key="RailButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Background" Value="Transparent"/><Setter Property="BorderBrush" Value="Transparent"/><Setter Property="Padding" Value="10,0"/><Setter Property="MinHeight" Value="36"/>
            <Setter Property="HorizontalContentAlignment" Value="Left"/><Setter Property="FontSize" Value="12.5"/><Setter Property="Foreground" Value="{DynamicResource MutedBrush}"/>
            <Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Frame" CornerRadius="10" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.8"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.48"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="DockButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#34443D"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="10,0"/><Setter Property="MinHeight" Value="36"/><Setter Property="FontSize" Value="12"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Frame" Background="{TemplateBinding Background}" CornerRadius="16" Padding="{TemplateBinding Padding}"><StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center"><TextBlock Text="{TemplateBinding Tag}" FontFamily="Segoe MDL2 Assets" FontSize="15" VerticalAlignment="Center" Margin="0,0,6,0"/><ContentPresenter VerticalAlignment="Center" RecognizesAccessKey="True"/></StackPanel></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="#E5EEE7"/><Setter Property="Foreground" Value="#14291E"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.7"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="DockActiveButton" TargetType="Button" BasedOn="{StaticResource DockButton}"><Setter Property="Background" Value="#D9F4E4"/><Setter Property="Foreground" Value="#17603F"/></Style>
          <Style TargetType="TextBox">
            <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="12,9"/><Setter Property="MinHeight" Value="40"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FontSize" Value="13"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10"><ScrollViewer x:Name="PART_ContentHost"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentRingBrush}"/></Trigger><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/><Setter TargetName="Frame" Property="BorderThickness" Value="2"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="FontSize" Value="13"/><Setter Property="Padding" Value="7,3,0,3"/><Setter Property="MinHeight" Value="30"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/></Style>
          <Style TargetType="ComboBox">
            <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/><Setter Property="MinHeight" Value="40"/><Setter Property="Padding" Value="12,9,32,9"/><Setter Property="MaxDropDownHeight" Value="300"/><Setter Property="ScrollViewer.CanContentScroll" Value="True"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid>
              <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" ClickMode="Press"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="Frame" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource LineStrongBrush}" BorderThickness="1" CornerRadius="9"><Path Margin="0,0,14,0" HorizontalAlignment="Right" VerticalAlignment="Center" Data="M0,0 L4,4 L8,0" Stroke="{DynamicResource MutedBrush}" StrokeThickness="1.5"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton>
              <ContentPresenter IsHitTestVisible="False" Margin="{TemplateBinding Padding}" VerticalAlignment="Center" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
              <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource LineBrush}" BorderThickness="1" CornerRadius="8" Padding="5" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="{TemplateBinding MaxDropDownHeight}"><ScrollViewer CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border></Popup>
            </Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBoxItem"><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Frame" Padding="{TemplateBinding Padding}" CornerRadius="5"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ListBox"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/><Setter Property="VirtualizingPanel.IsVirtualizing" Value="True"/><Setter Property="VirtualizingPanel.VirtualizationMode" Value="Recycling"/></Style>
          <Style TargetType="ListBoxItem"><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Padding" Value="13,11"/><Setter Property="Margin" Value="0,2"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="10" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentRingBrush}"/></Trigger><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style x:Key="NavigationItem" TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="Transparent" CornerRadius="9" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SidebarHoverBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SidebarSelectedBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style x:Key="ShelfItem" TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}"><Setter Property="Padding" Value="12,12"/><Setter Property="Margin" Value="0,0,14,14"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource GlassBorderBrush}" BorderThickness="1" CornerRadius="18" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentRingBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
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
