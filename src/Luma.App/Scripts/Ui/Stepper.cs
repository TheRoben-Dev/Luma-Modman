using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Controller-friendly value picker: a row showing "Caption   ‹ Value ›". Left/right (or A)
/// changes the value in place, so no popups or text fields are needed with a gamepad.
/// The new value slides in from the side it came from, and the arrow on that side nudges.
/// Rows with <see cref="Expands"/> open a short explanation beneath them while focused (what
/// the setting does plus the tradeoff of the current value), which follows value changes.
/// Other rows show their help line in the hint bar (TooltipText).
/// </summary>
public partial class Stepper : Button
{
    public const string ExpandsMeta = "ed_expands";
    private const float RowHeight = 54;

    private readonly Label _caption;
    private readonly Label _value;
    private readonly Label _left;
    private readonly Label _right;
    private readonly Control _valueBox;
    private readonly Label _detail;
    private readonly List<(string Value, string Label)> _options;
    private readonly string _help;
    private int _index;
    private float _open;
    private Tween? _openTween;
    private Tween? _slide;

    public bool Wrap { get; init; } = true;

    /// <summary>Optional explanation of a specific value (its tradeoff), shown in the expanded help.</summary>
    public Func<string, string?>? DetailFor { get; init; }

    /// <summary>Show an expanding explanation under the row while it has focus.</summary>
    public bool Expands
    {
        get => HasMeta(ExpandsMeta);
        init
        {
            if (value)
            {
                SetMeta(ExpandsMeta, true);
            }
        }
    }

    public event Action<string>? Changed;

    public Stepper(string caption, IEnumerable<(string Value, string Label)> options, string? current, string help = "")
    {
        _options = options.ToList();
        _index = Math.Max(0, _options.FindIndex(o => o.Value == current));
        _help = help;
        TooltipText = help;
        CustomMinimumSize = new Vector2(0, RowHeight);
        FocusMode = FocusModeEnum.All;
        ClipContents = true;

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        row.OffsetLeft = 18;
        row.OffsetRight = -18;
        row.OffsetBottom = RowHeight;
        row.AddThemeConstantOverride("separation", 10);
        _caption = UiTheme.Label(caption);
        _caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _caption.VerticalAlignment = VerticalAlignment.Center;
        _caption.MouseFilter = MouseFilterEnum.Ignore;
        _left = Arrow("‹");
        _right = Arrow("›");
        _value = UiTheme.Label("", color: UiTheme.AccentText);
        _value.VerticalAlignment = VerticalAlignment.Center;
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        _value.MouseFilter = MouseFilterEnum.Ignore;
        _value.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // The value lives in a plain box so it can slide without the row's layout snapping it back.
        _valueBox = new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.Fill };
        _valueBox.AddChild(_value);
        // Re-measure whenever the text or the font changes (the theme font arrives when the row enters the tree).
        _value.MinimumSizeChanged += () => _valueBox.CustomMinimumSize = _value.GetMinimumSize();
        row.AddChild(_caption);
        row.AddChild(_left);
        row.AddChild(_valueBox);
        row.AddChild(_right);
        AddChild(row);

        _detail = UiTheme.Label("", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true);
        _detail.MouseFilter = MouseFilterEnum.Ignore;
        _detail.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _detail.OffsetLeft = 18;
        _detail.OffsetRight = -18;
        _detail.OffsetTop = RowHeight - 8;
        _detail.Modulate = Colors.White with { A = 0 };
        AddChild(_detail);

