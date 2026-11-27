using Luma.Core.Detection;
using Luma.Core.Sources;
using Luma.Host;

namespace Luma.App.Screens;

/// <summary>
/// Nexus Mods collections, shared by both layouts: paste a collection's link (or press the
/// website's mod manager button), see what it holds, and import it into a new profile like a
/// modpack. Premium accounts get every mod downloaded; free accounts press "Mod Manager Download"
/// once per file on the website (Nexus's rule), and each file takes its place as it arrives.
/// </summary>
public static class CollectionActions
{
    private static Main App => Main.Instance;
    private static AppHost Host => Main.Instance.Host!;

    public const string About = "Paste the link of a collection on nexusmods.com. Its mods go into a new profile, in the exact versions the collection lists.";

    /// <summary>Asks for the collection's link.</summary>
    public static void Ask() =>
        App.Push(new TextEntryPage("Import a Nexus collection", "", text =>
        {
            if (text.Trim().Length == 0)
            {
                return;
            }
            if (NexusCollectionRef.TryParse(text) is not { } r)
            {
                App.Error("That isn't a Nexus Mods collection link. It looks like nexusmods.com/games/<game>/collections/<id>.");
                return;
            }
            Open(r);
        }, About));

    /// <summary>Looks the collection up and offers to import it.</summary>
    public static void Open(NexusCollectionRef r) =>
        App.Busy("Looking up the collection…", (_, ct) => Host.Sites.Nexus.CollectionAsync(r, ct), Show);

    /// <summary>What the collection holds, with the ways to import it.</summary>
    internal static void Show(NexusCollection c)
    {
        var game = Host.Games.Games.Where(g => g.Profile.Moddable && g.Profile.Sites.Nexus == c.Domain)
            .OrderByDescending(g => g == Host.ActiveGame).FirstOrDefault();
        if (game == null)
        {
            App.Error($"\"{c.Name}\" is for a game ({c.Domain}) Luma hasn't found on this device.");
            return;
        }
        AppLog.Info($"collection {c.Slug} rev {c.Revision}: {c.Mods.Count} files for {game.Key}");
        var optional = c.Mods.Count(m => m.Optional);
        var required = c.Mods.Count - optional;
        var premium = Host.Keys.Current.NexusPremium;
        var how = premium
            ? "Your Premium account downloads them all."
            : "With a free Nexus account, press \"Mod Manager Download\" on the website for each file (Nexus's rule); each takes its place in the profile as it arrives. Luma lists them with a link to each.";
        var options = new List<(string, Action)> { ($"Import ({required} mods)", () => Import(game, c, false)) };
        if (optional > 0)
        {
            options.Add(($"Import with the {optional} optional mods too", () => Import(game, c, true)));
        }
        options.Add(("Open it on Nexus Mods", () => ModActions.OpenUrl(new NexusCollectionRef(c.Domain, c.Slug, c.Revision).PageUrl)));
        options.Add(("Cancel", () => { }));
            App.Push(new ChoicePage($"\"{c.Name}\" by {(c.Author.Length > 0 ? c.Author : "unknown")}, revision {c.Revision}, for {game.Title}: {required} mods{(optional > 0 ? $" and {optional} optional ones" : "")}. They go into a new profile; your other profiles don't change. {how}\n\nInstallers (FOMOD) ask for their options as each mod installs, and the plugin order follows the mod order: use Sort plugins after.",
                options.ToArray()));
        }

    private static void Import(DetectedGame game, NexusCollection c, bool optional)
    {
        var pack = c.ToPack(game.Profile, optional);
        App.Busy($"Importing {c.Name}…", (progress, _) => Task.Run(() =>
            PackImporter.Import(Host, game, pack, null, null, (_, p) => progress.Report(p))), r =>
        {
            if (Host.ActiveGame != game)
            {
                App.SetActiveGame(game.Key);
            }
            Host.Mods.NotifyChanged(game);
            PackActions.ShowResult(r);
        }, cancellable: false);
    }
}
