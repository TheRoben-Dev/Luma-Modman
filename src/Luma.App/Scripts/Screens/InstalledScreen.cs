using Godot;

namespace Luma.App.Screens;

/// <summary>Installed mods per game: profiles, enable/disable, load order, conflicts and updates.</summary>
public partial class InstalledScreen : Screen
{
    private Control? _first;

    protected override void Build()
    {
        var items = EmptyState.Build(this, "No mods installed",
            "Mods you install are kept in Luma's own folder, outside the game. They are linked in when the game starts and taken out after, so your game files stay clean and every change can be undone.",
            ("Browse mods", () => App.SwitchTab("Browse"), "Find mods on Thunderstore, mod.io, CurseForge and Nexus Mods."));
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
