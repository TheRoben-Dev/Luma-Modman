using Godot;

namespace Luma.App.Ui;

/// <summary>Background decoration drawn behind the UI.</summary>
public enum Decor
{
    None,
    DotGrid,
    Scanlines,
    PixelGrid,
}

/// <summary>One selectable look. All themes are original palettes; retro ones only evoke an era.</summary>
public sealed record ThemeSpec(string Id, string Label, string Blurb)
{
    public Color Bg { get; init; }
    public Color Panel { get; init; }
    public Color PanelHi { get; init; }
    public Color Line { get; init; }
    public Color Accent { get; init; }
    public Color Text { get; init; }
    public Color Dim { get; init; }
    public Color Good { get; init; }
    public Color Warn { get; init; }
    public Color Bad { get; init; }
    public Color Info { get; init; } = new("5cc8e0");
    public Color Shell { get; init; } = new("2a303c");

    /// <summary>Corner radius cap (0 = square corners).</summary>
    public int Radius { get; init; } = 10;

    /// <summary>Straight-cut corners instead of round ones.</summary>
    public bool Chamfer { get; init; }

    /// <summary>Every opaque panel gets a 1-2 px border in Line.</summary>
    public int Hairline { get; init; }

    /// <summary>Hard drop "bevel" under opaque panels, in px (early-3D / chunky pixel look).</summary>
    public int Bevel { get; init; }

    public string FontKind { get; init; } = "";
    public bool Upper { get; init; }
    public Decor Decor { get; init; }
    public float Glow { get; init; } = 0.13f;
    public bool Light { get; init; }
    public string Wordmark { get; init; } = "LUMA";

    /// <summary>Tile label colours: hue spread, saturation, value.</summary>
    public (float Sat, float Val, int HueFrom, int HueSpan) Tiles { get; init; } = (0.45f, 0.30f, 0, 360);
}

