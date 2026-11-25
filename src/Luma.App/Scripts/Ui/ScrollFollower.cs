using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Smooth replacement for ScrollContainer.FollowFocus: when focus moves inside the scroller,
/// the list glides (critically damped) until the focused control is fully visible with a
/// margin. It keeps following while the focused row grows (expanding help), and backs off as
/// soon as anything else scrolls the list (touch drag, momentum, mouse wheel) until the next
/// focus move. Added as a child of the ScrollContainer by Screen.Scroller.
/// </summary>
public partial class ScrollFollower : Node
{
    private const float Margin = 28;
    private ScrollContainer _s = null!;
    private bool _active = true;
    private float _pos;
    private int _lastSet = -1;
    private int _frames;
    private int? _restore;

    /// <summary>Scroll offset to restore on the first frames (after a rebuild).</summary>
    public void Restore(int value) => _restore = value;

    /// <summary>Stops following until focus moves again (a finger is scrolling).</summary>
    public static void Pause(ScrollContainer s)
    {
        foreach (var n in s.GetChildren())
        {
            if (n is ScrollFollower f)
            {
                f._active = false;
            }
        }
    }

    public override void _Ready()
    {
        _s = GetParent<ScrollContainer>();
        _s.FollowFocus = false;
        _pos = _s.ScrollVertical;
        GetViewport().GuiFocusChanged += OnFocus;
    }

    public override void _ExitTree()
    {
        if (GetViewport() is { } vp)
        {
            vp.GuiFocusChanged -= OnFocus;
        }
    }

    private void OnFocus(Control c)
    {
        if (IsInstanceValid(_s) && _s.IsAncestorOf(c))
        {
            TouchScroll.StopMomentum();
            _active = true;
            _pos = _s.ScrollVertical;
            _lastSet = _s.ScrollVertical;
        }
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_restore is { } r)
        {
            _s.ScrollVertical = r;
            _pos = _lastSet = _s.ScrollVertical;
            if (_frames > 3)
            {
                _restore = null;
            }
            return;
        }
        if (!_active)
        {
            return;
        }
        if (_lastSet >= 0 && _s.ScrollVertical != _lastSet)
        {
            _active = false;  // someone else scrolled (touch, wheel): leave it there
            return;
        }
        var focus = _s.GetViewport().GuiGetFocusOwner();
        if (focus == null || !_s.IsAncestorOf(focus) || _s.GetChildCount() == 0 || _s.GetChild(0) is not Control content)
        {
            return;
        }
        var top = focus.GetGlobalRect().Position.Y - content.GetGlobalRect().Position.Y;
        var bottom = top + focus.Size.Y;
        var view = _s.Size.Y;
        var target = _pos;
        if (bottom - top + 2 * Margin > view || top - Margin < _pos)
        {
            target = top - Margin;
        }
        else if (bottom + Margin > _pos + view)
        {
            target = bottom + Margin - view;
        }
        var max = Math.Max(0, content.Size.Y - view);
        target = Math.Clamp(target, 0, max);
        if (Math.Abs(target - _pos) < 0.5f)
        {
            return;
        }
        if (!Anim.Enabled || _frames < 15)
        {
            _pos = target;  // fresh page (or reduced motion): no glide
        }
        else
        {
            // Exponential approach (~140 ms to settle), at least one pixel per frame so it never stalls.
            var next = Mathf.Lerp(_pos, target, 1 - (float)Math.Exp(-delta * 22));
            _pos = Math.Abs(next - _pos) < 1 ? Mathf.MoveToward(_pos, target, 1) : next;
        }
        _s.ScrollVertical = (int)Math.Round(_pos);
        _lastSet = _s.ScrollVertical;
    }
}
