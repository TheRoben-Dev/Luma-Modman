using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Top bar: app name plus the root tabs (shoulder buttons, Tab, or click). One underline glides
/// between tabs (stretching towards the new tab first, then settling), tab labels crossfade, and
/// a tab can carry a small badge dot (for example "Eden update available").
/// </summary>
public partial class Header : HBoxContainer
{
    private readonly List<Label> _tabs = new();
    private readonly List<Control> _badges = new();
    private readonly ButtonGlyph _prev = new();
    private readonly ButtonGlyph _next = new();
    private readonly HBoxContainer _tabRow = new();
    private readonly Control _underline = new() { MouseFilter = MouseFilterEnum.Ignore };
    private int _current = -1;
    private float _left;
    private float _right;
    private Tween? _tween;

    /// <summary>Mouse/trackpad click on a tab (desktop use).</summary>
    public event Action<int>? TabClicked;

    public Header(IEnumerable<string> tabs)
    {
        AddThemeConstantOverride("separation", 22);
        var name = UiTheme.Label(UiTheme.Spec.Wordmark, 22, UiTheme.Nightgrid ? UiTheme.Text : UiTheme.AccentText);
        name.CustomMinimumSize = new Vector2(170, 0);
        AddChild(name);
        AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _tabRow.AddThemeConstantOverride("separation", 30);
        _tabRow.AddChild(_prev);
        foreach (var t in tabs)
        {
            var index = _tabs.Count;
            var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
            col.AddThemeConstantOverride("separation", 7);
            col.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    TabClicked?.Invoke(index);
                }
            };
            var l = UiTheme.Label(UiTheme.Mono ? t.ToUpperInvariant() : t, UiTheme.Nightgrid ? 19 : 22);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.MouseFilter = MouseFilterEnum.Ignore;
            l.AddThemeColorOverride("font_color", UiTheme.Dim);
            var badge = new Badge { Visible = false };
            l.AddChild(badge);
            col.AddChild(l);
            col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 3), MouseFilter = MouseFilterEnum.Ignore });
            _tabs.Add(l);
            _badges.Add(badge);
            _tabRow.AddChild(col);
        }
        _tabRow.AddChild(_next);
        AddChild(_tabRow);
        AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var version = UiTheme.Label("v" + Luma.Host.Updates.AppUpdater.DisplayVersion(Luma.Host.Updates.AppUpdater.CurrentAppVersion()),
            UiTheme.SmallSize, UiTheme.Dim);
        version.CustomMinimumSize = new Vector2(170, 0);
        version.HorizontalAlignment = HorizontalAlignment.Right;
        version.VerticalAlignment = VerticalAlignment.Center;
        AddChild(version);

        // The underline floats over the row (not laid out by it) so it can glide between tabs.
        _underline.TopLevel = true;
        _underline.Draw += () => _underline.DrawRect(new Rect2(Vector2.Zero, _underline.Size), UiTheme.Accent);
        _tabRow.AddChild(_underline);
        _tabRow.SortChildren += () => Callable.From(() =>
        {
            if (_tween == null || !_tween.IsRunning())
            {
                Place(false);
            }
        }).CallDeferred();
    }

    /// <summary>Shows or hides the badge dot on a tab.</summary>
    public void SetBadge(int index, bool on)
    {
        if (index < 0 || index >= _badges.Count || _badges[index].Visible == on)
        {
            return;
        }
        var b = _badges[index];
        b.Visible = on;
        if (on && Anim.Enabled && b.IsInsideTree())
        {
            b.Scale = Vector2.Zero;
            b.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back)
                .TweenProperty(b, "scale", Vector2.One, Anim.Slow);
        }
    }

    public void SetTab(int index, bool tabsActive)
    {
        var changed = index != _current;
        for (var i = 0; i < _tabs.Count; i++)
        {
            var color = i == index ? UiTheme.Text : UiTheme.Dim;
            if (changed && Anim.Enabled && IsInsideTree())
            {
                CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic)
                    .TweenProperty(_tabs[i], "theme_override_colors/font_color", color, Anim.Normal);
            }
            else
            {
                _tabs[i].AddThemeColorOverride("font_color", color);
            }
        }
        var from = _current;
        _current = index;
        if (changed)
        {
            Place(from >= 0 && Anim.Enabled && IsInsideTree());
        }
        var dim = tabsActive ? Colors.White : new Color(1, 1, 1, 0.45f);
        if (Anim.Enabled && IsInsideTree() && _tabRow.Modulate != dim)
        {
            CreateTween().TweenProperty(_tabRow, "modulate", dim, Anim.Normal);
        }
        else
        {
            _tabRow.Modulate = dim;
        }
        _prev.SetGlyph(Pad.Glyph(Pad.TabPrev));
        _next.SetGlyph(Pad.Glyph(Pad.TabNext));
    }

    /// <summary>Moves the underline under the current tab. Animated: the leading edge goes first.</summary>
    private void Place(bool animate)
    {
        if (_current < 0 || _current >= _tabs.Count)
        {
            return;
        }
        var label = _tabs[_current];
        var col = (Control)label.GetParent();
        var rect = col.GetGlobalRect();
        var left = rect.Position.X;
        var right = rect.End.X;
        var y = rect.Position.Y + label.Size.Y + 6;
        _tween?.Kill();
        if (!animate || _right <= _left)
        {
            Apply(left, right, y);
            return;
        }
        var movingRight = left > _left;
        var (l0, r0) = (_left, _right);
        // Stretch: the edge in the direction of travel leads, the trailing edge catches up.
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(v =>
        {
            var lead = 1 - (float)Math.Pow(1 - v, 3);
            var trail = v < 0.5f ? 4 * v * v * v : 1 - (float)Math.Pow(-2 * v + 2, 3) / 2;
            Apply(Mathf.Lerp(l0, left, movingRight ? trail : lead), Mathf.Lerp(r0, right, movingRight ? lead : trail), y);
        }), 0f, 1f, Anim.Slow + 0.04);
    }

    private void Apply(float left, float right, float y)
    {
        _left = left;
        _right = right;
        _underline.GlobalPosition = new Vector2(left, y);
        _underline.Size = new Vector2(Math.Max(0, right - left), 3);
        _underline.QueueRedraw();
    }

    /// <summary>Small accent dot at a tab label's top-right corner.</summary>
    private partial class Badge : Control
    {
        public Badge()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsPreset(LayoutPreset.TopRight);
            Size = new Vector2(10, 10);
            Position = new Vector2(2, -2);
            PivotOffset = new Vector2(5, 5);
        }

        public override void _Draw()
        {
            DrawCircle(new Vector2(5, 5), 5, UiTheme.Warn);
            DrawArc(new Vector2(5, 5), 5, 0, Mathf.Tau, 24, UiTheme.Bg, 1.5f, true);
        }
    }
}
