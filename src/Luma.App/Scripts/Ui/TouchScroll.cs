using System.Runtime.CompilerServices;
using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Touchscreen behaviour for handhelds. Under gamescope/Steam the Deck's touchscreen usually
/// arrives as mouse events, so this works on those too:
/// - a finger that moves more than a few pixels in any direction is a swipe, never a press: the
///   press on whatever is under it is cancelled, and over a list the list scrolls (with momentum);
/// - touching a list while it is still flinging only stops it, like on a phone;
/// - the mouse cursor is never drawn (clicks and taps still work);
/// - holding a finger still on a row that allows it (<see cref="AllowReorder"/>) picks the row up,
///   and moving it then drags it to a new place in its list instead of scrolling.
/// </summary>
public static class TouchScroll
{
    /// <summary>A list whose rows can be dragged to a new place by touch.</summary>
    public interface IReorder
    {
        /// <summary>The row was picked up (held still long enough).</summary>
        void BeginReorder(Control row);

        /// <summary>The finger moved to <paramref name="at"/> (viewport coordinates).</summary>
        void MoveReorder(Control row, Vector2 at);

        /// <summary>The finger lifted: put the row where it is now.</summary>
        void DropReorder(Control row);
    }

    /// <summary>How long a finger has to rest on a row before it's picked up.</summary>
    public const double HoldSeconds = 0.45;

    private static readonly ConditionalWeakTable<Control, IReorder> Reorderable = new();
    private static Control? _holdRow;
    private static Viewport? _vp;
    private static ulong _pressUs;
    private static bool _reordering;

    /// <summary>Lets <paramref name="row"/> be picked up by a held finger and dragged; <paramref name="owner"/> moves it.</summary>
    public static void AllowReorder(Control row, IReorder owner) => Reorderable.AddOrUpdate(row, owner);

    /// <summary>A row is being dragged right now.</summary>
    public static bool Reordering => _reordering;
    /// <summary>Movement (in UI pixels, any direction) after which a press becomes a swipe.</summary>
    public const float Deadzone = 10;

    private const float StopFlingSpeed = 150;

    private static Vector2 _start;
    private static Vector2 _last;
    private static ScrollContainer? _target;
    private static bool _down;
    private static bool _dragging;
    private static bool _swallowRelease;
    private static float _velocity;
    private static ulong _lastMoveUs;
    private static Control? _focusBeforePress;
    private static bool _injecting;

    /// <summary>Call from Main._Input before anything else. True = event consumed.</summary>
    public static bool Handle(Viewport vp, InputEvent e)
    {
        if (_injecting)
        {
            return false;  // our own synthetic release: let the GUI have it
        }
        HideCursor();
        return e switch
        {
            // Godot turns real touches into mouse events (emulate_mouse_from_touch), handled below.
            InputEventMouseButton { ButtonIndex: MouseButton.Left } mb => OnButton(vp, mb),
            InputEventMouseMotion mm => OnMotion(vp, mm),
            _ => false,
        };
    }

    private static bool OnButton(Viewport vp, InputEventMouseButton mb)
    {
        if (mb.Pressed)
        {
            var flinging = Math.Abs(_velocity) > StopFlingSpeed && _target != null && GodotObject.IsInstanceValid(_target);
            _down = true;
            _dragging = false;
            _velocity = 0;
            _start = _last = mb.Position;
            _target = ScrollerAt(vp, mb.Position);
            _focusBeforePress = vp.GuiGetFocusOwner();
            _lastMoveUs = _pressUs = Time.GetTicksUsec();
            _vp = vp;
            _holdRow = flinging ? null : ReorderRowAt(mb.Position);
            // A touch on a moving list just stops it; it doesn't press the row that slid under the finger.
            _swallowRelease = flinging;
            return flinging;
        }
        if (!_down)
        {
            return false;
        }
        _down = false;
        _holdRow = null;
        if (_reordering)
        {
            _reordering = false;
            if (_reorderRow != null && GodotObject.IsInstanceValid(_reorderRow) && Reorderable.TryGetValue(_reorderRow, out var owner))
            {
                owner.DropReorder(_reorderRow);
            }
            _reorderRow = null;
            _dragging = false;
            _swallowRelease = false;
            return true;
        }
        if (_dragging || _swallowRelease)
        {
            if (_dragging && !_swallowRelease)
            {
                RestoreFocus(vp);
            }
            _dragging = false;
            _swallowRelease = false;
            return true;  // swallow the release so the row under the finger doesn't fire
        }
        return false;
    }

    private static bool OnMotion(Viewport vp, InputEventMouseMotion mm)
    {
        if (!_down)
        {
            return false;  // hover: nothing to do, the cursor is hidden anyway
        }
        if (_reordering)
        {
            _last = mm.Position;
            if (_reorderRow != null && GodotObject.IsInstanceValid(_reorderRow) && Reorderable.TryGetValue(_reorderRow, out var owner))
            {
                owner.MoveReorder(_reorderRow, mm.Position);
            }
            return true;
        }
        if (_swallowRelease && !_dragging)
        {
            // Stopping a fling: the press never reached the GUI, so only scrolling can follow.
            if ((mm.Position - _start).Length() > Deadzone)
            {
                _dragging = true;
                if (_target != null && GodotObject.IsInstanceValid(_target))
                {
                    ScrollFollower.Pause(_target);
                }
            }
        }
        else if (!_dragging && (mm.Position - _start).Length() > Deadzone)
        {
            _dragging = true;
            CancelPress(vp);
        }
        if (!_dragging)
        {
            return false;
        }
        var dy = mm.Position.Y - _last.Y;
        _last = mm.Position;
        if (_target == null || !GodotObject.IsInstanceValid(_target))
        {
            return true;  // a swipe outside any list: just make sure nothing gets pressed
        }
        _target.ScrollVertical -= (int)Math.Round(dy);
        var now = Time.GetTicksUsec();
        var dt = Math.Max(1, now - _lastMoveUs) / 1_000_000f;
        _lastMoveUs = now;
        _velocity = Mathf.Clamp(Mathf.Lerp(_velocity, -dy / dt, 0.4f), -3000f, 3000f);
        return true;
    }

