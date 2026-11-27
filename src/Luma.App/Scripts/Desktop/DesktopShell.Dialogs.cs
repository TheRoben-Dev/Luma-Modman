using Luma.App.Screens;
using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;
using Luma.Core.Sources;
using Luma.Host;
using Godot;

namespace Luma.App.Desktop;

/// <summary>
/// Pages the shared code pushes (Main.Push), shown the desktop way: questions, progress, text
/// entry and setup as dialog windows, file pickers as file dialogs, and pages the desktop
/// window already has (game list, mod details, log) as a jump to that place.
/// </summary>
public partial class DesktopShell
{
    private readonly Dictionary<Screen, Window> _open = new();
    private readonly List<Window> _windows = new();

    /// <summary>Whether a file picker should use the system's own dialog (not under the smoke test's virtual display).</summary>
    private static bool NativeFileDialogs => !OS.GetCmdlineUserArgs().Contains("--smoke") && DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile);

    /// <summary>Shows a pushed page. False: handled in place, so Main doesn't keep it on its stack.</summary>
    public bool Present(Screen page)
    {
        Window? w = page switch
        {
            ChoicePage c => ChoiceDialog(c),
            ConfirmPage c => ConfirmDialog(c),
            BusyPage b => BusyDialog(b),
            TextEntryPage t => TextDialog(t),
            ChangelogPage c => ChangelogDialog(c),
            SupportedGamesPage s => SupportedGamesDialog(s),
            SiteSetupPage s => SiteSetupDialog(s),
            SteamLaunchPage s => SteamDialog(s),
            ImportPage i => ImportDialog(i),
            AddFilePage a => AddFileDialog(a),
            ModPackPage m => m.Export ? ExportPackDialog(m) : ImportPackDialog(m),
            PluginsPage p => PluginsDialog(p),
            ConflictsPage c => ConflictsDialog(c),
            OverwritePage o => OverwriteDialog(o),
            ToolsPage t => ToolsDialog(t),
            PickFilePage p => PickFileDialog(p),
            FomodPage f => FomodDialog(f),
            _ => null,
        };
        if (w == null)
        {
            switch (page)
            {
                case LogPage:
                    ShowLog();
                    return false;
                case GamePickerPage:
                    _games.GrabFocus();
                    return false;
                case InstalledModPage m:
                    SelectTab("Mods");
                    _mods.Select(m.Id);
                    return false;
                case ModDetailsPage d:
                    SelectTab("Browse");
                    _browse.ShowMod(d.Site, d.Mod);
                    return false;
            }
            w = GenericDialog(page);
        }
        _open[page] = w;
        return true;
    }

    /// <summary>Closes the window of a page Main took off its stack.</summary>
    public void Dismiss(Screen page)
    {
        if (_open.Remove(page, out var w) && IsInstanceValid(w))
        {
            _windows.Remove(w);
            w.Hide();
            w.QueueFree();
        }
        if (!IsInstanceValid(page))
        {
            return;
        }
        if (page.IsInsideTree())
        {
            page.QueueFree();
        }
        else
        {
            page.Free();
        }
    }

    /// <summary>The window new dialogs open over: the newest open dialog, or the main window.</summary>
    private Node DialogParent()
    {
        _windows.RemoveAll(w => !IsInstanceValid(w) || w.IsQueuedForDeletion());
        return _windows.Count > 0 ? _windows[^1] : this;
    }

    private T ShowDialog<T>(T w, Screen page, Control? focus = null) where T : Window
    {
        DialogParent().AddChild(w);
        _windows.Add(w);
        switch (w)
        {
            case DesktopDialog d:
                d.Dismissed += () => App.Close(page);
                d.Open(focus);
                break;
            case FileDialog f:
                f.PopupCentered(new Vector2I(820, 560));
                break;
        }
        return w;
    }

    private static readonly HashSet<string> CancelWords = ["Cancel", "Back", "Later", "Close", "OK", "Not now"];

    private Window ChoiceDialog(ChoicePage page)
    {
        var d = new DesktopDialog("Luma", new Vector2I(560, 0));
        d.AddText(page.Message);
        var options = page.Options;
        var inline = options.Length <= 3 && options.All(o => o.Label.Length <= 30);
        Button? first = null;
        foreach (var (label, action) in options)
        {
            var a = action;
            void Run()
            {
                App.Close(page);
                a();
            }
            if (inline || CancelWords.Contains(label))
            {
                var b = d.AddButton(label, Run, primary: first == null && !CancelWords.Contains(label));
                first ??= b;
            }
            else
            {
                var b = DesktopTheme.Button(label, Run);
                b.Alignment = HorizontalAlignment.Left;
                d.Body.AddChild(b);
                first ??= b;
            }
        }
        return ShowDialog(d, page, first);
    }

    private Window ConfirmDialog(ConfirmPage page)
    {
        var d = new DesktopDialog("Please confirm", new Vector2I(500, 0));
        d.AddText(page.Message);
        var no = d.AddButton("Cancel", () => App.Close(page));
        d.AddButton(page.Yes, () =>
        {
            App.Close(page);
            page.OnYes();
        }, primary: true);
        return ShowDialog(d, page, no);
    }

    private Window BusyDialog(BusyPage page)
    {
        var d = new DesktopDialog("Working", new Vector2I(440, 0));
        d.AddText(page.Title);
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, Indeterminate = true, CustomMinimumSize = new Vector2(0, 18), ShowPercentage = false };
        d.Body.AddChild(bar);
        page.ProgressChanged += p =>
        {
            if (IsInstanceValid(bar))
            {
                bar.Indeterminate = p < 0;
                if (p >= 0)
                {
                    bar.Value = p;
                }
            }
        };
        if (page.Cts is { } cts)
        {
            d.AddButton("Cancel", cts.Cancel);
        }
        // Closing the window cancels the work when it can be cancelled; the dialog goes when the work stops.
        d.Dismissed += () => page.Cts?.Cancel();
        DialogParent().AddChild(d);
        _windows.Add(d);
        d.Open();
        return d;
    }

    private Window TextDialog(TextEntryPage page)
    {
        var d = new DesktopDialog(page.Title, new Vector2I(520, 0));
        if (page.Help.Length > 0)
        {
            d.AddText(page.Help, DesktopTheme.Dim);
        }
        var field = new LineEdit { Text = page.Current, Secret = page.Secret, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = page.Title, SelectAllOnFocus = true };
        d.Body.AddChild(field);
        void Ok()
        {
            var text = field.Text.Trim();
            App.Close(page);
            page.Done(text);
        }
        field.TextSubmitted += _ => Ok();
        d.AddButton("Cancel", () => App.Close(page));
        d.AddButton("OK", Ok, primary: true);
        return ShowDialog(d, page, field);
    }

    private Window ChangelogDialog(ChangelogPage page)
    {
        var d = new DesktopDialog(page.Title, new Vector2I(720, 560));
        var text = new RichTextLabel { BbcodeEnabled = false, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = true, FocusMode = FocusModeEnum.All };
        var current = Host.Updater.CurrentVersion;
        if (page.Entries.Count == 0)
        {
            text.AddText("No changelog in this build.");
        }
        foreach (var e in page.Entries)
        {
            var isCurrent = e.Version.Major == current.Major && e.Version.Minor == current.Minor && Math.Max(0, e.Version.Build) == Math.Max(0, current.Build);
            text.PushFontSize(DesktopTheme.FontSize + 3);
            text.PushColor(isCurrent ? DesktopTheme.Accent : DesktopTheme.Text);
            text.AddText(isCurrent ? $"{e.Display}  ·  this version" : e.Display);
            text.Pop();
            text.Pop();
            text.Newline();
            foreach (var item in e.Items)
            {
                text.PushColor(DesktopTheme.Dim);
                text.AddText("  •  " + item);
                text.Pop();
                text.Newline();
            }
            text.Newline();
        }
        var frame = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", DesktopTheme.Frame(10));
        frame.AddChild(text);
        d.Body.AddChild(frame);
        d.AddButton(page.Title.StartsWith("What's new", StringComparison.Ordinal) ? "Continue" : "Close", () => App.Close(page), primary: true);
        return ShowDialog(d, page);
    }

    private Window SupportedGamesDialog(SupportedGamesPage page)
    {
        var catalog = Host.Games.Catalog;
        var d = new DesktopDialog("Supported games", new Vector2I(900, 620));
        d.AddText($"{catalog.Games.Count(g => g.Moddable)} games Luma can mod, and {catalog.Games.Count(g => !g.Moddable)} it won't because of anti-cheat or online bans. Install one from Steam, Heroic or Lutris and Luma finds it.", DesktopTheme.Dim);
        var filter = new LineEdit { PlaceholderText = "Filter by name", ClearButtonEnabled = true, TooltipText = "Filter supported games" };
        d.Body.AddChild(filter);
        var tree = new Tree { HideRoot = true, Columns = 3, ColumnTitlesVisible = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Row, TooltipText = "Supported games" };
        tree.SetColumnTitle(0, "Game");
        tree.SetColumnTitle(1, "Status");
        tree.SetColumnTitle(2, "Loader and mod sites, or why not");
        tree.SetColumnExpand(0, true);
        tree.SetColumnExpandRatio(0, 2);
        tree.SetColumnExpand(1, false);
        tree.SetColumnCustomMinimumWidth(1, 110);
        tree.SetColumnExpand(2, true);
        tree.SetColumnExpandRatio(2, 3);
        d.Body.AddChild(tree);
        var installed = Host.Games.Games.Select(g => g.Profile.Id).ToHashSet();
        void Fill()
        {
            tree.Clear();
            var root = tree.CreateItem();
            foreach (var p in catalog.Games.Where(g => filter.Text.Length == 0 || g.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(g => installed.Contains(g.Id) ? 0 : 1).ThenBy(g => g.Moddable ? 0 : 1).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            {
                var it = tree.CreateItem(root);
                it.SetText(0, p.Name);
                var (status, color) = installed.Contains(p.Id) ? ("Installed", DesktopTheme.Good) : p.Moddable ? ("Supported", DesktopTheme.Dim) : ("Not moddable", DesktopTheme.Bad);
                it.SetText(1, status);
                it.SetCustomColor(1, color);
                var detail = p.Moddable
                    ? string.Join("  ·  ", new[] { p.Loader is { } l && catalog.Loaders.TryGetValue(l, out var li) ? li.Name : null, string.Join(", ", p.Sites.Names()) }.Where(s => !string.IsNullOrEmpty(s)))
                    : p.NotModdable!;
                it.SetText(2, detail);
                it.SetTooltipText(2, string.Join("\n", new[] { detail }.Concat(p.Quirks)));
            }
        }
        filter.TextChanged += _ => Fill();
        Fill();
        d.AddButton("Close", () => App.Close(page), primary: true);
        return ShowDialog(d, page, filter);
    }

    /// <summary>A FOMOD installer as a wizard window: option groups on the left, the hovered option's picture and description on the right.</summary>
    private Window FomodDialog(FomodPage page)
    {
        var s = page.Session;
        var d = new DesktopDialog($"Install options: {s.ModName}", new Vector2I(1000, 680));
        d.Dismissed += s.Cleanup;
        var stepLabel = d.AddText("", DesktopTheme.Dim);
        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        d.Body.AddChild(split);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.3f, CustomMinimumSize = new Vector2(360, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var groups = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        groups.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(groups);
        split.AddChild(scroll);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(320, 0) };
        side.AddThemeConstantOverride("separation", 8);
        var image = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(0, 280) };
        side.AddChild(image);
        var title = DesktopTheme.Label("", null, 0);
        title.ThemeTypeVariation = "HeaderLabel";
        side.AddChild(title);
        var about = DesktopTheme.Label("", DesktopTheme.Dim, 0, wrap: true);
        about.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        side.AddChild(about);
        split.AddChild(side);
        void Preview(FomodOption o)
        {
            image.Texture = s.Image(o.Image) ?? s.Image(s.Config.Image);
            title.Text = o.Name;
            about.Text = o.Description.Length > 0 ? o.Description : "";
        }
        Button back = null!, next = null!;
        void Fill()
        {
            foreach (var c in groups.GetChildren())
            {
                c.QueueFree();
            }
            var step = s.Current;
            stepLabel.Text = $"Step {s.StepIndex + 1} of {s.Visible.Count}: {step?.Name}";
            foreach (var g in step?.Groups ?? [])
            {
                var group = g;
                var header = DesktopTheme.Label($"{g.Name}  ({FomodSession.Rule(g)})", null, 0);
                header.ThemeTypeVariation = "PaneTitle";
                groups.AddChild(header);
                var picked = s.Picked(g);
                foreach (var o in g.Options)
                {
                    var option = o;
                    var type = s.TypeOf(o);
                    var note = FomodPage.Describe(type);
                    var box = new CheckBox
                    {
                        Text = o.Name + (note.Length > 0 ? $"   ({note})" : ""),
                        ButtonPressed = picked.Contains(o.Name, StringComparer.OrdinalIgnoreCase),
                        Disabled = type == "NotUsable" || g.Type == "SelectAll",
                        TooltipText = o.Description.Length > 0 ? o.Description : o.Name,
                        FocusMode = FocusModeEnum.All,
                    };
                    box.MouseEntered += () => Preview(option);
                    box.FocusEntered += () => Preview(option);
                    box.Pressed += () =>
                    {
                        if (!s.Toggle(group, option))
                        {
                            App.Toast($"{group.Name}: {FomodSession.Rule(group)}.");
                        }
                        Preview(option);
                        Callable.From(Fill).CallDeferred();
                    };
                    groups.AddChild(box);
                }
            }
            back.Disabled = s.StepIndex == 0;
            next.Text = s.IsLast ? "Install" : "Next";
            next.Disabled = !s.StepValid;
            if (step?.Groups.FirstOrDefault()?.Options.FirstOrDefault() is { } first && title.Text.Length == 0)
            {
                Preview(first);
            }
        }
        d.AddButton("Cancel", () =>
        {
            s.Cleanup();
            App.Close(page);
        });
        back = d.AddButton("Back", () =>
        {
            s.StepIndex--;
            title.Text = "";
            Fill();
        });
        next = d.AddButton("Next", () =>
        {
            if (s.IsLast)
            {
                s.Finish(page);
                return;
            }
            s.StepIndex++;
            title.Text = "";
            Fill();
        }, primary: true);
        Fill();
        return ShowDialog(d, page, next);
    }

    /// <summary>The plugin load order with a checkbox per plugin (MO2's Plugins list).</summary>
    private Window PluginsDialog(PluginsPage page)
    {
        var game = page.Game;
        var d = new DesktopDialog($"Plugins: {game.Title}", new Vector2I(980, 660));
        var info = d.AddText("", DesktopTheme.Dim);
        var warn = d.AddText("", DesktopTheme.Warn);
        var tree = new Tree { HideRoot = true, Columns = 5, ColumnTitlesVisible = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Row, TooltipText = "Plugins" };
        string[] titles = ["#", "On", "Plugin", "Type", "From mod"];
        for (var i = 0; i < titles.Length; i++)
        {
            tree.SetColumnTitle(i, titles[i]);
            tree.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
            tree.SetColumnExpand(i, i is 2 or 4);
        }
        tree.SetColumnCustomMinimumWidth(0, 44);
        tree.SetColumnCustomMinimumWidth(1, 44);
        tree.SetColumnCustomMinimumWidth(3, 110);
        d.Body.AddChild(tree);
        var filling = false;
        string? selected = null;
        Button? reset = null;
        void Fill()
        {
            filling = true;
            tree.Clear();
            var root = tree.CreateItem();
            var (list, problem) = PluginsPage.Load(game);
            var custom = PluginsPage.CustomOrder(game);
            info.Text = problem ?? $"{list.Count(p => p.Enabled)} of {list.Count} plugins on. Luma writes this order to plugins.txt when the mods go in: masters first, then {(custom ? "the order you set (Move up/down, Ctrl+Up/Down, or Sort)" : "your mods' plugins in mod order (reorder the mods, move single plugins, or Sort)")}. Plugins not from Luma keep the state they have in the game.";
            info.AddThemeColorOverride("font_color", problem != null ? DesktopTheme.Warn : DesktopTheme.Dim);
            var problems = PluginsPage.Problems(game);
            warn.Text = string.Join("\n", problems.Take(5).Select(p => "⚠ " + p.Text)) + (problems.Count > 5 ? $"\n…and {problems.Count - 5} more." : "");
            warn.Visible = problems.Count > 0;
            var bad = new HashSet<string>(problems.Where(x => x.Plugin != null).Select(x => x.Plugin!), StringComparer.OrdinalIgnoreCase);
            if (reset != null)
            {
                reset.Disabled = !custom;
            }
            var n = 0;
            foreach (var p in list)
            {
                var it = tree.CreateItem(root);
                it.SetText(0, (++n).ToString());
                it.SetCustomColor(0, DesktopTheme.Dim);
                it.SetCellMode(1, TreeItem.TreeCellMode.Check);
                it.SetChecked(1, p.Enabled);
                it.SetEditable(1, p.ModId != null);
                it.SetText(2, (bad.Contains(p.Name) ? "⚠ " : "") + p.Name);
                if (bad.Contains(p.Name))
                {
                    it.SetCustomColor(2, DesktopTheme.Warn);
                }
                else if (p.Master)
                {
                    it.SetCustomColor(2, DesktopTheme.Accent);
                }
                var needs = p.Needs is { Count: > 0 } nd ? $"\nNeeds: {string.Join(", ", nd)}" : "";
                var trouble = string.Join("\n", problems.Where(x => x.Plugin == p.Name).Select(x => x.Text));
                it.SetTooltipText(2, $"{p.Name} ({PluginsPage.Kind(p)}){needs}{(trouble.Length > 0 ? "\n" + trouble : "")}");
                it.SetText(3, PluginsPage.Kind(p));
                it.SetCustomColor(3, DesktopTheme.Dim);
                it.SetText(4, PluginsPage.ModName(game, p.ModId));
                it.SetCustomColor(4, DesktopTheme.Dim);
                it.SetMetadata(0, p.Name);
                if (p.Name == selected)
                {
                    it.Select(0);
                }
            }
            filling = false;
        }
        void MoveSel(int delta)
        {
            if (tree.GetSelected() is { } it)
            {
                selected = it.GetMetadata(0).AsString();
                PluginsPage.Move(game, selected, delta, () => Callable.From(Fill).CallDeferred());
            }
        }
        tree.ItemSelected += () =>
        {
            if (!filling && tree.GetSelected() is { } it)
            {
                selected = it.GetMetadata(0).AsString();
            }
        };
        tree.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, CtrlPressed: true } k && k.Keycode is Key.Up or Key.Down)
            {
                MoveSel(k.Keycode == Key.Up ? -1 : +1);
                tree.AcceptEvent();
            }
        };
        tree.ItemEdited += () =>
        {
            if (filling || tree.GetEdited() is not { } it)
            {
                return;
            }
            var name = it.GetMetadata(0).AsString();
            var (list, _) = PluginsPage.Load(game);
            if (list.FirstOrDefault(p => p.Name == name) is { ModId: not null } p)
            {
                PluginsPage.Toggle(game, p);
            }
            Callable.From(Fill).CallDeferred();
        };
        d.AddButton("Move up", () => MoveSel(-1), tooltip: "Moves the selected plugin up (Ctrl+Up). Masters stay above plugins.");
        d.AddButton("Move down", () => MoveSel(+1), tooltip: "Moves the selected plugin down (Ctrl+Down).");
        d.AddButton("Sort", () => PluginsPage.Sort(game, Fill), tooltip: "Moves plugins so each loads after the masters it needs, changing as little as possible. For LOOT's full rules, add LOOT under Tools.");
        reset = d.AddButton("Use mod order", () => PluginsPage.ResetOrder(game, Fill), tooltip: "Forgets the order you set: plugins load in the order of their mods.");
        Fill();
        d.AddButton("Close", () => App.Close(page), primary: true);
        return ShowDialog(d, page, tree);
    }

    private Window PickFileDialog(PickFilePage page)
    {
        var f = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            ModeOverridesTitle = false,
            Title = page.Title,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = [string.Join(", ", page.Patterns) + " ; Programs"],
            UseNativeDialog = NativeFileDialogs,
            Transient = true,
            Exclusive = true,
            CurrentDir = Host.Paths.Home,
        };
        var picked = false;
        f.FileSelected += path =>
        {
            picked = true;
            App.Close(page);
            page.Picked(path);
        };
        f.Canceled += () =>
        {
            if (!picked)
            {
                App.Close(page);
            }
        };
        return ShowDialog(f, page);
    }

    private Window ToolsDialog(ToolsPage page)
    {
        var game = page.Game;
        var d = new DesktopDialog($"Tools: {game.Title}", new Vector2I(940, 600));
        d.AddText(ToolsPage.About, DesktopTheme.Dim);
        var tree = new Tree { HideRoot = true, Columns = 3, ColumnTitlesVisible = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Row, TooltipText = "Tools" };
        string[] titles = ["Tool", "Program", "Arguments"];
        for (var i = 0; i < titles.Length; i++)
        {
            tree.SetColumnTitle(i, titles[i]);
            tree.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
            tree.SetColumnExpand(i, i > 0);
        }
        tree.SetColumnCustomMinimumWidth(0, 160);
        d.Body.AddChild(tree);
        d.AddText("Found on this device (double-click to add):", DesktopTheme.Dim);
        var found = new ItemList { CustomMinimumSize = new Vector2(0, 110), TooltipText = "Found tools" };
        d.Body.AddChild(found);
        List<ToolDef> foundTools = new();
        ToolDef? Selected() => tree.GetSelected() is { } it ? Host.Mods.Store(game).State.Tools.FirstOrDefault(t => t.Name == it.GetMetadata(0).AsString()) : null;
        void Fill()
        {
            tree.Clear();
            var root = tree.CreateItem();
            foreach (var t in Host.Mods.Store(game).State.Tools)
            {
                var it = tree.CreateItem(root);
                it.SetText(0, t.Name);
                it.SetText(1, ToolsPage.Where(game, t));
                it.SetText(2, t.Args);
                it.SetCustomColor(2, DesktopTheme.Dim);
                it.SetMetadata(0, t.Name);
            }
            found.Clear();
            foundTools = Host.Mods.FindTools(game);
            foreach (var t in foundTools)
            {
                found.AddItem($"{t.Name}  ·  {ToolsPage.Where(game, t)}");
            }
            found.Visible = foundTools.Count > 0;
        }
        tree.ItemActivated += () =>
        {
            if (Selected() is { } t)
            {
                ToolsPage.Run(game, t);
            }
        };
        found.ItemActivated += i =>
        {
            if (i >= 0 && i < foundTools.Count)
            {
                Host.Mods.Store(game).SetTool(foundTools[(int)i]);
                Host.Mods.NotifyChanged(game);
                Fill();
            }
        };
        d.AddButton("Run", () =>
        {
            if (Selected() is { } t)
            {
                ToolsPage.Run(game, t);
            }
        }, tooltip: "Puts the mods in, runs the selected tool, and takes them out when it closes (double-click does the same).");
        d.AddButton("Add a program…", () => ToolsPage.PickProgram(game, Fill));
        d.AddButton("Arguments…", () =>
        {
            if (Selected() is { } t)
            {
                ToolsPage.EditArgs(game, t, Fill);
            }
        });
        d.AddButton("Remove", () =>
        {
            if (Selected() is { } t)
            {
                ToolsPage.Remove(game, t, Fill);
            }
        });
        d.AddButton("Close", () => App.Close(page), primary: true);
        Fill();
        return ShowDialog(d, page, tree);
    }

    private Window OverwriteDialog(OverwritePage page)
    {
        var game = page.Game;
        var d = new DesktopDialog($"Overwrite: {game.Title}", new Vector2I(820, 560));
        d.AddText(OverwritePage.About, DesktopTheme.Dim);
        var list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single, TooltipText = "Files in Overwrite" };
        d.Body.AddChild(list);
        var count = d.AddText("", DesktopTheme.Dim);
        Button? make = null, clear = null;
        void Fill()
        {
            list.Clear();
            var files = Host.Mods.OverwriteFiles(game);
            foreach (var f in files.Take(2000))
            {
                list.AddItem(OverwritePage.Show(f));
            }
            count.Text = files.Count == 0 ? "Overwrite is empty." : $"{files.Count} file{(files.Count == 1 ? "" : "s")}";
            make!.Disabled = clear!.Disabled = files.Count == 0;
        }
        make = d.AddButton("Make a mod from these…", () => OverwritePage.MakeMod(game, Fill), tooltip: "Moves the files into a new mod at the end of the load order.");
        clear = d.AddButton("Delete them…", () => OverwritePage.Clear(game, Fill), tooltip: "Removes the files for good.");
        d.AddButton("Close", () => App.Close(page), primary: true);
        Fill();
        return ShowDialog(d, page, list);
    }

    private Window ConflictsDialog(ConflictsPage page)
    {
        var game = page.Game;
        var modName = page.ModId == null ? null : ConflictsPage.ModName(game, page.ModId);
        var d = new DesktopDialog(modName == null ? $"Conflicts: {game.Title}" : $"Conflicts: {modName}", new Vector2I(980, 620));
        var info = d.AddText("", DesktopTheme.Dim);
        var tree = new Tree { HideRoot = true, Columns = 3, ColumnTitlesVisible = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Multi, TooltipText = "Conflicts" };
        string[] titles = ["File", "Goes in from", "Also in"];
        for (var i = 0; i < titles.Length; i++)
        {
            tree.SetColumnTitle(i, titles[i]);
            tree.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
            tree.SetColumnExpand(i, true);
        }
        tree.SetColumnExpandRatio(0, 3);
        tree.SetColumnExpandRatio(1, 2);
        tree.SetColumnExpandRatio(2, 2);
        d.Body.AddChild(tree);
        var filling = false;
        var shown = new List<Conflict>();
        static string Clean(string s) => s.Replace(',', ' ');
        void Fill()
        {
            filling = true;
            tree.Clear();
            var root = tree.CreateItem();
            var (list, problem) = ConflictsPage.Load(game, page.ModId);
            shown = list;
            info.Text = problem ?? (list.Count == 0
                ? "No files are in more than one of the mods that are on."
                : $"{list.Count} file{(list.Count == 1 ? " is" : "s are")} in more than one mod. The mod lower in the load order wins unless you pick another under \"Goes in from\" (kept for this profile). Select several rows and right-click to pick for all of them.");
            info.AddThemeColorOverride("font_color", problem != null ? DesktopTheme.Warn : DesktopTheme.Dim);
            foreach (var c in list)
            {
                var it = tree.CreateItem(root);
                it.SetText(0, ConflictsPage.Show(c));
                it.SetTooltipText(0, c.Target);
                var providers = c.Providers ?? [c.Winner];
                // Option 0 follows the load order; then every mod that has the file.
                var options = new List<string> { $"Load order ({Clean(ConflictsPage.ModName(game, ConflictsPage.ByOrder(c)))})" };
                options.AddRange(providers.Select(id => Clean(ConflictsPage.ModName(game, id))));
                it.SetCellMode(1, TreeItem.TreeCellMode.Range);
                it.SetText(1, string.Join(",", options));
                it.SetRange(1, c.Picked ? providers.ToList().IndexOf(c.Winner) + 1 : 0);
                it.SetEditable(1, true);
                it.SetCustomColor(1, c.Picked ? DesktopTheme.Accent : DesktopTheme.Text);
                it.SetTooltipText(1, c.Picked ? "Picked by you" : "The mod lowest in the load order");
                it.SetText(2, string.Join(", ", c.Losers.Select(l => ConflictsPage.ModName(game, l))));
                it.SetCustomColor(2, DesktopTheme.Dim);
                it.SetMetadata(0, c.Key);
            }
            filling = false;
        }
        tree.ItemEdited += () =>
        {
            if (filling || (tree.GetEdited() ?? tree.GetSelected()) is not { } it)
            {
                return;
            }
            var key = it.GetMetadata(0).AsString();
            if (shown.FirstOrDefault(c => c.Key == key) is { } c)
            {
                var i = (int)it.GetRange(1);
                var providers = c.Providers ?? [c.Winner];
                ConflictsPage.Pick(game, [key], i <= 0 || i > providers.Count ? null : providers[i - 1]);
            }
            Callable.From(Fill).CallDeferred();
        };
        List<Conflict> Selected()
        {
            var keys = new HashSet<string>();
            for (var it = tree.GetNextSelected(null); it != null; it = tree.GetNextSelected(it))
            {
                keys.Add(it.GetMetadata(0).AsString());
            }
            return shown.Where(c => keys.Contains(c.Key)).ToList();
        }
        var menu = new PopupMenu();
        d.AddChild(menu);
        var menuActions = new List<Action>();
        menu.IdPressed += id => menuActions[(int)id]();
        tree.ItemMouseSelected += (pos, button) =>
        {
            var sel = Selected();
            if (button != (long)MouseButton.Right || sel.Count == 0)
            {
                return;
            }
            menu.Clear();
            menuActions.Clear();
            var keys = sel.Select(c => c.Key).ToList();
            foreach (var id in sel.SelectMany(c => c.Providers ?? []).Distinct())
            {
                var mod = id;
                menu.AddItem($"{ConflictsPage.ModName(game, id)} wins", menuActions.Count);
                menuActions.Add(() =>
                {
                    ConflictsPage.Pick(game, sel.Where(c => c.Providers?.Contains(mod) == true).Select(c => c.Key), mod);
                    Fill();
                });
            }
            menu.AddSeparator();
            menu.AddItem("Follow the load order", menuActions.Count);
            menuActions.Add(() =>
            {
                ConflictsPage.Pick(game, keys, null);
                Fill();
            });
            menu.Position = (Vector2I)(tree.GetScreenPosition() + pos);
            menu.Popup();
        };
        Fill();
        if (page.ModId is { } modId)
        {
            d.AddButton($"Make {modName} win all", () =>
            {
                ConflictsPage.Pick(game, shown.Select(c => c.Key), modId);
                Fill();
            });
        }
        d.AddButton("Close", () => App.Close(page), primary: true);
        return ShowDialog(d, page, tree);
    }

    private Window SiteSetupDialog(SiteSetupPage page)
    {
        var site = page.Site;
        var d = new DesktopDialog($"Set up {site.Name}", new Vector2I(620, 0));
        void Fill()
        {
            foreach (var c in d.Body.GetChildren())
            {
                c.QueueFree();
            }
            var g = SiteKeys.For(site);
            d.AddText(g.Why, DesktopTheme.Dim);
            var key = SiteKeys.CurrentKey(site);
            d.AddText(SiteKeys.Status(site), key == null ? DesktopTheme.Warn : DesktopTheme.Good);
            for (var i = 0; i < g.Steps.Length; i++)
            {
                d.AddText($"{i + 1}.  {g.Steps[i]}");
            }
            var open = DesktopTheme.Button("Open the key page in your browser", () => ModActions.OpenUrl(g.KeyPage), g.KeyPage);
            open.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            d.Body.AddChild(open);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var field = new LineEdit { Secret = true, PlaceholderText = key == null ? "Paste your API key" : "Paste a new key to replace the saved one", SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = $"{site.Name} API key" };
            async void Check()
            {
                var text = field.Text.Trim();
                if (text.Length == 0)
                {
                    return;
                }
                if (await SiteKeys.Check(site, text) && IsInstanceValid(d))
                {
                    Fill();
                }
            }
            field.TextSubmitted += _ => Check();
            row.AddChild(field);
            row.AddChild(DesktopTheme.Button("Check and save", Check, "Checks the key with the site, then keeps it on this device.", "PrimaryButton"));
            d.Body.AddChild(row);
            if (key != null)
            {
                var forget = DesktopTheme.Button("Forget the key", () => App.Confirm($"Remove your {site.Name} key from Luma?", "Forget it", () =>
                {
                    SiteKeys.Forget(site);
                    if (IsInstanceValid(d))
                    {
                        Fill();
                    }
                }), "Removes it from this device.");
                forget.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                d.Body.AddChild(forget);
            }
            if (site.Id == "nexus")
            {
                d.Body.AddChild(new HSeparator());
                var registered = NxmHandler.IsRegistered(Host.Paths);
                d.AddText(registered ? "\"Mod Manager Download\" buttons on Nexus Mods open in Luma." : "Let the website's \"Mod Manager Download\" buttons send files to Luma.", DesktopTheme.Dim);
                var links = DesktopTheme.Button(registered ? "Set up download links again" : "Handle Nexus download links", () =>
                {
                    SiteKeys.SetUpNxmLinks();
                    Fill();
                }, "Makes Luma the browser's handler for nxm:// links.");
                links.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                d.Body.AddChild(links);
            }
            d.AddText($"Your key stays on this device and is only sent to {site.Name}.", DesktopTheme.Dim, DesktopTheme.SmallSize);
            if (d.Visible)
            {
                Callable.From(d.Fit).CallDeferred();
            }
        }
        Fill();
        d.AddButton("Close", () =>
        {
            App.Close(page);
            _browse.GameChanged();
            _settings.Refresh();
        }, primary: true);
        d.Dismissed += () =>
        {
            _browse.GameChanged();
            _settings.Refresh();
        };
        return ShowDialog(d, page);
    }

    private Window SteamDialog(SteamLaunchPage page)
    {
        var game = page.Game;
        var option = ModManager.LaunchOption(ModActions.CliPath());
        var d = new DesktopDialog("Start with mods from Steam", new Vector2I(640, 0));
        d.AddText($"With this launch option, starting {game.Title} from Steam ({GameText.SteamTvMode} too) links your mods in first and takes them out when the game closes. The game folder stays as Steam installed it, and a problem with mods never stops the game from starting.", DesktopTheme.Dim);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var field = new LineEdit { Text = option, Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = option };
        field.AddThemeFontOverride("font", DesktopTheme.Mono);
        row.AddChild(field);
        row.AddChild(DesktopTheme.Button("Copy", () =>
        {
            DisplayServer.ClipboardSet(option);
            App.Success("Copied. Paste it in Steam's launch options.");
        }, "Copies the launch option.", "PrimaryButton"));
        d.Body.AddChild(row);
        string[] steps =
        [
            "Copy the launch option above.",
            $"In Steam, open {game.Title}'s Properties (right-click the game in the library).",
            "Under General → Launch options, paste it. If something is already there, put it in front of %command% instead of replacing it.",
            "Start the game as usual.",
        ];
        for (var i = 0; i < steps.Length; i++)
        {
            d.AddText($"{i + 1}.  {steps[i]}");
        }
        if (game.Install.LaunchOptions is { Length: > 0 } current)
        {
            d.AddText($"Current launch options: {current}", DesktopTheme.Dim, DesktopTheme.SmallSize);
        }
        d.AddButton("I've set it: check again", () =>
        {
            App.ScanGames();
            App.Close(page);
        }, tooltip: "Reads Steam's settings again.");
        d.AddButton("Close", () => App.Close(page), primary: true);
        return ShowDialog(d, page);
    }

    private FileDialog ArchivePicker(string title)
    {
        var f = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            ModeOverridesTitle = false,
            Title = title,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.zip, *.7z, *.rar ; Mod archives"],
            UseNativeDialog = NativeFileDialogs,
            Transient = true,
            Exclusive = true,
        };
        var downloads = Path.Combine(Host.Paths.Home, "Downloads");
        f.CurrentDir = Directory.Exists(downloads) ? downloads : Host.Paths.Home;
        return f;
    }

    private Window AddFileDialog(AddFilePage page)
    {
        var game = page.Game;
        var loader = Host.Mods.Loader(game);
        var f = ArchivePicker(loader?.Launcher != null ? $"Add a mod from a file (including {loader.Name})" : $"Add a mod from a file for {game.Title}");
        var picked = false;
        f.FileSelected += path =>
        {
            picked = true;
            App.Close(page);
            FomodSession.InstallFile(game, path);
        };
        f.Canceled += () =>
        {
            if (!picked)
            {
                App.Close(page);
            }
        };
        return ShowDialog(f, page);
    }

    /// <summary>Export: a save dialog with the pack's contents as an option (download list or full).</summary>
    private Window ExportPackDialog(ModPackPage page)
    {
        var game = page.Game!;
        var f = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile,
            ModeOverridesTitle = false,
            Title = $"Export {Host.Mods.Store(game).State.Active} as a modpack",
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*" + ModPack.Extension + " ; Luma modpacks"],
            UseNativeDialog = NativeFileDialogs,
            Transient = true,
            Exclusive = true,
        };
        f.AddOption("Contents", ["Download list (others download each mod)", "Full (mods that may be passed on go inside)"], 0);
        Directory.CreateDirectory(PackActions.Folder);
        f.CurrentDir = PackActions.Folder;
        f.CurrentFile = Path.GetFileName(PackActions.DefaultFile(game, false));
        var picked = false;
        void Save(string path, bool full)
        {
            picked = true;
            App.Close(page);
            if (!path.EndsWith(ModPack.Extension, StringComparison.OrdinalIgnoreCase))
            {
                path += ModPack.Extension;
            }
            PackActions.Export(game, full, path);
        }
        f.FileSelected += path => Save(path, f.GetSelectedOptions().TryGetValue("Contents", out var v) && v.AsInt32() == 1);
        f.Canceled += () =>
        {
            if (!picked)
            {
                App.Close(page);
            }
        };
        return ShowDialog(f, page);
    }

    private Window ImportPackDialog(ModPackPage page)
    {
        var f = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            ModeOverridesTitle = false,
            Title = "Import a modpack",
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*" + ModPack.Extension + " ; Luma modpacks"],
            UseNativeDialog = NativeFileDialogs,
            Transient = true,
            Exclusive = true,
        };
        var downloads = Path.Combine(Host.Paths.Home, "Downloads");
        f.CurrentDir = PackActions.Candidates().Select(Path.GetDirectoryName).FirstOrDefault() ?? (Directory.Exists(downloads) ? downloads : Host.Paths.Home);
        var picked = false;
        f.FileSelected += path =>
        {
            picked = true;
            App.Close(page);
            PackActions.Import(path);
        };
        f.Canceled += () =>
        {
            if (!picked)
            {
                App.Close(page);
            }
        };
        return ShowDialog(f, page);
    }

    private Window ImportDialog(ImportPage page)
    {
        var game = page.Game;
        var link = page.Link;
        var target = page.Target;
        var d = new DesktopDialog($"Add {link.Item}s from {link.Name}", new Vector2I(600, 0));
        d.AddText($"{link.Name} has no way for apps to download, so get {link.Item}s on the website (signed in), then choose the file here. Luma unpacks it where {game.Title} finds it.", DesktopTheme.Dim);
        Label? count = null;
        void Count()
        {
            if (count != null && target != null)
            {
                var n = Directory.Exists(target) ? Directory.GetDirectories(target).Length : 0;
                count.Text = $"{n} {link.Item}{(n == 1 ? "" : "s")} added so far.";
            }
        }
        if (target == null)
        {
            d.AddText(Luma.Core.Platform.IsWindows
                ? $"Luma doesn't know where {game.Title} keeps its {link.Item}s on Windows."
                : $"Start {game.Title} once from Steam first: its {link.Item}s go inside the folder Proton creates then.", DesktopTheme.Warn);
        }
        else
        {
            count = d.AddText("");
            Count();
        }
        d.AddButton($"Open {link.Name}", () => ModActions.OpenUrl(link.Url), tooltip: link.Url);
        if (target != null)
        {
            d.AddButton("Choose file…", () =>
            {
                var f = ArchivePicker($"Add a {link.Item} from {link.Name}");
                f.FileSelected += path => App.Busy($"Adding {Path.GetFileName(path)}…", (_, _) => Task.Run(() => ImportPage.Unpack(path, target)), name =>
                {
                    App.Success($"Added \"{name}\".");
                    if (IsInstanceValid(d))
                    {
                        Count();
                    }
                }, cancellable: false);
                f.Canceled += f.QueueFree;
                f.FileSelected += _ => f.QueueFree();
                d.AddChild(f);
                f.PopupCentered(new Vector2I(820, 560));
            }, primary: true);
        }
        d.AddButton("Close", () => App.Close(page));
        return ShowDialog(d, page);
    }

    /// <summary>Any other page: shown as it is, in a window.</summary>
    private Window GenericDialog(Screen page)
    {
        var d = new DesktopDialog(page.GetType().Name.Replace("Page", ""), new Vector2I(960, 680));
        page.SizeFlagsVertical = SizeFlags.ExpandFill;
        d.Body.AddChild(page);
        d.AddButton("Close", () => App.Close(page), primary: true);
        return ShowDialog(d, page);
    }
}
