using Luma.Core.Detection;
using Luma.Core.Games;

namespace Luma.Core.Mods;

/// <summary>A plugin in the game's load order, and where it comes from.</summary>
/// <param name="ModId">The Luma mod that brings it, or null for plugins Luma doesn't manage (the game's own, Creations, hand-installed).</param>
public sealed record PluginEntry(string Name, string? ModId, bool Enabled, bool Master);

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
    /// plugins.txt; then the enabled mods' plugins, masters first, in mod order.
    /// </summary>
    public static List<PluginEntry> Plugins(DeployPlan plan, string modsDir, GameMods state, PluginSettings settings, IEnumerable<string> current)
    {
        // Plugins the mods place directly in the mods folder (Data/x.esp), in load order.
        var order = state.Enabled().Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var fromMods = plan.Files
            .Where(f => IsPlugin(f.Key) && string.Equals(Path.GetDirectoryName(f.Key)?.TrimEnd('/', '\\'), modsDir.TrimEnd('/', '\\'), Platform.PathComparison))
            .Select(f => (Name: Path.GetFileName(f.Key), f.Value.ModId))
            .OrderBy(p => IsMaster(p.Name) ? 0 : 1)
            .ThenBy(p => order.TryGetValue(p.ModId, out var i) ? i : int.MaxValue)
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
            list.Add(new PluginEntry(name, null, on, IsMaster(name)));
        }
        foreach (var (name, modId) in fromMods)
        {
            if (seen.Add(name))
            {
                list.Add(new PluginEntry(name, modId, state.IsPluginOn(name), IsMaster(name)));
            }
        }
        // Masters before plugins, keeping each group's order: the game enforces that anyway.
        return list.Where(p => p.Master).Concat(list.Where(p => !p.Master)).ToList();
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
            plugins = Plugins(plan, modsDir, state, settings, current);
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
