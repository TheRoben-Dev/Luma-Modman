using Godot;

namespace Luma.App.Ui;

/// <summary>Bottom bar: help for the focused control on the left, button hints on the right.</summary>
public partial class HintBar : HBoxContainer
{
    private readonly Label _help = UiTheme.Label("", UiTheme.SmallSize + 1, UiTheme.Dim);
    private readonly HBoxContainer _hints = new();

    public HintBar()
    {
        CustomMinimumSize = new Vector2(0, 40);
        _help.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _help.ClipText = true;
        _help.VerticalAlignment = VerticalAlignment.Center;
        AddChild(_help);
        _hints.AddThemeConstantOverride("separation", 22);
        AddChild(_hints);
    }

    private Tween? _helpTween;

    /// <summary>Help text crossfades: out quickly, swap, in.</summary>
    public void SetHelp(string text)
    {
        if (_help.Text == text)
        {
            return;
        }
        _helpTween?.Kill();
        if (!Anim.Enabled || !IsInsideTree())
        {
            _help.Text = text;
            _help.Modulate = Colors.White;
            return;
        }
        _helpTween = CreateTween();
        _helpTween.TweenProperty(_help, "modulate:a", 0f, Anim.Fast * 0.6);
        _helpTween.TweenCallback(Callable.From(() => _help.Text = text));
        _helpTween.TweenProperty(_help, "modulate:a", 1f, Anim.Normal);
    }

    private string _sig = "";

    public void SetHints(IEnumerable<(string Action, string Label)> hints)
    {
        var list = hints.ToList();
        var sig = string.Join("|", list.Select(h => Pad.Glyph(h.Action) + h.Label));
        if (sig == _sig)
        {
            return;
        }
        _sig = sig;
        hints = list;
        foreach (var c in _hints.GetChildren())
        {
            c.QueueFree();
        }
        foreach (var (action, label) in hints)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new ButtonGlyph(Pad.Glyph(action)));
            var text = UiTheme.Label(UiTheme.Mono ? label.ToUpperInvariant() : label, UiTheme.SmallSize + 1, UiTheme.Dim);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(text);
            _hints.AddChild(row);
        }
        if (Anim.Enabled && IsInsideTree())
        {
            _hints.Modulate = Colors.White with { A = 0.3f };
            CreateTween().TweenProperty(_hints, "modulate:a", 1f, Anim.Normal);
        }
    }
}
