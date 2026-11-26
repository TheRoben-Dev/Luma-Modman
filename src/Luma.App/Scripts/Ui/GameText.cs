using Godot;
using Luma.Core.Detection;
using Luma.Core.Games;

namespace Luma.App.Ui;

/// <summary>How a detected game is described across the app (rows, header, status panel).</summary>
public static class GameText
{
    /// <summary>"Steam", "Steam · SD card", "Heroic (Epic)", "Lutris", plus "(Flatpak)"; "Epic Games", "GOG" on Windows.</summary>
    public static string Source(DetectedGame g)
    {
        var i = g.Install;
        var s = (i.Source, i.Packaging == WindowsLaunchers.Packaging) switch
        {
            (GameSource.Steam, _) => "Steam",
            (GameSource.Epic, true) => "Epic Games",
            (GameSource.Gog, true) => "GOG",
            (GameSource.Epic, _) => "Heroic · Epic",
            (GameSource.Gog, _) => "Heroic · GOG",
            (GameSource.Amazon, _) => "Heroic · Amazon",
            (GameSource.Lutris, _) => "Lutris",
            _ => i.Source.ToString(),
        };
        if (!Luma.Core.Platform.IsWindows && i.Library is { } lib && (lib.StartsWith("/run/media/", StringComparison.Ordinal) || lib.Contains("mmcblk", StringComparison.Ordinal)))
        {
            s += " · SD card";
        }
        if (i.Packaging == "flatpak")
        {
            s += " (Flatpak)";
        }
        return s;
    }

    /// <summary>Short status for lists: what the game needs, or why it can't be modded.</summary>
    public static (string Text, Color Color) Status(DetectedGame g, GameCatalog catalog)
    {
        var p = g.Profile;
        if (!p.Moddable)
        {
            return ("Not moddable", UiTheme.Bad);
        }
        if (NeedsFirstStart(g))
        {
            return ("Start once first", UiTheme.Warn);
        }
        if (p.Loader is { } id && !p.LoaderOptional && catalog.Loaders.TryGetValue(id, out var loader))
        {
            return ($"Uses {loader.Name}", UiTheme.AccentText);
        }
        return ("Ready", UiTheme.Good);
    }

    /// <summary>True when the game's mods go in its Proton prefix and Steam hasn't made it yet (Linux only).</summary>
    public static bool NeedsFirstStart(DetectedGame g) =>
        !Luma.Core.Platform.IsWindows && g.Profile.Runtime == "proton" && g.Install.Prefix == null && g.Profile.ModsPath.StartsWith('{');

    /// <summary>How the game runs here, for the status panel.</summary>
    public static string Runs(DetectedGame game)
    {
        var p = game.Profile;
        if (Luma.Core.Platform.IsWindows)
        {
            return "Windows version";
        }
        if (p.Runtime == "native")
        {
            return "Native Linux version";
        }
        if (!p.Moddable)
        {
            return "Proton";
        }
        return game.Install.Prefix == null ? "Proton (prefix created on first start)" : "Proton" + (game.Install.CompatTool is { } t ? $" ({t})" : "");
    }

    /// <summary>What Steam's controller-first mode is called here: "Game Mode" (SteamOS) or "Big Picture" (Windows).</summary>
    public static string SteamTvMode => Luma.Core.Platform.IsWindows ? "Big Picture" : "Game Mode";

    /// <summary>One line for the loader, for the status panel.</summary>
    public static string Loader(GameProfile p, GameCatalog catalog)
    {
        if (p.Loader == null || !catalog.Loaders.TryGetValue(p.Loader, out var l))
        {
            return "Mods load without a separate loader.";
        }
        return p.LoaderOptional
            ? $"{l.Name} for the mods that need it. Luma installs it with the first one."
            : $"{l.Name}. Luma installs it with the first mod.";
    }
}
