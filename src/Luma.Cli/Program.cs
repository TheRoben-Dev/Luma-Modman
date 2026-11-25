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
