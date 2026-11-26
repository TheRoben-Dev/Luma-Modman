using System.Text.Json;
using System.Text.Json.Serialization;
using Luma.Core.Games;
using Luma.Core.Sources;

namespace Luma.Core.Mods;

/// <summary>One mod installed for a game (stored in Luma's folder, linked into the game when it starts).</summary>
public sealed record InstalledMod
{
    /// <summary>"site:modId", or "local:name" for files added by hand.</summary>
    public required string Id { get; init; }
    public string Site { get; init; } = "";
    public string ModId { get; init; } = "";
    public required string Name { get; init; }
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string FileId { get; init; } = "";
    public string? PageUrl { get; init; }

    /// <summary>The downloaded file this was installed from (kept for reinstall and rollback).</summary>
    public string? Archive { get; init; }

    public DateTime InstalledAt { get; init; } = DateTime.UtcNow;
    public int Files { get; init; }
    public List<ModDependency> Dependencies { get; init; } = new();

    /// <summary>Earlier versions still on disk, newest first: rollback reinstalls one.</summary>
    public List<ModVersion> History { get; init; } = new();

    /// <summary>A newer version the last update check found, if any.</summary>
    public string? UpdateVersion { get; init; }

    /// <summary>The options picked in the mod's FOMOD installer; null when it has none (or none were picked yet).</summary>
    public List<FomodChoice>? Fomod { get; init; }
}

public sealed record ModVersion(string Version, string FileId, string Archive, DateTime InstalledAt);

public sealed record ProfileEntry(string Id, bool Enabled = true);

/// <summary>A game's installed mods and its mod profiles (each an ordered list: later wins conflicts).</summary>
public sealed record GameMods
{
    public const string DefaultProfile = "Default";

    public List<InstalledMod> Mods { get; init; } = new();
    public Dictionary<string, List<ProfileEntry>> Profiles { get; init; } = new() { [DefaultProfile] = new() };
    public string Active { get; init; } = DefaultProfile;

    [JsonIgnore]
    public List<ProfileEntry> Order => Profiles.TryGetValue(Active, out var l) ? l : Profiles[DefaultProfile];

    /// <summary>Enabled mods in load order.</summary>
    public IEnumerable<InstalledMod> Enabled() =>
        Order.Where(e => e.Enabled).Select(e => Mods.FirstOrDefault(m => m.Id == e.Id)).OfType<InstalledMod>();

    public InstalledMod? Find(string id) => Mods.FirstOrDefault(m => m.Id == id);

    public bool IsEnabled(string id) => Order.Any(e => e.Id == id && e.Enabled);

    /// <summary>Plugins (.esp/.esm/.esl) turned off by hand, per profile; plugins of enabled mods are on otherwise.</summary>
    public Dictionary<string, List<string>> PluginsOff { get; init; } = new();

