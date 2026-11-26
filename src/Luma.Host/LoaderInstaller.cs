using System.Text.Json;
using System.Text.RegularExpressions;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Sources;

namespace Luma.Host;

/// <summary>
/// Mod loaders (BepInEx, MelonLoader, UE4SS...) from their official GitHub releases. A loader is
/// installed like a mod ("loader:&lt;id&gt;"), so it is linked in with the rest and comes out with them.
/// </summary>
public static class LoaderInstaller
{
    public static string ModId(LoaderInfo l) => "loader:" + l.Id;

    /// <summary>Where the loader's files go, relative to the game folder: UE4SS next to the game's exe, others in the game folder.</summary>
    public static string InstallPath(GameProfile p, LoaderInfo? l = null) =>
        l?.Launcher != null ? l.LauncherDir
        : p.Loader == "ue4ss" && p.Detect.Select(d => d.Replace('\\', '/')).FirstOrDefault(d => d.Contains("/Binaries/Win64", StringComparison.OrdinalIgnoreCase)) is { } exe
            ? exe[..(exe.IndexOf("/Binaries/Win64", StringComparison.OrdinalIgnoreCase) + "/Binaries/Win64".Length)]
            : "";

    /// <summary>True when the loader is installed through Luma or already sits in the game folder.</summary>
    public static bool Installed(DetectedGame game, LoaderInfo l, Luma.Core.Mods.GameMods mods)
    {
        if (mods.Mods.Any(m => m.Id == ModId(l) || (l.Id == "bepinex" && m.Name.StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }
        if (l.Launcher != null)
        {
            return File.Exists(Path.Combine(game.GameDir, l.Launcher));
        }
        return l.Id switch
        {
            "bepinex" or "bepinex-il2cpp" => Directory.Exists(Path.Combine(game.GameDir, "BepInEx", "core")),
            "melonloader" => Directory.Exists(Path.Combine(game.GameDir, "MelonLoader")),
            "ue4ss" => Directory.Exists(Path.Combine(game.GameDir, InstallPath(game.Profile), "ue4ss")),
            _ => false,
        };
    }

    /// <summary>The newest release's file for this loader.</summary>
    public static async Task<(DownloadTicket Ticket, string Version, string Page)> LatestAsync(HttpGate http, LoaderInfo l, CancellationToken ct = default)
    {
        if (!l.AutoInstall)
        {
            throw new InvalidOperationException($"{l.Name} has to be installed from its website.");
        }
        var repo = l.Source["github:".Length..];
        using var doc = await http.GetJsonAsync($"https://api.github.com/repos/{repo}/releases/latest", ct: ct);
        var root = doc.RootElement;
        var tag = root.Str("tag_name");
        var re = new Regex(l.Asset!, RegexOptions.IgnoreCase);
        foreach (var a in root.Arr("assets"))
        {
            var name = a.Str("name");
            if (re.IsMatch(name))
            {
                return (new DownloadTicket(a.Str("browser_download_url"), Text.SafeFileName(name), a.Long("size")), tag.TrimStart('v'), root.Str("html_url"));
            }
        }
        throw new InvalidDataException($"The newest {l.Name} release ({tag}) has no file Luma recognises. Install it from {root.Str("html_url")}.");
    }
}
