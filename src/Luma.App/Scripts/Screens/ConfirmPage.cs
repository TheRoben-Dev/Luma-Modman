using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>Yes/No for steps that replace files. "No" is focused first.</summary>
public partial class ConfirmPage : Screen
{
    private readonly string _message;
    private readonly string _yes;
    private readonly Action _onYes;
    private Button? _no;

    internal string Message => _message;
    internal string Yes => _yes;
    internal Action OnYes => _onYes;

    public ConfirmPage(string message, string yes, Action onYes)
    {
        _message = message;
        _yes = yes;
        _onYes = onYes;
    }

    protected override void Build()
    {
        var center = new CenterContainer();
        AddChild(center);
        var col = Column(18);
        col.CustomMinimumSize = new Vector2(640, 0);
        center.AddChild(col);
        col.AddChild(UiTheme.Label(_message, wrap: true));
        var yes = new ActionButton(_yes, () =>
        {
            App.Pop();
            _onYes();
        });
        _no = new ActionButton("Cancel", App.Pop);
        col.AddChild(_no);
        col.AddChild(yes);
        Focus.Column([_no, yes]);
    }

    public override void FocusDefault() => _no?.GrabFocus();
}