    public bool IsPluginOn(string plugin) =>
        !(PluginsOff.TryGetValue(Active, out var off) && off.Contains(plugin, StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// The mods of one game, in ~/.local/share/luma/mods/&lt;game&gt;/: a folder per mod holding its
/// files as they will appear in the game ("game/..." from the game folder, "mods/..." from the
/// mods folder), and state.json with the profiles.
/// </summary>
public sealed class ModStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Root { get; }
    public GameMods State { get; private set; }

    public ModStore(string modsDir, string gameKey)
    {
        Root = Path.Combine(modsDir, Safe(gameKey));
        State = Load();
    }

    private string StateFile => Path.Combine(Root, "state.json");

    public static string Safe(string s) =>
        new string(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray()).Trim('.');

    public string ModDir(string id) => Path.Combine(Root, Safe(id));

    private GameMods Load()
    {
        try
        {
            if (File.Exists(StateFile))
            {
                var s = JsonSerializer.Deserialize<GameMods>(File.ReadAllText(StateFile), Json);
                if (s != null)
                {
                    if (!s.Profiles.ContainsKey(GameMods.DefaultProfile))
                    {
                        s.Profiles[GameMods.DefaultProfile] = new();
                    }
                    return s;
                }
            }
        }
        catch (JsonException)
        {
            File.Copy(StateFile, StateFile + ".broken", overwrite: true);
        }
        return new GameMods();
    }

    public void Save(GameMods state)
    {
        State = state;
        Directory.CreateDirectory(Root);
        File.WriteAllText(StateFile + ".tmp", JsonSerializer.Serialize(state, Json));
        File.Move(StateFile + ".tmp", StateFile, overwrite: true);
    }

    /// <summary>
    /// Unpacks <paramref name="archive"/> and stores it as <paramref name="mod"/>, replacing an
    /// earlier version (kept in its history for rollback). New mods are enabled at the end of every profile.
    /// </summary>
    /// <remarks>
    /// When <paramref name="loader"/> has a launcher and the archive holds it (SKSE64 added by hand),
    /// the archive is installed as that loader: in the launcher's folder, as "loader:&lt;id&gt;".
    /// </remarks>
    /// <param name="onlyProfile">Add a new mod to this profile only (a modpack's), not to every profile.</param>
    /// <param name="fileState">A plugin file's state for FOMOD conditions: "Active", "Inactive" or "Missing".</param>
    /// <exception cref="FomodNeededException">The archive has a FOMOD installer and <paramref name="mod"/> carries no choices.</exception>
    public InstalledMod Install(string archive, InstalledMod mod, GameProfile profile, string? gamePrefix = null, LoaderInfo? loader = null, string? onlyProfile = null,
        Func<string, string>? fileState = null)
    {
        Directory.CreateDirectory(Root);
        var tmp = Path.Combine(Root, ".unpack-" + Guid.NewGuid().ToString("N")[..8]);
        string? staged = null;
        try
        {
            Archive.Extract(archive, tmp);
            var root = tmp;
            if (loader?.Launcher is { } launcher && mod.Site != "loader" && FindFile(tmp, Path.GetFileName(launcher)) is { } found)
            {
                root = Path.GetDirectoryName(found)!;
                gamePrefix = loader.LauncherDir;
                mod = mod with { Id = "loader:" + loader.Id, Site = "loader", ModId = loader.Id, Name = loader.Name };
            }
            if (mod.Site != "loader" && Fomod.FindRoot(root) is { } fomodRoot)
            {
                if (mod.Fomod == null)
                {
                    throw new FomodNeededException(mod.Name);
                }
                var config = Fomod.Load(fomodRoot);
                var (_, _, files) = Fomod.Run(config, mod.Fomod, fileState ?? FileState);
                root = Path.Combine(tmp, ".fomod-out");
                if (Fomod.Assemble(fomodRoot, files, root) == 0)
                {
                    throw new InvalidDataException($"The options picked for {mod.Name} install no files.");
                }
            }
            var stage = staged = ModDir(mod.Id) + ".new";
            var plan = ModLayout.Plan(root, profile, mod.ModId.Length > 0 ? mod.ModId : mod.Name, gamePrefix);
            if (mod.Site == "loader")
            {
                // Script extenders ship their source code and readmes alongside; the game doesn't need them.
                var src = (gamePrefix is { Length: > 0 } ? gamePrefix.Trim('/') + "/" : "") + "src/";
                plan.RemoveAll(e => e.Target.StartsWith(src, StringComparison.OrdinalIgnoreCase)
                    || (e.Target.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && e.Target.Count(c => c == '/') == src.Count(c => c == '/') - 1));
            }
            if (plan.Count == 0)
            {
                throw new InvalidDataException($"{Path.GetFileName(archive)} has no files to install.");
            }
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, true);
            }
            foreach (var e in plan)
            {
                var dest = Path.Combine(stage, e.Root == DeployRoot.Game ? "game" : "mods", e.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(e.Source, dest, overwrite: true);
            }
            var dir = ModDir(mod.Id);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
            Directory.Move(stage, dir);

            var old = State.Find(mod.Id);
            var history = old?.History.ToList() ?? new List<ModVersion>();
            if (old?.Archive != null && old.Archive != archive && File.Exists(old.Archive))
            {
                history.Insert(0, new ModVersion(old.Version, old.FileId, old.Archive, old.InstalledAt));
            }
            history.RemoveAll(h => h.Archive == archive || !File.Exists(h.Archive));
            var installed = mod with { Archive = archive, Files = plan.Count, InstalledAt = DateTime.UtcNow, History = history.Take(5).ToList(), UpdateVersion = null };
            var mods = State.Mods.Where(m => m.Id != mod.Id).Append(installed).ToList();
            var profiles = State.Profiles.ToDictionary(p => p.Key, p => p.Value.Any(e => e.Id == mod.Id) || (onlyProfile != null && p.Key != onlyProfile)
                ? p.Value : p.Value.Append(new ProfileEntry(mod.Id)).ToList());
            Save(State with { Mods = mods, Profiles = profiles });
            return installed;
        }
        finally
        {
            if (Directory.Exists(tmp))
            {
                Directory.Delete(tmp, true);
            }
            if (staged != null && Directory.Exists(staged))
            {
                Directory.Delete(staged, true);
            }
        }
    }

    private static string? FindFile(string dir, string name) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Uninstall(string id)
    {
        var dir = ModDir(id);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
        Save(State with
        {
            Mods = State.Mods.Where(m => m.Id != id).ToList(),
            Profiles = State.Profiles.ToDictionary(p => p.Key, p => p.Value.Where(e => e.Id != id).ToList()),
        });
    }

    public void SetEnabled(string id, bool on) =>
        UpdateOrder(o => o.Select(e => e.Id == id ? e with { Enabled = on } : e).ToList());

    /// <summary>Moves a mod in the active profile's load order by <paramref name="delta"/> places.</summary>
    public void Move(string id, int delta) => UpdateOrder(o =>
    {
        var i = o.FindIndex(e => e.Id == id);
        if (i < 0)
        {
            return o;
        }
        var j = Math.Clamp(i + delta, 0, o.Count - 1);
        var e = o[i];
        o.RemoveAt(i);
        o.Insert(j, e);
        return o;
    });

    private void UpdateOrder(Func<List<ProfileEntry>, List<ProfileEntry>> change)
    {
        var profiles = State.Profiles.ToDictionary(p => p.Key, p => p.Value.ToList());
        var key = profiles.ContainsKey(State.Active) ? State.Active : GameMods.DefaultProfile;
        profiles[key] = change(profiles[key].ToList());
        Save(State with { Profiles = profiles });
    }

    /// <summary>Creates a profile (a copy of the active one) or switches to an existing one.</summary>
    public void UseProfile(string name, bool copyActive = true)
    {
        var profiles = State.Profiles.ToDictionary(p => p.Key, p => p.Value.ToList());
        if (!profiles.ContainsKey(name))
        {
            profiles[name] = copyActive ? State.Order.ToList() : State.Mods.Select(m => new ProfileEntry(m.Id, false)).ToList();
        }
        Save(State with { Profiles = profiles, Active = name });
    }

    /// <summary>Creates (or replaces) a profile with this order and makes it the active one.</summary>
    public void SetProfile(string name, IEnumerable<ProfileEntry> entries)
    {
        var profiles = State.Profiles.ToDictionary(p => p.Key, p => p.Value.ToList());
        profiles[name] = entries.ToList();
        Save(State with { Profiles = profiles, Active = name });
    }

    /// <summary>A plugin's state among this game's mods: Active (in an enabled mod), Inactive (in a disabled one) or Missing.</summary>
    public string FileState(string plugin)
    {
        var name = Path.GetFileName(plugin.Replace('\\', '/'));
        var holders = State.Mods.Where(m => Directory.Exists(Path.Combine(ModDir(m.Id), "mods"))
            && Directory.EnumerateFiles(Path.Combine(ModDir(m.Id), "mods")).Any(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
        return holders.Count == 0 ? "Missing" : holders.Any(m => State.IsEnabled(m.Id)) ? "Active" : "Inactive";
    }

    /// <summary>Turns one plugin of an enabled mod on or off in the active profile.</summary>
    public void SetPluginEnabled(string plugin, bool on)
    {
        var all = State.PluginsOff.ToDictionary(p => p.Key, p => p.Value.ToList());
        var off = all.TryGetValue(State.Active, out var l) ? l : all[State.Active] = new List<string>();
        off.RemoveAll(p => string.Equals(p, plugin, StringComparison.OrdinalIgnoreCase));
        if (!on)
        {
            off.Add(plugin);
        }
        Save(State with { PluginsOff = all });
    }

    /// <summary>Takes entries whose mod isn't installed (a modpack's that never arrived) out of the active profile.</summary>
    public void ForgetMissing() => UpdateOrder(o => o.Where(e => State.Find(e.Id) != null).ToList());

    public void DeleteProfile(string name)
    {
        if (name == GameMods.DefaultProfile)
        {
            throw new InvalidOperationException("The Default profile can't be deleted.");
        }
        var profiles = State.Profiles.Where(p => p.Key != name).ToDictionary(p => p.Key, p => p.Value);
        Save(State with { Profiles = profiles, Active = State.Active == name ? GameMods.DefaultProfile : State.Active });
    }

    /// <summary>Required mods (by site id) that aren't installed and enabled.</summary>
    public IEnumerable<(InstalledMod Mod, ModDependency Missing)> MissingDependencies()
    {
        foreach (var m in State.Enabled())
        {
            foreach (var d in m.Dependencies)
            {
                var id = $"{d.Site}:{d.Id}";
                if (!State.IsEnabled(id))
                {
                    yield return (m, d);
                }
            }
        }
    }
}
