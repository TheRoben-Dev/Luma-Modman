using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// A FOMOD installer being stepped through: the unpacked archive, the picks so far and the rules
/// of each group (pick one, at most one, at least one, any, all). Both layouts drive the same
/// session: <see cref="FomodPage"/> with a controller, a dialog window on the desktop.
/// </summary>
public sealed class FomodSession
{
    public required DetectedGame Game { get; init; }
    public required string ModName { get; init; }
    public required FomodConfig Config { get; init; }

    /// <summary>The folder holding the unpacked fomod/ (images are found from here).</summary>
    public required string Root { get; init; }

    /// <summary>The temporary unpack folder, removed when the installer closes.</summary>
    public required string TempDir { get; init; }

    /// <summary>Installs with the final picks (runs off the main thread).</summary>
    public required Func<List<FomodChoice>, InstalledMod> Install { get; init; }

    public List<FomodChoice> Choices { get; } = new();
    public int StepIndex { get; set; }

    private static AppHost Host => Main.Instance.Host!;

    public string FileState(string f) => Host.Mods.FileState(Game, f);

    private (List<FomodStep> Visible, Dictionary<string, string> Flags) Walk()
    {
        var (visible, flags, _) = Fomod.Run(Config, Choices, FileState);
        return (visible, flags);
    }

    public List<FomodStep> Visible => Walk().Visible;

    public FomodStep? Current => Visible is { Count: > 0 } v ? v[Math.Clamp(StepIndex, 0, v.Count - 1)] : null;

    public bool IsLast => StepIndex >= Visible.Count - 1;

    public List<string> Picked(FomodGroup g) =>
        Current is { } step ? Fomod.Picked(Choices, step, g) ?? Fomod.Defaults(g, Walk().Flags, FileState) : [];

    public string TypeOf(FomodOption o) => o.Type.Resolve(Walk().Flags, FileState);

