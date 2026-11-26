using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Luma.Core;

/// <summary>
/// Steam on Windows: where it is installed (from the registry, like Steam's own tools find it),
/// whether it is running, and its executable. Steam's files below its folder (steamapps,
/// libraryfolders.vdf, userdata, appcache) are laid out exactly as on Linux.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsSteam
{
    private const string UserKey = @"Software\Valve\Steam";

    /// <summary>Steam's install folder: HKCU SteamPath, HKLM InstallPath, then the default location.</summary>
    public static IReadOnlyList<string> Roots()
    {
        var candidates = new List<string?>
        {
            Read(Registry.CurrentUser, UserKey, "SteamPath"),
            Read(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            Read(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c))
            {
                continue;
            }
            // SteamPath is stored with forward slashes and in lower case ("c:/program files (x86)/steam").
            var dir = Path.GetFullPath(c.Replace('/', '\\'));
            if (Directory.Exists(Path.Combine(dir, "steamapps")) && seen.Add(dir.TrimEnd('\\')))
            {
                roots.Add(dir);
            }
        }
        return roots;
    }

    /// <summary>steam.exe, from HKCU SteamExe or the install folder.</summary>
    public static string? Exe()
    {
        if (Read(Registry.CurrentUser, UserKey, "SteamExe") is { Length: > 0 } exe && File.Exists(exe.Replace('/', '\\')))
        {
            return Path.GetFullPath(exe.Replace('/', '\\'));
        }
        return Roots().Select(r => Path.Combine(r, "steam.exe")).FirstOrDefault(File.Exists);
    }

    /// <summary>True while the Steam client runs (HKCU ActiveProcess\pid names a live process).</summary>
    public static bool Running()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserKey + @"\ActiveProcess");
            if (key?.GetValue("pid") is int pid && pid != 0 && Platform.ProcessAlive(pid))
            {
                return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
        return System.Diagnostics.Process.GetProcessesByName("steam").Any(p =>
        {
            using (p)
            {
                return !p.HasExited;
            }
        });
    }

    internal static string? Read(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
