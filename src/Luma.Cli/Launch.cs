using System.Diagnostics;
using System.Runtime.InteropServices;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Luma.Host;

/// <summary>
/// `luma run -- %command%`: Steam starts the game through this. Mods are linked in, the game runs
/// with the loader's DLL overrides added to WINEDLLOVERRIDES (Linux; Windows loads them itself),
/// and when it exits the mods are taken out again, so the game folder is left as Steam installed
/// it. When the game's script extender is in the game (installed as a mod, or by hand), the game
/// starts through its loader. A problem with mods never stops the game from starting: it then
/// runs without them.
/// </summary>
internal static class Launch
{
    public static int Run(AppHost host, string[] args)
    {
        var sep = Array.IndexOf(args, "--");
        var opts = sep < 0 ? args : args[..sep];
        var command = sep < 0 ? [] : args[(sep + 1)..];
        if (command.Length == 0)
        {
            Console.Error.WriteLine("usage: luma run [--game KEY] -- <command>   (Steam: luma run -- %command%)");
            return 2;
        }
        var gi = Array.IndexOf(opts, "--game");
        var key = gi >= 0 && gi + 1 < opts.Length ? opts[gi + 1] : SteamKey();
        DetectedGame? game = null;
        var deployed = false;
        try
        {
            if (key != null)
            {
                game = Cli.FindGame(host, key);
            }
            if (game is { Profile.Moddable: true } && host.Mods.Store(game).State.Enabled().Any())
            {
                var m = host.Mods.Deploy(game, temporary: true);
                deployed = true;
                AppLog.Info($"run: {game.Title} with {m.Files.Count} mod files");
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            AppLog.Warn($"run: starting without mods: {e.Message}");
            Console.Error.WriteLine($"luma: starting without mods: {e.Message}");
        }

        // Script extenders and launchers (skse64_loader.exe, Mod Engine 2, SMAPI): start the game through them when they're in the game.
        if (game is { Profile.Moddable: true })
        {
            var store = host.Mods.Store(game);
            foreach (var ext in host.Mods.Loaders(game))
            {
                if (LoaderRules.Launcher(game.Profile, ext) is not { } launcher || !File.Exists(Path.Combine(game.GameDir, launcher))
                    || (ext.Id != game.Profile.Loader && !LoaderInstaller.Installed(game, ext, store.State)))
                {
                    continue;
                }
                var native = game.Profile.Runtime == "native" && !OperatingSystem.IsWindows();
                var through = LaunchCommand.ThroughLauncher(command, game.GameDir, launcher, LoaderRules.LaunchArgs(game.Profile, ext), native);
                if (through != command)
                {
                    AppLog.Info($"run: starting {game.Title} through {ext.Name}");
                    command = through;
                    break;
                }
            }
        }

        var psi = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        foreach (var a in command[1..])
        {
            psi.ArgumentList.Add(a);
        }
        if (deployed && game != null && !OperatingSystem.IsWindows())
        {
            var overrides = game.Profile.DllOverrides.ToList();
            var state = host.Mods.Store(game).State;
            foreach (var loader in host.Mods.Loaders(game).Where(l => l.Id == game.Profile.Loader || LoaderInstaller.Installed(game, l, state)))
            {
                overrides.AddRange(loader.DllOverrides);
            }
            var existing = Environment.GetEnvironmentVariable("WINEDLLOVERRIDES");
            var all = string.Join(';', new[] { existing }.Concat(overrides).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
            if (all.Length > 0)
            {
                psi.Environment["WINEDLLOVERRIDES"] = all;
            }
        }

        // Steam stops a game by signalling the process group; keep running until the game has exited, then clean up.
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => c.Cancel = true);
        using var intr = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => c.Cancel = true);
        int code;
        try
        {
            code = GameProcess.Run(psi);
        }
        finally
        {
            if (deployed && game != null)
            {
                try
                {
                    host.Mods.Undeploy(game);
                }
                catch (IOException e)
                {
                    AppLog.Warn("run: " + e.Message);
                    Console.Error.WriteLine("luma: " + e.Message);
                }
            }
        }
        return code;
    }

    /// <summary>Steam sets SteamAppId (or SteamGameId) for the game it starts.</summary>
    private static string? SteamKey()
    {
        var id = Environment.GetEnvironmentVariable("SteamAppId") ?? Environment.GetEnvironmentVariable("SteamGameId");
        return id is { Length: > 0 } && id != "0" && long.TryParse(id, out _) ? "steam:" + id : null;
    }
}
