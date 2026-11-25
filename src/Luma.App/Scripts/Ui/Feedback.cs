using Godot;

namespace Luma.App.Ui;

/// <summary>Moments in the UI that get a sound and a rumble.</summary>
public enum Cue
{
    Move,
    Confirm,
    Back,
    Tab,
    Toggle,
    Error,
    Success,
    Launch,
}

/// <summary>
/// Console-style feedback: a short sound and a light rumble for moving, confirming, going back,
/// switching tabs, changing a value, errors and starting a game. Every sound is synthesised here
/// at startup (no audio files), with a palette per theme: soft sine blips for the calm themes,
/// square-wave chirps for the pixel themes, a detuned neon buzz for Nightgrid and an echoing
/// triangle chime for the 32-bit 3D theme. Rumble goes to the pad that was used last.
/// </summary>
public partial class Feedback : Node
{
    public static Feedback? Instance { get; private set; }

    private const int Rate = 22050;
    private readonly Dictionary<Cue, AudioStreamWav> _sounds = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private int _next;
    private ulong _lastMoveMs;

    /// <summary>0 (off) to 100.</summary>
    public int Volume { get; set; } = 50;

    public bool Haptics { get; set; } = true;

    public Feedback(string themeId)
    {
        Instance = this;
        var palette = PaletteFor(themeId);
        foreach (var cue in Enum.GetValues<Cue>())
        {
            _sounds[cue] = Wav(Render(cue, palette));
        }
    }

    public override void _Ready()
    {
        for (var i = 0; i < 4; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            _players.Add(p);
        }
    }

    public static void Play(Cue cue) => Instance?.PlayCue(cue);

    private void PlayCue(Cue cue)
    {
        if (cue == Cue.Move)
        {
            // Held directions repeat quickly: keep the tick from turning into a buzz.
            var now = Time.GetTicksMsec();
            if (now - _lastMoveMs < 45)
            {
                return;
            }
            _lastMoveMs = now;
        }
        if (Volume > 0 && _players.Count > 0)
        {
            var p = _players[_next];
            _next = (_next + 1) % _players.Count;
            p.Stream = _sounds[cue];
            p.VolumeDb = Mathf.LinearToDb(Volume / 100f * Gain(cue));
            p.Play();
        }
        if (Haptics && Pad.LastDevice is { } device && Pad.LastFamily != PadFamily.Keyboard)
        {
            var (weak, strong, seconds) = Rumble(cue);
            Input.StartJoyVibration(device, weak, strong, seconds);
        }
    }

    private static float Gain(Cue cue) => cue switch
    {
        Cue.Move => 0.45f,
        Cue.Toggle => 0.6f,
        _ => 0.85f,
    };

    private static (float Weak, float Strong, float Seconds) Rumble(Cue cue) => cue switch
    {
        Cue.Move => (0.12f, 0f, 0.025f),
        Cue.Toggle => (0.2f, 0f, 0.03f),
        Cue.Tab => (0.22f, 0.05f, 0.035f),
        Cue.Confirm => (0.35f, 0.15f, 0.045f),
        Cue.Back => (0.25f, 0.05f, 0.035f),
        Cue.Success => (0.3f, 0.2f, 0.07f),
        Cue.Error => (0.3f, 0.6f, 0.14f),
        Cue.Launch => (0.5f, 0.8f, 0.22f),
        _ => (0f, 0f, 0f),
    };

    // ---- synthesis ----

    public enum Wave
    {
        Sine,
        Square,
        Triangle,
        Saw,
    }

    public sealed record Palette(Wave Wave, float Detune = 0, float Echo = 0, float Pitch = 1, float Decay = 1);

    public static Palette PaletteFor(string themeId) => themeId switch
    {
        "terminal" or "brick" or "pocket" => new Palette(Wave.Square, Pitch: 1.0f, Decay: 0.8f),
        "sixteen" => new Palette(Wave.Square, Detune: 0.004f, Pitch: 1.12f),
        "nightgrid" => new Palette(Wave.Saw, Detune: 0.008f, Pitch: 0.9f),
        "polygon" or "cdera" => new Palette(Wave.Triangle, Echo: 0.35f, Pitch: 0.95f, Decay: 1.3f),
        _ => new Palette(Wave.Sine, Pitch: 1f),
    };

    private sealed record Note(float From, float To, float Seconds, float Level = 1, float Delay = 0);

