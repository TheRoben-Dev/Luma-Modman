using Godot;

namespace Luma.App.Ui;

/// <summary>
/// One shared focus ring that glides from control to control instead of snapping, like a console
/// menu cursor. It follows its target every frame (so it stays attached while lists scroll,
/// cards grow or rows expand), is clipped to the scroller the target lives in, and fades with
/// the target's own opacity. Controls that draw their own focus (cartridge tiles, the Play
/// button) set meta "ed_own_focus": the ring still travels to them but fades out on arrival.
/// </summary>
public partial class FocusHighlight : Control
{
    public const string OwnFocus = "ed_own_focus";

    private readonly Control _clip = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Ring _ring = new();
    private Control? _target;
    private Rect2 _from;
    private Rect2 _shown;
    private float _fromAlpha;
    private float _alpha;
    private double _t = 1;
    private float _press;

    /// <summary>Hidden while a game runs.</summary>
    public bool Suspended { get; set; }

    public static FocusHighlight? Instance { get; private set; }

    public FocusHighlight()
    {
        Instance = this;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_clip);
        _clip.AddChild(_ring);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>Theme changed: rebuild the ring's style.</summary>
    public void Restyle() => _ring.Style = UiTheme.Focus();

    /// <summary>Quick squeeze on press, so A/Enter has a visible response on every control.</summary>
    public void Pulse() => _press = Anim.Enabled ? 1f : 0f;

    public override void _Ready()
    {
        Restyle();
        GetViewport().GuiFocusChanged += OnFocus;
    }

    private void OnFocus(Control c)
    {
        if (c == _target)
        {
            return;
        }
        // Start from wherever the ring is drawn now, so a move mid-glide doesn't jump.
        _from = _alpha > 0.01f ? _shown : TargetRect(c);
        _fromAlpha = _alpha;
        _target = c;
        _t = Anim.Enabled && _alpha > 0.01f ? 0 : 1;
    }

    private static Rect2 TargetRect(Control c)
    {
        var xf = c.GetGlobalTransformWithCanvas();
        return xf * new Rect2(Vector2.Zero, c.Size);
    }

    /// <summary>Opacity of the target including its ancestors (stagger fades, card fades, hidden pages).</summary>
    private static float Opacity(Control c)
    {
        var a = 1f;
        for (Node? n = c; n != null && n is not Main; n = n.GetParent())
        {
            if (n is CanvasItem ci)
            {
                a *= ci.Modulate.A;
            }
        }
        return a * c.SelfModulate.A;
    }

    private static Rect2? ClipRect(Control c)
    {
        Rect2? clip = null;
        for (var n = c.GetParent(); n != null && n is not Main; n = n.GetParent())
        {
            if (n is Control { ClipContents: true } p)
            {
                var r = p.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, p.Size);
                clip = clip is { } prev ? prev.Intersection(r) : r;
            }
        }
        return clip;
    }

    public override void _Process(double delta)
    {
        var c = _target;
        var valid = c != null && IsInstanceValid(c) && c.IsVisibleInTree() && c.HasFocus() && !Suspended;
        if (!valid)
        {
            _alpha = Mathf.MoveToward(_alpha, 0, (float)delta * 10);
            _ring.Modulate = Colors.White with { A = _alpha };
            _ring.Visible = _alpha > 0.01f;
            return;
        }
        var own = c!.HasMeta(OwnFocus);
        var target = TargetRect(c);
        var targetAlpha = own ? 0f : Opacity(c);
        _t = Math.Min(1, _t + delta / 0.15);
        var k = (float)Ease(_t);
        var rect = new Rect2(_from.Position.Lerp(target.Position, k), _from.Size.Lerp(target.Size, k));
        _alpha = _t >= 1 ? targetAlpha : Mathf.Lerp(_fromAlpha, own ? Mathf.Lerp(_fromAlpha, 0, k) : targetAlpha, k);
        _shown = rect;

        // Press squeeze: shrink a few px and spring back.
        if (_press > 0)
        {
            _press = Math.Max(0, _press - (float)delta * 7);
            rect = rect.Grow(-4 * (float)Math.Sin(_press * Math.PI));
        }

        var clip = ClipRect(c) ?? GetViewportRect();
        // During a glide the ring may pass outside the target's scroller; let it, then clip once it lands.
        if (_t < 1)
        {
            clip = clip.Merge(_from).Merge(target);
        }
        _clip.Position = clip.Position;
        _clip.Size = clip.Size;
        _ring.Position = rect.Position - clip.Position;
        _ring.Size = rect.Size;
        _ring.Modulate = Colors.White with { A = _alpha };
        _ring.Visible = _alpha > 0.01f;
    }

    // Quartic ease-out: fast start so the cursor feels instant, soft landing.
    private static double Ease(double t) => 1 - Math.Pow(1 - t, 4);

    private partial class Ring : Control
    {
        private StyleBox? _style;

        public StyleBox? Style
        {
            get => _style;
            set
            {
                _style = value;
                QueueRedraw();
            }
        }

        public Ring()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Resized += QueueRedraw;
        }

        public override void _Draw()
        {
            if (_style != null)
            {
                DrawStyleBox(_style, new Rect2(Vector2.Zero, Size));
            }
        }
    }
}
