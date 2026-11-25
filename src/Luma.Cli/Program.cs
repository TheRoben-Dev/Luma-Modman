using Luma.Host;
using Luma.Host.Updates;

// Headless entry point (`luma`). Later phases add the Steam launch wrapper (`luma run -- %command%`)
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

        Luma never downloads games, DRM bypasses or pirated content.
        """;

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
                        Console.WriteLine($"    prefix  {g.Install.Prefix ?? (g.Profile.Runtime == "native" ? "(native Linux build)" : "(not created yet: start the game once)")}");
                        if (!g.Profile.Moddable)
                        {
                            Console.WriteLine($"    not moddable: {g.Profile.NotModdable}");
                        }
                    }
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }
}
