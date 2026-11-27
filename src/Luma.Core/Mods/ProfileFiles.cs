using Luma.Core.Detection;

namespace Luma.Core.Mods;

/// <summary>
/// A game folder or file a profile brought in at deploy: the game's save folder (<paramref name="Dir"/>)
/// or one of its INI files. <paramref name="Store"/> is the profile's own copy, <paramref name="Aside"/>
/// where the game's own folder was moved for the time being, <paramref name="Linked"/> that the save
/// folder is a link to the profile's (else the profile's saves were moved in).
/// </summary>
public sealed record ProfileSwap(string Target, string Store, bool Dir, string? Aside = null, bool Linked = false);

/// <summary>
/// Per-profile saves and INI files, like MO2's profile-specific saves and game INI files: while a
/// profile with its own saves is in, the game's save folder is the profile's; its INI files are the
/// profile's copies. Taking the mods out puts the game's own back.
/// </summary>
public static class ProfileFiles
{
    public const string AsideSuffix = ".luma-global";

    public static string? SavesDir(DetectedGame game, string home) =>
        game.Profile.Saves is { } s ? GamePaths.ResolvePath(game, s, home) : null;

    public static List<string> Inis(DetectedGame game, string home) =>
        game.Profile.Inis.Select(i => GamePaths.ResolvePath(game, i, home)).OfType<string>().ToList();

    /// <summary>Copies the game's current saves (when <paramref name="copySaves"/>) or INI files into a profile's folder, keeping what it already has.</summary>
    public static void Seed(DetectedGame game, string home, string profileDir, bool saves, bool copySaves)
    {
        if (saves)
        {
            var store = Path.Combine(profileDir, "saves");
            Directory.CreateDirectory(store);
            if (copySaves && SavesDir(game, home) is { } dir && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(store).Any())
            {
                CopyDir(dir, store);
            }
            return;
        }
        var inis = Path.Combine(profileDir, "inis");
        Directory.CreateDirectory(inis);
        foreach (var ini in Inis(game, home))
        {
            var copy = Path.Combine(inis, Path.GetFileName(ini));
            if (!File.Exists(copy) && File.Exists(ini))
            {
                File.Copy(ini, copy);
            }
        }
    }

    /// <summary>Brings the active profile's own saves and INI files in, recording each in <paramref name="m"/>.</summary>
    public static void Place(DetectedGame game, string home, GameMods state, string profileDir, DeployManifest m, string backupDir)
    {
        if (state.HasLocalSaves && SavesDir(game, home) is { } saves)
        {
            var store = Path.Combine(profileDir, "saves");
            Directory.CreateDirectory(store);
            string? aside = null;
            if (IsLink(saves))
            {
                RemoveLink(saves);  // left from a crash; it pointed at a profile's folder, nothing is lost
            }
            if (Directory.Exists(saves))
            {
                aside = saves + AsideSuffix;
                if (Directory.Exists(aside))
                {
                    throw new IOException($"{aside} is in the way: it holds the game's own saves from an earlier time the mods were in. Move its saves back into {Path.GetFileName(saves)} first.");
                }
                Directory.Move(saves, aside);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(saves)!);
            }
            m.Swaps.Add(new ProfileSwap(saves, store, true, aside, Linked: true));
            try
            {
                Directory.CreateSymbolicLink(saves, store);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Windows without the right to make links: the profile's saves move in for the time being.
                m.Swaps[^1] = m.Swaps[^1] with { Linked = false };
                MoveDir(store, saves);
            }
        }
        if (state.HasLocalInis)
        {
            var inis = Path.Combine(profileDir, "inis");
            foreach (var ini in Inis(game, home))
            {
                var copy = Path.Combine(inis, Path.GetFileName(ini));
                if (File.Exists(ini) && !m.Backups.ContainsKey(ini))
                {
                    Directory.CreateDirectory(backupDir);
                    var backup = Path.Combine(backupDir, Guid.NewGuid().ToString("N")[..12] + "-" + Path.GetFileName(ini));
                    File.Move(ini, backup);
                    m.Backups[ini] = backup;
                }
                m.Swaps.Add(new ProfileSwap(ini, copy, false));
                m.Written.Add(ini);
                if (File.Exists(copy))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
                    File.Copy(copy, ini, true);
                }
            }
        }
    }

    /// <summary>Takes the profile's saves and INI files back into its folder and puts the game's own back. Safe to run again after a failure.</summary>
    public static void Restore(DeployManifest m, List<string> problems)
    {
        foreach (var s in Enumerable.Reverse(m.Swaps))
        {
            try
            {
                if (!s.Dir)
                {
                    // The game's changes to its settings stay with the profile; the file itself is
                    // removed with Luma's other written files and the game's own put back after.
                    if (File.Exists(s.Target))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(s.Store)!);
                        File.Copy(s.Target, s.Store, true);
                    }
                    continue;
                }
                if (IsLink(s.Target))
                {
                    RemoveLink(s.Target);
                }
                else if (Directory.Exists(s.Target))
                {
                    MoveDir(s.Target, s.Store);
                }
                if (s.Aside != null && Directory.Exists(s.Aside) && !Directory.Exists(s.Target))
                {
                    Directory.Move(s.Aside, s.Target);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Couldn't put {Path.GetFileName(s.Target)} back: {e.Message}");
            }
        }
    }

    private static bool IsLink(string path) => new DirectoryInfo(path).LinkTarget != null;

    private static void RemoveLink(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.Delete(path);  // removes a folder link, not what it points at
        }
        else
        {
            File.Delete(path);
        }
    }

    /// <summary>Moves a folder, across drives too (copy, then delete). What is already at <paramref name="to"/> is kept unless the move brings a newer file of the same name.</summary>
    public static void MoveDir(string from, string to)
    {
        if (Directory.Exists(to) && !Directory.EnumerateFileSystemEntries(to).Any())
        {
            Directory.Delete(to);
        }
        if (!Directory.Exists(to))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                Directory.Move(from, to);
                return;
            }
            catch (IOException)
            {
                // another drive: copy below
            }
        }
        CopyDir(from, to);
        Directory.Delete(from, true);
    }

    public static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        }
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), true);
        }
    }
}
