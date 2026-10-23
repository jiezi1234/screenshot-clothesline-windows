using System;
using System.IO;
using System.Reflection;
using Clothesline.Storage;
using Microsoft.Win32;

namespace Clothesline.App;

internal interface ILoginRegistration
{
    string? Read();
    void Write(string? command);
}

internal sealed class LoginRegistration : ILoginRegistration
{
    private readonly string keyPath;
    private readonly string valueName;
    public LoginRegistration(string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", string valueName = "ScreenshotClothesline")
    { this.keyPath = keyPath; this.valueName = valueName; }
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
    public void Write(string? command)
    {
        if (command is not null && command.Length > 260) throw new ArgumentException(T("启动命令超过 Windows 的 260 字符限制，请将应用移到更短的路径。"));
        if (command is null) { using var existing = Registry.CurrentUser.OpenSubKey(keyPath, writable: true); existing?.DeleteValue(valueName, false); }
        else { using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true); key.SetValue(valueName, command, RegistryValueKind.String); }
        if (!string.Equals(Read(), command, StringComparison.Ordinal)) throw new IOException(T("登录启动设置写入后校验失败。"));
    }
    public static string LaunchCommand(string executable, string assembly)
    {
        static string Quote(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(['"', '\r', '\n']) >= 0) throw new ArgumentException(T("启动路径无效。"));
            return "\"" + path + "\"";
        }
        var command = Quote(executable);
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) command += " " + Quote(assembly);
        command += " --startup";
        if (command.Length > 260) throw new ArgumentException(T("启动命令过长，请将应用移到更短的路径。"));
        return command;
    }
}

internal sealed class DesktopPreferences(HotKey shortcut, ILoginRegistration login, Func<string> launchCommand)
{
    public string ShortcutStatus => shortcut.Gesture.Length == 0 ? T("快捷键已关闭") : shortcut.Registered ? shortcut.Gesture : shortcut.Gesture + T("（注册失败，可换组合或重试）");
    public bool LoginEnabled => login.Read() is not null;
    public void RetryShortcut() { if (!shortcut.Retry() && shortcut.Gesture.Length > 0) throw new InvalidOperationException(T("快捷键仍被占用或不可用，请使用其他组合。")); }
    public void Apply(LibrarySettings settings, Action<LibrarySettings> persist)
    {
        LibraryStore.ValidateSettings(settings);
        using var prepared = shortcut.Prepare(settings.Shortcut);
        var oldCommand = login.Read(); var desired = settings.RunAtLogin ? launchCommand() : null;
        var changed = !string.Equals(oldCommand, desired, StringComparison.Ordinal);
        try
        {
            if (changed) login.Write(desired);
            persist(settings);
            prepared.Commit();
        }
        catch (Exception primary)
        {
            if (changed)
            {
                try { login.Write(oldCommand); }
                catch (Exception rollback) { throw new InvalidOperationException(T("保存失败；登录启动设置也未能还原，请在 Windows 启动应用中检查。") + primary.Message + " / " + rollback.Message, primary); }
            }
            throw;
        }
    }
    public static DesktopPreferences Create(HotKey shortcut) => new(shortcut, new LoginRegistration(), () =>
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException(T("无法定位应用程序。"));
        var assembly = Assembly.GetExecutingAssembly().Location;
        if (!File.Exists(executable) || !File.Exists(assembly)) throw new IOException(T("应用路径已改变，请从新位置重新启动后再设置登录启动。"));
        return LoginRegistration.LaunchCommand(executable, assembly);
    });
}
