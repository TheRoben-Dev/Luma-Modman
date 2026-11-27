using System.Diagnostics;
using Luma.Core;
using Luma.Core.Detection;
using Luma.Core.Mods;

namespace Luma.Host;

/// <summary>
/// Runs a tool (xEdit, LOOT, BodySlide…) with the mods in the game, like MO2's executables: the
/// mods go in, the tool runs (on Linux in the game's Proton prefix, so it sees the same Data
/// folder and plugins.txt as the game), and when it closes the mods come out again, with
/// whatever it wrote kept in Overwrite. Mods that were already in stay in.
/// </summary>
public static class ToolRunner
{
    /// <summary>How the tool will be started, or why it can't be.</summary>
    public static ProcessStartInfo Command(ToolDef tool, DetectedGame game, AppPaths paths, string? modsDir)
    {
        var exe = Tools.ExePath(tool, game.GameDir, modsDir);
        var windowsExe = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        if (Platform.IsWindows || !windowsExe)
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            foreach (var a in Tools.SplitArgs(Tools.Expand(tool.Args, game.GameDir, modsDir, Tools.LootName(game.Profile.Id, game.Title), null)))
            {
                psi.ArgumentList.Add(a);
            }
            return psi;
        }
        // Linux: a Windows program runs in the game's own prefix, with the Proton Steam uses for it.
        if (game.Install.Prefix is not { } prefix)
        {
            throw new InvalidOperationException($"{game.Title} has no Proton prefix yet. Start it once from Steam, then try again.");
        }
        var args = Tools.SplitArgs(Tools.Expand(tool.Args, game.GameDir, modsDir, Tools.LootName(game.Profile.Id, game.Title), Tools.WinePath));
        var compatData = Path.GetFileName(prefix.TrimEnd('/')) == "pfx" ? Path.GetDirectoryName(prefix.TrimEnd('/'))! : null;
        var roots = new SteamScanner(paths.Home).Roots().Select(r => r.Root).ToList();
        var proton = compatData == null ? null
            : ProtonFinder.Find(game.Install.CompatTool, roots, game.Install.Library is { } lib ? [lib] : [], paths.Home);
        ProcessStartInfo run;
        if (proton != null)
        {
            run = new ProcessStartInfo(proton) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            run.ArgumentList.Add("run");
            run.Environment["STEAM_COMPAT_DATA_PATH"] = compatData!;
            run.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = roots.FirstOrDefault() ?? Path.Combine(paths.Home, ".local/share/Steam");
            if (game.Install.Source == GameSource.Steam)
            {
                run.Environment["SteamAppId"] = game.Install.SourceId;
                run.Environment["SteamGameId"] = game.Install.SourceId;
            }
        }
        else if (Which("wine") is { } wine)
        {
            // Heroic and Lutris prefixes (no Proton layout): plain Wine in that prefix.
            run = new ProcessStartInfo(wine) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            run.Environment["WINEPREFIX"] = prefix;
        }
        else
        {
            throw new InvalidOperationException("No Proton found in Steam (install one under Steam → Settings → Compatibility), and Wine isn't installed.");
        }
        run.ArgumentList.Add(exe);
        foreach (var a in args)
        {
            run.ArgumentList.Add(a);
        }
        return run;
    }

    /// <summary>
    /// Puts the mods in (unless they are already), runs the tool and waits for it to close, then
    /// takes the mods out again if it put them in. Returns the tool's exit code.
    /// </summary>
    public static int Run(AppHost host, DetectedGame game, ToolDef tool)
    {
        var mods = host.Mods;
        var wasIn = mods.IsDeployed(game);
        if (!wasIn)
        {
            mods.Deploy(game);
        }
        try
        {
            var exe = Tools.ExePath(tool, game.GameDir, mods.ModsDir(game));
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException($"{tool.Name} isn't at {host.Paths.Short(exe)}. If it's a mod, turn that mod on; else edit the tool's path.");
            }
            var psi = Command(tool, game, host.Paths, mods.ModsDir(game));
            AppLog.Info($"running tool {tool.Name} for {game.Key}: {psi.FileName} {string.Join(' ', psi.ArgumentList)}");
            var code = GameProcess.Run(psi);
            AppLog.Info($"tool {tool.Name} exited with {code}");
            return code;
        }
        finally
        {
            // LOOT sorts by rewriting plugins.txt: keep its order before the file goes back.
            if (Tools.Recognise(tool.Exe)?.Name == "LOOT" || tool.Name == "LOOT")
            {
                mods.AdoptPluginOrder(game);
            }
            if (!wasIn)
            {
                mods.Undeploy(game);
            }
        }
    }

    private static string? Which(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
}
