using Godot;

namespace Luma.App.Ui.Poly;

/// <summary>
/// Late-90s console 3D in a modern renderer, for the "32-bit 3D" theme:
/// - vertices snap to the low internal resolution (the wobble of fixed-point geometry),
/// - textures are mapped affinely (screen-linear, no perspective correction) and sampled nearest,
/// - lighting is per vertex (Gouraud) and so is fog, which fades into the background colour,
/// - the scene renders at a fraction of the screen and is shown with nearest filtering, through a
///   post pass that applies a 4x4 ordered dither and cuts colour to 15 bits (5 per channel).
/// Everything is original geometry built in code.
/// </summary>
public static class Ps1
{
    private const string SpatialCommon = """
        uniform vec4 albedo : source_color = vec4(1.0);
        uniform sampler2D tex : source_color, filter_nearest, repeat_enable;
        uniform bool use_tex = false;
        uniform vec2 uv_offset = vec2(0.0);
        uniform vec2 uv_scale = vec2(1.0);
        uniform vec3 emission : source_color = vec3(0.0);
        uniform float alpha = 1.0;
        uniform vec2 snap_res = vec2(320.0, 240.0);
        uniform vec3 light_dir = vec3(-0.45, -0.75, -0.5);
        uniform vec3 light_color : source_color = vec3(1.0, 0.97, 0.9);
        uniform vec3 ambient : source_color = vec3(0.32, 0.33, 0.42);
        uniform vec4 fog_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
        uniform float fog_near = 9.0;
        uniform float fog_far = 30.0;
        uniform float unlit = 0.0;
        varying vec3 v_light;
        varying float v_fog;
        varying vec3 v_affine;

        void vertex() {
            vec3 n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
            vec3 l = normalize(-light_dir);
            // Two-sided Gouraud: faces seen from behind (no culling) still get light.
            float d = max(dot(n, l), 0.0) * 0.85 + max(dot(-n, l), 0.0) * 0.25;
            v_light = mix(ambient + light_color * d, vec3(1.0), unlit);
            vec4 view = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
            v_fog = clamp((-view.z - fog_near) / (fog_far - fog_near), 0.0, 1.0);
            vec4 clip = PROJECTION_MATRIX * view;
            // Snap to the internal pixel grid: low-precision vertex positions wobble as things move.
            vec2 grid = snap_res * 0.5;
            clip.xy = round(clip.xy / clip.w * grid) / grid * clip.w;
            POSITION = clip;
            // Affine texture mapping: interpolate uv*w and w, divide per pixel -> screen-linear uv.
            v_affine = vec3((UV * uv_scale + uv_offset) * clip.w, clip.w);
        }

        """;

    /// <summary>Fragment body shared by both variants: leaves the lit, fogged colour in <c>c</c>.</summary>
    private const string Shade = """
            vec4 c = albedo * COLOR;
            if (use_tex) {
                c *= texture(tex, v_affine.xy / v_affine.z);
            }
            c.rgb = mix(c.rgb * v_light + emission, fog_color.rgb, v_fog);
        """;

    private static Shader? _opaque;
    private static Shader? _translucent;
    private static Shader? _post;

    private static Shader Opaque => _opaque ??= new Shader
    {
        Code = "shader_type spatial;\nrender_mode unshaded, cull_disabled, depth_draw_opaque, specular_disabled, shadows_disabled;\n" +
               SpatialCommon + "\nvoid fragment() {\n" + Shade + """
                if (c.a < 0.5) { discard; }
                ALBEDO = c.rgb;
            }
            """,
    };

    private static Shader? _translucentFront;

    /// <summary>Drops the shared shaders at quit, while .NET is still up (see Main._ExitTree).</summary>
    public static void ReleaseShared()
    {
        foreach (var s in new[] { _opaque, _translucent, _translucentFront, _post })
        {
            s?.Dispose();
        }
        _opaque = _translucent = _translucentFront = _post = null;
    }