        Pressed += () => Step(1);
        Anim.PressFlash(this);
        FocusEntered += () => SetOpen(true);
        FocusExited += () => SetOpen(false);
        Resized += () =>
        {
            if (_open > 0)
            {
                ApplyOpen(_open);  // width changed: re-measure the wrapped text
            }
        };
        Refresh();
    }

    private Label Arrow(string glyph)
    {
        var l = UiTheme.Label(glyph, color: UiTheme.AccentText);
        l.VerticalAlignment = VerticalAlignment.Center;
        l.MouseFilter = MouseFilterEnum.Ignore;
        l.Resized += () => l.PivotOffset = l.Size / 2;
        return l;
    }

    public string Value => _options.Count > 0 ? _options[_index].Value : "";

    public override void _GuiInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_left", allowEcho: true))
        {
            Step(-1);
            AcceptEvent();
        }
        else if (e.IsActionPressed("ui_right", allowEcho: true))
        {
            Step(1);
            AcceptEvent();
        }
    }

    private void Step(int d)
    {
        if (_options.Count < 2 || Disabled)
        {
            return;
        }
        var next = _index + d;
        next = Wrap ? (next + _options.Count) % _options.Count : Math.Clamp(next, 0, _options.Count - 1);
        if (next == _index)
        {
            return;
        }
        _index = next;
        Refresh();
        Slide(d);
        Feedback.Play(Cue.Toggle);
        Changed?.Invoke(Value);
    }

    /// <summary>New value slides in from the side it came from; that side's arrow nudges outwards.</summary>
    private void Slide(int d)
    {
        _slide?.Kill();
        if (!Anim.Enabled || !IsInsideTree())
        {
            return;
        }
        _value.Position = new Vector2(18 * d, 0);
        _value.Modulate = Colors.White with { A = 0.2f };
        var arrow = d < 0 ? _left : _right;
        arrow.Position += new Vector2(5 * d, 0);
        arrow.Scale = new Vector2(1.3f, 1.3f);
        _slide = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _slide.TweenProperty(_value, "position", Vector2.Zero, Anim.Normal);
        _slide.TweenProperty(_value, "modulate:a", 1f, Anim.Normal);
        _slide.TweenProperty(arrow, "scale", Vector2.One, Anim.Slow).SetTrans(Tween.TransitionType.Back);
        // The row's container puts the arrow back on its next layout pass; this only animates the nudge.
        _slide.TweenProperty(arrow, "position:x", arrow.Position.X - 5 * d, Anim.Normal);
    }

    public void SetValue(string value, bool notify = false)
    {
        var i = _options.FindIndex(o => o.Value == value);
        if (i >= 0 && i != _index)
        {
            _index = i;
            Refresh();
            if (notify)
            {
                Changed?.Invoke(Value);
            }
        }
    }

    private void Refresh()
    {
        var label = _options.Count > 0 ? _options[_index].Label : "-";
        var canLeft = _options.Count > 1 && (Wrap || _index > 0);
        var canRight = _options.Count > 1 && (Wrap || _index < _options.Count - 1);
        _value.Text = label;
        _left.Modulate = Colors.White with { A = canLeft ? 1 : 0 };
        _right.Modulate = Colors.White with { A = canRight ? 1 : 0 };
        _valueBox.CustomMinimumSize = _value.GetMinimumSize();
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        if (!Expands)
        {
            return;
        }
        var detail = DetailFor?.Invoke(Value);
        _detail.Text = string.IsNullOrEmpty(detail) ? _help : string.IsNullOrEmpty(_help) ? detail : _help + "\n" + detail;
        if (_open > 0)
        {
            ApplyOpen(_open);
        }
    }

    private float DetailHeight()
    {
        var width = Math.Max(100, Size.X - 36);
        var font = _detail.GetThemeFont("font");
        var size = _detail.GetThemeFontSize("font_size");
        var h = font.GetMultilineStringSize(_detail.Text, HorizontalAlignment.Left, width, size,
            brkFlags: TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound).Y;
        return h + 12;
    }

    private void ApplyOpen(float v)
    {
        _open = v;
        CustomMinimumSize = new Vector2(0, RowHeight + DetailHeight() * v);
        _detail.Modulate = Colors.White with { A = Math.Clamp((v - 0.3f) / 0.7f, 0, 1) };
        _detail.OffsetTop = RowHeight - 8 + (1 - v) * 8;
    }

    /// <summary>Expands (focus in) or collapses (focus out) the explanation.</summary>
    private void SetOpen(bool on)
    {
        UpdateDetail();  // DetailFor/Expands are set after the constructor ran
        if (!Expands || string.IsNullOrEmpty(_detail.Text))
        {
            return;
        }
        _openTween?.Kill();
        var target = on ? 1f : 0f;
        if (!Anim.Enabled || !IsInsideTree())
        {
            ApplyOpen(target);
            return;
        }
        _openTween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _openTween.TweenMethod(Callable.From<float>(ApplyOpen), _open, target, on ? Anim.Slow : Anim.Normal);
    }
}