    /// <summary>Picks or unpicks an option, keeping the group's rule. False when the rule doesn't allow it.</summary>
    public bool Toggle(FomodGroup g, FomodOption o)
    {
        if (Current is not { } step)
        {
            return false;
        }
        var type = TypeOf(o);
        var picked = Picked(g).ToList();
        var on = picked.Contains(o.Name, StringComparer.OrdinalIgnoreCase);
        if ((!on && type == "NotUsable") || (on && type == "Required") || g.Type == "SelectAll")
        {
            return false;
        }
        if (on)
        {
            if (g.Type is "SelectExactlyOne" || (g.Type == "SelectAtLeastOne" && picked.Count == 1))
            {
                return false;
            }
            picked.RemoveAll(p => p.Equals(o.Name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            if (g.Single)
            {
                picked.Clear();
            }
            picked.Add(o.Name);
        }
        Choices.RemoveAll(c => c.Step == step.Name && c.Group == g.Name);
        Choices.Add(new FomodChoice(step.Name, g.Name, picked));
        return true;
    }

    public bool StepValid => Current?.Groups.All(g => Fomod.Valid(g, Picked(g))) ?? true;

    /// <summary>Every visible group's picks, defaults written out, so a reinstall picks the same.</summary>
    public List<FomodChoice> Final()
    {
        var keep = StepIndex;
        var result = new List<FomodChoice>();
        var visible = Visible;
        for (var i = 0; i < visible.Count; i++)
        {
            StepIndex = i;
            result.AddRange(visible[i].Groups.Select(g => new FomodChoice(visible[i].Name, g.Name, Picked(g))));
        }
        StepIndex = keep;
        return result;
    }

    public static string Rule(FomodGroup g) => g.Type switch
    {
        "SelectExactlyOne" => "pick one",
        "SelectAtMostOne" => "pick one or none",
        "SelectAtLeastOne" => "pick at least one",
        "SelectAll" => "all are installed",
        _ => "pick any",
    };

    public Texture2D? Image(string? path)
    {
        if (path == null || Fomod.Find(Root, path) is not { } file)
        {
            return null;
        }
        var img = new Image();
        return img.Load(file) == Error.Ok ? ImageTexture.CreateFromImage(img) : null;
    }

    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Unpacks <paramref name="archive"/>, reads its installer and opens it (or installs straight away when it asks nothing).</summary>
    public static void Open(DetectedGame game, string archive, string modName, Func<List<FomodChoice>, InstalledMod> install, List<FomodChoice>? earlier = null, Action<InstalledMod>? done = null)
    {
        var app = Main.Instance;
        var tmp = Path.Combine(Host.Paths.Data, "fomod-" + Guid.NewGuid().ToString("N")[..8]);
        app.Busy($"Reading the installer of {modName}…", (_, _) => Task.Run(() =>
        {
            try
            {
                Archive.Extract(archive, tmp);
                var root = Fomod.FindRoot(tmp) ?? throw new InvalidDataException($"{Path.GetFileName(archive)} has no FOMOD installer.");
                return (Config: Fomod.Load(root), Root: root);
            }
            catch
            {
                if (Directory.Exists(tmp))
                {
                    Directory.Delete(tmp, true);
                }
                throw;
            }
        }), r =>
        {
            var s = new FomodSession { Game = game, ModName = modName, Config = r.Config, Root = r.Root, TempDir = tmp, Install = install, Done = done };
            if (earlier != null)
            {
                s.Choices.AddRange(earlier);
            }
            if (s.Visible.Count == 0)
            {
                s.Finish();
                return;
            }
            app.Push(new FomodPage(s));
        }, cancellable: false);
    }

    public Action<InstalledMod>? Done { get; init; }

    /// <summary>Installs with the picks and closes the installer.</summary>
    public void Finish(Screen? page = null)
    {
        var app = Main.Instance;
        var choices = Final();
        if (page != null)
        {
            app.Close(page);
        }
        app.Busy($"Installing {ModName}…", (_, _) => Task.Run(() => Install(choices)), mod =>
        {
            Cleanup();
            app.Success($"Installed {mod.Name}.");
            Host.Mods.NotifyChanged(Game);
            Done?.Invoke(mod);
        }, cancellable: false);
    }

    /// <summary>A mod added from a file: install it, or open its installer when it has one.</summary>
    public static void InstallFile(DetectedGame game, string file, Action<InstalledMod>? done = null)
    {
        var app = Main.Instance;
        app.Busy($"Installing {Path.GetFileName(file)}…", (_, _) => Task.Run(() =>
        {
            var kept = AddFilePage.Keep(Host, game, file);
            try
            {
                return (Mod: (InstalledMod?)Host.Mods.Install(game, kept, AddFilePage.LocalMod(file)), Kept: kept);
            }
            catch (FomodNeededException)
            {
                return (Mod: null, Kept: kept);
            }
        }), r =>
        {
            if (r.Mod is { } mod)
            {
                app.Success(mod.Site == "loader" ? $"{mod.Name} is installed. The game starts through it from now on." : $"Installed {mod.Name}.");
                Host.Mods.NotifyChanged(game);
                done?.Invoke(mod);
                return;
            }
            var template = AddFilePage.LocalMod(file);
            Open(game, r.Kept, template.Name, choices => Host.Mods.Install(game, r.Kept, template with { Fomod = choices }), done: done);
        }, cancellable: false);
    }

    /// <summary>A finished download whose mod has an installer.</summary>
    public static void ForDownload(DownloadItem item)
    {
        if (item.Path == null || Host.Games.Find(item.Game) is not { } game)
        {
            Main.Instance.Error("Luma hasn't found the game this download is for on this device.");
            return;
        }
        Open(game, item.Path, item.ModName, choices =>
        {
            item.Fomod = choices;
            item.InstallError = null;
            Host.InstallDownload(item);
            return item.Installed && Host.Mods.Store(game).State.Find($"{item.Site}:{item.ModId}") is { } m
                ? m : throw new InvalidOperationException(item.InstallError ?? $"{item.ModName} didn't install.");
        });
    }

    /// <summary>Installs an installed mod again with different options.</summary>
    public static void Change(DetectedGame game, InstalledMod mod)
    {
        if (mod.Archive == null || !File.Exists(mod.Archive))
        {
            Main.Instance.Error($"The download of {mod.Name} is gone, so its options can't be changed. Download it again.");
            return;
        }
        Open(game, mod.Archive, mod.Name, choices => Host.Mods.Install(game, mod.Archive, mod with { Fomod = choices }), mod.Fomod);
    }
}

/// <summary>The FOMOD installer in the controller layout: one step at a time, options as rows, the focused option's picture and description beside them.</summary>
public partial class FomodPage : Screen
{
    internal FomodSession Session { get; }
    private Control? _first;
    private TextureRect _image = null!;
    private Label _about = null!;
    private (string Group, string Option)? _focus;

    public FomodPage(FomodSession session) => Session = session;

    protected override void Build()
    {
        _first = null;
        var s = Session;
        var step = s.Current;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        AddChild(row);
        var col = Column(8);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.SizeFlagsStretchRatio = 3;
        row.AddChild(col);
        col.AddChild(UiTheme.Label($"Install options: {s.ModName}", UiTheme.TitleSize));
        var visible = s.Visible;
        col.AddChild(UiTheme.Label($"Step {s.StepIndex + 1} of {visible.Count}: {step?.Name}", UiTheme.SmallSize + 1, UiTheme.Dim));
        var list = Column(6);
        col.AddChild(Scroller(list));
        var items = new List<Control>();
        foreach (var g in step?.Groups ?? [])
        {
            var group = g;
            list.AddChild(Section($"{g.Name}  ·  {FomodSession.Rule(g)}"));
            var picked = s.Picked(g);
            foreach (var o in g.Options)
            {
                var option = o;
                var on = picked.Contains(o.Name, StringComparer.OrdinalIgnoreCase);
                var type = s.TypeOf(o);
                var status = on ? "Picked" : type == "NotUsable" ? "Can't use" : "";
                var r = new ListRow(o.Name, type is "Recommended" or "Required" or "NotUsable" or "CouldBeUsable" ? Describe(type) : "", status,
                    on ? UiTheme.Good : UiTheme.Dim, null, () =>
                    {
                        _focus = (group.Name, option.Name);
                        if (!s.Toggle(group, option))
                        {
                            App.Toast(type == "NotUsable" ? $"{option.Name} can't be used with what you have." : $"{group.Name}: {FomodSession.Rule(group)}.");
                            return;
                        }
                        Rebuild();
                    }, $"{o.Name}: A to {(on ? "unpick" : "pick")}.");
                r.FocusEntered += () => Preview(option);
                list.AddChild(r);
                items.Add(r);
                if (_focus == (g.Name, o.Name))
                {
                    _first = r;
                }
            }
        }
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        col.AddChild(buttons);
        if (s.StepIndex > 0)
        {
            var back = new ActionButton("Previous step", () =>
            {
                s.StepIndex--;
                _focus = null;
                Rebuild();
                Callable.From(FocusDefault).CallDeferred();
            });
            buttons.AddChild(back);
            items.Add(back);
        }
        var next = new ActionButton(s.IsLast ? "Install" : "Next step", () =>
        {
            if (!s.StepValid)
            {
                App.Toast("Pick an option in every group first.");
                return;
            }
            if (s.IsLast)
            {
                s.Finish(this);
                return;
            }
            s.StepIndex++;
            _focus = null;
            Rebuild();
            Callable.From(FocusDefault).CallDeferred();
        }, s.IsLast ? "Installs the mod with these options." : "On to the next step.");
        buttons.AddChild(next);
        items.Add(next);
        var cancel = new ActionButton("Cancel", () =>
        {
            s.Cleanup();
            App.Pop();
        }, "Leaves the mod uninstalled.");
        buttons.AddChild(cancel);
        items.Add(cancel);

        var side = Column(10);
        side.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        side.SizeFlagsStretchRatio = 2;
        row.AddChild(side);
        _image = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(0, 260) };
        side.AddChild(_image);
        _about = UiTheme.Label("", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true);
        side.AddChild(_about);
        Focus.Column(items);
        _first ??= items.FirstOrDefault();
        if (step?.Groups.FirstOrDefault()?.Options.FirstOrDefault() is { } firstOption)
        {
            Preview(firstOption);
        }
        else
        {
            _image.Texture = s.Image(s.Config.Image);
        }
    }

    internal static string Describe(string type) => type switch
    {
        "Required" => "Required",
        "Recommended" => "Recommended",
        "NotUsable" => "Needs something you don't have",
        "CouldBeUsable" => "May not work with your setup",
        _ => "",
    };

    private void Preview(FomodOption o)
    {
        _image.Texture = Session.Image(o.Image) ?? Session.Image(Session.Config.Image);
        _about.Text = o.Description.Length > 0 ? o.Description : o.Name;
    }

    public override bool OnBack()
    {
        Session.Cleanup();
        return false;
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
