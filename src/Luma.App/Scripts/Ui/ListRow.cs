using Godot;

namespace Luma.App.Ui;

/// <summary>
/// A full-width focusable row: optional leading picture, a title, a dim detail line and a status
/// chip on the right. The building block of Luma's lists (games, sites, mods).
/// </summary>
public partial class ListRow : Button
{
    private readonly Label _status;

    public ListRow(string title, string detail, string? status = null, Color? statusColor = null, Control? leading = null,
        Action? onPress = null, string help = "")
    {
        TooltipText = help;
        CustomMinimumSize = new Vector2(0, leading == null ? 64 : 84);
        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pad.AddThemeConstantOverride("margin_left", 14);
        pad.AddThemeConstantOverride("margin_right", 16);
        pad.AddThemeConstantOverride("margin_top", 8);
        pad.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(pad);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 16);
        pad.AddChild(row);
        if (leading != null)
        {
            row.AddChild(leading);
        }
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 2);
        row.AddChild(text);
        var t = UiTheme.Label(title, UiTheme.FontSize);
        t.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.AddChild(t);
        if (detail.Length > 0)
        {
            var d = UiTheme.Label(detail, UiTheme.SmallSize, UiTheme.Dim);
            d.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            text.AddChild(d);
        }
        _status = UiTheme.Label(status ?? "", UiTheme.SmallSize, statusColor ?? UiTheme.AccentText);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Visible = status != null;
        row.AddChild(_status);
        foreach (var c in new Control[] { t, _status })
        {
            c.MouseFilter = MouseFilterEnum.Ignore;
        }
        if (onPress != null)
        {
            Pressed += onPress;
        }
        Anim.PressFlash(this);
    }

    public void SetStatus(string? status, Color? color = null)
    {
        _status.Text = status ?? "";
        _status.Visible = status != null;
        _status.AddThemeColorOverride("font_color", color ?? UiTheme.AccentText);
    }
}
