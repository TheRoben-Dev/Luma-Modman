using Luma.Core.Games;
using Luma.Core.Sources;
using Luma.Host.Updates;

namespace Luma.Host;

/// <summary>The four mod sites, sharing one rate-limited HTTP gate and the player's keys.</summary>
public sealed class ModSites
{
    public HttpGate Http { get; }
    public ThunderstoreSource Thunderstore { get; }
    public ModioSource Modio { get; }
    public CurseForgeSource CurseForge { get; }
    public NexusSource Nexus { get; }

    /// <summary>In the order Browse lists them.</summary>
    public IReadOnlyList<IModSource> All { get; }

    public ModSites(CredentialStore keys, HttpGate? http = null)
    {
        var version = AppUpdater.DisplayVersion(AppUpdater.CurrentAppVersion());
        HttpGate.UserAgent = $"Luma/{version} (+https://github.com/TheRoben-Dev/Luma-Modman)";
        Http = http ?? new HttpGate();
        // Test hook: the UI smoke test points the sites at a local stand-in (tools/smoke/fake_sites.py).
        var fake = Environment.GetEnvironmentVariable("LUMA_FAKE_SITES");
        Thunderstore = fake == null ? new ThunderstoreSource(Http) : new ThunderstoreSource(Http, fake);
        Modio = new ModioSource(Http, () => keys.Current.Modio);
        CurseForge = new CurseForgeSource(Http, () => keys.Current.CurseForge);
        Nexus = fake == null
            ? new NexusSource(Http, () => keys.Current.Nexus, version)
            : new NexusSource(Http, () => keys.Current.Nexus, version, fake + "/nexus/graphql", fake + "/nexus/v1");
        All = [Thunderstore, Modio, CurseForge, Nexus];
    }

    public IModSource Get(string id) => All.First(s => s.Id == id);

    public IEnumerable<IModSource> For(GameProfile game) => All.Where(s => s.Carries(game));
}
