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
                    if (keys.Count == 4) break;
                }
                if (keys.Count < 4) throw new InvalidOperationException("No four free diagnostic hotkeys; no existing registration was changed.");
                var active = keys[0]; var blocker = keys[1]; var originalKey = active.Gesture; var candidateKey = blocker.Gesture;
                var conflict = false; try { using var rejected = active.Prepare(candidateKey); } catch (InvalidOperationException) { conflict = true; }
                Check("conflict-preserves-active-registration", conflict && active.Registered && active.Gesture == originalKey);
                blocker.Dispose(); keys.Remove(blocker);
                using (var pending = active.Prepare(candidateKey))
                using (var probe = new HotKey(() => { }, originalKey)) Check("preparation-keeps-old-key-reserved", !probe.Registered && active.Gesture == originalKey);
                using (var probe = new HotKey(() => { }, candidateKey)) Check("uncommitted-preparation-releases-candidate", probe.Registered);
                var originalHide = keys[1].Gesture; var candidateHide = keys[2].Gesture;
                active.Dispose(); keys.Remove(active); keys[0].Dispose(); keys.RemoveAt(0);
                using var shelf = new ShelfController(); shelf.SuppressFullscreen = false;
                var showCalls = 0; var hideCalls = 0;
                using var pair = new ShelfHotKeys(() => { showCalls++; shelf.Show(); }, () => { hideCalls++; shelf.HideFromShortcut(); }, originalKey, originalHide);
                Check("both-shortcuts-register", pair.ShowRegistered && pair.HideRegistered);
                pair.Dispatch(originalKey); pair.Dispatch(originalKey);
                Check("show-twice-keeps-empty-shelf-visible", shelf.Visible && showCalls == 2 && hideCalls == 0);
                pair.Dispatch(originalHide); pair.Dispatch(originalHide);
                Check("hide-twice-keeps-shelf-hidden", !shelf.Visible && hideCalls == 2);
                shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionLock); pair.Dispatch(originalKey);
                Check("show-shortcut-respects-locked-session", !shelf.Visible);
                shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionUnlock);
                var secondConflict = false;
                try { using var pending = pair.Prepare(candidateKey, candidateHide); } catch (InvalidOperationException) { secondConflict = true; }
                Check("second-shortcut-conflict-keeps-both-old-registrations", secondConflict && pair.ShowRegistered && pair.HideRegistered && pair.ShowGesture == originalKey && pair.HideGesture == originalHide);
                using (var probe = new HotKey(() => { }, candidateKey)) Check("second-conflict-releases-first-prepared-key", probe.Registered);
                keys[0].Dispose(); keys.Clear();
                var registration = new FakeLogin();
                var preferences = new DesktopPreferences(pair, registration, () => "\"C:\\Clothesline Test\\Clothesline.exe\" --startup");
                var settings = new LibrarySettings { ShowShortcut = candidateKey, HideShortcut = candidateHide, RunAtLogin = true };
                var persistenceFailed = false;
                try { preferences.Apply(settings, _ => throw new IOException("fixture database error")); } catch (IOException) { persistenceFailed = true; }
                Check("database-failure-rolls-back-startup-and-both-hotkeys", persistenceFailed && registration.Value is null && pair.ShowGesture == originalKey && pair.HideGesture == originalHide && pair.Registered);
                using (var showProbe = new HotKey(() => { }, candidateKey))
                using (var hideProbe = new HotKey(() => { }, candidateHide)) Check("failed-apply-releases-both-candidates", showProbe.Registered && hideProbe.Registered);
                registration.FailNextWrite = true; var wroteSettings = false; var writeFailed = false;
                try { preferences.Apply(settings, _ => wroteSettings = true); } catch (IOException) { writeFailed = true; }
                Check("startup-write-failure-keeps-database-and-both-old-hotkeys", writeFailed && !wroteSettings && registration.Value is null && pair.ShowGesture == originalKey && pair.HideGesture == originalHide && pair.Registered);
                preferences.Apply(settings, _ => wroteSettings = true);
                Check("successful-apply-commits-both-shortcuts", wroteSettings && preferences.LoginEnabled && pair.ShowGesture == candidateKey && pair.HideGesture == candidateHide && pair.Registered && registration.Value!.EndsWith(" --startup", StringComparison.Ordinal));
                using (var showProbe = new HotKey(() => { }, originalKey))
                using (var hideProbe = new HotKey(() => { }, originalHide)) Check("successful-change-releases-both-old-shortcuts", showProbe.Registered && hideProbe.Registered);
                preferences.Apply(settings with { ShowShortcut = candidateHide, HideShortcut = candidateKey }, _ => { });
                pair.Dispatch(candidateHide); Check("swapped-gesture-shows-shelf", shelf.Visible);
                pair.Dispatch(candidateKey); Check("swapped-gesture-hides-shelf", !shelf.Visible);
                preferences.Apply(settings with { ShowShortcut = "", RunAtLogin = false }, _ => { });
                Check("show-can-be-disabled-independently", !pair.ShowRegistered && pair.HideRegistered && registration.Value is null);
                preferences.Apply(settings with { ShowShortcut = "", HideShortcut = "", RunAtLogin = false }, _ => { });
                Check("disable-releases-both-hotkeys-and-login-entry", !pair.Registered && pair.ShowGesture.Length == 0 && pair.HideGesture.Length == 0 && registration.Value is null);
                using (var occupied = new HotKey(() => { }, candidateHide))
                using (var partial = new ShelfHotKeys(() => { }, () => { }, candidateKey, candidateHide))
                {
                    Check("startup-conflict-keeps-available-action-working", partial.ShowRegistered && !partial.HideRegistered && !partial.Retry());
                    occupied.Dispose(); Check("retry-recovers-only-missing-registration", partial.Retry() && partial.ShowRegistered && partial.HideRegistered);
                }
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
                using var runtime = new LibraryRuntime(shelf, Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N")));
                runtime.ApplySettings(runtime.Settings);
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
