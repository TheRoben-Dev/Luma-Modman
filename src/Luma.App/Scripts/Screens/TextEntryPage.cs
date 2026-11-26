using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Text entry that works with a controller alone: an on-screen keyboard (Y deletes, X adds a
/// space), plus Paste for API keys copied in a browser. A real keyboard, and Steam's own keyboard
/// (Steam + X in Game Mode), type straight into the field. Secret text (keys) is masked apart from
/// its last four characters.
/// </summary>
public partial class TextEntryPage : Screen
{
    private static readonly string[] Lower = ["1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm-_.", ":/@&?=+#!,"];

    private readonly string _title;
    private readonly string _help;
    private readonly bool _secret;
    private readonly Action<string> _done;
    private string _text;
    private bool _shift;
    private Label? _field;
    private readonly List<Button> _letters = new();
    private Control? _first;

    public TextEntryPage(string title, string current, Action<string> done, string help = "", bool secret = false)
    {
        _title = title;
        _text = current;
        _done = done;
        _help = help;
        _secret = secret;
    }

    public override IEnumerable<(string Action, string Label)> Hints => [(Pad.Secondary, "Delete"), (Pad.Tertiary, "Space")];

    protected override void Build()
    {
        _letters.Clear();
        var center = new CenterContainer();
        AddChild(center);
        var col = Column(14);
        col.CustomMinimumSize = new Vector2(Mathf.Min(980, GetViewportRect().Size.X - 80), 0);
        center.AddChild(col);
        col.AddChild(UiTheme.Label(_title, UiTheme.TitleSize));
        if (_help.Length > 0)
        {
            col.AddChild(UiTheme.Label(_help, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        var fieldBox = new PanelContainer();
        var fb = UiTheme.Box(UiTheme.PanelHi, 2, 12, UiTheme.Accent);
        fb.ContentMarginLeft = fb.ContentMarginRight = 18;
        fb.ContentMarginTop = fb.ContentMarginBottom = 12;
        fieldBox.AddThemeStyleboxOverride("panel", fb);
        col.AddChild(fieldBox);
        _field = UiTheme.Label("", UiTheme.FontSize + 4);
        _field.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _field.ClipText = true;
        fieldBox.AddChild(_field);
        ShowText();

        var rows = new List<List<Button>>();
        foreach (var line in Lower)
        {
            var row = new List<Button>();
            foreach (var ch in line)
            {
                var c = ch;
                var b = Key(c.ToString(), () => Type(_shift ? char.ToUpperInvariant(c).ToString() : c.ToString()));
                b.SetMeta("ch", c.ToString());
                _letters.Add(b);
                row.Add(b);
            }
            rows.Add(row);
        }
        rows.Add([
            Key("Shift", ToggleShift, 2),
            Key("Space", () => Type(" "), 3),
            Key("Delete", Delete, 2),
            Key("Paste", Paste, 2),
            Key("Clear", () => SetText(""), 1),
        ]);
        var keys = Column(8);
        col.AddChild(keys);
        foreach (var row in rows)
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 8);
            foreach (var b in row)
            {
                h.AddChild(b);
            }
            keys.AddChild(h);
        }
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12);
        col.AddChild(actions);
        var done = new ActionButton("Done", Finish, "Use this text.") { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var cancel = new ActionButton("Cancel", () => App.Pop(), "Go back without changing anything.") { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actions.AddChild(done);
        actions.AddChild(cancel);
        rows.Add([done, cancel]);
        Wire(rows);
        _first = rows[1][0];
        UpdateShift();
    }

    private static Button Key(string label, Action press, int weight = 1)
    {
        var b = new Button
        {
            Text = label,
            CustomMinimumSize = new Vector2(0, 54),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = weight,
            FocusMode = FocusModeEnum.All,
        };
        b.Pressed += press;
        Anim.PressFlash(b);
        return b;
    }

    /// <summary>Up/down land on the key nearest in x, so rows of different widths feel natural.</summary>
    private static void Wire(List<List<Button>> rows)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var i = 0; i < row.Count; i++)
            {
                var b = row[i];
                var frac = (i + 0.5f) / row.Count;
                b.FocusNeighborLeft = (i > 0 ? row[i - 1] : row[^1]).GetPath();
                b.FocusNeighborRight = (i < row.Count - 1 ? row[i + 1] : row[0]).GetPath();
                b.FocusNeighborTop = (r > 0 ? Nearest(rows[r - 1], frac) : b).GetPath();
                b.FocusNeighborBottom = (r < rows.Count - 1 ? Nearest(rows[r + 1], frac) : b).GetPath();
                b.FocusPrevious = b.FocusNeighborLeft;
                b.FocusNext = b.FocusNeighborRight;
            }
        }
    }

    private static Button Nearest(List<Button> row, float frac)
    {
        // Weighted positions: wide keys cover more of the row.
        var total = row.Sum(b => b.SizeFlagsStretchRatio);
        var x = 0f;
        foreach (var b in row)
        {
            x += b.SizeFlagsStretchRatio;
            if (frac <= x / total + 0.0001f)
            {
                return b;
            }
        }
        return row[^1];
    }

    private void Type(string s)
    {
        if (_text.Length < 400)
        {
            SetText(_text + s);
        }
        if (_shift)
        {
            ToggleShift();
        }
    }

    private void Delete()
    {
        if (_text.Length > 0)
        {
            SetText(_text[..^1]);
        }
    }

    private void Paste()
    {
        var clip = DisplayServer.ClipboardGet().Trim().Replace("\r", "").Replace("\n", "");
        if (clip.Length == 0)
        {
            App.Toast("The clipboard is empty. Copy the text in your browser first.");
            return;
        }
        SetText(_text + clip);
    }

    private void ToggleShift()
    {
        _shift = !_shift;
        UpdateShift();
    }

    private void UpdateShift()
    {
        foreach (var b in _letters)
        {
            var ch = (string)b.GetMeta("ch");
            b.Text = _shift ? ch.ToUpperInvariant() : ch;
        }
    }

    private void SetText(string s)
    {
        _text = s;
        ShowText();
    }

    private void ShowText()
    {
        if (_field == null)
        {
            return;
        }
        var shown = _secret && _text.Length > 4 ? new string('•', Math.Min(24, _text.Length - 4)) + _text[^4..] : _text;
        _field.Text = shown + "▏";
    }

    private void Finish()
    {
        var text = _text.Trim();
        App.Pop();
        _done(text);
    }

    public override void OnAction(string action)
    {
        if (action == Pad.Secondary)
        {
            Delete();
        }
        else if (action == Pad.Tertiary)
        {
            Type(" ");
        }
    }

    /// <summary>A real (or Steam's) keyboard types into the field; arrows and Escape keep navigating.</summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k || App.Top != this)
        {
            return;
        }
        if (k.CtrlPressed && k.Keycode == Godot.Key.V)
        {
            Paste();
        }
        else if (k.Keycode == Godot.Key.Backspace)
        {
            Delete();
        }
        else if (k.Keycode is Godot.Key.Enter or Godot.Key.KpEnter)
        {
            Finish();
        }
        else if (k.Unicode >= 32 && !k.CtrlPressed && !k.AltPressed)
        {
            SetText(_text + char.ConvertFromUtf32((int)k.Unicode));
        }
        else
        {
            return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
