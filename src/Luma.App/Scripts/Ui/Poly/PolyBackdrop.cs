using Godot;
using static Luma.App.Ui.Poly.Ps1;

namespace Luma.App.Ui.Poly;

/// <summary>
/// The "32-bit 3D" theme's background: a fogged grid floor drifting underneath (big quads, so the
/// affine texture warp shows), large shapes tumbling far behind, and a handful of era props around
/// the screen edges that idle-move and react to taps on empty space. The camera sways slowly and
/// leans a little toward whatever has focus, for parallax.
/// </summary>
public partial class PolyBackdrop : PolyStage
{
    // Screen anchor (0..1), distance from the camera, and height in UI pixels.
    private static readonly (Vector2 At, float Depth, float Px)[] Slots =
    [
        (new Vector2(0.06f, 0.86f), 7f, 120),
        (new Vector2(0.95f, 0.84f), 7f, 120),
        (new Vector2(0.95f, 0.26f), 9f, 100),
        (new Vector2(0.04f, 0.40f), 9f, 96),
        (new Vector2(0.78f, 0.93f), 8f, 84),
        (new Vector2(0.24f, 0.94f), 8.5f, 84),
    ];

    private readonly List<Prop> _props = new();
    private readonly List<(Node3D Node, Vector3 Spin)> _shapes = new();
    private readonly ShaderMaterial _floor;
    private readonly Vector3 _camBase = new(0, 1.4f, 11);
    private readonly Vector3 _lookAt = new(0, 0.3f, 0);

    private float _lean;
    private float _leanTarget;
    private Vector2 _laidOutFor;
    private Vector2 _pressAt;
    private bool _pressed;

    /// <param name="showcase">Clearer view for still artwork: fog starts further out, so the floor and the far shapes show.</param>
    public PolyBackdrop(ThemeSpec spec, bool showcase = false) : base(transparent: false)
    {
        Resolution = 0.34f;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Look.Fog = spec.Bg;
        if (showcase)
        {
            // Look down at the floor so the grid fills the lower part of the picture.
            _camBase = new Vector3(0, 2.0f, 11);
            _lookAt = new Vector3(0, -0.9f, 0);
        }
        Look.FogNear = showcase ? 10 : 4;
        Look.FogFar = showcase ? 48 : 24;
        Look.Ambient = spec.Bg.Lerp(new Color(0.42f, 0.42f, 0.5f), 0.8f);
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = spec.Bg };
        World.AddChild(new WorldEnvironment { Environment = env });

        // Faint grid in the theme's line colour; fog swallows it well before the horizon.
        _floor = Look.Mat(Colors.White, GridTexture(spec.Line.Lerp(spec.Accent, 0.25f), spec.Bg.Lerp(spec.Panel, 0.35f)));
        Part(World, Floor(64, 16, 32), _floor, new Vector3(0, -3.2f, -10));

        // Big abstract shapes far back, muted so they sit behind everything.
        (Mesh Mesh, Vector3 Pos, Color Color, Vector3 Spin)[] shapes =
        [
            (Sphere(2.2f, 5, 3), new Vector3(-9, 2.5f, -16), spec.Accent.Darkened(0.35f), new Vector3(0.11f, 0.17f, 0)),
            (Torus(1.4f, 2.2f, 8, 4), new Vector3(10, 3.5f, -18), spec.Info.Darkened(0.3f), new Vector3(0.2f, 0.05f, 0.13f)),
            (Sphere(1.8f, 4, 2), new Vector3(2, 6.5f, -22), spec.Accent.Lerp(spec.Info, 0.5f).Darkened(0.3f), new Vector3(0.07f, 0.23f, 0.05f)),
        ];
        // Artwork leaves them out: lit from the low showcase camera they read as dark blobs behind the wordmark.
        foreach (var s in showcase ? [] : shapes)
        {
            var mi = Part(World, s.Mesh, Look.Mat(s.Color), s.Pos);
            _shapes.Add((mi, s.Spin));
        }

