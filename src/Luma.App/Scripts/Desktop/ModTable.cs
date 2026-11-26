using Godot;

namespace Luma.App.Desktop;

/// <summary>A Tree whose rows can be dragged to a new place in the load order.</summary>
public partial class ModTable : Tree
{
    /// <summary>Whether rows can be dragged now (only while sorted by load order).</summary>
    public Func<bool> CanReorder { get; set; } = () => true;

    /// <summary>A row (mod id) was dropped before the row at this load-order index (Count = at the end).</summary>
    public event Action<string, int>? Dropped;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!CanReorder() || GetItemAtPosition(atPosition) is not { } item || item.GetMetadata(0).AsString() is not { Length: > 0 } id)
        {
            return default;
        }
        var preview = DesktopTheme.Label(item.GetText(2));
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", DesktopTheme.Frame(6));
        panel.AddChild(preview);
        SetDragPreview(panel);
        DropModeFlags = (int)DropModeFlagsEnum.Inbetween;
        return id;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => data.VariantType == Variant.Type.String && CanReorder();

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        DropModeFlags = 0;
        var id = data.AsString();
        var target = GetItemAtPosition(atPosition);
        int index;
        if (target == null)
        {
            index = GetRoot()?.GetChildCount() ?? 0;
        }
        else
        {
            index = target.GetMetadata(1).AsInt32() + (GetDropSectionAtPosition(atPosition) > 0 ? 1 : 0);
        }
        Dropped?.Invoke(id, index);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd)
        {
            DropModeFlags = 0;
        }
    }
}
