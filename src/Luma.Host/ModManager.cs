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

    public InstalledMod Install(DetectedGame game, string archive, InstalledMod mod, string? gamePrefix = null)
    {
        if (!game.Profile.Moddable)
        {
            throw new InvalidOperationException($"{game.Title} can't be modded safely: {game.Profile.NotModdable}");
        }
        var loader = Loader(game);
        var installed = Store(game).Install(archive, mod, game.Profile, gamePrefix ?? (mod.Site == "loader" ? LoaderInstaller.InstallPath(game.Profile, loader) : null), loader);
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

    public void NotifyChanged(DetectedGame game) => Changed?.Invoke(game.Key);

    /// <summary>The Steam launch options that make Steam start the game through Luma.</summary>
    public static string LaunchOption(string cliPath) => $"\"{cliPath}\" run -- %command%";
}