    /// <summary>Releases the press far away (so nothing fires) and puts focus back where it was.</summary>
    private static void CancelPress(Viewport vp)
    {
        // The GUI never saw the finger move (we eat the motion), so the pressed button still thinks
        // it's hovered and would fire on any release. Move the pointer off it first, then release.
        var away = new Vector2(-10000, -10000);
        _injecting = true;
        vp.PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away, ButtonMask = MouseButtonMask.Left });
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = away, GlobalPosition = away });
        _injecting = false;
        _lastMoveUs = Time.GetTicksUsec();
        RestoreFocus(vp);
        // Then stop following focus, so restoring it doesn't pull the list back under the finger.
        if (_target != null && GodotObject.IsInstanceValid(_target))
        {
            ScrollFollower.Pause(_target);
        }
    }

    /// <summary>
    /// The press also focused the row under the finger; a swipe shouldn't move focus. Done when the
    /// swipe starts and again when it ends, because the press's own focus change can land in between.
    /// </summary>
    private static void RestoreFocus(Viewport vp)
    {
        if (_focusBeforePress != null && GodotObject.IsInstanceValid(_focusBeforePress) && _focusBeforePress.IsVisibleInTree() &&
            vp.GuiGetFocusOwner() != _focusBeforePress)
        {
            _focusBeforePress.GrabFocus();
        }
    }

    /// <summary>Cancels leftover fling momentum (the d-pad took over).</summary>
    public static void StopMomentum()
    {
        if (!_down)
        {
            _velocity = 0;
        }
    }

    private static Control? _reorderRow;

    /// <summary>Momentum after the finger lifts, and picking rows up. Call every frame.</summary>
    public static void Process(double delta)
    {
        if (_down && !_dragging && !_reordering && _holdRow != null && _vp != null
            && (Time.GetTicksUsec() - _pressUs) / 1_000_000.0 >= HoldSeconds)
        {
            StartReorder();
        }
        if (_reordering)
        {
            EdgeScroll(delta);
            return;
        }
        if (_down || Math.Abs(_velocity) < 20 || _target == null || !GodotObject.IsInstanceValid(_target))
        {
            if (!_down)
            {
                _velocity = 0;
            }
            return;
        }
        _target.ScrollVertical += (int)Math.Round(_velocity * delta);
        _velocity *= (float)Math.Pow(0.05, delta);  // ~95% of the speed gone after a second
    }

    private static void StartReorder()
    {
        var row = _holdRow!;
        _holdRow = null;
        if (!GodotObject.IsInstanceValid(row) || !row.IsVisibleInTree() || !Reorderable.TryGetValue(row, out var owner))
        {
            return;
        }
        CancelPress(_vp!);  // the row mustn't fire when the finger lifts
        _reordering = true;
        _reorderRow = row;
        _velocity = 0;
        owner.BeginReorder(row);
    }

    // While dragging a row near the top or bottom of its list, the list scrolls to bring more rows in.
    private static void EdgeScroll(double delta)
    {
        if (_target == null || !GodotObject.IsInstanceValid(_target) || _reorderRow == null || !GodotObject.IsInstanceValid(_reorderRow))
        {
            return;
        }
        var r = _target.GetGlobalRect();
        const float edge = 70;
        var speed = _last.Y < r.Position.Y + edge ? -600 : _last.Y > r.End.Y - edge ? 600 : 0;
        if (speed != 0)
        {
            _target.ScrollVertical += (int)Math.Round(speed * delta);
            if (Reorderable.TryGetValue(_reorderRow, out var owner))
            {
                owner.MoveReorder(_reorderRow, _last);
            }
        }
    }

    private static Control? ReorderRowAt(Vector2 pos)
    {
        foreach (var (row, _) in Reorderable)
        {
            if (GodotObject.IsInstanceValid(row) && row.IsVisibleInTree() && row.GetGlobalRect().HasPoint(pos))
            {
                return row;
            }
        }
        return null;
    }

    private static void HideCursor()
    {
        if (Input.MouseMode != Input.MouseModeEnum.Hidden)
        {
            Input.MouseMode = Input.MouseModeEnum.Hidden;
        }
    }

    private static ScrollContainer? ScrollerAt(Viewport vp, Vector2 pos)
    {
        // The deepest visible scroller under the point.
        ScrollContainer? found = null;
        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is ScrollContainer s && s.IsVisibleInTree() && s.GetGlobalRect().HasPoint(pos) && !BlockedByFocusOff(s))
                {
                    found = s;
                }
                Walk(c);
            }
        }
        Walk(vp);
        return found;
    }

    // Pages behind an overlay card have focus turned off; don't scroll them.
    private static bool BlockedByFocusOff(ScrollContainer s) => s.HasMeta("ed_focus");
}
