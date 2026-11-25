using Godot;
using Luma.Core.Detection;
using Luma.Core.Games;

namespace Luma.App.Ui;

/// <summary>How a detected game is described across the app (rows, header, status panel).</summary>
public static class GameText
{
    /// <summary>"Steam", "Steam · SD card", "Heroic (Epic)", "Lutris", plus "(Flatpak)".</summary>
    public static string Source(DetectedGame g)
    {
        var i = g.Install;
        var s = i.Source switch
        {
            GameSource.Steam => "Steam",
            GameSource.Epic => "Heroic · Epic",
            GameSource.Gog => "Heroic · GOG",
            GameSource.Amazon => "Heroic · Amazon",
            GameSource.Lutris => "Lutris",
            _ => i.Source.ToString(),
        };
        if (i.Library is { } lib && (lib.StartsWith("/run/media/", StringComparison.Ordinal) || lib.Contains("mmcblk", StringComparison.Ordinal)))
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
        if (p.Runtime == "proton" && g.Install.Prefix == null && p.ModsPath.StartsWith('{'))
        {
            return ("Start once first", UiTheme.Warn);
        }
        if (p.Loader is { } id && !p.LoaderOptional && catalog.Loaders.TryGetValue(id, out var loader))
        {
            return ($"Uses {loader.Name}", UiTheme.AccentText);
        }
        return ("Ready", UiTheme.Good);
    }

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
