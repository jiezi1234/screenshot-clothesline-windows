using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Clothesline.App;

internal static class UiAccessibility
{
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    internal static void LiveStatus(Window owner, TextBlock status)
    {
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var property = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        var announce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        announce.Tick += (_, _) =>
        {
            announce.Stop();
            if (owner.IsVisible && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        };
        EventHandler changed = (_, _) => { if (!announce.IsEnabled && owner.IsVisible && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) announce.Start(); };
        property.AddValueChanged(status, changed);
        owner.Closed += (_, _) => { announce.Stop(); property.RemoveValueChanged(status, changed); };
    }
    internal static void Install(Window window)
    {
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(Clothesline.Core.UiText.Language);
        var minimumWidth = window.MinWidth; var minimumHeight = window.MinHeight;
        void Fit()
        {
            if (!window.IsVisible || window.WindowState != WindowState.Normal) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (!Native.GetWindowRect(handle, out var current)) return;
            var bounds = current.Pixels; var monitor = Native.MonitorAt(new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
            var dpi = Math.Max(96, Native.GetDpiForWindow(handle)) / 96.0;
            window.MinWidth = Math.Min(minimumWidth, monitor.WorkArea.Width / dpi);
            window.MinHeight = Math.Min(minimumHeight, monitor.WorkArea.Height / dpi);
            var width = Math.Min(bounds.Width, monitor.WorkArea.Width); var height = Math.Min(bounds.Height, monitor.WorkArea.Height);
            var x = Math.Clamp(bounds.X, monitor.WorkArea.X, monitor.WorkArea.Right - width);
            var y = Math.Clamp(bounds.Y, monitor.WorkArea.Y, monitor.WorkArea.Bottom - height);
            if (x != bounds.X || y != bounds.Y || width != bounds.Width || height != bounds.Height)
                Native.SetWindowPos(handle, 0, x, y, width, height, 0x14);
        }
        EventHandler displays = (_, _) => window.Dispatcher.BeginInvoke((Action)Fit);
        SystemEvents.DisplaySettingsChanged += displays;
        window.Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= displays;
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke((Action)Fit);
        window.Loaded += (_, _) => { Fit(); };
    }
}
