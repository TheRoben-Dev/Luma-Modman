using System.Text.Json;

namespace Luma.Core.Mods;

/// <summary>Two or more enabled mods provide the same file; the one later in the load order wins.</summary>
public sealed record Conflict(string Target, string Winner, IReadOnlyList<string> Losers);

public sealed record DeployPlan(IReadOnlyDictionary<string, (string ModId, string Source)> Files, IReadOnlyList<Conflict> Conflicts);

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
}

/// <summary>A placed file: a symlink to the mod store, or a copy (or Windows hard link) where symlinks don't work.</summary>
public sealed record DeployedFile(string Target, string Source, bool Copy);

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
        var files = new Dictionary<string, (string Target, string ModId, string Source)>(StringComparer.OrdinalIgnoreCase);
        var providers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in store.State.Enabled())
        {
            var dir = store.ModDir(mod.Id);
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
                foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var target = GamePaths.Resolve(root, Path.GetRelativePath(src, f));
                    if (!providers.TryGetValue(target, out var list))
                    {
                        providers[target] = list = new List<string>();
                    }
                    list.Add(mod.Id);
                    var spelling = files.TryGetValue(target, out var prev) ? prev.Target : target;
                    files[target] = (spelling, mod.Id, f);  // later mods win
                }
            }
        }
        var conflicts = providers.Where(p => p.Value.Distinct().Count() > 1)
            .Select(p => new Conflict(files[p.Key].Target, p.Value[^1], p.Value.Where(id => id != p.Value[^1]).Distinct().ToList()))
            .ToList();
        return new DeployPlan(files.Values.ToDictionary(v => v.Target, v => (v.ModId, v.Source), StringComparer.Ordinal), conflicts);
    }

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
        var m = new DeployManifest { Game = game };
        try
        {
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
                m.Files.Add(new DeployedFile(target, source, copy));
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
