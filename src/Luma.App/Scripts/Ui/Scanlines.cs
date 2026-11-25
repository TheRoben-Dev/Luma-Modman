using Godot;

namespace Luma.App.Ui;

/// <summary>
/// CRT look over the whole screen: part of the terminal theme (light: scanlines, a little line
/// wobble and a rolling band), or the "CRT effect" setting for any theme, which adds tube curvature,
/// darkened edges, colour fringing and a gentle flicker. A single post-process shader over the
/// screen texture, updated at 30 Hz, frozen while a game runs and still (no wobble, roll or flicker)
/// when "Animations" is off.
/// </summary>
public partial class Scanlines : Control
{
    private const double Step = 1.0 / 30;

    private readonly ShaderMaterial _material;
    private readonly ColorRect _rect;
    private double _time;
    private double _acc;
    private bool _paused;

    /// <param name="level">0 = the terminal theme's light lines; 1-3 = the CRT setting's Subtle, Medium, Strong.</param>
    public Scanlines(int level = 0)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 100;
        level = Math.Clamp(level, 0, 3);
        (float strength, float curve, float chroma, float vignette) = level switch
        {
            0 => (0.6f, 0f, 0.4f, 0f),
            1 => (0.7f, 0.02f, 0.6f, 0.15f),
            2 => (1.0f, 0.035f, 1.0f, 0.25f),
            _ => (1.4f, 0.05f, 1.5f, 0.28f),
        };
        _material = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _material.SetShaderParameter("strength", strength);
        _material.SetShaderParameter("curve", curve);
        _material.SetShaderParameter("chroma", chroma);
        _material.SetShaderParameter("vignette", vignette);
        _rect = new ColorRect { Material = _material, MouseFilter = MouseFilterEnum.Ignore };
        _rect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_rect);
        Push();
    }

    /// <summary>True while a game runs or the app isn't focused: nothing moves or redraws.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            SetProcess(!value);
        }
    }

    public override void _Process(double delta)
    {
        if (!Anim.Enabled)
        {
            if (_time != 0)
            {
                _time = 0;
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
        _material.SetShaderParameter("t", (float)(_time % 1000));
        _material.SetShaderParameter("motion", Anim.Enabled ? 1f : 0f);
        _rect.QueueRedraw();
    }

    private const string Code = """
        shader_type canvas_item;
        uniform sampler2D screen : hint_screen_texture, filter_linear;
        uniform float t;
        uniform float motion = 1.0;
        uniform float strength = 1.0;
        uniform float curve = 0.0;
        uniform float chroma = 0.0;
        uniform float vignette = 0.0;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

        void fragment() {
            vec2 size = 1.0 / SCREEN_PIXEL_SIZE;
            float scale = max(1.0, floor(size.y / 800.0 + 0.5));  // UI pixels, so 4K TVs get the same lines
            vec2 c = SCREEN_UV * 2.0 - 1.0;
            c *= 1.0 + curve * (c.yx * c.yx);  // barrel: the tube bulges toward you
            vec2 uv = c * 0.5 + 0.5;
            float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            float row = floor(uv.y * size.y / scale);
            // Horizontal image shift: a slow wobble down the screen plus the odd short tear.
            float wobble = sin(row * 0.045 + t * 2.3) * 0.35 + sin(row * 0.31 - t * 7.1) * 0.15;
            float k = floor(t * 3.0);
            float tear = step(0.985, hash(vec2(k, 0.0))) * exp(-pow((uv.y - hash(vec2(k, 1.0))) * 40.0, 2.0)) * 3.0;
            float shift = (wobble + tear) * motion * strength * scale / size.x;
            vec2 s = vec2(uv.x + shift, uv.y);
            float ca = chroma * scale / size.x;
            vec3 col = vec3(texture(screen, s + vec2(ca, 0.0)).r, texture(screen, s).g, texture(screen, s - vec2(ca, 0.0)).b);
            float lines = mod(uv.y * size.y / scale, 3.0) < 1.0 ? 1.0 - 0.18 * strength : 1.0;
            float band = exp(-pow((fract(t / 8.0) * 1.4 - 0.2 - uv.y) * 7.0, 2.0)) * 0.035 * strength * motion;
            float flicker = 1.0 - 0.02 * strength * motion * hash(vec2(floor(t * 30.0), 3.0));
            float vig = clamp(1.0 - vignette * dot(c * c, c * c) - vignette * 0.4 * dot(c, c), 0.0, 1.0);
            COLOR = vec4((col * lines * flicker + band) * vig * inside, 1.0);
        }
        """;
}