    /// <summary>The cue's melody: one or more short notes (Hz, start and end for slides).</summary>
    private static Note[] Notes(Cue cue) => cue switch
    {
        Cue.Move => [new(1320, 1320, 0.022f, 0.6f)],
        Cue.Toggle => [new(990, 1100, 0.03f, 0.7f)],
        Cue.Tab => [new(880, 880, 0.03f, 0.7f), new(1175, 1175, 0.035f, 0.6f, 0.035f)],
        Cue.Confirm => [new(660, 660, 0.04f), new(990, 990, 0.06f, 0.9f, 0.045f)],
        Cue.Back => [new(740, 740, 0.04f), new(494, 494, 0.06f, 0.9f, 0.045f)],
        Cue.Success => [new(784, 784, 0.05f), new(988, 988, 0.05f, 0.9f, 0.06f), new(1319, 1319, 0.09f, 0.8f, 0.12f)],
        Cue.Error => [new(220, 196, 0.09f), new(165, 147, 0.12f, 0.9f, 0.1f)],
        Cue.Launch => [new(330, 990, 0.22f, 0.8f), new(1319, 1319, 0.16f, 0.7f, 0.2f)],
        _ => [],
    };

    /// <summary>Mono float samples for a cue in a palette.</summary>
    public static float[] Render(Cue cue, Palette palette)
    {
        var notes = Notes(cue);
        var length = notes.Max(n => n.Delay + n.Seconds * palette.Decay) + (palette.Echo > 0 ? 0.16f : 0) + 0.01f;
        var buf = new float[(int)(length * Rate)];
        foreach (var n in notes)
        {
            var start = (int)(n.Delay * Rate);
            var count = (int)(n.Seconds * palette.Decay * Rate);
            var phase = 0.0;
            var phase2 = 0.0;
            for (var i = 0; i < count && start + i < buf.Length; i++)
            {
                var t = i / (float)count;
                var f = (n.From + (n.To - n.From) * t) * palette.Pitch;
                phase += f / Rate;
                phase2 += f * (1 + palette.Detune) / Rate;
                var s = Osc(palette.Wave, phase);
                if (palette.Detune > 0)
                {
                    s = (s + Osc(palette.Wave, phase2)) * 0.5f;
                }
                // 2 ms attack, then an exponential fall: a click-free blip.
                var attack = Math.Min(1f, i / (Rate * 0.002f));
                var env = attack * MathF.Exp(-4.5f * t);
                buf[start + i] += s * env * n.Level;
            }
        }
        if (palette.Echo > 0)
        {
            var d = (int)(0.07f * Rate);
            for (var i = buf.Length - 1; i >= d; i--)
            {
                buf[i] += buf[i - d] * palette.Echo;
            }
        }
        // Square and saw waves are loud for their peak; keep every palette at about the same level.
        var level = palette.Wave switch { Wave.Square => 0.42f, Wave.Saw => 0.55f, Wave.Triangle => 0.6f, _ => 0.6f };
        for (var i = 0; i < buf.Length; i++)
        {
            buf[i] = Math.Clamp(buf[i] * level, -1f, 1f);
        }
        return buf;
    }

    private static float Osc(Wave w, double phase)
    {
        var p = (float)(phase - Math.Floor(phase));
        return w switch
        {
            Wave.Square => p < 0.5f ? 1 : -1,
            Wave.Triangle => 4 * MathF.Abs(p - 0.5f) - 1,
            Wave.Saw => 2 * p - 1,
            _ => MathF.Sin(p * MathF.Tau),
        };
    }

    /// <summary>Writes every palette's cues as 16-bit mono .wav files into <paramref name="dir"/>/&lt;palette&gt;/.</summary>
    public static void WriteAll(string dir)
    {
        foreach (var (name, theme) in new[] { ("soft", "dark"), ("chip", "terminal"), ("bright-chip", "sixteen"), ("neon", "nightgrid"), ("polygon", "polygon") })
        {
            var d = System.IO.Path.Combine(dir, name);
            Directory.CreateDirectory(d);
            foreach (var cue in Enum.GetValues<Cue>())
            {
                var pcm = Wav(Render(cue, PaletteFor(theme))).Data;
                using var f = File.Create(System.IO.Path.Combine(d, cue.ToString().ToLowerInvariant() + ".wav"));
                using var w = new BinaryWriter(f);
                w.Write("RIFF"u8);
                w.Write(36 + pcm.Length);
                w.Write("WAVEfmt "u8);
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write("data"u8);
                w.Write(pcm.Length);
                w.Write(pcm);
            }
        }
    }

    private static AudioStreamWav Wav(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (short)(samples[i] * short.MaxValue);
            data[i * 2] = (byte)v;
            data[i * 2 + 1] = (byte)(v >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }
}
