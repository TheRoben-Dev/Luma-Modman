using Luma.Core.Detection;
using Luma.Core.Mods;

namespace Luma.App.Screens;

/// <summary>
/// The player's own organising, shared by both layouts: renaming mods, notes, categories,
/// separators in the load order (MO2-style headings) and renaming profiles.
/// </summary>
public static class ModInfo
{
    private static Main App => Main.Instance;

    private static void Changed(DetectedGame game, Action? done)
    {
        App.Host!.Mods.NotifyChanged(game);
        done?.Invoke();
    }

    public static void Rename(DetectedGame game, InstalledMod mod, Action? done = null) =>
        App.Push(new TextEntryPage($"Rename {mod.Name}", mod.Name, name =>
        {
            if (name.Trim().Length > 0 && App.Guard(() => App.Host!.Mods.Store(game).SetModInfo(mod.Id, name: name)))
            {
                Changed(game, done);
            }
        }, "Your name for it. Updates keep it."));

    public static void EditNotes(DetectedGame game, InstalledMod mod, Action? done = null) =>
        App.Push(new TextEntryPage($"Notes for {mod.Name}", mod.Notes, text =>
        {
            if (App.Guard(() => App.Host!.Mods.Store(game).SetModInfo(mod.Id, notes: text)))
            {
                Changed(game, done);
            }
        }, "Anything to remember about it: why it's here, settings, what it needs."));

    /// <summary>Picks a category from the ones in use, a new one, or none.</summary>
    public static void EditCategory(DetectedGame game, InstalledMod mod, Action? done = null)
    {
        var store = App.Host!.Mods.Store(game);
        void Set(string c)
        {
            if (App.Guard(() => store.SetModInfo(mod.Id, category: c)))
            {
                Changed(game, done);
            }
        }
        var options = new List<(string, Action)>();
        foreach (var c in store.State.Categories().Concat(Suggested).Distinct(StringComparer.OrdinalIgnoreCase).Take(14))
        {
            var cat = c;
            options.Add((c == mod.Category ? $"{c}  (now)" : c, () => Set(cat)));
        }
        options.Add(("New category…", () => App.Push(new TextEntryPage("New category", "", Set, "A name for the category, like Textures or UI."))));
        if (mod.Category.Length > 0)
        {
            options.Add(("No category", () => Set("")));
        }
        options.Add(("Cancel", () => { }));
        App.Push(new ChoicePage($"Category for {mod.Name}", options.ToArray()));
    }

    /// <summary>Common categories offered before any are in use.</summary>
    private static readonly string[] Suggested = ["Fixes", "UI", "Gameplay", "Visuals", "Textures", "Audio", "Characters", "Patches", "Utilities"];

    public static void AddSeparator(DetectedGame game, string? beforeId = null, Action? done = null) =>
        App.Push(new TextEntryPage("New separator", "", name =>
        {
            if (name.Trim().Length > 0 && App.Guard(() => App.Host!.Mods.Store(game).AddSeparator(name, beforeId)))
            {
                Changed(game, done);
            }
        }, beforeId == null ? "A heading at the end of the load order, to group the mods below it." : "A heading above this mod, to group the mods below it."));

    public static void RenameSeparator(DetectedGame game, string id, Action? done = null)
    {
        var store = App.Host!.Mods.Store(game);
        App.Push(new TextEntryPage("Rename separator", store.State.Separators.GetValueOrDefault(id, ""), name =>
        {
            store.RenameSeparator(id, name);
            Changed(game, done);
        }));
    }

    public static void RemoveSeparator(DetectedGame game, string id, Action? done = null)
    {
        App.Host!.Mods.Store(game).RemoveSeparator(id);
        Changed(game, done);
    }

    /// <summary>A separator's options (controller): rename, move, remove.</summary>
    public static void SeparatorOptions(DetectedGame game, string id, Action? done = null)
    {
        var store = App.Host!.Mods.Store(game);
        var name = store.State.Separators.GetValueOrDefault(id, "Separator");
        App.Push(new ChoicePage($"Separator \"{name}\"",
            ("Rename it", () => RenameSeparator(game, id, done)),
            ("Move up", () => { App.Guard(() => store.Move(id, -1)); Changed(game, done); }),
            ("Move down", () => { App.Guard(() => store.Move(id, +1)); Changed(game, done); }),
            ("Remove it (the mods stay)", () => RemoveSeparator(game, id, done)),
            ("Cancel", () => { })));
    }

    public static void RenameProfile(DetectedGame game, Action? done = null)
    {
        var store = App.Host!.Mods.Store(game);
        var from = store.State.Active;
        App.Push(new TextEntryPage($"Rename the {from} profile", from, name =>
        {
            if (name.Trim().Length > 0 && name.Trim() != from && App.Guard(() => App.Host!.Mods.RenameProfile(game, from, name)))
            {
                Changed(game, done);
            }
        }));
    }

    public const string SavesHelp = "This profile gets its own save folder while its mods are in, so a different mod list never loads (and breaks) your other saves. The game's own saves come back when the mods come out.";

    public const string InisHelp = "This profile gets its own copies of the game's INI files (graphics and game settings), changed only while its mods are in.";

    /// <summary>Turns the active profile's own saves (<paramref name="saves"/>) or INI files on or off, asking how to start.</summary>
    public static void SetLocal(DetectedGame game, bool saves, bool on, Action? done = null)
    {
        var mods = App.Host!.Mods;
        var profile = mods.Store(game).State.Active;
        void Set(bool copy)
        {
            if (App.Guard(() => mods.SetLocal(game, saves, on, copy)))
            {
                App.Success(on
                    ? $"The {profile} profile has its own {(saves ? "saves" : "game settings")} now."
                    : $"The {profile} profile uses the game's {(saves ? "saves" : "settings")} again. Its own stay in its folder for later.");
            }
            done?.Invoke();
        }
        if (on && saves)
        {
            App.Push(new ChoicePage($"Start the {profile} profile's own saves with a copy of your current saves, or with none?",
                ("Copy my current saves", () => Set(true)),
                ("Start with no saves", () => Set(false)),
                ("Cancel", () => done?.Invoke())));
            return;
        }
        Set(false);
    }
}
