using Luma.Core.Detection;
using Luma.Core.Games;

namespace Luma.Core.Mods;

/// <summary>A plugin in the game's load order, and where it comes from.</summary>
/// <param name="ModId">The Luma mod that brings it, or null for plugins Luma doesn't manage (the game's own, Creations, hand-installed).</param>
/// <param name="Light">A light plugin (ESL): doesn't count toward the plugin limit.</param>
/// <param name="Needs">The plugins its header names as masters.</param>
public sealed record PluginEntry(string Name, string? ModId, bool Enabled, bool Master, bool Light = false, IReadOnlyList<string>? Needs = null);

/// <summary>Something wrong with a plugin list: a plugin whose masters are missing or load after it, or too many plugins.</summary>
public sealed record PluginProblem(string? Plugin, string Text);

/// <summary>
/// The game files besides the mods themselves that Bethesda games need while mods are in:
/// the plugin list (plugins.txt, loadorder.txt, file dates) and INI settings such as archive
/// invalidation. Written at deploy, recorded in the deploy manifest and put back at undeploy.
/// </summary>
public static class GameSetup
{
    private static readonly string[] PluginExtensions = [".esm", ".esp", ".esl"];

    public static bool IsPlugin(string file) => PluginExtensions.Contains(Path.GetExtension(file).ToLowerInvariant());

    public static bool IsMaster(string file) => Path.GetExtension(file).ToLowerInvariant() is ".esm" or ".esl";

    /// <summary>
    /// The load order: plugins Luma doesn't manage keep their place and state from the current
    /// plugins.txt; then the enabled mods' plugins in mod order. An order set by hand or by
    /// <see cref="SortPlugins"/> (kept per profile) comes first; masters always load before plugins.
    /// </summary>
    /// <param name="header">Reads a plugin's header (the file in Luma's store or the game's folder); null: judged by the file name.</param>
    public static List<PluginEntry> Plugins(DeployPlan plan, string modsDir, GameMods state, PluginSettings settings, IEnumerable<string> current,
        Func<string, PluginHeader>? header = null)
    {
        header ??= f => PluginFile.Read(f, 0) with { Masters = [] };
        // Plugins the mods place directly in the mods folder (Data/x.esp), in load order.
        var order = state.Enabled().Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var fromMods = plan.Files
            .Where(f => IsPlugin(f.Key) && string.Equals(Path.GetDirectoryName(f.Key)?.TrimEnd('/', '\\'), modsDir.TrimEnd('/', '\\'), Platform.PathComparison))
            .Select(f => (Name: Path.GetFileName(f.Key), f.Value.ModId, Header: header(f.Value.Source)))
            .OrderBy(p => order.TryGetValue(p.ModId, out var i) ? i : int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ours = new HashSet<string>(fromMods.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var baseSet = new HashSet<string>(settings.Base, StringComparer.OrdinalIgnoreCase);
        var list = new List<PluginEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in current)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            var on = settings.Style == "plain" || line.StartsWith('*');
            var name = line.TrimStart('*').Trim();
            if (!IsPlugin(name) || ours.Contains(name) || baseSet.Contains(name) || !seen.Add(name))
            {
                continue;
            }
            var h = header(Path.Combine(modsDir, name));
            list.Add(new PluginEntry(name, null, on, h.Master, h.Light, h.Masters));
        }
        foreach (var (name, modId, h) in fromMods)
        {
            if (seen.Add(name))
            {
                list.Add(new PluginEntry(name, modId, state.IsPluginOn(name), h.Master, h.Light, h.Masters));
            }
        }
        if (state.PluginOrder.TryGetValue(state.Active, out var saved) && saved.Count > 0)
        {
            list = ApplyOrder(list, saved);
        }
        // Masters before plugins, keeping each group's order: the game enforces that anyway.
        return list.Where(p => p.Master).Concat(list.Where(p => !p.Master)).ToList();
    }

