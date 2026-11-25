using Godot;

namespace Luma.App.Ui;

/// <summary>Plain full-width action button with an optional help line.</summary>
public partial class ActionButton : Button
{
    public ActionButton(string text, Action onPress, string help = "")
    {
        Text = text;
        TooltipText = help;
        Alignment = HorizontalAlignment.Left;
        CustomMinimumSize = new Vector2(0, 54);
        Pressed += onPress;
        Anim.PressFlash(this);
    }
}
