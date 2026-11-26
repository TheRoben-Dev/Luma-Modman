using Luma.App.Ui;
using Godot;

namespace Luma.App.Desktop;

/// <summary>
/// The desktop layout's look: a plain, dense theme in the style of a desktop mod manager (small
/// system font, flat panels, thin borders, standard widgets). Dark or light follows the theme
/// picked in Settings; the accent colour comes from it too. Nothing here is shared with the
/// controller layout's 10-foot theme.
/// </summary>
public static class DesktopTheme
{
    public const int FontSize = 14;
    public const int SmallSize = 12;

    public static Color Bg { get; private set; }
    public static Color Panel { get; private set; }
    public static Color Field { get; private set; }
    public static Color Header { get; private set; }
    public static Color Line { get; private set; }
    public static Color Text { get; private set; }
    public static Color Dim { get; private set; }
    public static Color Accent { get; private set; }
    public static Color Selection { get; private set; }
    public static Color Good { get; private set; }
    public static Color Warn { get; private set; }
    public static Color Bad { get; private set; }
    public static bool Light { get; private set; }

    private static Font? _font;
    private static Font? _mono;

    /// <summary>Monospace font for the log pane and launch options.</summary>
    public static Font Mono => _mono ??= new SystemFont { FontNames = ["Cascadia Mono", "Consolas", "JetBrains Mono", "DejaVu Sans Mono", "Liberation Mono", "monospace"] };

    public static void ReleaseFonts()
    {
        _font = null;
        _mono = null;
    }

    private static void Palette()
    {
        Light = UiTheme.Spec.Light || UiTheme.Name == "pocket";
        var accent = UiTheme.Accent;
        if (Light)
        {
            Bg = new Color("f3f3f5");
            Panel = new Color("ffffff");
            Field = new Color("ffffff");
            Header = new Color("e8e8ec");
            Line = new Color("cfd0d6");
            Text = new Color("1d1f24");
            Dim = new Color("62666f");
            Good = new Color("1f8a4c");
            Warn = new Color("a8650a");
            Bad = new Color("c0392b");
            Accent = accent.Luminance > 0.55f ? accent.Darkened(0.35f) : accent;
        }
        else
        {
            Bg = new Color("1e1f23");
            Panel = new Color("26272c");
            Field = new Color("1a1b1f");
            Header = new Color("2e3036");
            Line = new Color("3a3c43");
            Text = new Color("dfe1e6");
            Dim = new Color("969aa4");
            Good = new Color("5fcf8a");
            Warn = new Color("e9b15a");
            Bad = new Color("ef7070");
            Accent = accent.Luminance < 0.25f ? accent.Lightened(0.3f) : accent;
        }
        Selection = Accent with { A = Light ? 0.22f : 0.35f };
    }

