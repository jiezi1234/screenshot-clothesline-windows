using System.Windows;
using System.Windows.Markup;

namespace Clothesline.App;

internal static class DashboardTheme
{
    public static ResourceDictionary Create()
    {
        var resources = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="CanvasBrush" Color="#EAE6E1"/>
          <SolidColorBrush x:Key="GlassBorderBrush" Color="#E8E4DF"/>
          <SolidColorBrush x:Key="SidebarBrush" Color="#FDFBF9"/>
          <SolidColorBrush x:Key="SurfaceBrush" Color="#FDFBF9"/>
          <SolidColorBrush x:Key="SoftBrush" Color="#F6F3EF"/>
          <SolidColorBrush x:Key="PaperEdgeBrush" Color="#F0ECE7"/>
          <SolidColorBrush x:Key="TextBrush" Color="#171717"/>
          <SolidColorBrush x:Key="Ink2Brush" Color="#5A5652"/>
          <SolidColorBrush x:Key="MutedBrush" Color="#A39E99"/>
          <SolidColorBrush x:Key="LineBrush" Color="#EDE9E4"/>
          <SolidColorBrush x:Key="LineStrongBrush" Color="#D9D4CE"/>
          <SolidColorBrush x:Key="AccentBrush" Color="#BA6D73"/>
          <SolidColorBrush x:Key="SidebarTextBrush" Color="#5A5652"/>
          <SolidColorBrush x:Key="SidebarMutedBrush" Color="#A39E99"/>
          <SolidColorBrush x:Key="SidebarHoverBrush" Color="#F7EFEC"/>
          <SolidColorBrush x:Key="SidebarSelectedBrush" Color="#F4E2E1"/>
          <SolidColorBrush x:Key="AccentTextBrush" Color="#FFFFFF"/>
          <SolidColorBrush x:Key="AccentSoftBrush" Color="#F6E2E1"/>
          <SolidColorBrush x:Key="AccentRingBrush" Color="#BA6D73"/>
          <SolidColorBrush x:Key="LiveBrush" Color="#BA6D73"/>
          <SolidColorBrush x:Key="HoverBrush" Color="#F7EFEC"/>
          <SolidColorBrush x:Key="SelectedBrush" Color="#F4E2E1"/>
          <SolidColorBrush x:Key="CordBrush" Color="#D4B4B2"/>
          <SolidColorBrush x:Key="CordPegBrush" Color="#BA6D73"/>
          <FontFamily x:Key="DisplayFont">Georgia, Constantia, SimSun, Microsoft YaHei UI</FontFamily>
          <Style x:Key="FocusRing" TargetType="Control"><Setter Property="Template"><Setter.Value><ControlTemplate><Border Margin="-3" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="2" CornerRadius="8"/></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="Button">
            <Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="14,7"/><Setter Property="MinHeight" Value="34"/><Setter Property="FontSize" Value="12.5"/>
            <Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Frame" CornerRadius="14" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.78"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.42"/><Setter Property="Cursor" Value="Arrow"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="{DynamicResource AccentBrush}"/><Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/><Setter Property="FontWeight" Value="SemiBold"/></Style>
          <Style x:Key="QuietButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="Transparent"/><Setter Property="BorderBrush" Value="Transparent"/><Setter Property="Foreground" Value="{DynamicResource Ink2Brush}"/><Setter Property="Padding" Value="10,6"/></Style>
          <Style x:Key="BookmarkTab" TargetType="ListBoxItem">
            <Setter Property="HorizontalContentAlignment" Value="Center"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem">
              <Border x:Name="Tab" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource LineBrush}" BorderThickness="1" CornerRadius="0,9,9,0" Padding="{TemplateBinding Padding}" Margin="0,0,0,10">
                <Border.Effect><DropShadowEffect Color="#3A302C" BlurRadius="10" ShadowDepth="3" Direction="315" Opacity="0.10"/></Border.Effect>
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Tab" Property="Background" Value="{DynamicResource HoverBrush}"/><Setter TargetName="Tab" Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/></Trigger>
                <Trigger Property="IsSelected" Value="True"><Setter TargetName="Tab" Property="Background" Value="#FFFFFF"/><Setter TargetName="Tab" Property="BorderBrush" Value="{DynamicResource AccentSoftBrush}"/><Setter TargetName="Tab" Property="RenderTransform"><Setter.Value><TranslateTransform X="4"/></Setter.Value></Setter><Setter TargetName="Tab" Property="Effect"><Setter.Value><DropShadowEffect Color="#3A302C" BlurRadius="14" ShadowDepth="4" Direction="315" Opacity="0.14"/></Setter.Value></Setter></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="TextBox">
            <Setter Property="Background" Value="#FFFDFC"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="12,9"/><Setter Property="Height" Value="40"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FontSize" Value="13"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8"><ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/></Trigger><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="FontSize" Value="13"/><Setter Property="Padding" Value="9,3,0,3"/><Setter Property="MinHeight" Value="30"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
              <Grid Background="Transparent"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/></Grid.ColumnDefinitions>
                <Border x:Name="Box" Width="16" Height="16" CornerRadius="4" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource LineStrongBrush}" BorderThickness="1" VerticalAlignment="Center"/>
                <Path x:Name="Tick" Data="M0,4 L3,7 L8,0" Stroke="{DynamicResource AccentTextBrush}" StrokeThickness="1.8" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
                <ContentPresenter Grid.Column="1" Margin="{TemplateBinding Padding}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="Box" Property="Background" Value="{DynamicResource AccentBrush}"/><Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/><Setter TargetName="Tick" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="Background" Value="#FFFDFC"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderBrush" Value="{DynamicResource LineStrongBrush}"/><Setter Property="MinHeight" Value="40"/><Setter Property="Padding" Value="12,9,32,9"/><Setter Property="MaxDropDownHeight" Value="300"/><Setter Property="ScrollViewer.CanContentScroll" Value="True"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid>
              <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" ClickMode="Press"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="Frame" Background="#FFFDFC" BorderBrush="{DynamicResource LineStrongBrush}" BorderThickness="1" CornerRadius="8"><Path Margin="0,0,14,0" HorizontalAlignment="Right" VerticalAlignment="Center" Data="M0,0 L4,4 L8,0" Stroke="{DynamicResource MutedBrush}" StrokeThickness="1.5"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton>
              <ContentPresenter IsHitTestVisible="False" Margin="{TemplateBinding Padding}" VerticalAlignment="Center" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
              <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource LineBrush}" BorderThickness="1" CornerRadius="8" Padding="5" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="{TemplateBinding MaxDropDownHeight}"><ScrollViewer CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border></Popup>
            </Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBoxItem"><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Frame" Padding="{TemplateBinding Padding}" CornerRadius="6"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ListBox"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/><Setter Property="VirtualizingPanel.IsVirtualizing" Value="True"/><Setter Property="VirtualizingPanel.VirtualizationMode" Value="Recycling"/></Style>
          <Style TargetType="ListBoxItem"><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Padding" Value="12,10"/><Setter Property="Margin" Value="0,2"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="8" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource HoverBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentSoftBrush}"/></Trigger><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style x:Key="ShelfItem" TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}"><Setter Property="Padding" Value="10,10"/><Setter Property="Margin" Value="0,0,12,12"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource GlassBorderBrush}" BorderThickness="1" CornerRadius="10" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentSoftBrush}"/><Setter TargetName="Frame" Property="Background" Value="#FFFDFC"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="{DynamicResource SelectedBrush}"/><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="Expander"><Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="Padding" Value="0,10,0,0"/><Setter Property="Margin" Value="0,12,0,0"/><Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/></Style>
        </ResourceDictionary>
        """);
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "CanvasBrush", "SidebarBrush", "SurfaceBrush", "SoftBrush", "PaperEdgeBrush" }) resources[key] = SystemColors.WindowBrush;
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
