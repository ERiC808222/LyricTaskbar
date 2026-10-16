using System.IO;
using Microsoft.Win32;

namespace DesktopLyrics.Services;

public interface IAutoStartStore
{
    string? Read();
    void Write(string command);
    void Remove();
}

/// <summary>Owns the current and legacy application values in the user's Windows login Run key.</summary>
public sealed class RegistryAutoStartStore(string keyPath = RegistryAutoStartStore.RunKey) : IAutoStartStore
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "LyricTaskbar";
    public const string LegacyValueName = "LyricBarJP";
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        var current = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(current) ? current : key?.GetValue(LegacyValueName) as string;
    }
    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
        // Remove the old name only after the replacement is confirmed, avoiding duplicate starts.
        if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal))
            throw new IOException("The replacement login entry could not be saved.");
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
    public void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
}

public sealed class AutoStartService
{
    private readonly IAutoStartStore _store;
    private readonly string _executable;
    public AutoStartService(IAutoStartStore? store = null, string? executable = null)
    {
        _store = store ?? new RegistryAutoStartStore();
        _executable = executable ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LyricTaskbar.exe");
    }
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_store.Read());
    public static bool IsStartupInvocation(string[] args) => args.Length == 1 && args[0] == "--startup";
    public static string CommandFor(string executable)
    {
        var path = Path.GetFullPath(executable);
        string name = Path.GetFileName(path);
        if ((!string.Equals(name, "LyricTaskbar.exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "LyricBarJP.exe", StringComparison.OrdinalIgnoreCase)) || path.Contains('"'))
            throw new InvalidOperationException("Use the published LyricTaskbar.exe to configure auto-start.");
        return $"\"{path}\" --startup";
    }
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            _store.Remove();
            if (IsEnabled) throw new IOException("The login entry could not be removed.");
            return;
        }
        if (!File.Exists(_executable)) throw new FileNotFoundException("The application executable is missing.", _executable);
        string command = CommandFor(_executable);
        _store.Write(command);
        if (!string.Equals(_store.Read(), command, StringComparison.Ordinal)) throw new IOException("The login entry could not be saved.");
    }
    // A manual launch from a new release updates an entry that the user already enabled.
    public void RefreshRegisteredPath()
    {
        string? existing = _store.Read();
        if (!string.IsNullOrWhiteSpace(existing) && existing != CommandFor(_executable)) SetEnabled(true);
    }
}
