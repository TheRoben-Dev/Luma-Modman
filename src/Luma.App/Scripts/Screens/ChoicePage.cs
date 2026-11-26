using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>A question with several answers; the first option is focused.</summary>
public partial class ChoicePage : Screen
{
    private readonly string _message;
    private readonly (string Label, Action Action)[] _options;
    private Button? _first;

    internal string Message => _message;
    internal (string Label, Action Action)[] Options => _options;

    public ChoicePage(string message, params (string Label, Action Action)[] options)
    {
        _message = message;
        _options = options;
    }

    protected override void Build()
    {
        var center = new CenterContainer();
        AddChild(center);
        var col = Column(18);
        col.CustomMinimumSize = new Vector2(640, 0);
        center.AddChild(col);
        col.AddChild(UiTheme.Label(_message, wrap: true));
        var buttons = new List<Control>();
        foreach (var (label, action) in _options)
        {
            var a = action;
            var b = new ActionButton(label, () =>
            {
                App.Pop();
                a();
            });
            col.AddChild(b);
            buttons.Add(b);
        }
        _first = buttons.FirstOrDefault() as Button;
        Focus.Column(buttons);
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
