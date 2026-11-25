using Godot;

namespace Luma.App.Screens;

/// <summary>Running and finished downloads, with progress, retry and the files kept for rollback.</summary>
public partial class DownloadsScreen : Screen
{
    private Control? _first;

    protected override void Build()
    {
        var items = EmptyState.Build(this, "Nothing downloading",
            "Mods you install from the Browse tab, and \"Mod Manager Download\" links from Nexus Mods, download here. Finished downloads are kept so a mod can be reinstalled or rolled back without going online.",
            ("Browse mods", () => App.SwitchTab("Browse"), "Find mods to install."));
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
