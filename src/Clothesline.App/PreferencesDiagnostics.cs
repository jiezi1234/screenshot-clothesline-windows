using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Storage;
using Microsoft.Win32;

namespace Clothesline.App;

internal static class PreferencesDiagnostics
{
    public static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok) { checks.Add(new { name, passed = ok }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(() =>
        {
            var keys = new List<HotKey>(); var registryPath = @"Software\ScreenshotClothesline\Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                foreach (var f in Enumerable.Range(13, 12))
                {
                    var key = new HotKey(() => { }, "Ctrl+Alt+Shift+F" + f);
                    if (key.Registered) keys.Add(key); else key.Dispose();
                    if (keys.Count == 2) break;
                }
                if (keys.Count < 2) throw new InvalidOperationException("No two free diagnostic hotkeys; no existing registration was changed.");
                var active = keys[0]; var blocker = keys[1]; var originalKey = active.Gesture; var candidateKey = blocker.Gesture;
                var conflict = false; try { using var rejected = active.Prepare(candidateKey); } catch (InvalidOperationException) { conflict = true; }
                Check("conflict-preserves-active-registration", conflict && active.Registered && active.Gesture == originalKey);
                blocker.Dispose(); keys.Remove(blocker);
                using (var pending = active.Prepare(candidateKey))
                using (var probe = new HotKey(() => { }, originalKey)) Check("preparation-keeps-old-key-reserved", !probe.Registered && active.Gesture == originalKey);
                using (var probe = new HotKey(() => { }, candidateKey)) Check("uncommitted-preparation-releases-candidate", probe.Registered);
                var registration = new FakeLogin();
                var preferences = new DesktopPreferences(active, registration, () => "\"C:\\Clothesline Test\\Clothesline.exe\" --startup");
                var settings = new LibrarySettings { Shortcut = candidateKey, RunAtLogin = true };
                var persistenceFailed = false;
                try { preferences.Apply(settings, _ => throw new IOException("fixture database error")); } catch (IOException) { persistenceFailed = true; }
                Check("database-failure-rolls-back-startup-and-hotkey", persistenceFailed && registration.Value is null && active.Gesture == originalKey && active.Registered);
                using (var probe = new HotKey(() => { }, candidateKey)) Check("failed-apply-leaves-candidate-available", probe.Registered);
                registration.FailNextWrite = true; var wroteSettings = false; var writeFailed = false;
                try { preferences.Apply(settings, _ => wroteSettings = true); } catch (IOException) { writeFailed = true; }
                Check("startup-write-failure-keeps-database-and-old-hotkey", writeFailed && !wroteSettings && registration.Value is null && active.Gesture == originalKey);
                preferences.Apply(settings, _ => wroteSettings = true);
                Check("successful-apply-commits-preferences", wroteSettings && preferences.LoginEnabled && active.Gesture == candidateKey && active.Registered && registration.Value!.EndsWith(" --startup", StringComparison.Ordinal));
                using (var probe = new HotKey(() => { }, originalKey)) Check("successful-change-releases-old-hotkey", probe.Registered);
                preferences.Apply(settings with { Shortcut = "", RunAtLogin = false }, _ => { });
                Check("disable-releases-hotkey-and-login-entry", !active.Registered && active.Gesture.Length == 0 && registration.Value is null);
                var command = LoginRegistration.LaunchCommand(@"C:\Program Files\Clothesline\Clothesline.exe", "unused");
                Check("startup-command-quotes-executable-path", command == "\"C:\\Program Files\\Clothesline\\Clothesline.exe\" --startup");
                var hosted = LoginRegistration.LaunchCommand(@"C:\Dotnet Runtime\dotnet.exe", @"D:\My App\Clothesline.dll");
                Check("framework-dependent-startup-quotes-host-and-assembly", hosted == "\"C:\\Dotnet Runtime\\dotnet.exe\" \"D:\\My App\\Clothesline.dll\" --startup");
                var invalid = false; try { LoginRegistration.LaunchCommand("relative.exe", "unused"); } catch (ArgumentException) { invalid = true; }
                Check("startup-rejects-relative-path", invalid);
                var tooLong = false; try { LoginRegistration.LaunchCommand(@"C:\" + new string('a', 260) + ".exe", "unused"); } catch (ArgumentException) { tooLong = true; }
                Check("startup-enforces-run-command-length", tooLong);
                var actual = new LoginRegistration(); var before = actual.Read();
                var isolated = new LoginRegistration(registryPath);
                using (var fixture = Registry.CurrentUser.CreateSubKey(registryPath)) fixture.SetValue("Unrelated", "keep");
                isolated.Write(command); Check("isolated-registry-roundtrip", isolated.Read() == command);
                isolated.Write(null);
                using (var fixture = Registry.CurrentUser.OpenSubKey(registryPath)) Check("disable-removes-only-owned-registry-value", isolated.Read() is null && fixture!.GetValue("Unrelated") as string == "keep");
                Check("real-login-registration-untouched-by-diagnostics", actual.Read() == before);
                using var shelf = new ShelfController(); using var runtime = new LibraryRuntime(shelf, Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N")));
                runtime.Desktop = preferences; runtime.ApplySettings(runtime.Settings with { Shortcut = candidateKey });
                var dashboard = new Dashboard(shelf, true, () => { }, runtime) { Exiting = true };
                dashboard.ShowDesktopSettings(); dashboard.UpdateLayout(); Render((FrameworkElement)dashboard.Content, Path.Combine(directory, "preferences.png")); dashboard.Close();
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "preferences-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                foreach (var key in keys) key.Dispose();
                Registry.CurrentUser.DeleteSubKey(registryPath, false);
                File.WriteAllText(Path.Combine(directory, "preferences-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private sealed class FakeLogin : ILoginRegistration
    {
        public string? Value; public bool FailNextWrite;
        public string? Read() => Value;
        public void Write(string? command) { if (FailNextWrite) { FailNextWrite = false; throw new IOException("fixture registry failure"); } Value = command; }
    }
    private static void Render(FrameworkElement element, string path)
    {
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight); dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, bounds); dc.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); DemoAssets.Save(bitmap, path);
    }
}
