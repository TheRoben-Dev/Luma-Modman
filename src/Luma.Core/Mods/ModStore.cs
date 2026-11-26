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
    public InstalledMod Install(string archive, InstalledMod mod, GameProfile profile, string? gamePrefix = null)
    {
        Directory.CreateDirectory(Root);
        var tmp = Path.Combine(Root, ".unpack-" + Guid.NewGuid().ToString("N")[..8]);
        var staged = ModDir(mod.Id) + ".new";
        try
        {
            Archive.Extract(archive, tmp);
            var plan = ModLayout.Plan(tmp, profile, mod.ModId.Length > 0 ? mod.ModId : mod.Name, gamePrefix);
            if (plan.Count == 0)
            {
                throw new InvalidDataException($"{Path.GetFileName(archive)} has no files to install.");
            }
            if (Directory.Exists(staged))
            {
                Directory.Delete(staged, true);
            }
            foreach (var e in plan)
            {
                var dest = Path.Combine(staged, e.Root == DeployRoot.Game ? "game" : "mods", e.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(e.Source, dest, overwrite: true);
            }
            var dir = ModDir(mod.Id);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
            Directory.Move(staged, dir);

            var old = State.Find(mod.Id);
            var history = old?.History.ToList() ?? new List<ModVersion>();
            if (old?.Archive != null && old.Archive != archive && File.Exists(old.Archive))
            {
                history.Insert(0, new ModVersion(old.Version, old.FileId, old.Archive, old.InstalledAt));
            }
            history.RemoveAll(h => h.Archive == archive || !File.Exists(h.Archive));
            var installed = mod with { Archive = archive, Files = plan.Count, InstalledAt = DateTime.UtcNow, History = history.Take(5).ToList(), UpdateVersion = null };
            var mods = State.Mods.Where(m => m.Id != mod.Id).Append(installed).ToList();
            var profiles = State.Profiles.ToDictionary(p => p.Key, p => p.Value.Any(e => e.Id == mod.Id) ? p.Value : p.Value.Append(new ProfileEntry(mod.Id)).ToList());
            Save(State with { Mods = mods, Profiles = profiles });
            return installed;
        }
        finally
        {
            if (Directory.Exists(tmp))
            {
                Directory.Delete(tmp, true);
            }
            if (Directory.Exists(staged))
            {
                Directory.Delete(staged, true);
            }
        }
    }

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