/// <summary>High-contrast 10-foot themes built in code (no .tres to drift out of sync).</summary>
public static class UiTheme
{
    public static readonly IReadOnlyList<ThemeSpec> Themes =
    [
        new("dark", "Dark", "The default: calm dark panels with a violet accent.")
        {
            Bg = new("0d0f14"), Panel = new("161a22"), PanelHi = new("1f2430"), Line = new("262c38"),
            Accent = new("7c6cff"), Text = new("e8eaf0"), Dim = new("8a91a3"),
            Good = new("4ccb8a"), Warn = new("f0b452"), Bad = new("ef6b6b"),
        },
        new("nightgrid", "Nightgrid", "Void black, dot grid, magenta highlights and chamfered corners.")
        {
            Bg = new("07080B"), Panel = new("0D0F14"), PanelHi = new("141821"), Line = new("1F2633"),
            Accent = new("FF2A6D"), Text = new("E6EDF3"), Dim = new("8B98A9"),
            Good = new("2BFF88"), Warn = new("FF9F1C"), Bad = new("FF003C"), Info = new("00E5FF"),
            Shell = new("151a22"), Chamfer = true, Hairline = 1, FontKind = "mono", Upper = true,
            Decor = Decor.DotGrid, Glow = 0.06f, Wordmark = "LU·MA", Tiles = (0.35f, 0.14f, 0, 360),
        },
        new("terminal", "Retro terminal", "Phosphor green on black with CRT scanlines.")
        {
            Bg = new("050806"), Panel = new("0b120d"), PanelHi = new("122019"), Line = new("1d3a28"),
            Accent = new("3dff8f"), Text = new("c9ffdc"), Dim = new("5f9a76"),
            Good = new("3dff8f"), Warn = new("ffd166"), Bad = new("ff5f6d"),
            Shell = new("0e1912"), Radius = 3, FontKind = "mono", Upper = true,
            Decor = Decor.Scanlines, Glow = 0.07f, Wordmark = "> LUMA_", Tiles = (0.55f, 0.18f, 130, 40),
        },
        new("brick", "8-bit brick", "Warm grey and black with a red accent, square corners and a chunky pixel font.")
        {
            Bg = new("1f1d1b"), Panel = new("34302c"), PanelHi = new("464039"), Line = new("0c0b0a"),
            Accent = new("e0353b"), Text = new("f3eee6"), Dim = new("a89f94"),
            Good = new("6cc04a"), Warn = new("f2b33d"), Bad = new("ff5a4c"),
            Shell = new("595249"), Radius = 0, Hairline = 2, Bevel = 4, FontKind = "pixel", Upper = true,
            Glow = 0.05f, Wordmark = "LUMA", Tiles = (0.55f, 0.42f, 0, 360),
        },
        new("sixteen", "16-bit", "Lavender and purple with rounded panels and colourful accents.")
        {
            Bg = new("221f35"), Panel = new("34304f"), PanelHi = new("443f66"), Line = new("575081"),
            Accent = new("b7a4ff"), Text = new("f3f0ff"), Dim = new("a9a3cc"),
            Good = new("5ccf7e"), Warn = new("f2c94c"), Bad = new("ec5a68"), Info = new("4f8ff0"),
            Shell = new("8e88ab"), Radius = 16, Hairline = 1, Glow = 0.16f, Tiles = (0.5f, 0.48f, 0, 360),
        },
        new("pocket", "Pocket LCD", "Four shades of green on a pale screen, with a visible pixel grid.")
        {
            Bg = new("b9c47e"), Panel = new("a4b267"), PanelHi = new("c8d290"), Line = new("4f6233"),
            Accent = new("27391c"), Text = new("1c2914"), Dim = new("4a5e30"),
            Good = new("27391c"), Warn = new("5b4a14"), Bad = new("6b2714"), Info = new("27391c"),
            Shell = new("8f9e5a"), Radius = 0, Hairline = 2, FontKind = "pixel", Upper = true,
            Decor = Decor.PixelGrid, Glow = 0f, Light = true, Tiles = (0.45f, 0.42f, 70, 25),
        },
        new("cdera", "CD era", "Dark grey with blue and orange accents and raised, early-3D panels.")
        {
            Bg = new("17191e"), Panel = new("2a2d35"), PanelHi = new("363a44"), Line = new("4a505d"),
            Accent = new("3478f0"), Text = new("eef1f6"), Dim = new("9aa1ad"),
            Good = new("47c27a"), Warn = new("ff8a1f"), Bad = new("f0524f"), Info = new("ff8a1f"),
            Shell = new("3d414b"), Radius = 5, Hairline = 1, Bevel = 3, Glow = 0.12f, Tiles = (0.5f, 0.36f, 0, 360),
        },
        new("polygon", "32-bit 3D", "Late-90s polygons: wobbly 3D cartridges, dithered colour, fog and era toys you can poke.")
        {
            Bg = new("0b0c1d"), Panel = new("1a1c36"), PanelHi = new("272a50"), Line = new("3b4176"),
            Accent = new("ffc23d"), Text = new("eef0ff"), Dim = new("9095c4"),
            Good = new("5ce08c"), Warn = new("ff9a3d"), Bad = new("ff5a6a"), Info = new("4fc3ff"),
            Shell = new("6c6f80"), Radius = 3, Hairline = 1, Bevel = 3, FontKind = "chakra", Upper = true,
            Glow = 0f, Wordmark = "LUMA", Tiles = (0.55f, 0.42f, 0, 360),
        },
    ];

    public static ThemeSpec Spec { get; private set; } = Themes[0];

    public static Color Bg => Spec.Bg;
    public static Color Panel => Spec.Panel;
    public static Color PanelHi => Spec.PanelHi;
    public static Color Accent => Spec.Accent;
    public static Color Text => Spec.Text;
    public static Color Dim => Spec.Dim;
    public static Color Good => Spec.Good;
    public static Color Warn => Spec.Warn;
    public static Color Bad => Spec.Bad;
    public static Color Info => Spec.Info;
    public static Color Line => Spec.Line;
    public static string Name => Spec.Id;
    public static bool Terminal => Name == "terminal";
    public static bool Nightgrid => Name == "nightgrid";

    /// <summary>The 32-bit 3D theme: the library draws real 3D cartridges.</summary>
    public static bool Poly3D => Name == "polygon";

    /// <summary>Themes with uppercase labels (terminal, pixel and grid looks).</summary>
    public static bool Mono => Spec.Upper;

