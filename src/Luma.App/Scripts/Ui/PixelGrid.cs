using Godot;

namespace Luma.App.Ui;

/// <summary>Pocket LCD backdrop: the faint gaps between the pixels of a dot-matrix screen.</summary>
public partial class PixelGrid : Control
{
    public PixelGrid()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 90;  // over the panels too, like the real thing; very faint so text stays crisp
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var gap = UiTheme.Line with { A = 0.07f };
        for (var x = 0f; x < Size.X; x += 4)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), gap, 1);
        }
        for (var y = 0f; y < Size.Y; y += 4)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), gap, 1);
        }
    }
}
