using Luma.Core;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;
using Luma.Core.Sources;

namespace Luma.Host;

/// <summary>
/// Installs, deploys and removes mods for detected games. Mods live in Luma's store; Deploy links
/// the active profile's enabled mods into the game and Undeploy takes them out again. The Steam
/// launch wrapper (`luma run -- %command%`) does both around each game session.
/// </summary>
public sealed class ModManager
{
    private readonly AppPaths _paths;
    private readonly Dictionary<string, ModStore> _stores = new();
    private readonly GameCatalog? _catalog;

    public ModManager(AppPaths paths, GameCatalog? catalog = null)
    {
        _paths = paths;
        _catalog = catalog;
    }

    /// <summary>The game's loader, if its profile names one.</summary>
    public LoaderInfo? Loader(DetectedGame game) =>
        game.Profile.Loader is { } id && _catalog?.Loaders.TryGetValue(id, out var l) == true ? l : null;

    /// <summary>All the game's loaders (main one first), with the loaders they need.</summary>
    public List<LoaderInfo> Loaders(DetectedGame game) =>
        _catalog == null ? [] : LoaderRules.WithRequired(game.Profile.AllLoaders, _catalog.Loaders).Select(id => _catalog.Loaders[id])
            .OrderBy(l => l.Id == game.Profile.Loader ? 0 : 1).ToList();

    /// <summary>Needed mods that aren't installed, leaving out loaders (Luma installs those itself, from their official release).</summary>
    public List<(InstalledMod Mod, ModDependency Missing)> MissingDependencies(DetectedGame game)
    {
        var loaders = Loaders(game);
        return Store(game).MissingDependencies().Where(x => !loaders.Any(l => LoaderRules.IsDependency(l, x.Missing.Name))).ToList();
    }

    /// <summary>The game's loaders and what needs them.</summary>
    public List<LoaderStatus> LoaderStatus(DetectedGame game) =>
        _catalog == null ? [] : LoaderInstaller.Status(game, _catalog, Store(game));

    /// <summary>Raised after a game's mods or deploy state change (any thread).</summary>
    public event Action<string>? Changed;

    public ModStore Store(DetectedGame game)
    {
        lock (_stores)
        {
            if (!_stores.TryGetValue(game.Key, out var s))
            {
                _stores[game.Key] = s = new ModStore(_paths.ModsDir, game.Key);
            }
            return s;
        }
    }

    public string Manifest(DetectedGame game) => Path.Combine(_paths.Data, "deploy", ModStore.Safe(game.Key) + ".json");

    private string BackupDir(DetectedGame game) => Path.Combine(_paths.Data, "backup", ModStore.Safe(game.Key));

    public string? ModsDir(DetectedGame game) => GamePaths.ModsDir(game, _paths.Home);

    /// <summary>True while mods are linked into the game (between a deploy and its undeploy).</summary>
    public bool IsDeployed(DetectedGame game) => File.Exists(Manifest(game));

    public DeployManifest? Deployed(DetectedGame game) => Deployer.ReadManifest(Manifest(game));

    public InstalledMod Install(DetectedGame game, string archive, InstalledMod mod, string? gamePrefix = null, string? onlyProfile = null)
    {
        if (!game.Profile.Moddable)
        {
            throw new InvalidOperationException($"{game.Title} can't be modded safely: {game.Profile.NotModdable}");
        }
        var loaders = Loaders(game);
        var store = Store(game);
        // An update or reinstall uses the FOMOD options picked last time.
        if (mod.Fomod == null && store.State.Find(mod.Id)?.Fomod is { } earlier)
        {
            mod = mod with { Fomod = earlier };
        }
        var installed = store.Install(archive, mod, game.Profile, gamePrefix, loaders, onlyProfile, f => FileState(game, f));
        if (installed.Site == "loader" && loaders.FirstOrDefault(l => l.Id == installed.ModId) is { } asLoader)
        {
            LoaderInstaller.AfterInstall(game.Profile, asLoader, store.ModDir(installed.Id));
        }
        AppLog.Info($"installed {mod.Id} {mod.Version} for {game.Key} ({installed.Files} files)");
        Changed?.Invoke(game.Key);
        return installed;
    }

