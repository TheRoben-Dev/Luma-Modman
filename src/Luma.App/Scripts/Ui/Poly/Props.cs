using Godot;
using static Luma.App.Ui.Poly.Ps1;

namespace Luma.App.Ui.Poly;

/// <summary>
/// One decorative low-poly object: generic late-90s things (no real products, logos or
/// characters). Each has a gentle idle motion so it reads as live 3D, a bop on tap, and some have
/// their own tap animation (a lid opens, a screen turns on).
/// </summary>
public sealed class Prop
{
    public enum Bop { Hop, Wiggle, Spin }

    public readonly Node3D Root = new();
    public readonly Node3D Body = new();
    public string Name = "";
    public float Radius = 0.75f;
    public Bop Style = Bop.Hop;
    public Vector3 BaseRot;          // degrees
    public float Sway = 14;          // idle yaw sway, degrees
    public float SwaySpeed = 0.45f;
    public float SpinSpeed;          // continuous yaw, degrees per second
    public float BobAmp = 0.05f;
    public float Phase;
    public float TapAge = 99;
    public int Taps;
    public float Open;               // 0..1 state some props animate toward (lids, flips)
    public float OpenTarget;
    public Action<Prop, float, float>? Animate;  // (prop, T, dt) for moving parts

    public Prop()
    {
        Root.AddChild(Body);
    }

    public void Tap()
    {
        TapAge = 0;
        Taps++;
    }

    public void Update(float t, float dt)
    {
        TapAge += dt;
        Open = Mathf.MoveToward(Open, OpenTarget, dt * 3.2f);
        var yaw = BaseRot.Y + Mathf.Sin(t * SwaySpeed + Phase) * Sway + SpinSpeed * t;
        var rot = new Vector3(BaseRot.X + Mathf.Sin(t * 0.37f + Phase * 2) * 3, yaw, BaseRot.Z);
        var pos = new Vector3(0, Mathf.Sin(t * 1.1f + Phase) * BobAmp, 0);
        var scale = Vector3.One;
        const float BopTime = 0.7f;
        if (TapAge < BopTime)
        {
            var a = TapAge / BopTime;
            switch (Style)
            {
                case Bop.Hop:
                    pos.Y += Mathf.Sin(a * Mathf.Pi) * 0.55f;
                    var squash = a < 0.15f ? 1 - a / 0.15f * 0.25f : 0.75f + 0.25f * Overshoot((a - 0.15f) / 0.85f);
                    scale = new Vector3(2 - squash, squash, 2 - squash);
                    rot.Y += 360 * Overshoot(a) * 0.5f;
                    break;
                case Bop.Wiggle:
                    rot.Z += Mathf.Sin(a * 34) * 18 * (1 - a);
                    pos.Y += Mathf.Sin(a * Mathf.Pi) * 0.15f;
                    break;
                case Bop.Spin:
                    rot.Y += 720 * Overshoot(a);
                    scale = Vector3.One * (1 + Mathf.Sin(a * Mathf.Pi) * 0.25f);
                    break;
            }
        }
        Body.Position = pos;
        Body.RotationDegrees = rot;
        Body.Scale = scale;
        Animate?.Invoke(this, t, dt);
    }
}

/// <summary>The prop catalogue. Sizes are about one unit tall.</summary>
public static class Props
{
    public static readonly Func<Look, Prop>[] All =
    [
        Tv, Tape, JewelCase, DiscPlayer, MemoryCard, PetEgg, Handheld, FlipPhone, LavaLamp, Floppy, Crystal, Mascot, PowerRing,
    ];

    private static Color C(string hex) => new(hex);

