using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Explicit focus wiring. Godot's automatic neighbour search picks the "closest" control, which
/// jumps diagonally in grids and between columns; every screen wires its focus graph explicitly.
/// A neighbour pointing at the control itself blocks movement in that direction.
/// Call only after the controls are inside the scene tree.
/// </summary>
public static class Focus
{
    private static NodePath P(Control c) => c.GetPath();

    /// <summary>Vertical list. Left/right are blocked unless given.</summary>
    public static void Column(IReadOnlyList<Control> items, Control? above = null, Control? below = null, Control? left = null, Control? right = null)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var c = items[i];
            c.FocusNeighborTop = P(i > 0 ? items[i - 1] : above ?? c);
            c.FocusNeighborBottom = P(i < items.Count - 1 ? items[i + 1] : below ?? c);
            c.FocusNeighborLeft = P(left ?? c);
            c.FocusNeighborRight = P(right ?? c);
            c.FocusPrevious = c.FocusNeighborTop;
            c.FocusNext = c.FocusNeighborBottom;
        }
    }

    /// <summary>Horizontal row. Up/down go to the given controls or are blocked.</summary>
    public static void Row(IReadOnlyList<Control> items, Control? above = null, Control? below = null)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var c = items[i];
            c.FocusNeighborLeft = P(i > 0 ? items[i - 1] : c);
            c.FocusNeighborRight = P(i < items.Count - 1 ? items[i + 1] : c);
            c.FocusNeighborTop = P(above ?? c);
            c.FocusNeighborBottom = P(below ?? c);
            c.FocusPrevious = c.FocusNeighborLeft;
            c.FocusNext = c.FocusNeighborRight;
        }
    }

    /// <summary>
    /// Row-major grid. Moving down from a column with no item below lands on the last item
    /// (straight down where possible, never a diagonal skip across rows).
    /// </summary>
    public static void Grid(IReadOnlyList<Control> items, int columns, Control? above = null)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var c = items[i];
            var col = i % columns;
            c.FocusNeighborLeft = P(col > 0 ? items[i - 1] : c);
            c.FocusNeighborRight = P(col < columns - 1 && i + 1 < items.Count ? items[i + 1] : c);
            c.FocusNeighborTop = P(i - columns >= 0 ? items[i - columns] : above ?? c);
            var down = i + columns;
            var lastRowStart = (items.Count - 1) / columns * columns;
            c.FocusNeighborBottom = P(down < items.Count ? items[down] : i < lastRowStart ? items[^1] : c);
            c.FocusPrevious = P(i > 0 ? items[i - 1] : c);
            c.FocusNext = P(i < items.Count - 1 ? items[i + 1] : c);
        }
    }
}
