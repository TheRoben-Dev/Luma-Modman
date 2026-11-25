using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Draws a controller/keyboard button glyph at a fixed shape: face buttons are true circles,
/// shoulders are pills, keyboard keys are keycaps. Drawn (not a padded label) so the shape
/// never stretches with the text width or the UI scale.
/// </summary>
public partial class ButtonGlyph : Control
{
    private const float Face = 28;
    private const float Height = 24;
    private string _text = "";

    public ButtonGlyph(string text = "")
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        SetGlyph(text);
    }

    public string Text => _text;

    private bool IsFace => _text.Length == 1 && !char.IsAsciiLetterLower(_text[0]) && !IsKeyboardKey;
    private static bool IsKeyboardKey => Pad.GlyphFamily == PadFamily.Keyboard;

    public void SetGlyph(string text)
    {
        _text = text;
        var font = GetThemeDefaultFont();
        var size = FontSize;
        var textW = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        CustomMinimumSize = IsFace ? new Vector2(Face, Face) : new Vector2(Mathf.Max(Height + 8, textW + 18), Height);
        QueueRedraw();
    }

    private int FontSize => IsFace ? UiTheme.SmallSize : UiTheme.SmallSize - 1;

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var fg = UiTheme.Text;
        var fill = UiTheme.PanelHi;
        var border = UiTheme.Dim.Darkened(0.3f);
        if (IsFace)
        {
            var d = Mathf.Min(Size.X, Size.Y);
            var c = Size / 2;
            var tint = FaceColor(_text);
            DrawCircle(c, d / 2, tint is { } t ? t.Darkened(0.55f) : fill);
            DrawArc(c, d / 2 - 0.75f, 0, Mathf.Tau, 48, tint ?? border, 1.5f, true);
            fg = tint?.Lightened(0.35f) ?? fg;
        }
        else
        {
            var box = new StyleBoxFlat { BgColor = fill, BorderColor = border, AntiAliasing = true };
            box.SetBorderWidthAll(1);
            // Shoulder buttons are pills; keyboard keys are keycaps.
            box.SetCornerRadiusAll(IsKeyboardKey ? 5 : (int)(Size.Y / 2));
            if (IsKeyboardKey)
            {
                box.BorderWidthBottom = 3;
            }
            DrawStyleBox(box, new Rect2(Vector2.Zero, Size));
        }
        var size = FontSize;
        var ts = font.GetStringSize(_text, HorizontalAlignment.Left, -1, size);
        var baseline = (Size.Y - ts.Y) / 2 + font.GetAscent(size) - (IsKeyboardKey ? 1 : 0);
        DrawString(font, new Vector2((Size.X - ts.X) / 2, baseline), _text, HorizontalAlignment.Left, -1, size, fg);
    }

    /// <summary>Familiar face-button colours (Xbox letters, PlayStation symbols); none for Nintendo/keyboard.</summary>
    private static Color? FaceColor(string g) => (Pad.GlyphFamily, g) switch
    {
        (PadFamily.Nintendo, _) => null,
        (_, "A") => new Color("5dc21e"),
        (_, "B") => new Color("e8463b"),
        (_, "X") => new Color("2f8bff"),
        (_, "Y") => new Color("f5c518"),
        (_, "✕") => new Color("7aa2ff"),
        (_, "○") => new Color("ff6b6b"),
        (_, "△") => new Color("3fd6a8"),
        (_, "□") => new Color("e589ff"),
        _ => null,
    };
}