    /// <summary>
    /// Semi-transparent surfaces (the era's translucent plastic and glass), blended, no depth write.
    /// Drawn in two passes, far side then near side, so a shell never shows its faces in a random
    /// order (which flickered as parts turned).
    /// </summary>
    private static Shader Translucent => _translucent ??= TranslucentPass("cull_front");

    private static Shader TranslucentFront => _translucentFront ??= TranslucentPass("cull_back");

    private static Shader TranslucentPass(string cull) => new()
    {
        Code = $"shader_type spatial;\nrender_mode unshaded, {cull}, depth_draw_never, blend_mix, specular_disabled, shadows_disabled;\n" +
               SpatialCommon + "\nvoid fragment() {\n" + Shade + """
                ALBEDO = c.rgb;
                ALPHA = c.a * alpha;
            }
            """,
    };

    /// <summary>Ordered dither + 15-bit colour on the low-resolution frame (shown with nearest filtering).</summary>
    public static Shader Post => _post ??= new Shader
    {
        Code = """
            shader_type canvas_item;
            uniform vec2 res = vec2(320.0, 240.0);
            uniform float levels = 31.0;
            uniform float strength = 1.0;
            const float BAYER[16] = float[](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);

            void fragment() {
                vec4 c = texture(TEXTURE, UV);
                ivec2 p = ivec2(floor(UV * res));
                float b = (BAYER[(p.x & 3) + (p.y & 3) * 4] / 16.0 - 0.47) * 1.6 / levels;
                vec3 q = floor((c.rgb + b) * levels + 0.5) / levels;
                c.rgb = mix(c.rgb, q, strength);
                COLOR = c * COLOR;
            }
            """,
    };

    /// <summary>Scene-wide values every material of a stage shares.</summary>
    public sealed class Look
    {
        public Vector2 SnapRes = new(320, 240);
        public Color Fog = Colors.Black;
        public float FogNear = 9;
        public float FogFar = 30;
        public Vector3 LightDir = new(-0.45f, -0.75f, -0.5f);
        public Color Ambient = new(0.32f, 0.33f, 0.42f);
        public readonly List<ShaderMaterial> Materials = new();

        public void Apply(ShaderMaterial m)
        {
            m.Param("snap_res", SnapRes);
            m.Param("fog_color", Fog);
            m.Param("fog_near", FogNear);
            m.Param("fog_far", FogFar);
            m.Param("light_dir", LightDir);
            m.Param("ambient", Ambient);
        }

        public void ApplyAll()
        {
            foreach (var m in Materials)
            {
                Apply(m);
            }
        }

        public ShaderMaterial Mat(Color albedo, Texture2D? tex = null, float alpha = 1, Color? emission = null, bool unlit = false)
        {
            var m = new ShaderMaterial { Shader = alpha < 1 ? Translucent : Opaque };
            if (alpha < 1)
            {
                m.NextPass = new ShaderMaterial { Shader = TranslucentFront };
            }
            m.Param("albedo", albedo);
            if (tex != null)
            {
                m.Param("tex", tex);
                m.Param("use_tex", true);
            }
            m.Param("alpha", alpha);
            m.Param("emission", emission ?? Colors.Black);
            m.Param("unlit", unlit ? 1f : 0f);
            Apply(m);
            Materials.Add(m);
            return m;
        }
    }

    /// <summary>Sets a shader parameter on a material and its extra passes.</summary>
    public static void Param(this ShaderMaterial m, StringName name, Variant value)
    {
        for (Material? x = m; x is ShaderMaterial sm; x = sm.NextPass)
        {
            sm.SetShaderParameter(name, value);
        }
    }

    // ---- Mesh kit -------------------------------------------------------------------------------