    /// <summary>Text colour for focused/pressed controls: brighter than Text on dark themes.</summary>
    public static Color Bright => Spec.Light ? Spec.Text : Colors.White;

    /// <summary>Accent used for text (values, labels): lifted on dark themes, as-is on light ones.</summary>
    public static Color AccentText => Spec.Light ? Accent : Accent.Lightened(0.35f);

    private static Font? _font;
    private static Font? _pixel;

    public static IEnumerable<string> Names => Themes.Select(t => t.Id);

    private static readonly string[] MonoFonts = ["JetBrains Mono", "Fira Code", "Cascadia Mono", "DejaVu Sans Mono", "Liberation Mono", "monospace"];

    /// <summary>
    /// Accent colours anyone can pick for any theme, chosen to stay readable on every dark theme.
    /// Light themes get a darker shade of the same hue (see <see cref="AccentFor"/>).
    /// </summary>
    public static readonly IReadOnlyList<(string Id, string Label, Color Color)> AccentSwatches =
    [
        ("gold", "Gold", new Color("ffc23d")),
        ("orange", "Orange", new Color("ff8a3d")),
        ("red", "Red", new Color("ff5a6a")),
        ("pink", "Pink", new Color("ff5ab4")),
        ("violet", "Violet", new Color("9b7cff")),
        ("blue", "Blue", new Color("4f8ff0")),
        ("cyan", "Cyan", new Color("3fd6e6")),
        ("green", "Green", new Color("4fd88a")),
    ];

    /// <summary>The swatch's colour as used on <paramref name="theme"/>, or null for an unknown id.</summary>
    public static Color? AccentFor(ThemeSpec theme, string? id) =>
        AccentSwatches.FirstOrDefault(s => s.Id == id) is { Id: not null } s ? (theme.Light ? s.Color.Darkened(0.62f) : s.Color) : null;

    /// <summary>
    /// Pocket LCD's whole point is its four-shade screen, so instead of an accent it offers other
    /// four-shade screens: (lightest background, panel, raised panel, lines, darkest ink, text, dim, shell).
    /// </summary>
    private static readonly IReadOnlyList<(string Id, string Label, string[] Shades)> PocketPalettes =
    [
        ("grey", "Grey screen", ["c4c7bd", "aeb2a6", "d2d5cb", "51554c", "2a2c28", "1e201c", "50544a", "9a9d93"]),
        ("ice", "Ice blue", ["b8d4dc", "9cbcc8", "cce2e8", "3c5c6c", "1c3440", "142630", "3e5e6c", "8aa8b4"]),
        ("amber", "Amber", ["e0c890", "c8ac70", "ecd8a8", "6c4c1c", "3c2408", "2c1a06", "6a4a1e", "b49660"]),
        ("rose", "Rose", ["e4c0c8", "cca4b0", "f0d4da", "6c3448", "3c1424", "2c0e1a", "6c3a4c", "b88c98"]),
    ];

    /// <summary>What the "Accent colour" row offers for a theme: accent swatches, or whole palettes for Pocket LCD.</summary>
    public static IEnumerable<(string Id, string Label)> SwatchesFor(ThemeSpec theme) =>
        theme.Id == "pocket" ? PocketPalettes.Select(p => (p.Id, p.Label)) : AccentSwatches.Select(s => (s.Id, s.Label));

    /// <summary>The theme with a swatch (or Pocket palette) applied; unknown ids leave it as designed.</summary>
    public static ThemeSpec WithSwatch(ThemeSpec theme, string? id)
    {
        if (theme.Id == "pocket")
        {
            if (PocketPalettes.FirstOrDefault(p => p.Id == id) is { Shades: { } s })
            {
                var ink = new Color(s[4]);
                return theme with
                {
                    Bg = new(s[0]), Panel = new(s[1]), PanelHi = new(s[2]), Line = new(s[3]), Accent = ink,
                    Text = new(s[5]), Dim = new(s[6]), Shell = new(s[7]), Good = ink, Info = ink,
                };
            }
            return theme;
        }
        return AccentFor(theme, id) is { } a ? theme with { Accent = a } : theme;
    }