    /// <summary>Puts a previous version back from its kept download.</summary>
    public InstalledMod Rollback(DetectedGame game, string id, ModVersion version)
    {
        var mod = Store(game).State.Find(id) ?? throw new KeyNotFoundException("That mod isn't installed any more.");
        return Install(game, version.Archive, mod with { Version = version.Version, FileId = version.FileId });
    }

    public void Uninstall(DetectedGame game, string id)
    {
        Store(game).Uninstall(id);
        AppLog.Info($"uninstalled {id} from {game.Key}");
        Changed?.Invoke(game.Key);
    }

    public DeployPlan Plan(DetectedGame game) => Deployer.Plan(Store(game), game.GameDir, ModsDir(game));

    /// <param name="temporary">For one game or tool run that takes them out again when it ends.</param>
    public DeployManifest Deploy(DetectedGame game, bool temporary = false)
    {
        var plan = Plan(game);
        var m = Deployer.Deploy(plan, game.Key, Manifest(game), BackupDir(game)) with { Temporary = temporary, Pid = Environment.ProcessId };
        try
        {
            var store = Store(game);
            ProfileFiles.Place(game, _paths.Home, store.State, store.ProfileDir(store.State.Active), m, BackupDir(game));
            Deployer.Write(Manifest(game), m);
            var plugins = GameSetup.Apply(game, _paths.Home, plan, ModsDir(game), store.State, m, BackupDir(game));
            Deployer.Write(Manifest(game), m);
            if (game.Profile.Plugins != null)
            {
                AppLog.Info($"plugin list for {game.Key}: {plugins.Count(p => p.Enabled)} of {plugins.Count} plugins on");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Deployer.Write(Manifest(game), m);
            Deployer.Undeploy(Manifest(game));
            throw new IOException($"Couldn't put in {game.Title}'s plugin list, settings or profile saves: {e.Message}", e);
        }
        AppLog.Info($"deployed {m.Files.Count} files for {game.Key} ({m.Backups.Count} game files set aside, {plan.Conflicts.Count} conflicts)");
        Changed?.Invoke(game.Key);
        return m;
    }

    public void Undeploy(DetectedGame game)
    {
        Deployer.Undeploy(Manifest(game));
        AppLog.Info($"mods removed from {game.Key}");
        Changed?.Invoke(game.Key);
    }

    /// <summary>A plugin's state for FOMOD conditions: from the game's mods, else the game's own files.</summary>
    public string FileState(DetectedGame game, string file)
    {
        var state = Store(game).FileState(file);
        if (state == "Missing" && ModsDir(game) is { } dir && Fomod.Find(dir, file.Replace('\\', '/')) != null)
        {
            return "Active";
        }
        return state;
    }

    /// <summary>The plugin list the next deploy writes (for showing and toggling plugins), or null for games without one.</summary>
    public List<PluginEntry>? Plugins(DetectedGame game)
    {
        if (game.Profile.Plugins is not { } settings || ModsDir(game) is not { } mods)
        {
            return null;
        }
        var file = GamePaths.ResolvePath(game, settings.File, _paths.Home);
        // While deployed, plugins.txt is Luma's own: the game's original is the backup (or there was none).
        var deployed = Deployed(game);
        var source = file == null ? null
            : deployed != null && deployed.Written.Contains(file) ? deployed.Backups.GetValueOrDefault(file)
            : file;
        var current = source != null && File.Exists(source) ? File.ReadAllLines(source) : [];
        return GameSetup.Plugins(Plan(game), mods, Store(game).State, settings, current, f => PluginFile.Read(f, settings.Light));
    }

    /// <summary>Missing or late masters and plugin-limit problems in the list the next deploy writes.</summary>
    public List<PluginProblem> PluginProblems(DetectedGame game) =>
        game.Profile.Plugins is { } settings && Plugins(game) is { } list ? GameSetup.Problems(list, settings) : new();

    /// <summary>Sorts the plugins so each loads after its masters, and keeps that order for the profile.</summary>
    public int SortPlugins(DetectedGame game)
    {
        var list = Plugins(game) ?? throw new InvalidOperationException($"{game.Title} has no plugin list.");
        var sorted = GameSetup.SortPlugins(list);
        var moved = sorted.Where((p, i) => !string.Equals(list[i].Name, p.Name, StringComparison.OrdinalIgnoreCase)).Count();
        Store(game).SetPluginOrder(sorted.Select(p => p.Name));
        AppLog.Info($"sorted plugins for {game.Key}: {moved} moved");
        Changed?.Invoke(game.Key);
        return moved;
    }

    /// <summary>Moves a plugin up or down the load order (masters stay above plugins).</summary>
    public void MovePlugin(DetectedGame game, string name, int delta)
    {
        var list = Plugins(game) ?? throw new InvalidOperationException($"{game.Title} has no plugin list.");
        var i = list.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
        {
            return;
        }
        var j = Math.Clamp(i + delta, 0, list.Count - 1);
        if (list[j].Master != list[i].Master)
        {
            return;  // a plugin can't go above a master, nor a master below a plugin
        }
        var p = list[i];
        list.RemoveAt(i);
        list.Insert(j, p);
        Store(game).SetPluginOrder(list.Select(x => x.Name));
        Changed?.Invoke(game.Key);
    }

    /// <summary>
    /// Takes the plugin order a tool (LOOT) wrote to the game's plugins.txt while the mods were in,
    /// and keeps it as the profile's plugin order. Returns how many plugins it lists.
    /// </summary>
    public int AdoptPluginOrder(DetectedGame game)
    {
        if (game.Profile.Plugins is not { } settings || GamePaths.ResolvePath(game, settings.File, _paths.Home) is not { } file || !File.Exists(file))
        {
            return 0;
        }
        var names = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.TrimStart('*').Trim()).Where(GameSetup.IsPlugin).ToList();
        // Older games list only what's on; loadorder.txt (Skyrim 2011) has the full order.
        var loadOrder = Path.Combine(Path.GetDirectoryName(file)!, "loadorder.txt");
        if (settings.LoadOrderFile && File.Exists(loadOrder))
        {
            names = File.ReadAllLines(loadOrder).Select(l => l.Trim()).Where(GameSetup.IsPlugin).ToList();
        }
        if (names.Count > 0)
        {
            Store(game).SetPluginOrder(names);
            AppLog.Info($"kept the plugin order from {Path.GetFileName(file)} for {game.Key} ({names.Count} plugins)");
            Changed?.Invoke(game.Key);
        }
        return names.Count;
    }

