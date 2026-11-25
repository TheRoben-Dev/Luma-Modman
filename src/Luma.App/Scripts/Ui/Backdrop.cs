using Godot;

namespace Luma.App.Ui;

/// <summary>Dims and blurs whatever is behind an overlay card. Click/tap closes the overlay.</summary>
public partial class Backdrop : ColorRect
{
    private const string Shader = @"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform vec4 tint : source_color = vec4(0.0, 0.0, 0.0, 0.72);
uniform float radius = 2.0;
uniform float strength = 1.0;
void fragment() {
    vec2 px = SCREEN_PIXEL_SIZE * radius * strength;
    vec3 c = vec3(0.0);
    float w = 0.0;
    for (int x = -2; x <= 2; x++) {
        for (int y = -2; y <= 2; y++) {
            float k = 1.0 / (1.0 + float(x * x + y * y));
            c += texture(screen_tex, SCREEN_UV + vec2(float(x), float(y)) * px).rgb * k;
            w += k;
        }
    }
    c /= w;
    COLOR = vec4(mix(c, tint.rgb, tint.a * strength), 1.0);
}";

    private Tween? _tween;
    public event Action? Clicked;

    public Backdrop()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Material = new ShaderMaterial { Shader = new Shader { Code = Shader } };
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Clicked?.Invoke();
            }
        };
    }

    private void SetStrength(float v) => ((ShaderMaterial)Material).SetShaderParameter("strength", v);

    public void FadeTo(bool on)
    {
        _tween?.Kill();
        if (on)
        {
            Visible = true;
        }
        if (!Anim.Enabled || !IsInsideTree())
        {
            SetStrength(on ? 1 : 0);
            Visible = on;
            return;
        }
        var from = on ? 0f : 1f;
        SetStrength(from);
        _tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenMethod(Callable.From<float>(SetStrength), from, on ? 1f : 0f, on ? Anim.Slow : Anim.Normal);
        if (!on)
        {
            _tween.TweenCallback(Callable.From(() => Visible = false));
        }
    }
}
