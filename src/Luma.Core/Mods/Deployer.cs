using System.Text.Json;

namespace Luma.Core.Mods;

/// <summary>
/// Two or more enabled mods provide the same file. The one later in the load order wins, unless the
/// player picked a winner for this file (<see cref="Picked"/>). <see cref="Key"/> names the file
/// inside a mod ("mods/meshes/a.nif"), <see cref="Providers"/> lists every mod that has it, in load order.
/// </summary>
public sealed record Conflict(string Target, string Winner, IReadOnlyList<string> Losers, string Key = "", IReadOnlyList<string>? Providers = null, bool Picked = false);

/// <summary>
/// What a deploy will place. <see cref="Roots"/> maps "game" and "mods" to the folders those mod
/// files go into; <see cref="Overwrite"/> is the game's Overwrite folder, where files the game
/// makes while mods are in are kept (see <see cref="Deployer.Undeploy"/>).
/// </summary>
public sealed record DeployPlan(IReadOnlyDictionary<string, (string ModId, string Source)> Files, IReadOnlyList<Conflict> Conflicts,
    IReadOnlyDictionary<string, string>? Roots = null, string? Overwrite = null);

/// <summary>What a deploy changed, so it can be undone exactly.</summary>
public sealed record DeployManifest
{
    public string Game { get; init; } = "";
    public DateTime At { get; init; } = DateTime.UtcNow;
    public List<DeployedFile> Files { get; init; } = new();

    /// <summary>Game files moved aside because a mod replaces them: target → backup.</summary>
    public Dictionary<string, string> Backups { get; init; } = new();

    /// <summary>Folders created for mod files, deepest last (removed again when empty).</summary>
    public List<string> Dirs { get; init; } = new();

    /// <summary>Files Luma generated (plugins.txt, INI settings): deleted at undeploy, before the game's own are put back.</summary>
    public List<string> Written { get; init; } = new();

    /// <summary>The Overwrite folder new files go to at undeploy (null: leave them where they are).</summary>
    public string? Overwrite { get; init; }

    /// <summary>Folders that received mod files, with what was in them before: anything new in them at undeploy goes to <see cref="Overwrite"/>.</summary>
    public List<WatchedDir> Watched { get; init; } = new();

    /// <summary>The profile's own saves folder and INI files brought in (see <see cref="ProfileFiles"/>): put back first at undeploy.</summary>
    public List<ProfileSwap> Swaps { get; init; } = new();

    /// <summary>Put in for one game or tool run (taken out when it ends), by the process <see cref="Pid"/>: still here after that process is gone means Luma was stopped while they were in.</summary>
    public bool Temporary { get; init; }

    public int Pid { get; init; }

    /// <summary>The player saw the "mods are still in the game" notice and chose to leave them in.</summary>
    public bool Acknowledged { get; init; }
}

/// <summary>A folder mods were placed in: <paramref name="Sub"/> ("game"/"mods") and <paramref name="Rel"/> say where it is under that root; <paramref name="Before"/> lists its entries before the deploy.</summary>
public sealed record WatchedDir(string Dir, string Sub, string Rel, List<string> Before);

/// <summary>A placed file: a symlink to the mod store, or a copy (or Windows hard link) where symlinks don't work.</summary>
/// <remarks><see cref="Size"/> and <see cref="Written"/> are a copy's size and time as placed, to notice when the game changes it.</remarks>
public sealed record DeployedFile(string Target, string Source, bool Copy, long Size = -1, DateTime Written = default);

