using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>A centred title, explanation and a column of actions: what a tab shows when it has nothing yet.</summary>
public static class EmptyState
{
    /// <summary>Adds the block to <paramref name="parent"/> and returns its focusable rows (first = default focus).</summary>
    public static List<Control> Build(Control parent, string title, string body, params (string Label, Action Act, string Help)[] actions)
    {
        var center = new CenterContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        parent.AddChild(center);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        col.AddThemeConstantOverride("separation", 16);
        center.AddChild(col);
        col.AddChild(UiTheme.Label(title, 30));
        col.AddChild(UiTheme.Label(body, UiTheme.SmallSize + 2, UiTheme.Dim, wrap: true));
        var items = new List<Control>();
        foreach (var (label, act, help) in actions)
        {
            var b = new ActionButton(label, act, help);
            col.AddChild(b);
            items.Add(b);
        }
        if (items.Count > 0)
        {
            Focus.Column(items);
        }
        return items;
    }
}
