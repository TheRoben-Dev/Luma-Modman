using Luma.App.Ui.Poly;
using Godot;
using Poly3DBackdrop = Luma.App.Ui.Poly.PolyBackdrop;

namespace Luma.App.Ui;

/// <summary>
/// The theme's slow animated backdrop, between the background colour and the panels. Most themes
/// are one small fragment shader; CD era draws a few flat-shaded low-poly shapes. Kept faint so text
/// stays readable, updated at 30 Hz (the app runs in low-processor mode, so this is what decides how
/// often the idle UI redraws), frozen while a game runs or the window is in the background, and a
/// still frame when "Animations" is off.
/// </summary>
public partial class AnimatedBackground : Control
{
    private const double Step = 1.0 / 30;

    /// <summary>Time used for the still frame (reduce motion), picked so every theme looks settled.</summary>
    private const float StillTime = 41f;

    private readonly ShaderMaterial? _material;
    private readonly ColorRect? _rect;
    private readonly Poly[] _shapes = [];
    private readonly Poly3DBackdrop? _poly;
    private double _time = StillTime;
    private double _acc;
    private bool _paused;

    /// <summary>True while a game runs or the app isn't focused: nothing moves or redraws.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            SetProcess(!value && _poly == null);
            if (_poly != null)
            {
                _poly.Paused = value;
            }
        }
    }

    public AnimatedBackground(ThemeSpec spec)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (spec.Id == "polygon")
        {
            // A real 3D scene (its own low-res renderer, clock and pause).
            _poly = new Poly3DBackdrop(spec);
            AddChild(_poly);
            SetProcess(false);
            return;
        }
        if (spec.Id == "cdera")
        {
            _shapes =
            [
                new Poly(Solid.Icosahedron, new Vector2(0.80f, 0.64f), 0.24f, new Vector3(0.11f, 0.17f, 0.05f), spec.Accent),
                new Poly(Solid.Octahedron, new Vector2(0.14f, 0.80f), 0.12f, new Vector3(0.19f, -0.13f, 0.08f), spec.Info),
                new Poly(Solid.Tetrahedron, new Vector2(0.56f, 0.16f), 0.075f, new Vector3(-0.15f, 0.21f, 0.12f), spec.Accent),
            ];
            Resized += QueueRedraw;
            return;
        }
        _material = new ShaderMaterial { Shader = new Shader { Code = Common + Body(spec.Id) } };
        _material.SetShaderParameter("bg", spec.Bg);
        _material.SetShaderParameter("accent", spec.Accent);
        _material.SetShaderParameter("info", spec.Info);
        _material.SetShaderParameter("line", spec.Line);
        _material.SetShaderParameter("text", spec.Text);
        _rect = new ColorRect { Material = _material, MouseFilter = MouseFilterEnum.Ignore };
        _rect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_rect);
        Resized += Push;
    }

    public override void _Ready() => Push();

    public override void _Process(double delta)
    {
        if (_poly != null)
        {
            return;
        }
        if (!Anim.Enabled)
        {
            if (_time != StillTime)
            {
                _time = StillTime;
                Push();
            }
            return;
        }
        _time += delta;
        _acc += delta;
        if (_acc >= Step)
        {
            _acc %= Step;
            Push();
        }
    }

    private void Push()
    {
        if (_poly != null)
        {
            return;
        }
        if (_material != null)
        {
            _material.SetShaderParameter("t", (float)_time);
            _material.SetShaderParameter("size", Size);
            _rect!.QueueRedraw();
        }
        else
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_shapes.Length == 0)
        {
            return;
        }
        var t = (float)_time;
        var light = new Vector3(-0.5f, -0.7f, -1f).Normalized();
        for (var i = 0; i < _shapes.Length; i++)
        {
            var s = _shapes[i];
            var r = s.Radius * Size.Y;
            var center = s.Center * Size + new Vector2(0, Mathf.Sin(t * 0.2f + i * 2.1f) * Size.Y * 0.012f);
            var rot = new Basis(Vector3.Right, t * s.Spin.X) * new Basis(Vector3.Up, t * s.Spin.Y) * new Basis(Vector3.Back, t * s.Spin.Z);
            var pts = s.Solid.Vertices.Select(v => rot * v).ToArray();
            const float d = 4f;
            Vector2 Project(Vector3 v) => center + new Vector2(v.X, v.Y) * (r * d / (d + v.Z));
            foreach (var f in s.Solid.Faces.OrderByDescending(f => pts[f.A].Z + pts[f.B].Z + pts[f.C].Z))
            {
                var n = (pts[f.B] - pts[f.A]).Cross(pts[f.C] - pts[f.A]).Normalized();
                if (n.Dot((pts[f.A] + pts[f.B] + pts[f.C]) / 3f) < 0)
                {
                    n = -n;
                }
                if (n.Z > 0)
                {
                    continue;  // facing away
                }
                var shade = 0.35f + 0.65f * Mathf.Max(0, n.Dot(light));
                Vector2[] poly = [Project(pts[f.A]), Project(pts[f.B]), Project(pts[f.C])];
                DrawColoredPolygon(poly, new Color(s.Color.R * shade, s.Color.G * shade, s.Color.B * shade, 0.11f));
                DrawPolyline([.. poly, poly[0]], s.Color.Lightened(0.2f) with { A = 0.2f }, 1.5f, true);
            }
        }
    }

    private sealed record Poly(Solid Solid, Vector2 Center, float Radius, Vector3 Spin, Color Color);

    private sealed record Face(int A, int B, int C);

    /// <summary>A regular solid with unit circumradius; faces are the vertex triples whose sides are all edges.</summary>
    private sealed class Solid
    {
        public Vector3[] Vertices { get; }
        public Face[] Faces { get; }

        private Solid(IEnumerable<Vector3> vertices)
        {
            Vertices = vertices.Select(v => v.Normalized()).ToArray();
            var edge = float.MaxValue;
            for (var i = 0; i < Vertices.Length; i++)
            {
                for (var j = i + 1; j < Vertices.Length; j++)
                {
                    edge = Mathf.Min(edge, Vertices[i].DistanceTo(Vertices[j]));
                }
            }
            bool IsEdge(int a, int b) => Mathf.Abs(Vertices[a].DistanceTo(Vertices[b]) - edge) < 1e-3f;
            var faces = new List<Face>();
            for (var a = 0; a < Vertices.Length; a++)
            {
                for (var b = a + 1; b < Vertices.Length; b++)
                {
                    for (var c = b + 1; c < Vertices.Length; c++)
                    {
                        if (IsEdge(a, b) && IsEdge(b, c) && IsEdge(a, c))
                        {
                            faces.Add(new Face(a, b, c));
                        }
                    }
                }
            }
            Faces = faces.ToArray();
        }

        private const float Phi = 1.618034f;

        public static readonly Solid Tetrahedron = new([new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1)]);

        public static readonly Solid Octahedron = new([new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)]);

        public static readonly Solid Icosahedron = new(
            from a in new[] { -1f, 1f }
            from b in new[] { -Phi, Phi }
            from v in new[] { new Vector3(0, a, b), new Vector3(a, b, 0), new Vector3(b, 0, a) }
            select v);
    }

    private const string Common = """
        shader_type canvas_item;
        uniform vec4 bg : source_color;
        uniform vec4 accent : source_color;
        uniform vec4 info : source_color;
        uniform vec4 line : source_color;
        uniform vec4 text : source_color;
        uniform float t;
        uniform vec2 size = vec2(1280.0, 800.0);

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float noise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
                       mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
        }

        """;

    /// <summary>Shader body per theme id; unknown ids get the Dark one.</summary>
    internal static string Body(string id) => id switch
    {
        // Pulsing dot grid: dots light up under a slow scan sweep and an expanding ring.
        "nightgrid" => """
            void fragment() {
                vec2 p = UV * size;
                vec2 cell = mod(p, 16.0);
                float dotm = step(cell.x, 1.5) * step(cell.y, 1.5);
                float period = 9.0;
                float ph = fract(t / period);
                float sy = ph * (size.y + 400.0) - 200.0;
                float band = exp(-pow((p.y - sy) / 70.0, 2.0));
                float edge = exp(-abs(p.y - sy) / 1.5);
                float k = floor(t / period);
                vec2 c = floor(vec2(hash(vec2(k, 7.0)), hash(vec2(k, 9.0))) * size / 128.0) * 128.0;
                float ring = exp(-pow((length(p - c) - ph * 900.0) / 40.0, 2.0)) * (1.0 - ph);
                vec3 col = mix(accent.rgb, info.rgb, clamp(band * 1.5, 0.0, 1.0));
                float a = dotm * (band * 0.9 + ring * 0.8) + edge * 0.08;
                COLOR = vec4(col, clamp(a, 0.0, 1.0) * 0.55);
            }
            """,
        // Phosphor rain: sparse columns of 3x5 glyphs falling, plus a slow rolling refresh band.
        "terminal" => """
            void fragment() {
                vec2 p = UV * size;
                vec2 cs = vec2(14.0, 18.0);
                vec2 id = floor(p / cs);
                float rows = size.y / cs.y + 24.0;
                float speed = mix(2.0, 6.0, hash(vec2(id.x, 1.0)));
                float head = fract((t * speed + hash(vec2(id.x, 2.0)) * 97.0) / rows) * rows - 12.0;
                float dist = head - id.y;
                float trail = (dist >= 0.0 && dist < 12.0) ? 1.0 - dist / 12.0 : 0.0;
                trail *= step(0.6, hash(vec2(id.x, 3.0)));
                vec2 g = floor(fract(p / cs) * vec2(5.0, 7.0));
                float inside = step(1.0, g.x) * step(g.x, 3.0) * step(1.0, g.y) * step(g.y, 5.0);
                float bit = step(0.45, hash(id * 7.0 + g + floor(t * 1.5 + hash(id) * 10.0)));
                float glyph = inside * bit * trail;
                float roll = exp(-pow((UV.y - fract(t / 7.0)) * 6.0, 2.0));
                float flicker = 0.88 + 0.12 * noise(vec2(t * 7.0, 0.0));
                COLOR = vec4(accent.rgb, (glyph * 0.2 + roll * 0.035) * flicker);
            }
            """,
        // Chunky pixel clouds in two parallax layers drifting right to left.
        "brick" => """
            void fragment() {
                float px = 6.0;
                vec2 q = floor(UV * size / px) * px;
                float far = noise(vec2(q.x * 0.004 + t * 0.012, q.y * 0.012)) + 0.5 * noise(vec2(q.x * 0.009 + t * 0.024, q.y * 0.025));
                float near = noise(vec2(q.x * 0.003 + t * 0.03 + 40.0, q.y * 0.009 + 11.0)) + 0.5 * noise(vec2(q.x * 0.007 + t * 0.06, q.y * 0.02 + 5.0));
                float below = noise(vec2(q.x * 0.003 + t * 0.03 + 40.0, (q.y - px * 2.0) * 0.009 + 11.0)) + 0.5 * noise(vec2(q.x * 0.007 + t * 0.06, (q.y - px * 2.0) * 0.02 + 5.0));
                float cf = step(1.0, far);
                float cn = step(1.05, near);
                float shadow = step(1.05, below) * (1.0 - cn);
                float fade = smoothstep(1.0, 0.15, UV.y);
                vec3 col = cn > 0.5 ? text.rgb : (shadow > 0.5 ? line.rgb : text.rgb);
                float a = (cn * 0.06 + shadow * 0.08 + cf * (1.0 - cn) * 0.03) * fade;
                COLOR = vec4(col, a);
            }
            """,
        // Mode-7 style floor: a perspective checkerboard scrolling and slowly turning below the horizon.
        "sixteen" => """
            void fragment() {
                float horizon = 0.56;
                float y = UV.y - horizon;
                float glow = exp(-abs(y) * 70.0) * 0.08;
                if (y <= 0.0) {
                    COLOR = vec4(accent.rgb, glow);
                } else {
                    float z = 0.12 / y;
                    vec2 w = vec2((UV.x - 0.5) * (size.x / size.y) * z * 3.0, z);
                    float ang = sin(t * 0.035) * 0.5;
                    w = vec2(w.x * cos(ang) - w.y * sin(ang), w.x * sin(ang) + w.y * cos(ang));
                    w += vec2(t * 0.15, t * 0.6);
                    float chk = mod(floor(w.x) + floor(w.y), 2.0);
                    vec2 gf = abs(fract(w) - 0.5);
                    float gl = smoothstep(0.46, 0.5, max(gf.x, gf.y));
                    float fog = smoothstep(0.02, 0.4, y);
                    COLOR = vec4(mix(accent.rgb, info.rgb, chk), (chk * 0.05 + gl * 0.1) * fog + glow);
                }
            }
            """,
        // Dot-matrix shimmer: a diagonal wave of darker LCD pixels and the odd stuck-pixel twinkle.
        "pocket" => """
            void fragment() {
                vec2 id = floor(UV * size / 4.0);
                float wave = pow(sin((id.x + id.y) * 0.03 - t * 0.8) * 0.5 + 0.5, 6.0);
                float wave2 = pow(sin((id.x - id.y * 0.6) * 0.012 + t * 0.35) * 0.5 + 0.5, 8.0);
                float tw = step(0.997, hash(id + floor(t * 2.0)));
                float a = floor((wave * 0.06 + wave2 * 0.04 + tw * 0.1) * 40.0) / 40.0;
                COLOR = vec4(line.rgb, a);
            }
            """,
        // Dark (and fallback): a few large, soft bokeh lights drifting slowly.
        _ => """
            void fragment() {
                vec2 p = UV * size;
                vec3 col = vec3(0.0);
                float a = 0.0;
                for (int i = 0; i < 9; i++) {
                    float fi = float(i);
                    vec2 c = vec2(hash(vec2(fi, 1.0)), hash(vec2(fi, 2.0))) * size;
                    c += vec2(sin(t * 0.05 * (1.0 + hash(vec2(fi, 3.0))) + fi),
                              cos(t * 0.04 * (1.0 + hash(vec2(fi, 4.0))) + fi * 1.7)) * size * 0.14;
                    float r = mix(110.0, 280.0, hash(vec2(fi, 5.0)));
                    float g = smoothstep(1.0, 0.0, length(p - c) / r);
                    g *= g;
                    col += mix(accent.rgb, info.rgb, step(0.65, hash(vec2(fi, 6.0)))) * g;
                    a += g;
                }
                COLOR = vec4(col / max(a, 0.001), clamp(a * 0.08, 0.0, 0.12));
            }
            """,
    };
}
