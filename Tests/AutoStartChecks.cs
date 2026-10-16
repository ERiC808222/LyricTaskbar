using System.IO;
using DesktopLyrics.Services;

internal static class AutoStartChecks
{
    public static void Run(Action<bool, string> check, string scratch)
    {
        var store = new FakeStore();
        var executable = Path.Combine(scratch, "歌词程序 with spaces", "LyricBarJP.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!); File.WriteAllText(executable, "isolated executable fixture");
        var service = new AutoStartService(store, executable);
        service.RefreshRegisteredPath();
        check(!service.IsEnabled && store.Writes == 0, "auto-start defaults off and launching does not register it");
        service.SetEnabled(true);
        check(service.IsEnabled && store.Command == $"\"{executable}\" --startup", "auto-start command quotes paths with spaces and Japanese characters");
        service.SetEnabled(false); service.SetEnabled(false);
        check(!service.IsEnabled && store.Command == null, "disabling auto-start removes its entry and is idempotent");
        store.Command = "\"C:\\old folder\\LyricBarJP.exe\" --startup";
        service.RefreshRegisteredPath();
        check(store.Command == AutoStartService.CommandFor(executable), "an already-enabled entry follows the new release location");
        int writes = store.Writes; service.RefreshRegisteredPath();
        check(store.Writes == writes, "unchanged startup paths are not repeatedly rewritten");
        check(AutoStartService.IsStartupInvocation(new[] { "--startup" }) && !AutoStartService.IsStartupInvocation(Array.Empty<string>()) && !AutoStartService.IsStartupInvocation(new[] { "--probe", "a" }), "login launch is accepted without confusing diagnostic commands");
        try { AutoStartService.CommandFor(Path.Combine(scratch, "dotnet.exe")); check(false, "invalid launch host"); }
        catch (InvalidOperationException) { check(true, "the dotnet host cannot accidentally become the startup application"); }
        var missing = new AutoStartService(new FakeStore(), Path.Combine(scratch, "missing", "LyricBarJP.exe"));
        try { missing.SetEnabled(true); check(false, "missing startup executable"); }
        catch (FileNotFoundException) { check(!missing.IsEnabled, "missing executables cannot create a broken startup entry"); }
        var failedStore = new FakeStore { RejectWrites = true };
        try { new AutoStartService(failedStore, executable).SetEnabled(true); check(false, "startup write failure"); }
        catch (IOException) { check(failedStore.Command == null, "failed writes do not falsely report auto-start as enabled"); }
    }
    private sealed class FakeStore : IAutoStartStore
    {
        public string? Command;
        public int Writes;
        public bool RejectWrites;
        public string? Read() => Command;
        public void Write(string command) { Writes++; if (RejectWrites) throw new IOException("denied"); Command = command; }
        public void Remove() => Command = null;
    }
}
