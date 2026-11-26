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
    public static string[] ThroughLauncher(string[] command, string gameDir, string launcher)
    {
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDir)) + "/";
        for (var i = command.Length - 1; i >= 0; i--)
        {
            var a = command[i].Replace('\\', '/');
            if (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(a)
                && Path.GetFullPath(a).StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                var copy = command.ToArray();
                copy[i] = Path.Combine(gameDir, launcher.Replace('\\', '/'));
                return copy;
            }
        }
        return command;
    }
}