        // A different handful of props each day.
        var day = (int)(DateTime.Now - DateTime.UnixEpoch).TotalDays;
        var rng = new Random(day);
        var picks = Props.All.OrderBy(_ => rng.Next()).Take(Slots.Length).ToList();
        for (var i = 0; i < picks.Count; i++)
        {
            var prop = picks[i](Look);
            prop.Phase = i * 1.37f;
            World.AddChild(prop.Root);
            _props.Add(prop);
        }
        PlaceCamera();
    }

    public override void _Ready() => GetViewport().GuiFocusChanged += OnFocus;

    public override void _ExitTree() => GetViewport().GuiFocusChanged -= OnFocus;

    private void OnFocus(Control c)
    {
        if (!GodotObject.IsInstanceValid(c) || Size.X < 1)
        {
            return;
        }
        var r = c.GetGlobalRect();
        _leanTarget = Mathf.Clamp((r.GetCenter().X - GlobalPosition.X) / Size.X - 0.5f, -0.5f, 0.5f);
    }

    private void PlaceCamera()
    {
        var sway = Anim.Enabled ? new Vector3(Mathf.Sin(T * 0.13f) * 0.5f + _lean * 1.6f, Mathf.Sin(T * 0.09f) * 0.25f, 0) : Vector3.Zero;
        Camera.LookAtFromPosition(_camBase + sway, _lookAt + sway * 0.3f, Vector3.Up);
    }

    protected override void Follow()
    {
        if (Size != _laidOutFor && Size.X > 4)
        {
            // Pin the props to their screen anchors using the resting camera, so camera sway gives parallax.
            _laidOutFor = Size;
            Camera.LookAtFromPosition(_camBase, _lookAt, Vector3.Up);
            for (var i = 0; i < _props.Count; i++)
            {
                var (at, depth, px) = Slots[i];
                var root = _props[i].Root;
                root.Position = ToWorld(at * Size, depth);
                root.Scale = Vector3.One * px * UnitsPerPixel(depth);
            }
            PlaceCamera();
        }
    }

    protected override void Tick(float dt)
    {
        _lean = Mathf.Lerp(_lean, _leanTarget, Math.Min(1, dt * 2.5f));
        PlaceCamera();
        _floor.SetShaderParameter("uv_offset", new Vector2(0, -T * 0.08f));
        foreach (var (node, spin) in _shapes)
        {
            node.Rotation = spin * T;
        }
        foreach (var p in _props)
        {
            p.Update(T, dt);
        }
    }

    // ---- Taps on empty space bop the prop under the finger ------------------------------------------

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb || Paused || !Anim.Enabled || !IsVisibleInTree())
        {
            return;
        }
        if (mb.Pressed)
        {
            _pressAt = mb.Position;
            _pressed = true;
            return;
        }
        if (!_pressed || (mb.Position - _pressAt).Length() > TouchScroll.Deadzone || OverUi(mb.Position))
        {
            _pressed = false;
            return;
        }
        _pressed = false;
        var (from, dir) = Ray(mb.Position - GlobalPosition);
        Prop? hit = null;
        var best = float.MaxValue;
        foreach (var p in _props)
        {
            var c = p.Body.GlobalPosition;
            var r = p.Radius * p.Root.Scale.X;
            var along = (c - from).Dot(dir);
            var miss = (from + dir * along - c).Length();
            if (along > 0 && miss < r * 1.15f && along < best)
            {
                best = along;
                hit = p;
            }
        }
        hit?.Tap();
    }

    /// <summary>True when the point is over something the UI handles (buttons, rows, panels, overlays).</summary>
    private bool OverUi(Vector2 at)
    {
        var main = Main.Instance;
        bool Walk(Node n)
        {
            foreach (var child in n.GetChildren())
            {
                if (child is Control c)
                {
                    if (!c.Visible || c.IsAncestorOf(this) || c == this)
                    {
                        continue;
                    }
                    // Focusable controls, panels and full-screen catchers (overlay backdrops) count as UI;
                    // plain layout controls, containers and scrollers don't, so empty space inside a list still reaches the props.
                    var blocks = c.FocusMode != FocusModeEnum.None || c is PanelContainer or Panel ||
                                 (c.MouseFilter == MouseFilterEnum.Stop && c is not Container && c.GetType() != typeof(Control));
                    if (blocks && c.GetGlobalRect().HasPoint(at))
                    {
                        return true;
                    }
                }
                if (child is CanvasItem { Visible: false })
                {
                    continue;
                }
                if (Walk(child))
                {
                    return true;
                }
            }
            return false;
        }
        return main != null && Walk(main);
    }
}