/// <summary>
/// Puts enabled mods into the game as symbolic links to Luma's mod store (copies where a link
/// can't be made), moving any game file a mod replaces aside first. Undeploy removes exactly
/// what was placed and puts the game's files back, so the game folder ends as it started.
/// </summary>
public static class Deployer
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static DeployPlan Plan(ModStore store, string gameDir, string? modsDir)
    {
        // Keyed without case: on a case-sensitive disk "Data/x.esp" and "data/x.esp" are the same file to the game.
        var spelling = new Dictionary<string, (string Target, string Key)>(StringComparer.OrdinalIgnoreCase);
        var providers = new Dictionary<string, List<(string ModId, string Source)>>(StringComparer.OrdinalIgnoreCase);
        // The Overwrite folder (files the game made) goes in last, so it wins like it does in MO2.
        var sets = store.State.Enabled().Select(m => (m.Id, Dir: store.ModDir(m.Id))).Append((ModStore.OverwriteId, store.OverwriteDir));
        foreach (var (modId, dir) in sets)
        {
            foreach (var (sub, root) in new[] { ("game", (string?)gameDir), ("mods", modsDir) })
            {
                var src = Path.Combine(dir, sub);
                if (!Directory.Exists(src))
                {
                    continue;
                }
                if (root == null)
                {
                    throw new InvalidOperationException("Start the game once from Steam so its Proton folder exists, then try again: its mods go inside it.");
                }
                var mod = store.State.Find(modId);
                foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(src, f);
                    if (mod != null && mod.Hidden.Count > 0 && mod.IsHidden(FileKey(sub, rel)))
                    {
                        continue;  // hidden by the player: stays out of the game
                    }
                    var target = GamePaths.Resolve(root, rel);
                    if (!providers.TryGetValue(target, out var list))
                    {
                        providers[target] = list = new();
                        spelling[target] = (target, FileKey(sub, rel));
                    }
                    list.Add((modId, f));
                }
            }
        }
        var files = new Dictionary<string, (string ModId, string Source)>(StringComparer.Ordinal);
        var conflicts = new List<Conflict>();
        foreach (var (target, list) in providers)
        {
            var (shown, key) = spelling[target];
            var picked = store.State.WinnerOf(key) is { } w && list.Any(p => p.ModId == w) ? w : null;
            // Later mods win; a picked winner's last copy wins instead.
            var win = picked != null ? list.Last(p => p.ModId == picked) : list[^1];
            files[shown] = win;
            var ids = list.Select(p => p.ModId).Distinct().ToList();
            if (ids.Count > 1)
            {
                conflicts.Add(new Conflict(shown, win.ModId, ids.Where(id => id != win.ModId).ToList(), key, ids, picked != null));
            }
        }
        var roots = new Dictionary<string, string> { ["game"] = gameDir };
        if (modsDir != null)
        {
            roots["mods"] = modsDir;
        }
        return new DeployPlan(files, conflicts.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).ToList(), roots, store.OverwriteDir);
    }

    /// <summary>A file's name inside a mod, the same for every mod and device: "mods/meshes/a.nif".</summary>
    public static string FileKey(string sub, string rel) => sub + "/" + rel.Replace('\\', '/');

    public static DeployManifest? ReadManifest(string manifestFile)
    {
        try
        {
            return File.Exists(manifestFile) ? JsonSerializer.Deserialize<DeployManifest>(File.ReadAllText(manifestFile), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Places the plan's files. An earlier deploy is undone first. Returns the manifest written.</summary>
    public static DeployManifest Deploy(DeployPlan plan, string game, string manifestFile, string backupDir)
    {
        Undeploy(manifestFile);
        var m = new DeployManifest { Game = game, Overwrite = plan.Overwrite };
        try
        {
            Watch(plan, m);
            foreach (var (target, (_, source)) in plan.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                MakeDirs(Path.GetDirectoryName(target)!, m);
                if (File.Exists(target) || IsLink(target))
                {
                    var backup = Path.Combine(backupDir, Guid.NewGuid().ToString("N")[..12] + "-" + Path.GetFileName(target));
                    Directory.CreateDirectory(backupDir);
                    File.Move(target, backup);
                    m.Backups[target] = backup;
                }
                // Hard links (Windows without symlink rights) are recorded as copies: removing one never touches the store.
                var copy = FileLinks.TryLink(target, source) != FileLinks.Kind.Symlink;
                if (!File.Exists(target))
                {
                    File.Copy(source, target);
                }
                var placed = new FileInfo(target);
                m.Files.Add(copy ? new DeployedFile(target, source, true, placed.Length, placed.LastWriteTimeUtc) : new DeployedFile(target, source, false));
            }
        }
        catch
        {
            Write(manifestFile, m);
            Undeploy(manifestFile);
            throw;
        }
        Write(manifestFile, m);
        return m;
    }

    /// <summary>Removes what the last deploy placed and restores the game's own files. Safe to call when nothing is deployed.</summary>
    public static void Undeploy(string manifestFile)
    {
        var m = ReadManifest(manifestFile);
        if (m == null)
        {
            return;
        }
        var problems = new List<string>();
        ProfileFiles.Restore(m, problems);
        if (problems.Count > 0)
        {
            // The game's own saves aren't back: keep the manifest so the next try finishes the job.
            throw new IOException(string.Join("; ", problems));
        }
        try
        {
            Capture(m);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add("Couldn't keep the files the game made: " + e.Message);
        }
        foreach (var w in m.Written)
        {
            try
            {
                File.Delete(w);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add(e.Message);
            }
        }
        foreach (var f in m.Files)
        {
            try
            {
                if (IsLink(f.Target) ? SamePath(File.ResolveLinkTarget(f.Target, false)?.FullName, f.Source) || SamePath(new FileInfo(f.Target).LinkTarget, f.Source) : f.Copy && File.Exists(f.Target))
                {
                    File.Delete(f.Target);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add(e.Message);
            }
        }
        foreach (var (target, backup) in m.Backups)
        {
            try
            {
                if (File.Exists(backup) && !File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(backup, target);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add(e.Message);
            }
        }
        foreach (var d in Enumerable.Reverse(m.Dirs))
        {
            try
            {
                if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any())
                {
                    Directory.Delete(d);
                }
            }
            catch (IOException)
            {
            }
        }
        if (problems.Count > 0)
        {
            throw new IOException("Some mod files couldn't be removed from the game: " + string.Join("; ", problems.Distinct().Take(3)));
        }
        File.Delete(manifestFile);
    }

    /// <summary>Folder and file names never taken into Overwrite: saves and caches belong to the game.</summary>
    private static readonly HashSet<string> NotCaptured = new(StringComparer.OrdinalIgnoreCase)
    {
        "saves", "save", "savegames", "savedata", "shadercache", "screenshots",
    };

    private static readonly HashSet<string> NotCapturedExt = new(StringComparer.OrdinalIgnoreCase) { ".ess", ".sav", ".fos", ".skse", ".f4se" };

    /// <summary>Records what is in each folder that gets mod files (below the game or mods folder itself).</summary>
    private static void Watch(DeployPlan plan, DeployManifest m)
    {
        if (plan.Overwrite == null || plan.Roots == null)
        {
            return;
        }
        var seen = new HashSet<string>(Platform.PathComparer);
        foreach (var target in plan.Files.Keys)
        {
            var dir = Path.GetDirectoryName(target)!;
            // Each folder from the root down to the file's: a new folder made in any of them is new too.
            // The innermost root first: a mods folder inside the game folder (Data) keeps its own paths.
            foreach (var (sub, root) in plan.Roots.OrderByDescending(r => r.Value.Length))
            {
                var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                if (!dir.StartsWith(rootFull + Path.DirectorySeparatorChar, Platform.PathComparison) && !string.Equals(dir, rootFull, Platform.PathComparison))
                {
                    continue;
                }
                // The game folder itself isn't watched (games keep saves and logs there); the mods folder (Data, plugins) is.
                var floor = sub == "mods" && !string.Equals(rootFull, Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Roots["game"])), Platform.PathComparison)
                    ? rootFull.Length - 1 : rootFull.Length;
                for (var d = dir; d.Length > floor; d = Path.GetDirectoryName(d)!)
                {
                    if (!seen.Add(d))
                    {
                        break;
                    }
                    var before = Directory.Exists(d) ? Directory.EnumerateFileSystemEntries(d).Select(e => Path.GetFileName(e)).ToList() : new List<string>();
                    var rel = Path.GetRelativePath(rootFull, d);
                    m.Watched.Add(new WatchedDir(d, sub, rel == "." ? "" : rel, before));
                }
                break;
            }
        }
    }

    /// <summary>
    /// Moves what the game made while mods were in (new files in the folders mods went into, mod
    /// files the game replaced or changed) to the Overwrite folder, so the game folder can be put
    /// back exactly and the files come back with the mods next time.
    /// </summary>
    private static void Capture(DeployManifest m)
    {
        if (m.Overwrite == null)
        {
            return;
        }
        var ours = new HashSet<string>(m.Files.Select(f => f.Target).Concat(m.Written), Platform.PathComparer);
        var made = new HashSet<string>(m.Dirs, Platform.PathComparer);
        var watched = new HashSet<string>(m.Watched.Select(w => w.Dir), Platform.PathComparer);
        foreach (var w in m.Watched)
        {
            if (!Directory.Exists(w.Dir))
            {
                continue;
            }
            var before = new HashSet<string>(w.Before, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Directory.EnumerateFileSystemEntries(w.Dir).ToList())
            {
                var name = Path.GetFileName(entry);
                if (before.Contains(name) || ours.Contains(entry) || NotCaptured.Contains(name))
                {
                    continue;
                }
                if (Directory.Exists(entry) && !IsLink(entry))
                {
                    if (made.Contains(entry) || watched.Contains(entry))
                    {
                        continue; // a folder Luma made: watched on its own
                    }
                    foreach (var f in Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories).ToList())
                    {
                        Keep(m.Overwrite, w.Sub, Path.Combine(w.Rel, Path.GetRelativePath(w.Dir, f)), f);
                    }
                    DeleteEmptyTree(entry);
                }
                else if (File.Exists(entry) && !NotCapturedExt.Contains(Path.GetExtension(name)))
                {
                    Keep(m.Overwrite, w.Sub, Path.Combine(w.Rel, name), entry);
                }
            }
        }
        // Mod files the game rewrote: a link replaced by a real file, or a copy changed in place.
        foreach (var f in m.Files)
        {
            if (!File.Exists(f.Target) || m.Watched.FirstOrDefault(w => SamePath(Path.GetDirectoryName(f.Target), w.Dir)) is not { } w)
            {
                continue;
            }
            var info = new FileInfo(f.Target);
            var replaced = !f.Copy && info.LinkTarget == null;
            var changed = f.Copy && f.Size >= 0 && (info.Length != f.Size || info.LastWriteTimeUtc != f.Written)
                && !(File.Exists(f.Source) && new FileInfo(f.Source) is var src && src.Length == info.Length && src.LastWriteTimeUtc == info.LastWriteTimeUtc);
            if (replaced || changed)
            {
                Keep(m.Overwrite, w.Sub, Path.Combine(w.Rel, Path.GetFileName(f.Target)), f.Target);
            }
        }
    }

    private static void Keep(string overwrite, string sub, string rel, string file)
    {
        var dest = Path.Combine(overwrite, sub, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(file, dest, overwrite: true);
    }

    private static void DeleteEmptyTree(string dir)
    {
        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            DeleteEmptyTree(d);
        }
        if (!Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
        }
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget != null;

    private static bool SamePath(string? a, string b) => a != null && string.Equals(a, b, Platform.PathComparison);

    private static void MakeDirs(string dir, DeployManifest m)
    {
        var missing = new Stack<string>();
        var cur = dir;
        while (!Directory.Exists(cur))
        {
            missing.Push(cur);
            cur = Path.GetDirectoryName(cur);
            if (cur == null)
            {
                break;
            }
        }
        while (missing.Count > 0)
        {
            var d = missing.Pop();
            Directory.CreateDirectory(d);
            m.Dirs.Add(d);
        }
    }

    public static void Write(string file, DeployManifest m)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(m, Json));
        File.Move(file + ".tmp", file, overwrite: true);
    }
}