    /// <summary>
    /// Puts the plugins named in <paramref name="saved"/> in that order; a plugin it doesn't name
    /// (new since) stays right after the plugin it followed before.
    /// </summary>
    public static List<PluginEntry> ApplyOrder(List<PluginEntry> list, IReadOnlyList<string> saved)
    {
        var at = saved.Select((n, i) => (n, i)).GroupBy(x => x.n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        var keyed = new List<(PluginEntry P, double Key)>();
        var last = -1.0;
        var step = 0;
        foreach (var p in list)
        {
            if (at.TryGetValue(p.Name, out var i))
            {
                last = i;
                step = 0;
                keyed.Add((p, i));
            }
            else
            {
                keyed.Add((p, last + ++step / 10000.0));
            }
        }
        return keyed.OrderBy(k => k.Key).Select(k => k.P).ToList();
    }

    /// <summary>
    /// A load order where every plugin comes after the masters it needs, changing as little as
    /// possible: masters first, then each plugin in its current place unless a master it needs is
    /// later, in which case that master moves up in front of it.
    /// </summary>
    public static List<PluginEntry> SortPlugins(IReadOnlyList<PluginEntry> list)
    {
        var byName = list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PluginEntry>();
        void Visit(PluginEntry p)
        {
            if (done.Contains(p.Name) || !visiting.Add(p.Name))
            {
                return;  // placed already, or a cycle: leave it where it is
            }
            foreach (var m in p.Needs ?? [])
            {
                if (byName.TryGetValue(m, out var needed) && (needed.Master || !p.Master))
                {
                    Visit(needed);
                }
            }
            visiting.Remove(p.Name);
            if (done.Add(p.Name))
            {
                result.Add(p);
            }
        }
        foreach (var p in list.Where(p => p.Master))
        {
            Visit(p);
        }
        foreach (var p in list.Where(p => !p.Master))
        {
            Visit(p);
        }
        return result.Where(p => p.Master).Concat(result.Where(p => !p.Master)).ToList();
    }

    /// <summary>What would stop the game loading this list: missing or late masters, and too many plugins.</summary>
    public static List<PluginProblem> Problems(IReadOnlyList<PluginEntry> list, PluginSettings settings)
    {
        var problems = new List<PluginProblem>();
        var baseSet = new HashSet<string>(settings.Base, StringComparer.OrdinalIgnoreCase);
        var pos = list.Select((p, i) => (p, i)).GroupBy(x => x.p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (!p.Enabled)
            {
                continue;
            }
            var missing = new List<string>();
            var late = new List<string>();
            foreach (var m in p.Needs ?? [])
            {
                if (baseSet.Contains(m))
                {
                    continue;
                }
                if (!pos.TryGetValue(m, out var at) || !at.p.Enabled)
                {
                    missing.Add(m);
                }
                else if (at.i > i)
                {
                    late.Add(m);
                }
            }
            if (missing.Count > 0)
            {
                problems.Add(new PluginProblem(p.Name, $"{p.Name} needs {string.Join(", ", missing)}, which {(missing.Count == 1 ? "isn't" : "aren't")} installed and on. The game crashes at start without {(missing.Count == 1 ? "it" : "them")}."));
            }
            if (late.Count > 0)
            {
                problems.Add(new PluginProblem(p.Name, $"{p.Name} loads before {string.Join(", ", late)}, which it needs first. Sort the plugins to fix it."));
            }
        }
        var full = list.Count(p => p.Enabled && !p.Light) + settings.Base.Count;
        if (full > settings.MaxPlugins)
        {
            problems.Add(new PluginProblem(null, $"{full} full plugins are on (the game's own included), more than the {settings.MaxPlugins} the game can load. Turn some off, or use light (ESL) versions."));
        }
        var light = list.Count(p => p.Enabled && p.Light);
        if (light > 4096)
        {
            problems.Add(new PluginProblem(null, $"{light} light plugins are on, more than the 4096 the game can load."));
        }
        return problems;
    }

    /// <summary>
    /// plugins.txt as the game reads it. Games with the "asterisk" style load their own plugins
    /// implicitly; the older ones list them too (<paramref name="baseHere"/>: those installed).
    /// </summary>
    public static string PluginsTxt(IEnumerable<PluginEntry> plugins, PluginSettings settings, IEnumerable<string>? baseHere = null)
    {
        var lines = new List<string> { "# This file is used by the game to keep track of your downloaded content.", "# Written by Luma while mods are in the game." };
        lines.AddRange(settings.Style == "asterisk"
            ? plugins.Select(p => (p.Enabled ? "*" : "") + p.Name)
            : (baseHere ?? []).Concat(plugins.Where(p => p.Enabled).Select(p => p.Name)));
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>loadorder.txt (Skyrim 2011): every plugin, the game's own first.</summary>
    public static string LoadOrderTxt(IEnumerable<PluginEntry> plugins, PluginSettings settings) =>
        string.Join("\r\n", settings.Base.Concat(plugins.Select(p => p.Name))) + "\r\n";

    /// <summary>Sets [section] key=value in INI text, adding the section or key when missing.</summary>
    public static string SetIni(string text, string section, string key, string value)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        var start = lines.FindIndex(l => string.Equals(l.Trim(), $"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add("");
            }
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
        }
        else
        {
            var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
            if (end < 0)
            {
                end = lines.Count;
            }
            var at = lines.FindIndex(start + 1, end - start - 1, l => l.Split('=')[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                lines[at] = $"{key}={value}";
            }
            else
            {
                // After the section's last setting, before any blank lines that separate it from the next.
                var insert = end;
                while (insert > start + 1 && lines[insert - 1].Trim().Length == 0)
                {
                    insert--;
                }
                lines.Insert(insert, $"{key}={value}");
            }
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// Writes the plugin list and INI settings for <paramref name="game"/> after its mods were
    /// placed, recording each file in <paramref name="m"/> so undeploy puts the originals back.
    /// Returns the plugins written (empty for games without a plugin list).
    /// </summary>
    public static List<PluginEntry> Apply(DetectedGame game, string home, DeployPlan plan, string? modsDir, GameMods state, DeployManifest m, string backupDir)
    {
        var plugins = new List<PluginEntry>();
        if (game.Profile.Plugins is { } settings && modsDir != null && GamePaths.ResolvePath(game, settings.File, home) is { } file)
        {
            var current = File.Exists(file) ? File.ReadAllLines(file) : [];
            plugins = Plugins(plan, modsDir, state, settings, current, f => PluginFile.Read(f, settings.Light));
            var baseHere = settings.Base.Where(b => File.Exists(GamePaths.Resolve(modsDir, b))).ToList();
            Write(file, PluginsTxt(plugins, settings, baseHere), m, backupDir);
            if (settings.LoadOrderFile)
            {
                Write(Path.Combine(Path.GetDirectoryName(file)!, "loadorder.txt"), LoadOrderTxt(plugins, settings), m, backupDir);
            }
            if (settings.Timestamps)
            {
                SetDates(modsDir, settings, plugins, plan);
            }
        }
        if (game.Profile.ModSettings is { } ms && modsDir != null && GamePaths.ResolvePath(game, ms, home) is { } lsx)
        {
            var mods = LarianModules(plan, modsDir, state);
            if (mods.Count > 0)
            {
                var existing = File.Exists(lsx) ? File.ReadAllText(lsx) : null;
                Write(lsx, Larian.ModSettings(existing, mods, mods.Select(x => x.Uuid.ToLowerInvariant()).ToHashSet()), m, backupDir);
            }
        }
        foreach (var group in game.Profile.Ini.GroupBy(i => i.File, StringComparer.OrdinalIgnoreCase))
        {
            if (GamePaths.ResolvePath(game, group.Key, home) is not { } ini)
            {
                continue;
            }
            // A game's main INI is made by the game on first start; only the *Custom.ini ones may be created.
            if (!File.Exists(ini) && !Path.GetFileName(ini).Contains("Custom", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var text = File.Exists(ini) ? File.ReadAllText(ini) : "";
            foreach (var s in group)
            {
                text = SetIni(text, s.Section, s.Key, s.Value);
            }
            Write(ini, text, m, backupDir);
        }
        return plugins;
    }

    /// <summary>The modules of the .pak files the plan puts at the top of the mods folder, in the profile's load order.</summary>
    public static List<LarianModule> LarianModules(DeployPlan plan, string modsDir, GameMods state)
    {
        var rank = state.Order.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var dir = Path.GetFullPath(modsDir).TrimEnd(Path.DirectorySeparatorChar);
        var list = new List<LarianModule>();
        foreach (var f in plan.Files
                     .Where(f => f.Key.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(f.Key)), dir, Platform.PathComparison))
                     .OrderBy(f => rank.GetValueOrDefault(f.Value.ModId, int.MaxValue)).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            var info = Path.Combine(Path.GetDirectoryName(f.Value.Source)!, "info.json");
            if (Larian.Module(f.Value.Source, info) is { } mod && !list.Any(x => x.Uuid.Equals(mod.Uuid, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(mod);
            }
        }
        return list;
    }

    /// <summary>Puts a generated file in place: the existing one is moved aside (restored at undeploy).</summary>
    private static void Write(string target, string content, DeployManifest m, string backupDir)
    {
        var existing = m.Files.FirstOrDefault(f => string.Equals(f.Target, target, Platform.PathComparison));
        if (existing != null)
        {
            return;  // a mod brings this file itself: it wins
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target) && !m.Backups.ContainsKey(target))
        {
            Directory.CreateDirectory(backupDir);
            var backup = Path.Combine(backupDir, Guid.NewGuid().ToString("N")[..12] + "-" + Path.GetFileName(target));
            File.Copy(target, backup);
            m.Backups[target] = backup;
            File.Delete(target);
        }
        File.WriteAllText(target, content);
        m.Written.Add(target);
    }

    /// <summary>Older games load plugins by file date: one minute apart, after the game's own.</summary>
    private static void SetDates(string modsDir, PluginSettings settings, List<PluginEntry> plugins, DeployPlan plan)
    {
        var start = settings.Base.Select(b => Path.Combine(modsDir, b)).Where(File.Exists).Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(new DateTime(2008, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Max();
        var i = 0;
        foreach (var p in plugins.Where(p => p.ModId != null))
        {
            var target = plan.Files.Keys.FirstOrDefault(k => string.Equals(Path.GetFileName(k), p.Name, StringComparison.OrdinalIgnoreCase));
            if (target != null && File.Exists(target))
            {
                // Follows the link to the copy in Luma's store, which only Luma uses.
                File.SetLastWriteTimeUtc(target, start.AddMinutes(++i));
            }
        }
    }
}