    /// <summary>Back to plugins following the mod order.</summary>
    public void ResetPluginOrder(DetectedGame game)
    {
        Store(game).SetPluginOrder(null);
        Changed?.Invoke(game.Key);
    }

    /// <summary>
    /// Games whose mods are still in although whatever put them there is gone (Luma, the game's
    /// launch or a tool run was stopped, or they were put in by hand in an earlier session) and the
    /// player hasn't chosen to leave them.
    /// </summary>
    public List<(DetectedGame Game, DeployManifest Manifest)> LeftIn(IEnumerable<DetectedGame> games) =>
        games.Select(g => (Game: g, Manifest: Deployed(g)))
            .Where(x => x.Manifest is { Acknowledged: false } m && !Alive(m.Pid))
            .Select(x => (x.Game, x.Manifest!)).ToList();

    /// <summary>Stops <see cref="LeftIn"/> listing the game until its mods are put in again.</summary>
    public void LeaveIn(DetectedGame game)
    {
        if (Deployed(game) is { } m)
        {
            Deployer.Write(Manifest(game), m with { Acknowledged = true });
        }
    }

    private static bool Alive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }
        if (pid == Environment.ProcessId)
        {
            return true;
        }
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            // A reused process id belongs to something else.
            return !p.HasExited && p.ProcessName.Contains("luma", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>The game has a save folder and INI files Luma knows, so profiles can keep their own.</summary>
    public static bool CanKeepSaves(DetectedGame game) => game.Profile.Saves != null;

    public static bool CanKeepInis(DetectedGame game) => game.Profile.Inis.Count > 0;

    /// <summary>
    /// Gives the active profile its own saves (<paramref name="saves"/>) or INI files, or goes back to
    /// the game's. Turning saves on with <paramref name="copyCurrent"/> starts the profile with a copy
    /// of the game's saves; INI files always start as copies of the game's.
    /// </summary>
    public void SetLocal(DetectedGame game, bool saves, bool on, bool copyCurrent = false)
    {
        NotWhileDeployed(game);
        var store = Store(game);
        if (on)
        {
            ProfileFiles.Seed(game, _paths.Home, store.ProfileDir(store.State.Active), saves, copyCurrent);
        }
        store.SetLocal(saves, on);
        AppLog.Info($"{game.Key} profile {store.State.Active}: own {(saves ? "saves" : "INI files")} {(on ? "on" : "off")}");
        Changed?.Invoke(game.Key);
    }

    /// <summary>Renames a profile; one with its own saves or INI files only while they aren't in the game (they live in its folder).</summary>
    public void RenameProfile(DetectedGame game, string from, string to)
    {
        var store = Store(game);
        if (store.State.LocalSaves.Contains(from) || store.State.LocalInis.Contains(from))
        {
            NotWhileDeployed(game);
        }
        store.RenameProfile(from, to);
        Changed?.Invoke(game.Key);
    }

    /// <summary>The active profile's folder holding its own saves and INI files.</summary>
    public string ProfileDir(DetectedGame game) => Store(game).ProfileDir(Store(game).State.Active);

    public List<(string Key, long Size)> ModFiles(DetectedGame game, string id) => Store(game).ModFiles(id);

    /// <summary>Hides files or folders of a mod from the game, or shows them again.</summary>
    public void SetHidden(DetectedGame game, string id, IEnumerable<string> keys, bool hidden)
    {
        Store(game).SetHidden(id, keys, hidden);
        Changed?.Invoke(game.Key);
    }

    /// <summary>Files in the game's Overwrite folder (made by the game or a tool while mods were in).</summary>
    public List<string> OverwriteFiles(DetectedGame game) => Store(game).OverwriteFiles();

    public void ClearOverwrite(DetectedGame game)
    {
        NotWhileDeployed(game);
        Store(game).ClearOverwrite();
        AppLog.Info($"cleared Overwrite for {game.Key}");
        Changed?.Invoke(game.Key);
    }

    /// <summary>Makes a mod of the Overwrite folder's files.</summary>
    public InstalledMod OverwriteToMod(DetectedGame game, string name)
    {
        NotWhileDeployed(game);
        var mod = Store(game).OverwriteToMod(name);
        AppLog.Info($"made {mod.Id} from Overwrite for {game.Key} ({mod.Files} files)");
        Changed?.Invoke(game.Key);
        return mod;
    }

    private void NotWhileDeployed(DetectedGame game)
    {
        if (IsDeployed(game))
        {
            throw new InvalidOperationException("The mods are in the game right now. Take them out first (or close the game), then try again.");
        }
    }

    /// <summary>Known tools found in the game's mods, its folder, Downloads and the Desktop, not yet added.</summary>
    public List<ToolDef> FindTools(DetectedGame game)
    {
        var store = Store(game);
        var extra = new[] { "Downloads", "Desktop" }.Select(d => Path.Combine(_paths.Home, d)).ToList();
        if (Platform.IsWindows)
        {
            extra.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            extra.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } la ? Path.Combine(la, "Programs") : "");
        }
        return Tools.Find(store, game.GameDir, ModsDir(game), extra.Where(d => d.Length > 0))
            .Where(t => !store.State.Tools.Any(x => string.Equals(x.Exe, t.Exe, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public void NotifyChanged(DetectedGame game) => Changed?.Invoke(game.Key);

    /// <summary>The Steam launch options that make Steam start the game through Luma.</summary>
    public static string LaunchOption(string cliPath) => $"\"{cliPath}\" run -- %command%";
}
