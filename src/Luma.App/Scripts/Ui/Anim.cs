using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Short, consistent motion. Everything here is 80-220 ms with ease-out curves so the UI feels
/// responsive on a controller, and everything turns off with "Reduce motion" in Settings.
/// </summary>
public static class Anim
{
    public const double Fast = 0.09;
    public const double Normal = 0.16;
    public const double Slow = 0.22;

    /// <summary>Set from AppSettings at startup and when the setting changes.</summary>
    public static bool Enabled { get; set; } = true;

    private static Tween? Tween(Node n, Tween.TransitionType trans = Godot.Tween.TransitionType.Cubic)
    {
        if (!Enabled || !n.IsInsideTree())
        {
            return null;
        }
        return n.CreateTween().SetEase(Godot.Tween.EaseType.Out).SetTrans(trans);
    }

    /// <summary>Scales the control up a little while it has focus, and dips on press.</summary>
    public static void FocusPop(Control c, float scale = 1.05f, bool press = true)
    {
        void Center() => c.PivotOffset = c.Size / 2;
        c.Resized += Center;
        Tween? running = null;
        void To(float s, double t, Tween.TransitionType trans = Godot.Tween.TransitionType.Back)
        {
            running?.Kill();
            if (!Enabled)
            {
                c.Scale = Vector2.One;
                return;
            }
            Center();
            running = Tween(c, trans);
            running?.TweenProperty(c, "scale", new Vector2(s, s), t);
        }
        c.FocusEntered += () => To(scale, Normal);
        c.FocusExited += () => To(1f, Fast, Godot.Tween.TransitionType.Cubic);
        if (press && c is BaseButton b)
        {
            b.ButtonDown += () => To(scale * 0.95f, Fast * 0.6, Godot.Tween.TransitionType.Cubic);
            b.ButtonUp += () => To(c.HasFocus() ? scale : 1f, Fast);
        }
    }

    /// <summary>Press feedback for list rows: a quick brightness flash (no scale, keeps text crisp).</summary>
    public static void PressFlash(BaseButton b)
    {
        b.ButtonDown += () =>
        {
            FocusHighlight.Instance?.Pulse();
            var t = Tween(b);
            if (t == null)
            {
                return;
            }
            b.Modulate = new Color(1.25f, 1.25f, 1.35f);
            t.TweenProperty(b, "modulate", Colors.White, Normal);
        };
    }

    /// <summary>Page enters: fade in while sliding from the side (dir -1 from left, +1 from right).</summary>
    public static void Enter(Control c, int dir = 1, double delay = 0)
    {
        var t = Tween(c);
        if (t == null)
        {
            return;
        }
        c.Modulate = c.Modulate with { A = 0 };
        var offset = new Vector2(36 * dir, 0);
        c.Position += offset;
        var target = c.Position - offset;
        t.SetParallel();
        t.TweenProperty(c, "modulate:a", 1f, Slow).SetDelay(delay);
        t.TweenProperty(c, "position", target, Slow).SetDelay(delay);
        // Moving a full-rect page pins its offsets to the size it had at that moment (a page that was
        // still laying out can be far taller than the screen, and then never scrolls): snap it back.
        t.Chain().TweenCallback(Callable.From(() => c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect)));
    }

    /// <summary>
    /// Children of a container fade and rise in one after another. Uses modulate only (plus a
    /// one-off position nudge after layout), so containers keep control of the layout.
    /// </summary>
    public static void Stagger(IReadOnlyList<Control> items, double step = 0.025, int max = 24)
    {
        if (!Enabled)
        {
            return;
        }
        for (var i = 0; i < items.Count; i++)
        {
            var c = items[i];
            var delay = Math.Min(i, max) * step;
            c.Modulate = c.Modulate with { A = 0 };
            var t = Tween(c);
            t?.TweenProperty(c, "modulate:a", 1f, Slow).SetDelay(delay);
        }
    }

    /// <summary>Page leaves: fade out while sliding away (dir -1 to the left, +1 to the right), then free it.</summary>
    public static void Exit(Control c, int dir)
    {
        c.MouseFilter = Control.MouseFilterEnum.Ignore;
        var t = Tween(c, Godot.Tween.TransitionType.Cubic);
        if (t == null)
        {
            c.QueueFree();
            return;
        }
        t.SetEase(Godot.Tween.EaseType.In).SetParallel();
        t.TweenProperty(c, "modulate:a", 0f, Normal);
        t.TweenProperty(c, "position", c.Position + new Vector2(28 * dir, 0), Normal);
        t.Chain().TweenCallback(Callable.From(c.QueueFree));
    }

    public static void FadeIn(CanvasItem c, double time = Slow)
    {
        var t = Tween(c);
        if (t == null)
        {
            return;
        }
        c.Modulate = c.Modulate with { A = 0 };
        t.TweenProperty(c, "modulate:a", 1f, time);
    }
}
