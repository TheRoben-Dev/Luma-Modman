using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>Progress for background work (scan, download). B cancels when allowed.</summary>
public partial class BusyPage : Screen
{
    private readonly string _title;
    private readonly CancellationTokenSource? _cts;
    private ProgressBar? _bar;
    private Button? _cancel;

    public BusyPage(string title, CancellationTokenSource? cts)
    {
        _title = title;
        _cts = cts;
    }

    public override IEnumerable<(string Action, string Label)> Hints => [];

    protected override void Build()
    {
        var center = new CenterContainer();
        AddChild(center);
        var col = Column(18);
        col.CustomMinimumSize = new Vector2(560, 0);
        center.AddChild(col);
        col.AddChild(UiTheme.Label(_title, 26));
        _bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, Indeterminate = true, CustomMinimumSize = new Vector2(0, 14) };
        col.AddChild(_bar);
        if (_cts != null)
        {
            _cancel = new ActionButton("Cancel", () => _cts.Cancel());
            col.AddChild(_cancel);
        }
    }

    public void SetProgress(double p)
    {
        if (_bar == null)
        {
            return;
        }
        _bar.Indeterminate = p < 0;
        if (p >= 0)
        {
            _bar.Value = p;
        }
    }

    public override void FocusDefault() => _cancel?.GrabFocus();

    public override bool OnBack()
    {
        _cts?.Cancel();
        return true;  // the busy page closes itself when the work stops
    }
}
