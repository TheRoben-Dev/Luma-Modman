namespace Luma.Host;

/// <summary>Rewrites the command Steam passes to `luma run` (its %command%).</summary>
public static class LaunchCommand
{
    /// <summary>
    /// Starts <paramref name="launcher"/> (a script extender's loader, inside the game folder) in
    /// place of the game's own program: the last argument that is a .exe in the game folder, which
    /// is what Proton is asked to run. Steam's own wrappers before it are kept. Returns the command
    /// unchanged when no argument is a program in the game folder.
    /// </summary>
    public static string[] ThroughLauncher(string[] command, string gameDir, string launcher) =>
        ThroughLauncher(command, gameDir, launcher, [], native: false);

    /// <param name="args">Arguments the launcher needs, put right after it (Mod Engine 2: "-t", "er").</param>
    /// <param name="native">A native Linux game: its program has no .exe ending (SMAPI's StardewModdingAPI).</param>
    public static string[] ThroughLauncher(string[] command, string gameDir, string launcher, IReadOnlyList<string> args, bool native)
    {
        static string Norm(string p) => p.Replace('\\', '/');
        var dir = Norm(gameDir).TrimEnd('/') + "/";
        for (var i = command.Length - 1; i >= 0; i--)
        {
            var a = Norm(command[i]);
            var rooted = a.StartsWith('/') || (a.Length > 2 && a[1] == ':' && a[2] == '/');
            var program = native ? !a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) : a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            if (rooted && program && a.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && !a.Contains("/../"))
            {
                var copy = command.ToList();
                copy[i] = Path.Combine(gameDir, launcher.Replace('/', Path.DirectorySeparatorChar));
                copy.InsertRange(i + 1, args);
                return copy.ToArray();
            }
        }
        return command;
    }
}
