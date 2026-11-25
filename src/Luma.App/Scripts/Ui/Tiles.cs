using Godot;

namespace Luma.App.Ui;

/// <summary>Cartridge tiles: a button as the hit area with a <see cref="Cartridge"/> drawing everything.</summary>
public static class Tiles
{
    public static Button Card(string title, string monogram, string? chips, string help, Action onPress, Vector2? size = null)
    {
        var b = new Button
        {
            CustomMinimumSize = size ?? new Vector2(168, 246),
            TooltipText = help,
        };
        // The button is only the hit area; the cartridge draws everything (focus included).
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            b.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }
        b.SetMeta(FocusHighlight.OwnFocus, true);
        var cart = new Cartridge(title, monogram, chips);
        cart.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        b.AddChild(cart);
        b.FocusEntered += () => cart.SetFocused(true);
        b.FocusExited += () => cart.SetFocused(false);
        b.MouseEntered += () =>
        {
            if (!b.HasFocus())
            {
                b.GrabFocus();
            }
        };
        Anim.PressFlash(b);
        b.Pressed += onPress;
        return b;
    }

    /// <summary>Up to two initials from a title ("Mountain Climb Deluxe" -> "MC").</summary>
    public static string Monogram(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]) && w.Length > 1 || char.IsDigit(w[0]))
            .ToList();
        var letters = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return letters.Length > 0 ? letters : title.Length > 0 ? title[..1].ToUpperInvariant() : "?";
    }
}
