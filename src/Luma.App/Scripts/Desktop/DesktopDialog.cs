using Godot;

namespace Luma.App.Desktop;

/// <summary>
/// A modal dialog window for the desktop layout: title bar, a body and a row of buttons at the
/// bottom right. Esc or the close button dismisses it.
/// </summary>
public partial class DesktopDialog : Window
{
    public VBoxContainer Body { get; }
    public HBoxContainer Buttons { get; }

    /// <summary>The user closed the window (close button or Esc).</summary>
    public event Action? Dismissed;

    private readonly Vector2I _size;

    public DesktopDialog(string title, Vector2I size)
    {
        Title = title;
        _size = size;
        Transient = true;
        Exclusive = true;
        Unresizable = false;
        WrapControls = true;
        Visible = false;
        MinSize = new Vector2I(Math.Min(size.X, 360), 140);
        var bg = new PanelContainer();
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var box = DesktopTheme.Frame(16);
        box.SetBorderWidthAll(0);
        box.SetCornerRadiusAll(0);
        bg.AddThemeStyleboxOverride("panel", box);
        AddChild(bg);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        bg.AddChild(col);
        Body = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 8);
        col.AddChild(Body);
        Buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Buttons.AddThemeConstantOverride("separation", 8);
        col.AddChild(Buttons);
        CloseRequested += () => Dismissed?.Invoke();
        WindowInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            {
                Dismissed?.Invoke();
            }
        };
    }

    /// <summary>Height to fit the contents (dialogs opened with no fixed height), centred again.</summary>
    public void Fit()
    {
        if (!IsInstanceValid(this) || _size.Y > 0)
        {
            return;
        }
        Size = new Vector2I(_size.X, (int)GetContentsMinimumSize().Y);
        MoveToCenter();
    }

    /// <summary>Adds a button to the bottom row. The primary one is drawn in the accent colour.</summary>
    public Button AddButton(string text, Action pressed, bool primary = false, string tooltip = "")
    {
        var b = DesktopTheme.Button(text, pressed, tooltip, primary ? "PrimaryButton" : "");
        b.CustomMinimumSize = new Vector2(88, 0);
        Buttons.AddChild(b);
        return b;
    }

    public Label AddText(string text, Color? color = null, int size = 0)
    {
        var l = DesktopTheme.Label(text, color, size, wrap: true);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // Wrapped text needs a width to work out its height, so the window can size itself to it.
        l.CustomMinimumSize = new Vector2(_size.X - 34, 0);
        Body.AddChild(l);
        return l;
    }

    /// <summary>Shows it centred over the main window; <paramref name="focus"/> gets the keyboard.</summary>
    public void Open(Control? focus = null)
    {
        PopupCentered(_size.Y > 0 ? _size : new Vector2I(_size.X, 120));
        if (_size.Y == 0)
        {
            Callable.From(Fit).CallDeferred();
        }
        var target = focus ?? Buttons.GetChildren().OfType<Button>().LastOrDefault();
        target?.CallDeferred(Control.MethodName.GrabFocus);
    }
}
