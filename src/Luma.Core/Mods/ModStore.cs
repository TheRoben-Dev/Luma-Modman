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

    /// <summary>The player renamed it: updates keep <see cref="Name"/> instead of the site's.</summary>
    public bool Renamed { get; init; }

    /// <summary>The player's own notes.</summary>
    public string Notes { get; init; } = "";

    /// <summary>The player's category for it ("Textures", "UI"…), empty for none.</summary>
    public string Category { get; init; } = "";

    /// <summary>Files left out when the mod goes in, by key ("mods/textures/a.dds"); a key ending in "/" hides a whole folder.</summary>
    public List<string> Hidden { get; init; } = new();

    public bool IsHidden(string key) => Hidden.Any(h => string.Equals(h, key, StringComparison.OrdinalIgnoreCase)
        || (h.EndsWith('/') && key.StartsWith(h, StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>Separators in the load order (MO2-style headings): id ("sep:…") → name. They sit in profiles like mods.</summary>
    public Dictionary<string, string> Separators { get; init; } = new();

    public static bool IsSeparator(string id) => id.StartsWith("sep:", StringComparison.Ordinal);

    /// <summary>Entries of the active profile whose mod isn't installed (a modpack's, still downloading); separators aren't counted.</summary>
    public int Pending() => Order.Count(e => !IsSeparator(e.Id) && Find(e.Id) == null);

    /// <summary>The categories in use, sorted.</summary>
    public List<string> Categories() => Mods.Select(m => m.Category).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    public bool IsEnabled(string id) => Order.Any(e => e.Id == id && e.Enabled);

    /// <summary>Plugins (.esp/.esm/.esl) turned off by hand, per profile; plugins of enabled mods are on otherwise.</summary>
    public Dictionary<string, List<string>> PluginsOff { get; init; } = new();

    public bool IsPluginOn(string plugin) =>
        !(PluginsOff.TryGetValue(Active, out var off) && off.Contains(plugin, StringComparer.OrdinalIgnoreCase));

    /// <summary>Programs run with the mods in (xEdit, LOOT, BodySlide…), for every profile.</summary>
    public List<ToolDef> Tools { get; init; } = new();

    /// <summary>The plugin load order set by hand (moved or sorted), per profile; plugins not in it follow the mod order.</summary>
    public Dictionary<string, List<string>> PluginOrder { get; init; } = new();

    /// <summary>Conflict winners picked by hand, per profile: file key ("mods/meshes/a.nif") → mod id.</summary>
    public Dictionary<string, Dictionary<string, string>> Winners { get; init; } = new();

    /// <summary>Profiles that keep their own saves (the game's save folder is theirs while their mods are in).</summary>
    public List<string> LocalSaves { get; init; } = new();

    /// <summary>Profiles that keep their own copies of the game's INI files.</summary>
    public List<string> LocalInis { get; init; } = new();

    public bool HasLocalSaves => LocalSaves.Contains(Active);

    public bool HasLocalInis => LocalInis.Contains(Active);

    /// <summary>The mod picked to win <paramref name="key"/> in the active profile, if any.</summary>
    public string? WinnerOf(string key) =>
        Winners.TryGetValue(Active, out var w) && w.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) is { Value: { } id } ? id : null;
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

    /// <summary>The id the Overwrite folder has in deploy plans and conflicts.</summary>
    public const string OverwriteId = "overwrite";

    /// <summary>
    /// Files the game (or a tool) made while mods were in, kept like a mod ("game/..." and "mods/...")
    /// and put in last at every deploy, like MO2's Overwrite.
    /// </summary>
    public string OverwriteDir => Path.Combine(Root, ".overwrite");

    /// <summary>Where a profile keeps its own saves ("saves") and INI files ("inis").</summary>
    public string ProfileDir(string profile) =>
        Path.Combine(Root, ".profiles", Safe(profile) + "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile)))[..8].ToLowerInvariant());

    /// <summary>A mod's files as keys ("game/…", "mods/…") with their sizes, sorted.</summary>
    public List<(string Key, long Size)> ModFiles(string id)
    {
        var dir = ModDir(id);
        var list = new List<(string, long)>();
        foreach (var sub in new[] { "game", "mods" })
        {
            var src = Path.Combine(dir, sub);
            if (Directory.Exists(src))
            {
                list.AddRange(Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
                    .Select(f => (Deployer.FileKey(sub, Path.GetRelativePath(src, f)), new FileInfo(f).Length)));
            }
        }
        return list.OrderBy(f => f.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Hides (leaves out of the game) or shows files or folders ("mods/textures/" for a folder) of a mod.
    /// Showing a folder shows everything in it.
    /// </summary>
    public void SetHidden(string id, IEnumerable<string> keys, bool hidden)
    {
        var mod = State.Find(id) ?? throw new KeyNotFoundException("That mod isn't installed any more.");
        var list = mod.Hidden.ToList();
        foreach (var key in keys)
        {
            if (hidden)
            {
                if (!mod.IsHidden(key) && !list.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    // A folder hidden as a whole replaces the files hidden inside it.
                    if (key.EndsWith('/'))
                    {
                        list.RemoveAll(h => h.StartsWith(key, StringComparison.OrdinalIgnoreCase));
                    }
                    list.Add(key);
                }
            }
            else
            {
                list.RemoveAll(h => string.Equals(h, key, StringComparison.OrdinalIgnoreCase)
                    || (key.EndsWith('/') && h.StartsWith(key, StringComparison.OrdinalIgnoreCase)));
            }
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        Save(State with { Mods = State.Mods.Select(m => m.Id == id ? m with { Hidden = list } : m).ToList() });
    }

    /// <summary>Turns the active profile's own saves or INI files on or off.</summary>
    public void SetLocal(bool saves, bool on)
    {
        var list = (saves ? State.LocalSaves : State.LocalInis).Where(p => p != State.Active).ToList();
        if (on)
        {
            list.Add(State.Active);
        }
        Save(saves ? State with { LocalSaves = list } : State with { LocalInis = list });
    }

    /// <summary>The Overwrite folder's files, as "game/…" or "mods/…" paths.</summary>
    public List<string> OverwriteFiles() => Directory.Exists(OverwriteDir)
        ? Directory.EnumerateFiles(OverwriteDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(OverwriteDir, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
        : new List<string>();

    /// <summary>Deletes the Overwrite folder's files.</summary>
    public void ClearOverwrite()
    {
        if (Directory.Exists(OverwriteDir))
        {
            Directory.Delete(OverwriteDir, true);
        }
    }

    /// <summary>Turns the Overwrite folder's files into a mod of their own (added at the end of every profile, enabled).</summary>
    public InstalledMod OverwriteToMod(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Give the new mod a name.");
        }
        var files = OverwriteFiles();
        if (files.Count == 0)
        {
            throw new InvalidOperationException("Overwrite is empty.");
        }
        var id = "local:" + Safe(name);
        if (State.Find(id) != null)
        {
            throw new InvalidOperationException($"There's already a mod called {name}.");
        }
        Directory.Move(OverwriteDir, ModDir(id));
        var mod = new InstalledMod { Id = id, Site = "local", Name = name, Files = files.Count };
        Save(State with
        {
            Mods = State.Mods.Append(mod).ToList(),
            Profiles = State.Profiles.ToDictionary(p => p.Key, p => p.Value.Append(new ProfileEntry(id)).ToList()),
        });
        return mod;
    }

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
            if (old != null)
            {
                // The player's own name, notes and category survive updates and reinstalls.
                installed = installed with { Notes = old.Notes, Category = old.Category, Hidden = old.Hidden, Renamed = old.Renamed, Name = old.Renamed ? old.Name : installed.Name };
            }
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

    /// <summary>Adds a tool, or replaces the one with the same name.</summary>
    public void SetTool(ToolDef tool, string? replacing = null) =>
        Save(State with { Tools = State.Tools.Where(t => t.Name != tool.Name && t.Name != replacing).Append(tool).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList() });

    public void RemoveTool(string name) => Save(State with { Tools = State.Tools.Where(t => t.Name != name).ToList() });

    /// <summary>Sets the active profile's plugin load order (null or empty: back to the mod order).</summary>
    public void SetPluginOrder(IEnumerable<string>? names)
    {
        var all = State.PluginOrder.ToDictionary(p => p.Key, p => p.Value.ToList());
        var list = names?.ToList();
        if (list is { Count: > 0 })
        {
            all[State.Active] = list;
        }
        else
        {
            all.Remove(State.Active);
        }
        Save(State with { PluginOrder = all });
    }

    /// <summary>
    /// Picks <paramref name="modId"/> to win the files <paramref name="keys"/> in the active profile,
    /// whatever the load order says; null goes back to the load order for them.
    /// </summary>
    public void SetWinner(IEnumerable<string> keys, string? modId)
    {
        var all = State.Winners.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value, StringComparer.OrdinalIgnoreCase));
        var w = all.TryGetValue(State.Active, out var d) ? d : all[State.Active] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (modId == null)
            {
                w.Remove(key);
            }
            else
            {
                w[key] = modId;
            }
        }
        if (w.Count == 0)
        {
            all.Remove(State.Active);
        }
        Save(State with { Winners = all });
    }

    /// <summary>Takes entries whose mod isn't installed (a modpack's that never arrived) out of the active profile.</summary>
    public void ForgetMissing() => UpdateOrder(o => o.Where(e => GameMods.IsSeparator(e.Id) || State.Find(e.Id) != null).ToList());

    /// <summary>Changes a mod's name, notes or category (null leaves it as it is).</summary>
    public void SetModInfo(string id, string? name = null, string? notes = null, string? category = null)
    {
        var mod = State.Find(id) ?? throw new KeyNotFoundException("That mod isn't installed any more.");
        if (name != null && name.Trim().Length == 0)
        {
            throw new ArgumentException("A mod needs a name.");
        }
        var changed = mod with
        {
            Name = name?.Trim() ?? mod.Name,
            Renamed = mod.Renamed || (name != null && name.Trim() != mod.Name),
            Notes = notes?.Trim() ?? mod.Notes,
            Category = category?.Trim() ?? mod.Category,
        };
        Save(State with { Mods = State.Mods.Select(m => m.Id == id ? changed : m).ToList() });
    }

    /// <summary>Adds a separator to the active profile, before <paramref name="beforeId"/> (or at the end). Returns its id.</summary>
    public string AddSeparator(string name, string? beforeId = null)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Give the separator a name.");
        }
        var id = "sep:" + Guid.NewGuid().ToString("N")[..10];
        var seps = new Dictionary<string, string>(State.Separators) { [id] = name };
        Save(State with { Separators = seps });
        UpdateOrder(o =>
        {
            var at = beforeId == null ? -1 : o.FindIndex(e => e.Id == beforeId);
            o.Insert(at < 0 ? o.Count : at, new ProfileEntry(id));
            return o;
        });
        return id;
    }

    public void RenameSeparator(string id, string name)
    {
        if (name.Trim().Length == 0 || !State.Separators.ContainsKey(id))
        {
            return;
        }
        Save(State with { Separators = new Dictionary<string, string>(State.Separators) { [id] = name.Trim() } });
    }

    /// <summary>Takes a separator out of the active profile (and forgets it when no profile has it).</summary>
    public void RemoveSeparator(string id)
    {
        UpdateOrder(o => o.Where(e => e.Id != id).ToList());
        if (!State.Profiles.Values.Any(p => p.Any(e => e.Id == id)))
        {
            Save(State with { Separators = State.Separators.Where(s => s.Key != id).ToDictionary(s => s.Key, s => s.Value) });
        }
    }

    /// <summary>Renames a profile, with its plugin settings, picked winners and plugin order.</summary>
    public void RenameProfile(string from, string to)
    {
        to = to.Trim();
        if (from == GameMods.DefaultProfile)
        {
            throw new InvalidOperationException("The Default profile can't be renamed. Make a copy with New profile instead.");
        }
        if (to.Length == 0 || State.Profiles.ContainsKey(to))
        {
            throw new InvalidOperationException(to.Length == 0 ? "Give the profile a name." : $"There's already a profile called {to}.");
        }
        static Dictionary<string, T> Move<T>(Dictionary<string, T> d, string a, string b) =>
            d.ToDictionary(p => p.Key == a ? b : p.Key, p => p.Value);
        if (Directory.Exists(ProfileDir(from)) && !Directory.Exists(ProfileDir(to)))
        {
            Directory.Move(ProfileDir(from), ProfileDir(to));  // its own saves and INI files
        }
        Save(State with
        {
            Profiles = Move(State.Profiles, from, to),
            PluginsOff = Move(State.PluginsOff, from, to),
            Winners = Move(State.Winners, from, to),
            PluginOrder = Move(State.PluginOrder, from, to),
            LocalSaves = State.LocalSaves.Select(p => p == from ? to : p).ToList(),
            LocalInis = State.LocalInis.Select(p => p == from ? to : p).ToList(),
            Active = State.Active == from ? to : State.Active,
        });
    }

    public void DeleteProfile(string name)
    {
        if (name == GameMods.DefaultProfile)
        {
            throw new InvalidOperationException("The Default profile can't be deleted.");
        }
        var profiles = State.Profiles.Where(p => p.Key != name).ToDictionary(p => p.Key, p => p.Value);
        // Its own saves stay in its folder under .profiles (a profile made again with the name gets them back).
        Save(State with
        {
            Profiles = profiles,
            Active = State.Active == name ? GameMods.DefaultProfile : State.Active,
            LocalSaves = State.LocalSaves.Where(p => p != name).ToList(),
            LocalInis = State.LocalInis.Where(p => p != name).ToList(),
        });
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