    private static StyleBoxFlat Flat(Color bg, int border = 0, Color? borderColor = null, int radius = 3, int padX = 6, int padY = 4)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = borderColor ?? Line };
        b.SetBorderWidthAll(border);
        b.SetCornerRadiusAll(radius);
        b.ContentMarginLeft = b.ContentMarginRight = padX;
        b.ContentMarginTop = b.ContentMarginBottom = padY;
        return b;
    }

    /// <summary>A panel style for a framed area (lists, the detail pane).</summary>
    public static StyleBoxFlat Frame(int pad = 8) => Flat(Panel, 1, Line, 3, pad, pad);

    public static Theme Build()
    {
        Palette();
        _font ??= new SystemFont { FontNames = ["Segoe UI", "Inter", "Noto Sans", "Cantarell", "Ubuntu", "DejaVu Sans", "sans-serif"] };
        var t = new Theme { DefaultFontSize = FontSize, DefaultFont = _font };
        var hover = Light ? Header.Darkened(0.04f) : Header.Lightened(0.06f);
        var focus = Flat(Colors.Transparent, 1, Accent, 3);
        focus.DrawCenter = false;

        t.SetColor("font_color", "Label", Text);
        t.SetStylebox("panel", "Panel", Flat(Bg, 0, null, 0, 0, 0));
        t.SetStylebox("panel", "PanelContainer", Flat(Panel, 1, Line, 3, 8, 8));
        t.SetColor("default_color", "RichTextLabel", Text);
        t.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());
        t.SetStylebox("focus", "RichTextLabel", new StyleBoxEmpty());

        foreach (var type in new[] { "Button", "OptionButton", "MenuButton", "CheckBox", "CheckButton" })
        {
            var plain = type is "CheckBox" or "CheckButton";
            t.SetStylebox("normal", type, plain ? Flat(Colors.Transparent, 0, null, 3, 4, 3) : Flat(Header, 1, Line, 3, 10, 4));
            t.SetStylebox("hover", type, plain ? Flat(Selection with { A = 0.15f }, 0, null, 3, 4, 3) : Flat(hover, 1, Line, 3, 10, 4));
            t.SetStylebox("pressed", type, plain ? Flat(Colors.Transparent, 0, null, 3, 4, 3) : Flat(Selection, 1, Accent, 3, 10, 4));
            t.SetStylebox("hover_pressed", type, plain ? Flat(Colors.Transparent, 0, null, 3, 4, 3) : Flat(Selection, 1, Accent, 3, 10, 4));
            t.SetStylebox("disabled", type, plain ? Flat(Colors.Transparent, 0, null, 3, 4, 3) : Flat(Header with { A = 0.5f }, 1, Line with { A = 0.5f }, 3, 10, 4));
            t.SetStylebox("focus", type, focus);
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Text);
            t.SetColor("font_pressed_color", type, Text);
            t.SetColor("font_hover_pressed_color", type, Text);
            t.SetColor("font_focus_color", type, Text);
            t.SetColor("font_disabled_color", type, Dim with { A = 0.6f });
            t.SetColor("icon_normal_color", type, Text);
            t.SetConstant("h_separation", type, 6);
        }
        // Toolbar buttons: flat until hovered.
        t.SetTypeVariation("ToolButton", "Button");
        t.SetStylebox("normal", "ToolButton", Flat(Colors.Transparent, 0, null, 3, 8, 4));
        t.SetStylebox("hover", "ToolButton", Flat(hover, 1, Line, 3, 8, 4));
        t.SetStylebox("pressed", "ToolButton", Flat(Selection, 1, Accent, 3, 8, 4));
        t.SetStylebox("disabled", "ToolButton", Flat(Colors.Transparent, 0, null, 3, 8, 4));
        // Primary action (Download, Install).
        t.SetTypeVariation("PrimaryButton", "Button");
        t.SetStylebox("normal", "PrimaryButton", Flat(Accent, 1, Accent.Darkened(0.2f), 3, 12, 5));
        t.SetStylebox("hover", "PrimaryButton", Flat(Accent.Lightened(0.1f), 1, Accent, 3, 12, 5));
        t.SetStylebox("pressed", "PrimaryButton", Flat(Accent.Darkened(0.15f), 1, Accent, 3, 12, 5));
        var onAccent = Accent.Luminance > 0.6f ? new Color("111111") : Colors.White;
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
        {
            t.SetColor(c, "PrimaryButton", onAccent);
        }
        t.SetTypeVariation("HeaderLabel", "Label");
        t.SetFontSize("font_size", "HeaderLabel", FontSize + 4);
        t.SetTypeVariation("DimLabel", "Label");
        t.SetColor("font_color", "DimLabel", Dim);
        t.SetFontSize("font_size", "DimLabel", SmallSize);
        t.SetTypeVariation("PaneTitle", "Label");
        t.SetColor("font_color", "PaneTitle", Dim);
        t.SetFontSize("font_size", "PaneTitle", SmallSize);

        foreach (var type in new[] { "LineEdit", "TextEdit", "SpinBox" })
        {
            t.SetStylebox("normal", type, Flat(Field, 1, Line, 3, 6, 4));
            t.SetStylebox("focus", type, focus);
            t.SetStylebox("read_only", type, Flat(Bg, 1, Line, 3, 6, 4));
            t.SetColor("font_color", type, Text);
            t.SetColor("font_placeholder_color", type, Dim with { A = 0.8f });
            t.SetColor("caret_color", type, Text);
            t.SetColor("selection_color", type, Selection);
        }

        foreach (var type in new[] { "Tree", "ItemList" })
        {
            t.SetStylebox("panel", type, Flat(Field, 1, Line, 3, 1, 1));
            t.SetStylebox("focus", type, focus);
            t.SetStylebox("selected", type, Flat(Selection, 0, null, 0, 2, 2));
            t.SetStylebox("selected_focus", type, Flat(Selection, 0, null, 0, 2, 2));
            t.SetStylebox("cursor", type, Flat(Colors.Transparent, 1, Accent with { A = 0.6f }, 0));
            t.SetStylebox("cursor_unfocused", type, new StyleBoxEmpty());
            t.SetStylebox("hovered", type, Flat(Selection with { A = 0.12f }, 0, null, 0));
            t.SetColor("font_color", type, Text);
            t.SetColor("font_selected_color", type, Text);
            t.SetColor("font_hovered_color", type, Text);
            t.SetColor("guide_color", type, Line with { A = 0.5f });
            t.SetConstant("v_separation", type, 4);
            t.SetConstant("h_separation", type, 6);
        }
        t.SetStylebox("title_button_normal", "Tree", Flat(Header, 0, null, 0, 6, 4));
        t.SetStylebox("title_button_hover", "Tree", Flat(hover, 0, null, 0, 6, 4));
        t.SetStylebox("title_button_pressed", "Tree", Flat(Selection, 0, null, 0, 6, 4));
        t.SetColor("title_button_color", "Tree", Dim);
        t.SetFontSize("title_button_font_size", "Tree", SmallSize);
        t.SetColor("drop_position_color", "Tree", Accent);
        t.SetConstant("item_margin", "Tree", 12);
        t.SetConstant("inner_item_margin_left", "Tree", 4);
        t.SetConstant("draw_guides", "Tree", 0);
        t.SetConstant("draw_relationship_lines", "Tree", 0);
        t.SetColor("font_outline_color", "Tree", Colors.Transparent);

        t.SetStylebox("panel", "TabContainer", Flat(Panel, 1, Line, 0, 0, 0));
        t.SetStylebox("tabbar_background", "TabContainer", Flat(Bg, 0, null, 0, 0, 0));
        foreach (var type in new[] { "TabContainer", "TabBar" })
        {
            t.SetStylebox("tab_selected", type, Flat(Panel, 0, null, 0, 14, 6) .WithTop(Accent, 2));
            t.SetStylebox("tab_unselected", type, Flat(Bg, 0, null, 0, 14, 6));
            t.SetStylebox("tab_hovered", type, Flat(hover, 0, null, 0, 14, 6));
            t.SetStylebox("tab_focus", type, focus);
            t.SetColor("font_selected_color", type, Text);
            t.SetColor("font_unselected_color", type, Dim);
            t.SetColor("font_hovered_color", type, Text);
        }

        t.SetStylebox("normal", "MenuBar", Flat(Colors.Transparent, 0, null, 2, 8, 3));
        t.SetStylebox("hover", "MenuBar", Flat(hover, 0, null, 2, 8, 3));
        t.SetStylebox("pressed", "MenuBar", Flat(Selection, 0, null, 2, 8, 3));
        t.SetStylebox("disabled", "MenuBar", Flat(Colors.Transparent, 0, null, 2, 8, 3));
        t.SetColor("font_color", "MenuBar", Text);
        t.SetColor("font_hover_color", "MenuBar", Text);
        t.SetColor("font_pressed_color", "MenuBar", Text);

        var popup = Flat(Panel, 1, Line, 3, 2, 4);
        popup.ShadowColor = new Color(0, 0, 0, Light ? 0.18f : 0.45f);
        popup.ShadowSize = 8;
        popup.ShadowOffset = new Vector2(0, 3);
        t.SetStylebox("panel", "PopupMenu", popup);
        t.SetStylebox("hover", "PopupMenu", Flat(Selection, 0, null, 2, 6, 2));
        t.SetStylebox("separator", "PopupMenu", LineBox());
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Text);
        t.SetColor("font_disabled_color", "PopupMenu", Dim with { A = 0.6f });
        t.SetColor("font_accelerator_color", "PopupMenu", Dim);
        t.SetColor("font_separator_color", "PopupMenu", Dim);
        t.SetConstant("v_separation", "PopupMenu", 6);
        t.SetConstant("item_start_padding", "PopupMenu", 10);
        t.SetConstant("item_end_padding", "PopupMenu", 14);
        t.SetStylebox("panel", "PopupPanel", popup);
        t.SetStylebox("panel", "TooltipPanel", Flat(Header, 1, Line, 3, 8, 5));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetFontSize("font_size", "TooltipLabel", SmallSize);

        var win = Flat(Panel, 1, Line, 4, 0, 0);
        win.ExpandMarginTop = 30;
        win.ExpandMarginLeft = win.ExpandMarginRight = win.ExpandMarginBottom = 1;
        win.BorderWidthTop = 30;
        win.BorderColor = Header;
        win.ShadowColor = new Color(0, 0, 0, Light ? 0.25f : 0.55f);
        win.ShadowSize = 16;
        win.ShadowOffset = new Vector2(0, 6);
        t.SetStylebox("embedded_border", "Window", win);
        var winUnfocused = (StyleBoxFlat)win.Duplicate();
        winUnfocused.ShadowSize = 6;
        t.SetStylebox("embedded_unfocused_border", "Window", winUnfocused);
        t.SetColor("title_color", "Window", Text);
        t.SetFontSize("title_font_size", "Window", FontSize);
        t.SetConstant("title_height", "Window", 30);
        t.SetStylebox("panel", "AcceptDialog", Flat(Panel, 0, null, 0, 14, 12));
        t.SetConstant("buttons_separation", "AcceptDialog", 8);

        t.SetStylebox("background", "ProgressBar", Flat(Field, 1, Line, 3, 0, 0));
        t.SetStylebox("fill", "ProgressBar", Flat(Accent, 0, null, 3, 0, 0));
        t.SetColor("font_color", "ProgressBar", Text);
        t.SetFontSize("font_size", "ProgressBar", SmallSize);

        t.SetStylebox("separator", "HSeparator", LineBox());
        t.SetStylebox("separator", "VSeparator", LineBox(vertical: true));
        t.SetConstant("separation", "HSeparator", 6);
        t.SetConstant("separation", "VSeparator", 8);
        t.SetConstant("separation", "HSplitContainer", 6);
        t.SetConstant("separation", "VSplitContainer", 6);
        t.SetConstant("minimum_grab_thickness", "HSplitContainer", 8);
        t.SetConstant("minimum_grab_thickness", "VSplitContainer", 8);

        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", bar, Flat(Colors.Transparent, 0, null, 0, 4, 4));
            t.SetStylebox("scroll_focus", bar, Flat(Colors.Transparent, 0, null, 0, 4, 4));
            t.SetStylebox("grabber", bar, Flat(Line, 0, null, 4, 4, 4));
            t.SetStylebox("grabber_highlight", bar, Flat(Dim with { A = 0.6f }, 0, null, 4, 4, 4));
            t.SetStylebox("grabber_pressed", bar, Flat(Accent, 0, null, 4, 4, 4));
        }
        return t;
    }

    private static StyleBoxLine LineBox(bool vertical = false) => new() { Color = Line, Thickness = 1, Vertical = vertical };

    private static StyleBoxFlat WithTop(this StyleBoxFlat b, Color color, int width)
    {
        b.BorderColor = color;
        b.BorderWidthTop = width;
        return b;
    }

    /// <summary>A label in the desktop theme's colours.</summary>
    public static Label Label(string text, Color? color = null, int size = 0, bool wrap = false)
    {
        var l = new Label { Text = text };
        if (size > 0)
        {
            l.AddThemeFontSizeOverride("font_size", size);
        }
        if (color is { } c)
        {
            l.AddThemeColorOverride("font_color", c);
        }
        if (wrap)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(40, 0);
        }
        return l;
    }

    public static Button Button(string text, Action pressed, string tooltip = "", string variation = "")
    {
        var b = new Button { Text = text, TooltipText = tooltip, ThemeTypeVariation = variation, FocusMode = Control.FocusModeEnum.All };
        b.Pressed += pressed;
        return b;
    }
}
