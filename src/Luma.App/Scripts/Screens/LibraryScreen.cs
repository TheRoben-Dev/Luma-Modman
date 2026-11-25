using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>Detected, supported games as cartridges. Game detection fills this in.</summary>
public partial class LibraryScreen : Screen
{
    private Control? _first;

    protected override void Build()
    {
        var items = EmptyState.Build(this, "No games yet",
            "Luma looks for supported games in every Steam library (including the SD card), and in Heroic, Lutris and Flatpak installs. Games that can be modded show up here with their art.",
            ("Browse mods", () => App.SwitchTab("Browse"), "Mod sites: Thunderstore, mod.io, CurseForge and Nexus Mods."),
            ("Settings", () => App.SwitchTab("Settings"), "Themes, sounds and updates."));
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
