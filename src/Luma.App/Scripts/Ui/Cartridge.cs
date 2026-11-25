using Godot;

namespace Luma.App.Ui;

/// <summary>
/// A game drawn as a small cartridge (Luma's own design): a portrait shell with a cut
/// top-left corner and a recessed slot along the top edge, a label window for the art (cover art
/// later; a monogram until then) and the title on the shell below it. Drawn in code so it stays
/// crisp at any scale and follows the theme.
/// </summary>
public partial class Cartridge : Control
{
    private readonly string _title;
    private readonly string _monogram;
    private readonly string? _chips;
    private readonly bool _large;
    private float _lift;
    private bool _focused;
    private Tween? _tween;
    private ImageTexture? _icon;

    /// <summary>Draw only the title and chips (the 32-bit 3D theme draws the cart itself in 3D).</summary>
    public bool TitleOnly { get; init; }

    public Cartridge(string title, string monogram, string? chips = null, bool large = false)
    {
        _title = title;
        _monogram = monogram;
        _chips = chips;
        _large = large;
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    /// <summary>Shows the game's own icon (already pixel-sized, see <see cref="GameIcons"/>) on the label instead of the monogram.</summary>
    public void SetIcon(Image? icon)
    {
        _icon = icon == null || icon.IsEmpty() ? null : ImageTexture.CreateFromImage(icon);
        TextureFilter = TextureFilterEnum.Nearest;  // chunky pixels, like the era
        QueueRedraw();
    }

    /// <summary>Focus state from the owning button: lifts the cart and outlines it in the accent colour.</summary>
    public void SetFocused(bool on)
    {
        _focused = on;
        _tween?.Kill();
        var target = on ? 1f : 0f;
        if (!Anim.Enabled || !IsInsideTree())
        {
            _lift = target;
            QueueRedraw();
            return;
        }
        _tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(on ? Tween.TransitionType.Back : Tween.TransitionType.Cubic);
        _tween.TweenMethod(Callable.From<float>(v => { _lift = v; QueueRedraw(); }), _lift, target, on ? Anim.Normal : Anim.Fast);
    }

    private static Color Shell => UiTheme.Spec.Shell;

    public override void _Draw()
    {
        var w = Size.X;
        var h = Size.Y;
        var s = _large ? 1.35f : 1f;
        var lift = -6 * s * _lift;
        var origin = new Vector2(0, lift + 6 * s);  // leave room above for the lift
        var bodyH = h - 6 * s;

        if (TitleOnly)
        {
            origin = new Vector2(0, 6 * s);
        }
        var shell = ShellPolygon(w, bodyH, 12 * s, 20 * s).Select(p => p + origin).ToArray();
        var labelRect = new Rect2(origin + new Vector2(10 * s, 20 * s), new Vector2(w - 20 * s, _large ? bodyH - 50 * s : bodyH * 0.6f));
        var font = GetThemeDefaultFont();
        if (TitleOnly)
        {
            DrawTitle(font, labelRect, s, w);
            return;
        }
        // Soft drop shadow that grows as the cart lifts.
        var shadow = shell.Select(p => p + new Vector2(0, (4 + 6 * _lift) * s)).ToArray();
        DrawColoredPolygon(shadow, new Color(0, 0, 0, 0.28f + 0.12f * _lift));
        DrawColoredPolygon(shell, Shell.Lightened(0.06f * _lift));
        var outline = shell.Append(shell[0]).ToArray();
        DrawPolyline(outline, _focused ? UiTheme.Accent : UiTheme.Line.Lightened(0.15f), _focused ? 3 * s : 1.5f, true);

        // Recessed slot along the top edge, and three grip ridges at the bottom.
        var slot = new Rect2(origin + new Vector2(w * 0.34f, 5 * s), new Vector2(w * 0.48f, 5 * s));
        DrawRect(slot, Shell.Darkened(0.35f));
        for (var i = 0; i < 3; i++)
        {
            var y = origin.Y + bodyH - (10 + i * 5) * s;
            DrawLine(new Vector2(w * 0.4f, y), new Vector2(w * 0.6f, y), Shell.Lightened(0.12f), 1.5f * s, true);
        }

        // Label window with the art.
        // The big cart on the game card has no printed title (the card shows it), so the label is taller.
        var tile = UiTheme.TileColor(_title);
        var label = new StyleBoxFlat { BgColor = tile, AntiAliasing = true };
        label.SetCornerRadiusAll((int)(7 * s));
        DrawStyleBox(label, labelRect);
        // Light sheen across the top of the label.
        var sheen = new StyleBoxFlat { BgColor = Colors.White with { A = 0.06f } };
        sheen.SetCornerRadiusAll((int)(7 * s));
        DrawStyleBox(sheen, new Rect2(labelRect.Position, new Vector2(labelRect.Size.X, labelRect.Size.Y * 0.38f)));

        if (_icon != null)
        {
            // The square icon fills the whole label (cropped to its shape, centred), corners rounded like the label.
            var pts = RoundedRect(labelRect, 7 * s);
            var uvs = CoverUvs(pts, labelRect);
            DrawPolygon(pts, new[] { Colors.White }, uvs, _icon);
        }
        var monoSize = (int)(labelRect.Size.Y * 0.42f);
        var ms = font.GetStringSize(_monogram, HorizontalAlignment.Left, -1, monoSize);
        if (_icon == null)
        {
            DrawString(font, labelRect.Position + new Vector2((labelRect.Size.X - ms.X) / 2, (labelRect.Size.Y - ms.Y) / 2 + font.GetAscent(monoSize)),
                _monogram, HorizontalAlignment.Left, -1, monoSize, tile.Lightened(0.4f) with { A = 0.45f });
        }

        if (_large)
        {
            return;
        }
        DrawTitle(font, labelRect, s, w);
    }

    /// <summary>Title (two lines max) and chips under the label.</summary>
    private void DrawTitle(Font font, Rect2 labelRect, float s, float w)
    {
        var textSize = (int)((_large ? 17 : 14) * (UiTheme.Mono ? 0.92f : 1f));
        var textTop = labelRect.End.Y + 6 * s;
        var textW = w - 20 * s;
        var lines = Wrap(font, UiTheme.Mono ? _title.ToUpperInvariant() : _title, textSize, textW, 2);
        var lineH = font.GetHeight(textSize);
        for (var i = 0; i < lines.Count; i++)
        {
            DrawString(font, new Vector2(10 * s, textTop + font.GetAscent(textSize) + i * lineH), lines[i],
                HorizontalAlignment.Left, textW, textSize, TitleOnly && _focused ? UiTheme.AccentText : UiTheme.Text);
        }
        if (_chips != null)
        {
            var chipSize = textSize - 3;
            DrawString(font, new Vector2(10 * s, textTop + lines.Count * lineH + font.GetAscent(chipSize) + 1), _chips,
                HorizontalAlignment.Left, textW, chipSize, UiTheme.AccentText with { A = 0.85f });
        }
    }

    private static Vector2[] RoundedRect(Rect2 r, float radius)
    {
        var pts = new List<Vector2>();
        void Arc(Vector2 c, float from)
        {
            for (var i = 0; i <= 5; i++)
            {
                var a = from + i * Mathf.Pi / 2 / 5;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }
        Arc(r.Position + new Vector2(radius, radius), Mathf.Pi);
        Arc(new Vector2(r.End.X - radius, r.Position.Y + radius), -Mathf.Pi / 2);
        Arc(r.End - new Vector2(radius, radius), 0);
        Arc(new Vector2(r.Position.X + radius, r.End.Y - radius), Mathf.Pi / 2);
        return pts.ToArray();
    }

    /// <summary>UVs that map a square texture onto <paramref name="r"/> cover-style: fills it, cropping the overflow evenly.</summary>
    internal static Vector2[] CoverUvs(Vector2[] pts, Rect2 r)
    {
        var aspect = r.Size.X / r.Size.Y;
        var span = aspect >= 1 ? new Vector2(1, 1 / aspect) : new Vector2(aspect, 1);
        var start = (Vector2.One - span) / 2;
        return pts.Select(p => start + (p - r.Position) / r.Size * span).ToArray();
    }

    /// <summary>Rounded rectangle with the top-left corner cut diagonally.</summary>
    private static Vector2[] ShellPolygon(float w, float h, float r, float cut)
    {
        var pts = new List<Vector2> { new(0, cut), new(cut, 0) };
        void Arc(Vector2 c, float from)
        {
            for (var i = 0; i <= 6; i++)
            {
                var a = from + i * Mathf.Pi / 2 / 6;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }
        Arc(new Vector2(w - r, r), -Mathf.Pi / 2);       // top-right
        Arc(new Vector2(w - r, h - r), 0);                // bottom-right
        Arc(new Vector2(r, h - r), Mathf.Pi / 2);         // bottom-left
        return pts.ToArray();
    }

    private static List<string> Wrap(Font font, string text, int size, float width, int maxLines)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (font.GetStringSize(candidate, HorizontalAlignment.Left, -1, size).X <= width || line.Length == 0)
            {
                line = candidate;
                continue;
            }
            lines.Add(line);
            line = word;
            if (lines.Count == maxLines)
            {
                break;
            }
        }
        if (lines.Count < maxLines && line.Length > 0)
        {
            lines.Add(line);
        }
        else if (lines.Count == maxLines && line.Length > 0 && !lines[^1].EndsWith('…'))
        {
            lines[^1] = Ellipsize(font, lines[^1] + "…", size, width);
        }
        return lines;
    }

    private static string Ellipsize(Font font, string s, int size, float width)
    {
        while (s.Length > 2 && font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X > width)
        {
            s = s[..^2] + "…";
        }
        return s;
    }
}
