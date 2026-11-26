using System.Diagnostics;
using Luma.Core;
using Luma.Core.Sources;

namespace Luma.Host;

/// <summary>
/// Nexus "Mod Manager Download" links. The desktop entry (on Windows, the nxm:// protocol key in
/// HKCU\Software\Classes) runs `luma nxm &lt;link&gt;`, which drops the link into an inbox folder; the app picks it up (and is started if it isn't running).
/// </summary>
public static class NxmHandler
{
    public const string DesktopFile = "luma-nxm.desktop";

    public static string Inbox(AppPaths paths) => Path.Combine(paths.Data, "nxm-inbox");

    /// <summary>Queues a link for the app. Returns false when it isn't an nxm link.</summary>
    public static bool Drop(AppPaths paths, string link)
    {
        if (NxmLink.TryParse(link) == null)
        {
            return false;
        }
        var dir = Inbox(paths);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{DateTime.UtcNow.Ticks}-{Guid.NewGuid().ToString("N")[..6]}.nxm");
        File.WriteAllText(file + ".tmp", link.Trim());
        File.Move(file + ".tmp", file);
        return true;
    }

    /// <summary>Links waiting in the inbox, oldest first; each file is removed as it is read.</summary>
    public static List<NxmLink> Take(AppPaths paths)
    {
        var dir = Inbox(paths);
        var list = new List<NxmLink>();
        if (!Directory.Exists(dir))
        {
            return list;
        }
        foreach (var f in Directory.EnumerateFiles(dir, "*.nxm").Order())
        {
            try
            {
                if (NxmLink.TryParse(File.ReadAllText(f)) is { } l)
                {
                    list.Add(l);
                }
                File.Delete(f);
            }
            catch (IOException)
            {
            }
        }
        return list;
    }

    /// <summary>The desktop entry that makes Luma the handler for nxm:// links.</summary>
    public static string DesktopEntry(string cliPath) => $"""
        [Desktop Entry]
        Type=Application
        Name=Luma (Nexus downloads)
        Comment=Sends Nexus Mods "Mod Manager Download" links to Luma
        Exec="{cliPath}" nxm %u
        NoDisplay=true
        Terminal=false
        MimeType=x-scheme-handler/nxm;
        """;

    /// <summary>Writes the desktop entry and sets it as the default nxm handler (xdg-mime).</summary>
    public static void Register(AppPaths paths, string cliPath)
    {
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"Luma's command line helper ({Platform.CliBinary}) wasn't found next to the app. Install Luma from a release to use Nexus download links.", cliPath);
        }
        if (OperatingSystem.IsWindows())
        {
            WindowsRegister(cliPath);
            AppLog.Info($"nxm handler registered: {cliPath}");
            return;
        }
        var apps = Path.Combine(paths.Home, ".local", "share", "applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, DesktopFile), DesktopEntry(cliPath));
        Run("xdg-mime", "default", DesktopFile, "x-scheme-handler/nxm");
        Run("update-desktop-database", apps);
        AppLog.Info($"nxm handler registered: {cliPath}");
    }

    public static bool IsRegistered(AppPaths paths) =>
        OperatingSystem.IsWindows()
            ? WindowsCommand()?.Contains(Platform.CliBinary, StringComparison.OrdinalIgnoreCase) == true
            : File.Exists(Path.Combine(paths.Home, ".local", "share", "applications", DesktopFile));

    private const string ClassesKey = @"Software\Classes\nxm";

    /// <summary>The command line Windows runs for nxm:// links: `"...\luma-cli.exe" nxm "%1"`.</summary>
    public static string WindowsCommandLine(string cliPath) => $"\"{cliPath}\" nxm \"%1\"";

    /// <summary>The nxm:// handler for this user (HKCU, no admin rights needed); takes over from another mod manager.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void WindowsRegister(string cliPath)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ClassesKey);
        key.SetValue("", "URL:NXM Protocol");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue("", $"\"{Path.Combine(Path.GetDirectoryName(cliPath)!, Platform.AppBinary)}\",0");
        }
        using var cmd = key.CreateSubKey(@"shell\open\command");
        cmd.SetValue("", WindowsCommandLine(cliPath));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? WindowsCommand()
    {
        try
        {
            using var cmd = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ClassesKey + @"\shell\open\command");
            return cmd?.GetValue("") as string;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static void Run(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Tool not installed (rare on SteamOS); the desktop entry alone still works for most browsers.
        }
    }
}
