using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DesktopLyrics.Services;

public static class AutoStartDiagnostics
{
    public static void Verify(string path)
    {
        // This private HKCU test key is not a Windows Run key and cannot launch anything.
        string testKey = @"Software\LyricBarJP-AutoStartTest-" + Guid.NewGuid().ToString("N");
        var production = new RegistryAutoStartStore(); string? originalProduction = production.Read();
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(testKey)) key.SetValue("OtherApplication", "keep");
            var store = new RegistryAutoStartStore(testKey);
            var service = new AutoStartService(store);
            service.RefreshRegisteredPath(); Check(!service.IsEnabled, "default off on a real registry store");
            service.SetEnabled(true);
            Check(store.Read() == AutoStartService.CommandFor(Environment.ProcessPath!), "published executable is quoted and registered with --startup");
            service.SetEnabled(false);
            Check(!service.IsEnabled, "disabling removes the owned registry value");
            using (var key = Registry.CurrentUser.OpenSubKey(testKey)) Check((string?)key?.GetValue("OtherApplication") == "keep", "unrelated registry values are preserved");
            Check(production.Read() == originalProduction, "real Windows auto-start setting remains unchanged");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false); }
        using (var remaining = Registry.CurrentUser.OpenSubKey(testKey)) Check(remaining == null, "temporary test key is removed");
        File.WriteAllText(path, JsonSerializer.Serialize(new { passed = true, checks, actualAutoStartChanged = false,
            limitation = "Enable/disable exercised in an isolated real HKCU key; Windows sign-in was not performed." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
