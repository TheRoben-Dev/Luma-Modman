using System.Diagnostics;
using Luma.Core;
using Luma.Core.Steam;

namespace Luma.Host;

/// <summary>
/// "Add Luma to Steam": a non-Steam shortcut with Luma's own library art, in every Steam account
/// on the device, so it opens from Game Mode. Steam only reads shortcuts when it starts and
/// overwrites the file when it exits, so the shortcut is written while Steam is closed.
/// </summary>
public static class SteamSetup
{
    public const string AppName = "Luma";

    /// <summary>The steam-art folder shipped next to the app.</summary>
    public static string ArtDir(string installDir) => Path.Combine(installDir, "steam-art");

    public static SteamShortcut Shortcut(string installDir) => new()
    {
        AppName = AppName,
        Exe = Path.Combine(installDir, Platform.AppBinary),
        StartDir = installDir,
        Icon = File.Exists(Path.Combine(ArtDir(installDir), "icon.png")) ? Path.Combine(ArtDir(installDir), "icon.png") : "",
        Tags = ["Tools"],
    };

    /// <summary>True when every account already has the shortcut.</summary>
    public static bool IsAdded(string home, string installDir)
    {
        var dirs = SteamShortcuts.AccountConfigDirs(home);
        var id = Shortcut(installDir).AppId;
        return dirs.Count > 0 && dirs.All(d => SteamShortcuts.Load(d).Find(id) != null);
    }

    /// <summary>True in Game Mode (gamescope session), where Steam can't be closed from an app.</summary>
    public static bool InGameMode =>
        !Platform.IsWindows && (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.Contains("gamescope", StringComparison.OrdinalIgnoreCase) == true ||
        Environment.GetEnvironmentVariable("SteamGamepadUI") == "1");

    /// <summary>Adds (or updates) the shortcut and its art. Returns the number of accounts changed.</summary>
    public static int Add(string home, string installDir)
    {
        if (!File.Exists(Path.Combine(installDir, Platform.AppBinary)))
        {
            throw new InvalidOperationException("Luma isn't installed from a release here, so there's no app for Steam to start.");
        }
        if (SteamShortcuts.SteamRunning(home))
        {
            throw new InvalidOperationException(InGameMode
                ? "Steam is running. Switch to Desktop Mode, close Steam, and add Luma from there (or run the installer)."
                : "Close Steam first (Steam → Exit), then try again: Steam only reads shortcuts when it starts.");
        }
        var dirs = SteamShortcuts.AccountConfigDirs(home);
        if (dirs.Count == 0)
        {
            throw new InvalidOperationException("No Steam account found on this device. Sign in to Steam once, then try again.");
        }
        var s = Shortcut(installDir);
        foreach (var d in dirs)
        {
            var file = SteamShortcuts.Load(d);
            file.AddOrUpdate(s);
            file.Save();
            SteamShortcuts.InstallArtwork(d, s.AppId, ArtDir(installDir));
        }
        AppLog.Info($"added to Steam ({dirs.Count} account(s)), app id {s.AppId}");
        return dirs.Count;
    }

    /// <summary>Asks a running Steam to exit and waits up to <paramref name="timeout"/> for it.</summary>
    public static bool CloseSteam(string home, TimeSpan timeout)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(SteamCommand() ?? "steam", "-shutdown") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (!SteamShortcuts.SteamRunning(home))
            {
                return true;
            }
            Thread.Sleep(500);
        }
        return false;
    }

    /// <summary>steam.exe on Windows (from the registry); "steam" from PATH on Linux.</summary>
    private static string? SteamCommand() => OperatingSystem.IsWindows() ? Luma.Core.WindowsSteam.Exe() : "steam";

    public static void StartSteam()
    {
        try
        {
            Process.Start(new ProcessStartInfo(SteamCommand() ?? "steam") { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
