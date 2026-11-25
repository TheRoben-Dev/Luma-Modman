using Godot;

namespace Luma.App.Ui.Poly;

/// <summary>
/// A low-resolution 3D view: its own world in a SubViewport rendered at <see cref="Resolution"/> of the
/// area it covers, shown with nearest filtering through the dither / 15-bit post pass. Animation
/// time advances in 30 Hz steps (the era's frame rate, and cheap with low-processor mode); it
/// freezes while a game runs, when the window is in the background, and with reduce motion.
/// </summary>
public abstract partial class PolyStage : Control
{
    protected readonly SubViewport View;
    protected readonly Node3D World = new();
    protected readonly Camera3D Camera = new() { Fov = 32, Near = 0.1f, Far = 80 };
    protected readonly Ps1.Look Look = new();
    protected readonly TextureRect Screen;
    private readonly ShaderMaterial _post;
    private double _acc;
    private bool _paused;
    private bool _held;

    /// <summary>Animation clock in seconds (steps at 30 Hz; frozen with reduce motion).</summary>
    protected float T { get; private set; } = 12f;

    /// <summary>Internal resolution as a fraction of the covered area.</summary>
    protected float Resolution { get; init; } = 0.34f;

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            UpdateMode();
        }
    }

    protected PolyStage(bool transparent)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        View = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = transparent,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Size = new Vector2I(320, 200),
        };
        AddChild(View);
        View.AddChild(World);
        View.AddChild(Camera);
        Camera.Current = true;
        _post = new ShaderMaterial { Shader = Ps1.Post };
        Screen = new TextureRect
        {
            Texture = View.GetTexture(),
            TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Material = _post,
        };
        AddChild(Screen);
    }

    /// <summary>Where the 3D view is drawn, in this control's coordinates (default: all of it).</summary>
    protected virtual Rect2 ViewRect => new(Vector2.Zero, Size);

    private void UpdateMode() =>
        View.RenderTargetUpdateMode = _paused || _held ? SubViewport.UpdateMode.Disabled : SubViewport.UpdateMode.Always;

    public override void _Process(double delta)
    {
        // Nothing renders while a game runs.
        var held = Main.Instance?.GameRunning == true;
        if (held != _held)
        {
            _held = held;
            UpdateMode();
        }
        Fit();
        if (_paused || _held)
        {
            return;
        }
        if (Anim.Enabled)
        {
            _acc += delta;
            if (_acc >= 1.0 / 30)
            {
                var step = (float)(Math.Floor(_acc * 30) / 30);
                _acc -= step;
                T += step;
                Tick(step);
            }
        }
        Follow();
    }

    /// <summary>Keeps the viewport size and the snap grid matched to the area shown.</summary>
    private void Fit()
    {
        var r = ViewRect;
        if (r.Size.X < 4 || r.Size.Y < 4)
        {
            Screen.Visible = false;
            return;
        }
        Screen.Visible = true;
        Screen.Position = r.Position;
        Screen.Size = r.Size;
        var px = new Vector2I(Math.Max(16, (int)(r.Size.X * Resolution)), Math.Max(16, (int)(r.Size.Y * Resolution)));
        if (px != View.Size)
        {
            View.Size = px;
            Look.SnapRes = px;
            Look.ApplyAll();
            _post.SetShaderParameter("res", (Vector2)px);
        }
    }

    /// <summary>Animation step at 30 Hz (dt = seconds since the last step).</summary>
    protected abstract void Tick(float dt);

    /// <summary>Every frame: things that must track the 2D UI exactly (positions of tiles).</summary>
    protected virtual void Follow()
    {
    }

    /// <summary>Screen point (this control's coordinates) to a world point at <paramref name="depth"/> from the camera.</summary>
    protected Vector3 ToWorld(Vector2 local, float depth)
    {
        var r = ViewRect;
        var p = (local - r.Position) / r.Size * (Vector2)View.Size;
        return Camera.ProjectPosition(p, depth);
    }

    /// <summary>World units per UI pixel at <paramref name="depth"/> from the camera.</summary>
    protected float UnitsPerPixel(float depth) =>
        2 * depth * Mathf.Tan(Mathf.DegToRad(Camera.Fov) / 2) / Math.Max(1, ViewRect.Size.Y);

    /// <summary>Ray from the camera through a point of this control.</summary>
    protected (Vector3 From, Vector3 Dir) Ray(Vector2 local)
    {
        var r = ViewRect;
        var p = (local - r.Position) / r.Size * (Vector2)View.Size;
        return (Camera.ProjectRayOrigin(p), Camera.ProjectRayNormal(p));
    }
}