    /// <summary>Selects the palette (with an optional accent swatch). Main rebuilds the UI after a change.</summary>
    public static void Use(string name, string? accent = null)
    {
        Spec = Themes.FirstOrDefault(t => t.Id == name) ?? Themes[0];
        Spec = WithSwatch(Spec, accent);
        _font = Spec.FontKind switch
        {
            "mono" => new FontVariation { BaseFont = new SystemFont { FontNames = MonoFonts }, SpacingGlyph = Nightgrid ? 1 : 0 },
            "pixel" => PixelFont(),
            "chakra" => ChakraFont(),
            _ => null,
        };
    }

    /// <summary>Pixelify Sans (SIL OFL 1.1, bundled under Assets/Fonts) with the mono font as fallback.</summary>
    private static Font PixelFont()
    {
        if (_pixel == null)
        {
            var file = ResourceLoader.Exists("res://Assets/Fonts/PixelifySans.ttf") ? GD.Load<FontFile>("res://Assets/Fonts/PixelifySans.ttf") : null;
            _pixel = file == null
                ? new SystemFont { FontNames = MonoFonts }
                : new FontVariation
                {
                    BaseFont = file,
                    VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = 500 },
                    // Its fi/fl ligatures read as odd single glyphs at small sizes.
                    OpentypeFeatures = new Godot.Collections.Dictionary
                    {
                        [TextServerManager.GetPrimaryInterface().NameToTag("liga")] = 0,
                        [TextServerManager.GetPrimaryInterface().NameToTag("clig")] = 0,
                    },
                    SpacingGlyph = 1,
                    Fallbacks = [new SystemFont { FontNames = MonoFonts }],
                };
        }
        return _pixel;
    }

    private static Font? _chakra;

    /// <summary>Drops the cached fonts at quit (see Main._ExitTree).</summary>
    public static void ReleaseFonts()
    {
        foreach (var f in new[] { _font, _pixel, _chakra })
        {
            f?.Dispose();
        }
        _font = _pixel = _chakra = null;
    }

    /// <summary>Chakra Petch SemiBold (SIL OFL 1.1, bundled under Assets/Fonts): squared-off, late-90s menu lettering.</summary>
    private static Font ChakraFont()
    {
        if (_chakra == null)
        {
            var file = ResourceLoader.Exists("res://Assets/Fonts/ChakraPetch-SemiBold.ttf") ? GD.Load<FontFile>("res://Assets/Fonts/ChakraPetch-SemiBold.ttf") : null;
            _chakra = file == null
                ? new SystemFont { FontNames = MonoFonts }
                : new FontVariation { BaseFont = file, SpacingGlyph = 1, Fallbacks = [new SystemFont { FontNames = MonoFonts }] };
        }
        return _chakra;
    }

    public const int FontSize = 20;
    public const int TitleSize = 34;
    public const int SmallSize = 15;

    /// <summary>
    /// Builds something with another theme's colours and font in place (for offscreen artwork),
    /// then puts the live theme back. Only what <paramref name="build"/> reads right away is affected.
    /// </summary>
    public static T With<T>(string themeId, Func<T> build)
    {
        var (spec, font) = (Spec, _font);
        Spec = Themes.FirstOrDefault(t => t.Id == themeId) ?? Themes[0];
        _font = Spec.FontKind switch { "chakra" => ChakraFont(), "pixel" => PixelFont(), _ => font };
        try
        {
            return build();
        }
        finally
        {
            (Spec, _font) = (spec, font);
        }
    }

    /// <summary>The font in use (theme default or Godot's fallback).</summary>
    public static Font CurrentFont => _font ?? ThemeDB.FallbackFont;

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = FontSize };
        if (_font != null)
        {
            t.DefaultFont = _font;
        }

        foreach (var type in new[] { "Button", "CheckButton" })
        {
            t.SetStylebox("normal", type, Box(Panel, 0));
            t.SetStylebox("hover", type, Box(PanelHi, 0));
            t.SetStylebox("pressed", type, Box(Spec.Light ? Panel.Darkened(0.12f) : Accent.Darkened(0.35f), 0));
            t.SetStylebox("disabled", type, Box(Panel.Darkened(0.3f), 0));
            // Focus is drawn by the shared, sliding FocusHighlight, not per control.
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Text);
            t.SetColor("font_focus_color", type, Bright);
            t.SetColor("font_pressed_color", type, Bright);
            t.SetColor("font_disabled_color", type, Spec.Light ? Dim.Lightened(0.2f) : Dim.Darkened(0.3f));
        }
        t.SetConstant("h_separation", "Button", 12);

        t.SetColor("font_color", "Label", Text);
        t.SetStylebox("panel", "PanelContainer", Box(Panel, 0));
        t.SetStylebox("panel", "Panel", Box(Panel, 0));

        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", bar, Thin(new Color(0, 0, 0, 0.15f)));
            t.SetStylebox("scroll_focus", bar, Thin(new Color(0, 0, 0, 0.15f)));
            t.SetStylebox("grabber", bar, Thin(Dim.Darkened(0.45f)));
            t.SetStylebox("grabber_highlight", bar, Thin(Dim));
            t.SetStylebox("grabber_pressed", bar, Thin(Accent));
        }
        return t;
    }

    public static StyleBoxFlat Box(Color bg, int border, int radius = 10, Color? borderColor = null)
    {
        var b = new StyleBoxFlat
        {
            BgColor = bg,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
            BorderColor = borderColor ?? Accent,
        };
        var r = Math.Min(radius, Spec.Radius);
        if (Spec.Chamfer)
        {
            // A one-segment "radius" is a straight cut.
            b.CornerDetail = 1;
        }
        else if (r <= 4)
        {
            b.CornerDetail = Math.Max(1, r);
        }
        b.SetCornerRadiusAll(r);
        var opaque = bg.A > 0.5f;
        if (border == 0 && opaque && Spec.Hairline > 0)
        {
            border = Spec.Hairline;
            b.BorderColor = borderColor ?? Line;
        }
        if (Spec.Bevel > 0 && opaque)
        {
            // Solid offset shadow, no blur: reads as a raised block. Only under opaque fills, so no dithering.
            b.ShadowColor = Spec.Light ? Line with { A = 0.75f } : new Color(0, 0, 0, 0.8f);
            b.ShadowSize = 2;
            b.ShadowOffset = new Vector2(Spec.Bevel * 0.5f, Spec.Bevel);
        }
        b.SetBorderWidthAll(border);
        b.AntiAliasing = Spec.Radius > 4 && !Spec.Chamfer;
        return b;
    }

    private static StyleBoxFlat Thin(Color c)
    {
        var b = new StyleBoxFlat { BgColor = c };
        b.SetCornerRadiusAll(3);
        b.ContentMarginLeft = b.ContentMarginRight = 3;
        b.ContentMarginTop = b.ContentMarginBottom = 3;
        return b;
    }

    /// <summary>
    /// Focus ring: a flat accent tint and one crisp accent border. No shadow: a StyleBox shadow
    /// is drawn under the whole box, so behind a translucent fill it showed as a dithered glow.
    /// </summary>
    public static StyleBoxFlat Focus()
    {
        var b = new StyleBoxFlat
        {
            BgColor = Accent with { A = Spec.Light ? 0.14f : Nightgrid ? 0.12f : 0.16f },
            BorderColor = Accent,
        };
        b.SetBorderWidthAll(3);
        var r = Math.Min(10, Spec.Radius);
        if (Spec.Chamfer)
        {
            b.CornerDetail = 1;
        }
        else if (r <= 4)
        {
            b.CornerDetail = Math.Max(1, r);
        }
        b.SetCornerRadiusAll(r);
        b.AntiAliasing = Spec.Radius > 4 && !Spec.Chamfer;
        return b;
    }

    /// <summary>A stable, muted tile colour per title so the grid isn't a wall of grey.</summary>
    public static Color TileColor(string seed)
    {
        uint h = 2166136261;
        foreach (var ch in seed)
        {
            h = (h ^ ch) * 16777619;
        }
        var (sat, val, from, span) = Spec.Tiles;
        return Color.FromHsv((from + h % (uint)span) / 360f, sat, val + h % 5 / 100f);
    }

    public static Label Label(string text, int size = FontSize, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
        {
            l.AddThemeColorOverride("font_color", c);
        }
        if (wrap)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        return l;
    }
}