    /// <summary>Chunky CRT television with rabbit-ear antenna. Tap: flickers on with static, then colour bars.</summary>
    public static Prop Tv(Look l)
    {
        var p = new Prop { Name = "tv", Radius = 0.8f, BaseRot = new Vector3(0, 18, 0), Style = Prop.Bop.Wiggle };
        var b = p.Body;
        var plastic = l.Mat(C("5a5d68"));
        Part(b, Box(1.2f, 0.95f, 0.9f), plastic, new Vector3(0, 0, -0.05f));
        Part(b, Box(0.9f, 0.7f, 0.25f), l.Mat(C("4a4d57")), new Vector3(0, 0, -0.6f));  // tube back
        Part(b, Box(1.24f, 0.08f, 0.94f), l.Mat(C("43464f")), new Vector3(0, -0.5f, -0.05f));
        var bezel = l.Mat(C("2b2d33"));
        Part(b, Box(0.9f, 0.7f, 0.04f), bezel, new Vector3(-0.1f, 0.03f, 0.41f));
        var noise = Tex(Noise(28, 20, 1));
        var bars = ColorBars();
        var screen = l.Mat(C("ffffff"), noise, emission: C("000000"));
        screen.Param("albedo", C("1c2422"));
        screen.Param("use_tex", false);
        Part(b, Box(0.8f, 0.6f, 0.03f), screen, new Vector3(-0.1f, 0.03f, 0.44f));
        var knob = l.Mat(C("b0b3bb"));
        Part(b, Cyl(0.05f, 0.05f, 0.05f, 8), knob, new Vector3(0.5f, 0.2f, 0.42f), new Vector3(90, 0, 0));
        Part(b, Cyl(0.05f, 0.05f, 0.05f, 8), knob, new Vector3(0.5f, 0.02f, 0.42f), new Vector3(90, 0, 0));
        var ant = l.Mat(C("c8ccd4"));
        Part(b, Cyl(0.012f, 0.012f, 0.8f, 4), ant, new Vector3(-0.18f, 0.8f, -0.1f), new Vector3(0, 0, 28));
        Part(b, Cyl(0.012f, 0.012f, 0.8f, 4), ant, new Vector3(0.18f, 0.8f, -0.1f), new Vector3(0, 0, -28));
        Part(b, Sphere(0.07f, 6, 3), l.Mat(C("3a3c44")), new Vector3(0, 0.5f, -0.1f));
        var seed = 2;
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            if (a < 0.2f)
            {
                screen.Param("use_tex", false);
                screen.Param("albedo", C("ffffff"));
                screen.Param("emission", C("aaaaaa"));
            }
            else if (a < 1.3f)
            {
                screen.Param("tex", noise);
                screen.Param("use_tex", true);
                screen.Param("albedo", C("ffffff"));
                screen.Param("emission", C("303030"));
                noise.Update(Noise(28, 20, seed++));
            }
            else if (a < 3.6f)
            {
                screen.Param("tex", bars);
                screen.Param("use_tex", true);
                screen.Param("emission", C("202020"));
                // Rolling hold: the picture slips vertically for a moment.
                screen.Param("uv_offset", new Vector2(0, a < 1.8f ? (a - 1.3f) * 2 : 0));
            }
            else
            {
                screen.Param("use_tex", false);
                screen.Param("albedo", C("1c2422"));
                screen.Param("emission", C("000000"));
                screen.Param("uv_offset", Vector2.Zero);
            }
        };
        return p;
    }

    /// <summary>Video tape half out of its sleeve. Tap: the tape ejects, flips over and slides back.</summary>
    public static Prop Tape(Look l)
    {
        var p = new Prop { Name = "tape", Radius = 0.7f, BaseRot = new Vector3(-12, -25, 8), Sway = 25, Style = Prop.Bop.Hop };
        var b = p.Body;
        var sleeve = l.Mat(C("e8e2d0"));
        Part(b, Box(1.1f, 0.66f, 0.02f), sleeve, new Vector3(0, 0, 0.1f));
        Part(b, Box(1.1f, 0.66f, 0.02f), sleeve, new Vector3(0, 0, -0.1f));
        Part(b, Box(1.1f, 0.02f, 0.2f), sleeve, new Vector3(0, -0.33f, 0));
        Part(b, Box(1.1f, 0.18f, 0.005f), l.Mat(C("d23c3c")), new Vector3(0, 0.12f, 0.113f));
        var tape = new Node3D();
        b.AddChild(tape);
        Part(tape, Box(1.0f, 0.6f, 0.16f), l.Mat(C("1d1d22")), Vector3.Zero);
        Part(tape, Box(0.7f, 0.16f, 0.005f), l.Mat(C("f4f1e8")), new Vector3(0, 0.17f, 0.083f));
        var window = l.Mat(C("3a3a46"));
        Part(tape, Box(0.5f, 0.18f, 0.005f), window, new Vector3(0, -0.06f, 0.083f));
        var reel = l.Mat(C("e0e0e8"));
        Part(tape, Cyl(0.07f, 0.07f, 0.01f, 6), reel, new Vector3(-0.15f, -0.06f, 0.086f), new Vector3(90, 0, 0));
        Part(tape, Cyl(0.07f, 0.07f, 0.01f, 6), reel, new Vector3(0.15f, -0.06f, 0.086f), new Vector3(90, 0, 0));
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            var slide = a < 0.4f ? Overshoot(a / 0.4f) : a < 2.4f ? 1 : a < 2.8f ? 1 - (a - 2.4f) / 0.4f : 0;
            tape.Position = new Vector3(0.35f + 0.6f * slide, 0, 0);
            tape.RotationDegrees = new Vector3(a is > 0.4f and < 2.4f ? Mathf.Clamp((a - 0.6f) / 0.6f, 0, 1) * 180 : 0, 0, 0);
        };
        return p;
    }

    /// <summary>Clear disc case with a disc inside. Tap: the lid swings open and the disc spins.</summary>
    public static Prop JewelCase(Look l)
    {
        var p = new Prop { Name = "case", Radius = 0.7f, BaseRot = new Vector3(-20, 0, 0), Sway = 0, SpinSpeed = 16, Style = Prop.Bop.Hop };
        var b = p.Body;
        Part(b, Box(1f, 0.9f, 0.04f), l.Mat(C("2a2c34")), new Vector3(0, 0, -0.04f));  // tray
        var disc = Part(b, Cyl(0.4f, 0.4f, 0.012f, 16), l.Mat(C("ffffff"), DiscTexture()), new Vector3(0.04f, 0, 0), new Vector3(90, 0, 0));
        var hinge = new Node3D { Position = new Vector3(-0.5f, 0, 0.02f) };
        b.AddChild(hinge);
        // Only the lid is see-through: two overlapping translucent parts swap draw order as the lid
        // turns, which flickered. The booklet is printed paper (opaque), tucked just inside the lid.
        Part(hinge, Box(1f, 0.9f, 0.03f), l.Mat(C("c8dcf0"), alpha: 0.38f), new Vector3(0.5f, 0, 0.035f));
        Part(hinge, Box(0.86f, 0.8f, 0.01f), l.Mat(C("3c64b4")), new Vector3(0.52f, 0, 0.012f));  // booklet
        Part(b, Box(0.06f, 0.9f, 0.08f), l.Mat(C("7f8fa8")), new Vector3(-0.53f, 0, 0));  // spine
        var spin = 0f;
        p.Animate = (pr, t, dt) =>
        {
            pr.OpenTarget = pr.TapAge < 2.6f ? 1 : 0;
            hinge.RotationDegrees = new Vector3(0, -115 * Overshoot(pr.Open), 0);
            spin += dt * (pr.Open > 0.5f ? 360 : 0);  // slow enough not to strobe at 30 Hz
            disc.RotationDegrees = new Vector3(90, spin, 0);
        };
        return p;
    }

    /// <summary>Round portable disc player. Tap: the lid opens and the disc spins up.</summary>
    public static Prop DiscPlayer(Look l)
    {
        var p = new Prop { Name = "player", Radius = 0.75f, BaseRot = new Vector3(52, 0, 0), Sway = 20, Style = Prop.Bop.Wiggle };
        var b = p.Body;
        var shell = l.Mat(C("9aa4b8"));
        Part(b, Cyl(0.62f, 0.62f, 0.14f, 14), shell, Vector3.Zero);
        Part(b, Box(0.5f, 0.08f, 0.1f), l.Mat(C("5c6478")), new Vector3(0, 0, 0.6f));  // button strip
        var btn = l.Mat(C("e04a5a"));
        Part(b, Box(0.08f, 0.04f, 0.04f), btn, new Vector3(-0.12f, 0.05f, 0.64f));
        Part(b, Box(0.08f, 0.04f, 0.04f), l.Mat(C("e0e4ec")), new Vector3(0.02f, 0.05f, 0.64f));
        Part(b, Box(0.08f, 0.04f, 0.04f), l.Mat(C("e0e4ec")), new Vector3(0.16f, 0.05f, 0.64f));
        var disc = Part(b, Cyl(0.5f, 0.5f, 0.01f, 16), l.Mat(C("ffffff"), DiscTexture()), new Vector3(0, 0.075f, 0));
        var hinge = new Node3D { Position = new Vector3(0, 0.07f, -0.6f) };
        b.AddChild(hinge);
        Part(hinge, Cyl(0.6f, 0.6f, 0.06f, 14), l.Mat(C("b4bccc")), new Vector3(0, 0.03f, 0.6f));
        Part(hinge, Cyl(0.2f, 0.2f, 0.01f, 10), l.Mat(C("3a4050")), new Vector3(0, 0.065f, 0.6f));
        var spin = 0f;
        p.Animate = (pr, t, dt) =>
        {
            pr.OpenTarget = pr.TapAge < 2.8f ? 1 : 0;
            hinge.RotationDegrees = new Vector3(-80 * Overshoot(pr.Open), 0, 0);
            spin += dt * (pr.Open > 0.3f ? 420 * pr.Open : 0);
            disc.RotationDegrees = new Vector3(0, spin, 0);
        };
        return p;
    }

    /// <summary>Console memory card. Tap: hop and spin, the label lights up like a save.</summary>
    public static Prop MemoryCard(Look l)
    {
        var p = new Prop { Name = "card", Radius = 0.5f, BaseRot = new Vector3(-10, 0, -8), SpinSpeed = 30, Sway = 0, Style = Prop.Bop.Hop };
        var b = p.Body;
        Part(b, Box(0.5f, 0.66f, 0.1f), l.Mat(C("3c3f48")), Vector3.Zero);
        var label = l.Mat(C("e8e4dc"));
        Part(b, Box(0.38f, 0.3f, 0.005f), label, new Vector3(0, 0.08f, 0.053f));
        var ridge = l.Mat(C("2c2e36"));
        for (var i = 0; i < 3; i++)
        {
            Part(b, Box(0.42f, 0.025f, 0.11f), ridge, new Vector3(0, 0.29f - i * 0.04f, 0));
        }
        var gold = l.Mat(C("d8b040"));
        for (var i = 0; i < 6; i++)
        {
            Part(b, Box(0.04f, 0.06f, 0.01f), gold, new Vector3(-0.15f + i * 0.06f, -0.3f, 0.052f));
        }
        p.Animate = (pr, t, dt) =>
            label.Param("emission", pr.TapAge < 1.2f ? C("3a5a90") * (1 - pr.TapAge / 1.2f) : Colors.Black);
        return p;
    }

    /// <summary>Egg-shaped keychain pet. Tap: wiggles and the critter jumps; every third tap it hatches.</summary>
    public static Prop PetEgg(Look l)
    {
        var p = new Prop { Name = "egg", Radius = 0.55f, Sway = 10, Style = Prop.Bop.Wiggle };
        var b = p.Body;
        Part(b, Sphere(0.5f, 8, 5), l.Mat(C("ff9ac8"), alpha: 0.85f), Vector3.Zero, scale: new Vector3(0.9f, 1.1f, 0.55f));
        var on = C("1e2a1a");
        var off = C("a8b890");
        var egg = Critter(on, off, false);
        var hatched = Critter(on, off, true);
        var screen = l.Mat(C("ffffff"), egg, emission: C("101810"));
        screen.Param("uv_scale", new Vector2(0.5f, 1));
        Part(b, Quad(0.36f, 0.3f), screen, new Vector3(0, 0.05f, 0.28f));
        var btn = l.Mat(C("f4f0f6"));
        for (var i = -1; i <= 1; i++)
        {
            Part(b, Sphere(0.045f, 6, 3), btn, new Vector3(i * 0.12f, -0.22f, 0.25f));
        }
        Part(b, Torus(0.07f, 0.1f, 8, 4), l.Mat(C("c8ccd4")), new Vector3(0, 0.6f, 0), new Vector3(90, 0, 0));
        p.Animate = (pr, t, dt) =>
        {
            var frame = (int)(t * 2) % 2;
            var jump = pr.TapAge < 0.8f ? (int)(pr.TapAge * 10) % 2 : frame;
            screen.Param("tex", pr.Taps % 3 == 0 && pr.Taps > 0 ? hatched : egg);
            screen.Param("uv_offset", new Vector2(jump * 0.5f, 0));
            // Hanging from its ring: a slow pendulum.
            pr.BaseRot = new Vector3(0, 0, Mathf.Sin(t * 1.3f + pr.Phase) * 9);
        };
        return p;
    }

    /// <summary>Handheld game console in see-through plastic (our own shape). Tap: the screen lights up.</summary>
    public static Prop Handheld(Look l)
    {
        var p = new Prop { Name = "handheld", Radius = 0.75f, BaseRot = new Vector3(-8, -20, 6), Sway = 16, Style = Prop.Bop.Hop };
        var b = p.Body;
        Part(b, Box(0.86f, 1.3f, 0.06f), l.Mat(C("1f5a3a")), Vector3.Zero);  // circuit board inside
        Part(b, Box(0.9f, 1.36f, 0.22f), l.Mat(C("2fd2c0"), alpha: 0.42f), Vector3.Zero);
        Part(b, Box(0.66f, 0.5f, 0.02f), l.Mat(C("30343c")), new Vector3(0, 0.3f, 0.112f));
        var off = C("6f7f5c");
        var screen = l.Mat(off);
        Part(b, Box(0.5f, 0.38f, 0.01f), screen, new Vector3(0, 0.3f, 0.125f));
        var dpad = l.Mat(C("22242a"));
        Part(b, Box(0.24f, 0.07f, 0.04f), dpad, new Vector3(-0.2f, -0.25f, 0.12f));
        Part(b, Box(0.07f, 0.24f, 0.04f), dpad, new Vector3(-0.2f, -0.25f, 0.12f));
        var btn = l.Mat(C("e0407a"));
        Part(b, Cyl(0.06f, 0.06f, 0.04f, 8), btn, new Vector3(0.14f, -0.3f, 0.12f), new Vector3(90, 0, 0));
        Part(b, Cyl(0.06f, 0.06f, 0.04f, 8), btn, new Vector3(0.28f, -0.2f, 0.12f), new Vector3(90, 0, 0));
        p.Animate = (pr, t, dt) =>
        {
            var lit = pr.TapAge < 3f;
            var pulse = 0.5f + 0.5f * Mathf.Sin(pr.TapAge * 9);
            screen.Param("albedo", lit ? C("b8d878").Lerp(C("e8f0a0"), pulse) : off);
            screen.Param("emission", lit ? C("405020") : Colors.Black);
        };
        return p;
    }

    /// <summary>Clamshell phone. Tap: flips open (screen glows) or shut.</summary>
    public static Prop FlipPhone(Look l)
    {
        var p = new Prop { Name = "phone", Radius = 0.65f, BaseRot = new Vector3(-15, 25, -10), Sway = 22, Style = Prop.Bop.Wiggle };
        var b = p.Body;
        var shell = l.Mat(C("a8b0c0"));
        Part(b, Box(0.42f, 0.8f, 0.08f), shell, new Vector3(0, -0.4f, 0));
        var key = l.Mat(C("dfe3ea"));
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                Part(b, Box(0.09f, 0.07f, 0.02f), key, new Vector3(-0.11f + c * 0.11f, -0.3f - r * 0.11f, 0.045f));
            }
        }
        var hinge = new Node3D();
        b.AddChild(hinge);
        Part(hinge, Box(0.42f, 0.74f, 0.07f), shell, new Vector3(0, 0.37f, 0));
        var screen = l.Mat(C("2a3540"));
        Part(hinge, Box(0.32f, 0.4f, 0.01f), screen, new Vector3(0, 0.42f, 0.04f));
        Part(hinge, Cyl(0.025f, 0.025f, 0.22f, 6), l.Mat(C("3a3e48")), new Vector3(0.15f, 0.8f, -0.02f));
        p.OpenTarget = 1;
        p.Open = 1;
        var lastTaps = 0;
        p.Animate = (pr, t, dt) =>
        {
            if (pr.Taps != lastTaps)
            {
                lastTaps = pr.Taps;
                pr.OpenTarget = pr.OpenTarget > 0.5f ? 0 : 1;
            }
            // Open = flat out; shut = folded onto the keypad.
            hinge.RotationDegrees = new Vector3(-(1 - pr.Open) * 172 + 12, 0, 0);
            var glow = pr.Open > 0.9f;
            screen.Param("albedo", glow ? C("7ac8ff") : C("2a3540"));
            screen.Param("emission", glow ? C("204060") : Colors.Black);
        };
        return p;
    }

    /// <summary>Lava lamp. Blobs rise and sink; tap: they churn faster and glow brighter.</summary>
    public static Prop LavaLamp(Look l)
    {
        var p = new Prop { Name = "lamp", Radius = 0.7f, Sway = 8, BobAmp = 0.02f, Style = Prop.Bop.Wiggle };
        var b = p.Body;
        var metal = l.Mat(C("8c8f9c"));
        Part(b, Cyl(0.18f, 0.32f, 0.36f, 10), metal, new Vector3(0, -0.62f, 0));
        var glass = l.Mat(C("ff7a2a"), alpha: 0.5f, emission: C("401808"));
        Part(b, Cyl(0.15f, 0.26f, 0.9f, 10), glass, new Vector3(0, 0, 0));
        Part(b, Cyl(0.06f, 0.15f, 0.22f, 10), metal, new Vector3(0, 0.56f, 0));
        var blobMat = l.Mat(C("ffd040"), emission: C("603000"));
        var blobs = new List<MeshInstance3D>();
        for (var i = 0; i < 4; i++)
        {
            blobs.Add(Part(b, Sphere(0.08f + i % 2 * 0.03f, 6, 4), blobMat, Vector3.Zero));
        }
        var churn = 0f;
        p.Animate = (pr, t, dt) =>
        {
            var fast = pr.TapAge < 3f ? 1 - pr.TapAge / 3f : 0;
            churn += dt * (0.5f + fast * 3);
            for (var i = 0; i < blobs.Count; i++)
            {
                var y = Mathf.Sin(churn * (0.6f + i * 0.17f) + i * 1.7f) * 0.32f;
                var r = 0.12f - y * 0.08f;
                blobs[i].Position = new Vector3(Mathf.Cos(i * 2.1f + churn * 0.3f) * r * 0.6f, y, Mathf.Sin(i * 2.1f + churn * 0.3f) * r * 0.6f);
                blobs[i].Scale = new Vector3(1, 1.2f + Mathf.Sin(churn * 2 + i) * 0.25f, 1);
            }
            blobMat.Param("emission", C("603000").Lerp(C("ff8020"), fast));
        };
        return p;
    }

    /// <summary>Floppy disk. Tap: the metal shutter slides open and closes again.</summary>
    public static Prop Floppy(Look l)
    {
        var p = new Prop { Name = "floppy", Radius = 0.6f, BaseRot = new Vector3(-20, 15, 12), Sway = 24, Style = Prop.Bop.Spin };
        var b = p.Body;
        Part(b, Box(0.8f, 0.82f, 0.05f), l.Mat(C("2c4c9c")), Vector3.Zero);
        Part(b, Box(0.6f, 0.36f, 0.005f), l.Mat(C("f2f0ea")), new Vector3(0, -0.16f, 0.028f));
        Part(b, Box(0.6f, 0.03f, 0.006f), l.Mat(C("e04848")), new Vector3(0, -0.02f, 0.029f));
        Part(b, Box(0.14f, 0.2f, 0.006f), l.Mat(C("1a1a20")), new Vector3(0.05f, 0.3f, 0.026f));  // disk window
        var shutter = Part(b, Box(0.42f, 0.26f, 0.06f), l.Mat(C("c8ccd4")), new Vector3(0.05f, 0.27f, 0));
        p.Animate = (pr, t, dt) =>
        {
            pr.OpenTarget = pr.TapAge < 1.8f ? 1 : 0;
            shutter.Position = new Vector3(0.05f - 0.22f * pr.Open, 0.27f, 0);
        };
        return p;
    }

    /// <summary>Floating save-point crystal. Tap: spins and pulses with light.</summary>
    public static Prop Crystal(Look l)
    {
        var p = new Prop { Name = "crystal", Radius = 0.6f, SpinSpeed = 40, Sway = 0, BobAmp = 0.12f, Style = Prop.Bop.Spin };
        var b = p.Body;
        var mat = l.Mat(C("5ae0ff"), alpha: 0.75f, emission: C("104060"));
        Part(b, Sphere(0.32f, 4, 2), mat, Vector3.Zero, scale: new Vector3(1, 1.9f, 1));
        var core = l.Mat(C("e8ffff"), emission: C("80c0ff"), unlit: true);
        Part(b, Sphere(0.1f, 4, 2), core, Vector3.Zero, scale: new Vector3(1, 1.9f, 1));
        p.Animate = (pr, t, dt) =>
        {
            var pulse = pr.TapAge < 1.2f ? 1 - pr.TapAge / 1.2f : 0;
            mat.Param("emission", C("104060").Lerp(C("60e0ff"), pulse + 0.15f * (0.5f + 0.5f * Mathf.Sin(t * 2.4f))));
        };
        return p;
    }

    /// <summary>Our own low-poly mascot: a round critter with fin ears and an antenna. Tap: jumps and spins.</summary>
    public static Prop Mascot(Look l)
    {
        var p = new Prop { Name = "mascot", Radius = 0.7f, Sway = 30, SwaySpeed = 0.6f, BobAmp = 0.03f, Style = Prop.Bop.Hop };
        var b = p.Body;
        var fur = l.Mat(C("ff8a3a"));
        var belly = l.Mat(C("ffe0b0"));
        Part(b, Sphere(0.32f, 8, 5), fur, new Vector3(0, -0.3f, 0), scale: new Vector3(1, 0.9f, 0.9f));
        Part(b, Sphere(0.2f, 8, 4), belly, new Vector3(0, -0.32f, 0.16f), scale: new Vector3(1, 1, 0.6f));
        var head = new Node3D { Position = new Vector3(0, 0.18f, 0) };
        b.AddChild(head);
        Part(head, Sphere(0.36f, 8, 5), fur, Vector3.Zero);
        Part(head, Prism(0.22f, 0.38f, 0.06f), fur, new Vector3(-0.3f, 0.3f, -0.02f), new Vector3(0, 0, 30));
        Part(head, Prism(0.22f, 0.38f, 0.06f), fur, new Vector3(0.3f, 0.3f, -0.02f), new Vector3(0, 0, -30));
        var eye = l.Mat(C("16161c"));
        var eyeL = Part(head, Box(0.07f, 0.13f, 0.04f), eye, new Vector3(-0.12f, 0.04f, 0.32f));
        var eyeR = Part(head, Box(0.07f, 0.13f, 0.04f), eye, new Vector3(0.12f, 0.04f, 0.32f));
        Part(head, Box(0.08f, 0.04f, 0.04f), l.Mat(C("c84a2a")), new Vector3(0, -0.1f, 0.33f));
        Part(head, Cyl(0.015f, 0.015f, 0.26f, 4), l.Mat(C("3a3c44")), new Vector3(0, 0.46f, 0));
        Part(head, Sphere(0.06f, 6, 3), l.Mat(C("5ae0ff"), emission: C("104050")), new Vector3(0, 0.6f, 0));
        var shoe = l.Mat(C("3048a0"));
        Part(b, Box(0.14f, 0.08f, 0.22f), shoe, new Vector3(-0.14f, -0.6f, 0.04f));
        Part(b, Box(0.14f, 0.08f, 0.22f), shoe, new Vector3(0.14f, -0.6f, 0.04f));
        p.Animate = (pr, t, dt) =>
        {
            head.RotationDegrees = new Vector3(0, Mathf.Sin(t * 0.9f + pr.Phase) * 25, Mathf.Sin(t * 0.7f) * 6);
            // Blink every few seconds.
            var blink = Mathf.PosMod(t + pr.Phase, 3.7f) < 0.12f ? 0.15f : 1f;
            eyeL.Scale = eyeR.Scale = new Vector3(1, blink, 1);
        };
        return p;
    }

    /// <summary>Spinning gold ring pickup. Tap: spins faster with a flash.</summary>
    public static Prop PowerRing(Look l)
    {
        var p = new Prop { Name = "ring", Radius = 0.45f, SpinSpeed = 120, Sway = 0, BobAmp = 0.1f, Style = Prop.Bop.Spin };
        var mat = l.Mat(C("ffd040"), emission: C("402800"));
        Part(p.Body, Torus(0.22f, 0.34f, 10, 5), mat, Vector3.Zero, new Vector3(90, 0, 0));
        p.Animate = (pr, t, dt) =>
            mat.Param("emission", pr.TapAge < 0.8f ? C("ffe080") * (1 - pr.TapAge / 0.8f) : C("402800"));
        return p;
    }
}