    public static MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rotDeg = null, Vector3? scale = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        if (rotDeg is { } r)
        {
            mi.RotationDegrees = r;
        }
        if (scale is { } s)
        {
            mi.Scale = s;
        }
        parent.AddChild(mi);
        return mi;
    }

    public static BoxMesh Box(float x, float y, float z) => new() { Size = new Vector3(x, y, z) };

    public static CylinderMesh Cyl(float rTop, float rBottom, float h, int seg = 10) =>
        new() { TopRadius = rTop, BottomRadius = rBottom, Height = h, RadialSegments = seg, Rings = 0 };

    public static SphereMesh Sphere(float r, int seg = 8, int rings = 5) =>
        new() { Radius = r, Height = r * 2, RadialSegments = seg, Rings = rings };

    public static TorusMesh Torus(float inner, float outer, int rings = 10, int seg = 5) =>
        new() { InnerRadius = inner, OuterRadius = outer, Rings = rings, RingSegments = seg };

    public static PrismMesh Prism(float x, float y, float z) => new() { Size = new Vector3(x, y, z) };

    /// <summary>A flat 2D outline (counter-clockwise, y up) extruded to <paramref name="depth"/>; front face uv spans the outline's bounds.</summary>
    public static ArrayMesh Extrude(Vector2[] outline, float depth)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var min = new Vector2(outline.Min(p => p.X), outline.Min(p => p.Y));
        var max = new Vector2(outline.Max(p => p.X), outline.Max(p => p.Y));
        var size = max - min;
        var tris = Geometry2D.TriangulatePolygon(outline);
        var hz = depth / 2;
        void V(Vector3 p, Vector3 n, Vector2 uv)
        {
            st.SetNormal(n);
            st.SetUV(uv);
            st.AddVertex(p);
        }
        Vector2 Uv(Vector2 p) => new((p.X - min.X) / size.X, 1 - (p.Y - min.Y) / size.Y);
        for (var i = 0; i < tris.Length; i += 3)
        {
            for (var k = 0; k < 3; k++)
            {
                var p = outline[tris[i + k]];
                V(new Vector3(p.X, p.Y, hz), Vector3.Back, Uv(p));
            }
            for (var k = 2; k >= 0; k--)
            {
                var p = outline[tris[i + k]];
                V(new Vector3(p.X, p.Y, -hz), Vector3.Forward, Uv(p));
            }
        }
        for (var i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            var e = (b - a).Normalized();
            var n = new Vector3(e.Y, -e.X, 0);
            var uv = new Vector2(0.02f, 0.02f);
            V(new Vector3(a.X, a.Y, hz), n, uv);
            V(new Vector3(b.X, b.Y, hz), n, uv);
            V(new Vector3(b.X, b.Y, -hz), n, uv);
            V(new Vector3(a.X, a.Y, hz), n, uv);
            V(new Vector3(b.X, b.Y, -hz), n, uv);
            V(new Vector3(a.X, a.Y, -hz), n, uv);
        }
        return st.Commit();
    }

    /// <summary>A flat quad facing +Z, centred, with uv 0..1 (top-left = 0,0).</summary>
    public static ArrayMesh Quad(float w, float h)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var hw = w / 2;
        var hh = h / 2;
        void V(float x, float y, float u, float v)
        {
            st.SetNormal(Vector3.Back);
            st.SetUV(new Vector2(u, v));
            st.AddVertex(new Vector3(x, y, 0));
        }
        V(-hw, hh, 0, 0); V(hw, hh, 1, 0); V(hw, -hh, 1, 1);
        V(-hw, hh, 0, 0); V(hw, -hh, 1, 1); V(-hw, -hh, 0, 1);
        return st.Commit();
    }

    /// <summary>A flat grid of large quads (big quads show the affine texture warp best).</summary>
    public static ArrayMesh Floor(float size, int cells, float uvRepeat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var step = size / cells;
        for (var z = 0; z < cells; z++)
        {
            for (var x = 0; x < cells; x++)
            {
                var x0 = -size / 2 + x * step;
                var z0 = -size / 2 + z * step;
                Vector2 Uv(float px, float pz) => new((px + size / 2) / size * uvRepeat, (pz + size / 2) / size * uvRepeat);
                void V(float px, float pz)
                {
                    st.SetNormal(Vector3.Up);
                    st.SetUV(Uv(px, pz));
                    st.AddVertex(new Vector3(px, 0, pz));
                }
                V(x0, z0); V(x0 + step, z0); V(x0 + step, z0 + step);
                V(x0, z0); V(x0 + step, z0 + step); V(x0, z0 + step);
            }
        }
        return st.Commit();
    }

    // ---- Small textures ----------------------------------------------------------------------------

    public static ImageTexture Tex(Image img) => ImageTexture.CreateFromImage(img);

    public static ImageTexture GridTexture(Color line, Color fill, int size = 32)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(fill);
        for (var i = 0; i < size; i++)
        {
            img.SetPixel(i, 0, line);
            img.SetPixel(0, i, line);
            img.SetPixel(i, size / 2, line with { A = line.A * 0.4f });
            img.SetPixel(size / 2, i, line with { A = line.A * 0.4f });
        }
        return Tex(img);
    }

    /// <summary>TV static: random greys, regenerated by the caller for movement.</summary>
    public static Image Noise(int w, int h, int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var v = rng.Randf();
                img.SetPixel(x, y, new Color(v, v, v));
            }
        }
        return img;
    }

    /// <summary>Generic colour bars (seven vertical bars and a darker strip), the universal "TV is on" image.</summary>
    public static ImageTexture ColorBars()
    {
        Color[] bars = [new("c0c0c0"), new("c0c000"), new("00c0c0"), new("00c000"), new("c000c0"), new("c00000"), new("0000c0")];
        var img = Image.CreateEmpty(28, 20, false, Image.Format.Rgba8);
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 28; x++)
            {
                var c = bars[x / 4];
                img.SetPixel(x, y, y < 15 ? c : (x / 4 % 2 == 0 ? new Color("101010") : bars[6 - x / 4].Darkened(0.4f)));
            }
        }
        return Tex(img);
    }

    /// <summary>Rainbow sheen for disc surfaces.</summary>
    public static ImageTexture DiscTexture()
    {
        var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var d = new Vector2(x - 15.5f, y - 15.5f);
                var r = d.Length() / 16;
                var a = Mathf.Atan2(d.Y, d.X);
                var c = r < 0.22f ? new Color("d8dce4") : Color.FromHsv(Mathf.PosMod(a / Mathf.Tau + r * 0.6f, 1), 0.35f, 0.95f);
                if (r is > 0.2f and < 0.26f)
                {
                    c = new Color("9aa0aa");
                }
                img.SetPixel(x, y, c);
            }
        }
        return Tex(img);
    }

    /// <summary>A tiny pixel critter for the pet egg's screen (two frames side by side, 8x8 each).</summary>
    public static ImageTexture Critter(Color on, Color off, bool hatched)
    {
        string[] a = hatched
            ? ["........", "..#..#..", ".######.", "##.##.##", "########", ".######.", ".#....#.", "#......#"]
            : ["........", "...##...", "..####..", ".##..##.", ".######.", ".######.", "..####..", "........"];
        string[] b = hatched
            ? ["..#..#..", ".######.", "##.##.##", "########", ".######.", "..#..#..", ".#....#.", "........"]
            : ["........", "........", "...##...", "..####..", ".##..##.", ".######.", ".######.", "..####.."];
        var img = Image.CreateEmpty(16, 8, false, Image.Format.Rgba8);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                img.SetPixel(x, y, a[y][x] == '#' ? on : off);
                img.SetPixel(x + 8, y, b[y][x] == '#' ? on : off);
            }
        }
        return Tex(img);
    }

    /// <summary>Spring-ish ease used by tap animations: 0 -> 1 with a little overshoot.</summary>
    public static float Overshoot(float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        const float c = 1.9f;
        return 1 + (c + 1) * Mathf.Pow(t - 1, 3) + c * Mathf.Pow(t - 1, 2);
    }
}
