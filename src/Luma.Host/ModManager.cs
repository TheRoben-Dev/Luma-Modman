using Luma.Core;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;

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

    private string Manifest(DetectedGame game) => Path.Combine(_paths.Data, "deploy", ModStore.Safe(game.Key) + ".json");

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
        var loader = Loader(game);
        var store = Store(game);
        // An update or reinstall uses the FOMOD options picked last time.
        if (mod.Fomod == null && store.State.Find(mod.Id)?.Fomod is { } earlier)
        {
            mod = mod with { Fomod = earlier };
        }
        var installed = store.Install(archive, mod, game.Profile, gamePrefix ?? (mod.Site == "loader" ? LoaderInstaller.InstallPath(game.Profile, loader) : null), loader, onlyProfile,
            f => FileState(game, f));
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

    public DeployManifest Deploy(DetectedGame game)
    {
        var plan = Plan(game);
        var m = Deployer.Deploy(plan, game.Key, Manifest(game), BackupDir(game));
        try
        {
            var plugins = GameSetup.Apply(game, _paths.Home, plan, ModsDir(game), Store(game).State, m, BackupDir(game));
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
            throw new IOException($"Couldn't write {game.Title}'s plugin list or settings: {e.Message}", e);
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

    /// <summary>Back to plugins following the mod order.</summary>
    public void ResetPluginOrder(DetectedGame game)
    {
        Store(game).SetPluginOrder(null);
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

    public void NotifyChanged(DetectedGame game) => Changed?.Invoke(game.Key);

    /// <summary>The Steam launch options that make Steam start the game through Luma.</summary>
    public static string LaunchOption(string cliPath) => $"\"{cliPath}\" run -- %command%";
}
