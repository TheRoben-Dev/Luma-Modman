using Luma.Host;
using Luma.Host.Updates;

// Headless entry point (`luma`), including the Steam launch wrapper (`luma run -- %command%`)
// that links a game's enabled mods in before it starts and takes them out again after it exits.

return Cli.Run(args);

internal static class Cli
{
    private const string Usage = """
        Luma command line

          luma version                 show the installed version
          luma settings                print the settings file path and contents
          luma games                   scan for installed games Luma supports
          luma games --all             list every supported game (profiles)
          luma nxm <nxm://link>        hand a Nexus "Mod Manager Download" link to the app
          luma run [--game KEY] -- CMD start a game with its mods linked in, removed after it exits
                                       (Steam launch options: luma run -- %command%)
          luma deploy KEY              link a game's enabled mods in and leave them there
          luma undeploy KEY            take a game's mods out again
          luma mods KEY                list a game's mods in load order
          luma steam-add [--restart]   add Luma to Steam with its art (--restart closes and
                                       restarts Steam around it)

        Luma never downloads games, DRM bypasses or pirated content.
        """;

    /// <summary>Starts Luma (next to this binary) unless it's already running; it picks up the link from its inbox.</summary>
    private static void StartAppIfNeeded(AppHost host)
    {
        if (File.Exists(host.PidFile) && int.TryParse(File.ReadAllText(host.PidFile).Trim(), out var pid) && Luma.Core.Platform.ProcessAlive(pid))
        {
            return;
        }
        var app = Path.Combine(AppContext.BaseDirectory, Luma.Core.Platform.AppBinary);
        if (File.Exists(app))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(app) { UseShellExecute = false });
        }
        else
        {
            Console.WriteLine("Link saved. Luma picks it up the next time it starts.");
        }
    }

    /// <summary>A detected game by key ("steam:489830") or profile id ("skyrimse").</summary>
    public static Luma.Core.Detection.DetectedGame FindGame(AppHost host, string key)
    {
        var games = host.Games.Games.Count > 0 ? host.Games.Games : host.Games.Scan();
        return games.FirstOrDefault(g => g.Key == key) ?? games.FirstOrDefault(g => g.Profile.Id == key)
            ?? throw new ArgumentException($"no installed game \"{key}\" (see `luma games`)");
    }

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            var host = new AppHost();
            switch (args[0])
            {
                case "version":
                    Console.WriteLine(AppUpdater.DisplayVersion(AppUpdater.CurrentAppVersion()));
                    return 0;
                case "settings":
                    Console.WriteLine(host.Paths.SettingsFile);
                    Console.WriteLine(File.Exists(host.Paths.SettingsFile) ? File.ReadAllText(host.Paths.SettingsFile) : "(defaults)");
                    return 0;
                case "nxm" when args.Length > 1:
                    GameProcess.HideOwnConsole();
                    if (!NxmHandler.Drop(host.Paths, args[1]))
                    {
                        Console.Error.WriteLine("error: not a Nexus Mods download link");
                        return 1;
                    }
                    StartAppIfNeeded(host);
                    return 0;
                case "games" when args.Length > 1 && args[1] == "--all":
                    foreach (var g in host.Games.Catalog.Games)
                    {
                        var status = g.Moddable ? g.Loader ?? "no loader needed" : "not moddable";
                        Console.WriteLine($"{g.Id,-34} {g.Name}  [{status}; {string.Join(", ", g.Sites.Names())}]");
                    }
                    Console.WriteLine($"{host.Games.Catalog.Games.Count} games");
                    foreach (var p in host.Games.Catalog.Problems)
                    {
                        Console.Error.WriteLine("problem: " + p);
                    }
                    return 0;
                case "games":
                    foreach (var g in host.Games.Scan())
                    {
                        Console.WriteLine($"{g.Key,-24} {g.Title}");
                        Console.WriteLine($"    folder  {g.GameDir}");
                        if (!Luma.Core.Platform.IsWindows)
                        {
                            Console.WriteLine($"    prefix  {g.Install.Prefix ?? (g.Profile.Runtime == "native" ? "(native Linux build)" : "(not created yet: start the game once)")}");
                        }
                        if (!g.Profile.Moddable)
                        {
                            Console.WriteLine($"    not moddable: {g.Profile.NotModdable}");
                        }
                    }
                    return 0;
                case "steam-add":
                {
                    var dir = AppContext.BaseDirectory.TrimEnd('/', '\\');
                    var restart = args.Contains("--restart") && Luma.Core.Steam.SteamShortcuts.SteamRunning(host.Paths.Home);
                    if (restart && !SteamSetup.CloseSteam(host.Paths.Home, TimeSpan.FromSeconds(30)))
                    {
                        Console.Error.WriteLine("error: Steam didn't close. Exit it yourself, then run this again.");
                        return 1;
                    }
                    var n = SteamSetup.Add(host.Paths.Home, dir);
                    Console.WriteLine($"Luma added to Steam ({n} account{(n == 1 ? "" : "s")}).");
                    if (restart)
                    {
                        SteamSetup.StartSteam();
                    }
                    return 0;
                }
                case "run":
                    GameProcess.HideOwnConsole();
                    return Launch.Run(host, args[1..]);
                case "deploy" when args.Length > 1:
                {
                    var g = FindGame(host, args[1]);
                    var m = host.Mods.Deploy(g);
                    Console.WriteLine($"{m.Files.Count} mod files linked into {g.Title}.");
                    return 0;
                }
                case "undeploy" when args.Length > 1:
                {
                    var g = FindGame(host, args[1]);
                    host.Mods.Undeploy(g);
                    Console.WriteLine($"Mods taken out of {g.Title}.");
                    return 0;
                }
                case "mods" when args.Length > 1:
                {
                    var g = FindGame(host, args[1]);
                    var state = host.Mods.Store(g).State;
                    Console.WriteLine($"{g.Title}, profile {state.Active}{(host.Mods.IsDeployed(g) ? " (deployed)" : "")}");
                    foreach (var e in state.Order)
                    {
                        var mod = state.Find(e.Id);
                        Console.WriteLine($"  [{(e.Enabled ? "x" : " ")}] {mod?.Name ?? e.Id} {mod?.Version}");
                    }
                    return 0;
                }
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }
}
