using Godot;

namespace Luma.App.Ui;

/// <summary>Nightgrid backdrop: a 16 px dot matrix with faint 128 px grid lines.</summary>
public partial class DotGrid : Control
{
    public DotGrid()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var dot = UiTheme.Line with { A = 0.8f * 0.55f };
        var line = UiTheme.Line with { A = 0.35f * 0.55f };
        for (var x = 0f; x < Size.X; x += 128)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), line, 1);
        }
        for (var y = 0f; y < Size.Y; y += 128)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), line, 1);
        }
        for (var y = 0f; y < Size.Y; y += 16)
        {
            for (var x = 0f; x < Size.X; x += 16)
            {
                DrawRect(new Rect2(x, y, 1.5f, 1.5f), dot);
            }
        }
    }
}
