using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Two panes, console style: categories on the left, the selected category on the right.
/// Focusing a category shows its contents; A or right moves into them, and only B goes back to the
/// category (left inside the pane never leaves it). While the pane has focus the category list
/// narrows a little and dims every category but the selected one. Used by Settings and Emulators → Eden.
/// </summary>
public abstract partial class TwoPaneScreen : Screen
{
    private int _category;
    private bool _enterOnFocus;
    private bool _startApplied;

    /// <summary>Category to open with (and step into) when the screen first appears, e.g. after a restart.</summary>
    public string? StartIn { get; init; }
    private readonly List<Button> _cats = new();
    private VBoxContainer? _pane;
    private ScrollContainer? _paneScroll;
    private readonly List<Control> _paneItems = new();
    private IReadOnlyList<(string Name, string Summary)> _list = [];
    private VBoxContainer? _left;
    private bool _inPane;
    private float _away;  // 0 = category list active, 1 = pane active
    private Tween? _awayTween;

    private const float ListWidth = 280;
    private const float ListShift = 16;

    /// <summary>Heading over the category list.</summary>
    protected abstract string Heading { get; }

    /// <summary>Category names and the one-line summary shown in the hint bar. Read on every build.</summary>
    protected abstract IReadOnlyList<(string Name, string Summary)> Categories { get; }

    /// <summary>Fills the right pane for <paramref name="name"/>; focusable rows go through <paramref name="add"/>.</summary>
    protected abstract void BuildCategory(string name, VBoxContainer col, Action<Control> add);

    /// <summary>Optional marker after a category name (like "•" for something that needs attention).</summary>
    protected virtual string? Badge(string name) => null;

    protected override void Build()
    {
        _cats.Clear();
        _list = Categories;
        if (StartIn != null && !_startApplied)
        {
            _startApplied = true;
            var start = _list.ToList().FindIndex(c => c.Name == StartIn);
            if (start >= 0)
            {
                _category = start;
                _enterOnFocus = true;
            }
        }
        _category = Math.Clamp(_category, 0, Math.Max(0, _list.Count - 1));
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 28);
        AddChild(row);

        // The list sits in a plain holder so sliding it aside is a pure transform: the row never
        // re-lays out mid-animation (that rounding is what made the end of the slide jitter).
        var holder = new Control { CustomMinimumSize = new Vector2(ListWidth, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddChild(holder);
        var left = Column(6);
        holder.AddChild(left);
        holder.Resized += () =>
        {
            left.Size = holder.Size;
            ApplyAway(_away);
        };
        // The holder doesn't size itself from the list, so widen it when a theme's font makes a
        // category name longer than ListWidth; otherwise the list runs into the pane.
        void FitList() => holder.CustomMinimumSize = new Vector2(Mathf.Max(ListWidth, Mathf.Ceil(left.GetCombinedMinimumSize().X)), 0);
        left.MinimumSizeChanged += FitList;
        _left = left;
        left.AddChild(Section(Heading));
        for (var i = 0; i < _list.Count; i++)
        {
            var index = i;
            var name = _list[i].Name;
            var badge = Badge(name);
            var b = new ActionButton(badge != null ? $"{name}  {badge}" : name, EnterPane, _list[i].Summary);
            b.FocusEntered += () =>
            {
                SetInPane(false);
                SelectCategory(index);
            };
            left.AddChild(b);
            _cats.Add(b);
        }

        FitList();

        _pane = Column(8);
        _paneScroll = Scroller(_pane);
        row.AddChild(_paneScroll);
        BuildPane();
        _awayTween = null;
        ApplyAway(_inPane ? 1 : 0);
    }

    /// <summary>Slides the category list aside (and dims it) while the pane has focus, and back.</summary>
    private void SetInPane(bool on)
    {
        if (on == _inPane)
        {
            return;
        }
        _inPane = on;
        _awayTween?.Kill();
        var target = on ? 1f : 0f;
        if (!Anim.Enabled || !IsInsideTree())
        {
            ApplyAway(target);
            return;
        }
        _awayTween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _awayTween.TweenMethod(Callable.From<float>(ApplyAway), _away, target, Anim.Slow);
    }

    private void ApplyAway(float v)
    {
        _away = v;
        if (_left == null)
        {
            return;
        }
        // Whole pixels only, so text never shimmers between frames or snaps when the tween ends.
        _left.Position = new Vector2(-Mathf.Round(ListShift * v), 0);
        for (var i = 0; i < _cats.Count; i++)
        {
            var selected = i == _category;
            var c = _cats[i];
            // Selected stays fully lit; the rest fade to a grey ghost and shrink toward their left edge.
            c.Modulate = selected ? Colors.White : new Color(1, 1, 1, 1 - 0.65f * v);
            c.PivotOffset = new Vector2(0, c.Size.Y / 2);
            c.Scale = Vector2.One * (1 - 0.06f * v * (selected ? 0 : 1));
        }
        if (_left.GetChildCount() > 0 && _left.GetChild(0) is Control heading)
        {
            heading.Modulate = new Color(1, 1, 1, 1 - 0.5f * v);
        }
    }

    private void SelectCategory(int index)
    {
        if (index == _category && _paneItems.Count > 0)
        {
            return;
        }
        _category = index;
        BuildPane();
        if (_paneScroll != null)
        {
            _paneScroll.ScrollVertical = 0;
        }
    }

    private void EnterPane()
    {
        if (_paneItems.Count > 0)
        {
            _paneItems[0].GrabFocus();
        }
    }

    public override bool OnBack()
    {
        var f = GetViewport()?.GuiGetFocusOwner();
        if (f != null && _pane != null && _pane.IsAncestorOf(f) && _category < _cats.Count)
        {
            _cats[_category].GrabFocus();
            return true;
        }
        return false;
    }

    private void BuildPane()
    {
        if (_pane == null || _list.Count == 0)
        {
            return;
        }
        foreach (var c in _pane.GetChildren())
        {
            _pane.RemoveChild(c);
            c.QueueFree();
        }
        _paneItems.Clear();
        var col = _pane;
        var items = _paneItems;
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }
        col.AddChild(UiTheme.Label(_list[_category].Name, UiTheme.TitleSize));
        BuildCategory(_list[_category].Name, col, Add);

        for (var i = 0; i < _cats.Count; i++)
        {
            _cats[i].AddThemeColorOverride("font_color", i == _category ? UiTheme.AccentText : UiTheme.Text);
        }
        Focus.Column(_cats, right: items.FirstOrDefault());
        // Left never leaves the pane (steppers use left/right for their value); only B goes back.
        Focus.Column(items);
        foreach (var item in items)
        {
            item.FocusEntered += () => SetInPane(true);
        }
        Anim.Stagger(items, 0.015);
    }

    public override void FocusDefault()
    {
        if (_enterOnFocus && _paneItems.Count > 0)
        {
            _enterOnFocus = false;
            _paneItems[0].GrabFocus();
            return;
        }
        (_category < _cats.Count ? _cats[_category] : null)?.GrabFocus();
    }

    /// <summary>Rebuilds the open category's pane in place and puts focus back on the same row.</summary>
    protected void RefreshPane()
    {
        var f = GetViewport()?.GuiGetFocusOwner();
        var index = f != null ? _paneItems.IndexOf(f) : -1;
        BuildPane();
        if (index >= 0 && _paneItems.Count > 0)
        {
            _paneItems[Math.Min(index, _paneItems.Count - 1)].GrabFocus();
        }
    }
}
