using System.Diagnostics;
using System.Runtime.InteropServices;
using Luma.Core.Detection;
using Luma.Host;

/// <summary>
/// `luma run -- %command%`: Steam starts the game through this. Mods are linked in, the game runs
/// with the loader's DLL overrides added to WINEDLLOVERRIDES, and when it exits the mods are taken
/// out again, so the game folder is left as Steam installed it. A problem with mods never stops
/// the game from starting: it then runs without them.
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
                var m = host.Mods.Deploy(game);
                deployed = true;
                AppLog.Info($"run: {game.Title} with {m.Files.Count} mod files");
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            AppLog.Warn($"run: starting without mods: {e.Message}");
            Console.Error.WriteLine($"luma: starting without mods: {e.Message}");
        }

        var psi = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        foreach (var a in command[1..])
        {
            psi.ArgumentList.Add(a);
        }
        if (deployed && game != null)
        {
            var overrides = game.Profile.DllOverrides.ToList();
            if (game.Profile.Loader != null && host.Games.Catalog.Loaders.TryGetValue(game.Profile.Loader, out var loader))
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
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            code = p.ExitCode;
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
