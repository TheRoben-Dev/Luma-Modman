using Godot;
using static Luma.App.Ui.Poly.Ps1;

namespace Luma.App.Ui.Poly;

/// <summary>
/// Library cartridges as real low-poly 3D objects for the "32-bit 3D" theme. The 2D grid stays in
/// charge of layout, focus and input (same navigation as every theme); this draws a 3D cart over
/// each tile, pinned to the tile's position every frame, at half resolution with the era's
/// rendering. The focused cart slides toward you and sways; pressing one spins it toward the
/// screen before the game card opens. A single-cart mode turns one cart on a turntable (game card).
/// </summary>
public partial class CartStage : PolyStage
{
    private const float CamDistance = 12;
    private const int CellW = 64;
    private const int CellH = 80;
    private const int AtlasCols = 16;

    private sealed class Cart
    {
        public required Control? Tile;
        public required Node3D Root;
        public float Focus;
        public float Launch = -1;
        public Action? OnLaunched;
        public float Phase;
    }

    private readonly List<Cart> _carts = new();
    private readonly Control? _clip;
    private readonly bool _single;
    private readonly SubViewport _atlas;
    private readonly LabelArt _labels;

    private static ArrayMesh? _shellMesh;

    public static void ReleaseShared()
    {
        _shellMesh?.Dispose();
        _shellMesh = null;
    }

    /// <summary>Grid mode: one cart per tile, drawn inside <paramref name="clip"/> (the scroller).</summary>
    public CartStage(IReadOnlyList<(Control Tile, string Title)> tiles, Control clip) : this(tiles.Select(t => t.Title).ToList(), single: false, null)
    {
        _clip = clip;
        for (var i = 0; i < tiles.Count; i++)
        {
            _carts[i].Tile = tiles[i].Tile;
        }
    }

    /// <summary>Single mode: one cart filling this control, turning slowly.</summary>
    /// <param name="icon">The game's own square icon: shown pixelated on the label instead of the generated art.</param>
    public CartStage(string title, Font? labelFont = null, Image? icon = null) : this([title], single: true, labelFont, icon)
    {
    }

    private CartStage(List<string> titles, bool single, Font? labelFont = null, Image? icon = null) : base(transparent: true)
    {
        _single = single;
        Resolution = 0.5f;
        Look.Fog = UiTheme.Bg;
        Look.FogNear = 40;
        Look.FogFar = 80;
        Look.LightDir = new Vector3(-0.35f, -0.55f, -0.75f);
        Look.Ambient = new Color(0.5f, 0.5f, 0.58f);
        Camera.Fov = 30;
        Camera.Position = new Vector3(0, 0, CamDistance);

        _atlas = new SubViewport
        {
            Size = new Vector2I(AtlasCols * CellW, Math.Max(1, (titles.Count + AtlasCols - 1) / AtlasCols) * CellH),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = true,
        };
        _labels = new LabelArt(titles, labelFont, icon);
        _atlas.AddChild(_labels);
        AddChild(_atlas);
        var atlasTex = _atlas.GetTexture();

        var shell = Look.Mat(UiTheme.Spec.Shell);
        var dark = Look.Mat(UiTheme.Spec.Shell.Darkened(0.45f));
        var ridge = Look.Mat(UiTheme.Spec.Shell.Lightened(0.12f));
        _shellMesh ??= ShellMesh();
        var label = Quad(0.62f, 0.56f);
        var ridgeMesh = Box(0.34f, 0.025f, 0.02f);
        var slotMesh = Box(0.4f, 0.05f, 0.1f);
        var cellScale = new Vector2((float)CellW / _atlas.Size.X, (float)CellH / _atlas.Size.Y);
        for (var i = 0; i < titles.Count; i++)
        {
            var root = new Node3D();
            World.AddChild(root);
            var body = new Node3D { Name = "Body" };
            root.AddChild(body);
            Part(body, _shellMesh, shell, Vector3.Zero);
            Part(body, slotMesh, dark, new Vector3(0.1f, 0.49f, 0));
            for (var r = 0; r < 3; r++)
            {
                Part(body, ridgeMesh, ridge, new Vector3(0, -0.34f - r * 0.045f, 0.085f));
            }
            var labelMat = Look.Mat(Colors.White, atlasTex);
            labelMat.SetShaderParameter("uv_scale", cellScale);
            labelMat.SetShaderParameter("uv_offset", new Vector2(i % AtlasCols * cellScale.X, i / AtlasCols * cellScale.Y));
            // The label sits a hair in front of the shell (no depth bias tricks, as in the era).
            Part(body, label, labelMat, new Vector3(0, 0.08f, 0.0815f));
            _carts.Add(new Cart { Tile = null, Root = root, Phase = i * 0.73f });
        }
    }

    /// <summary>Puts a game's own icon on cart <paramref name="index"/>'s label (redraws the label atlas once).</summary>
    public void SetIcon(int index, Image? icon)
    {
        if (index < 0 || index >= _carts.Count || icon == null || icon.IsEmpty())
        {
            return;
        }
        _labels.SetIcon(index, icon);
        _atlas.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    /// <summary>Cart outline: 0.8 x 1.0 with the top-left corner cut (the cartridge shape), extruded.</summary>
    private static ArrayMesh ShellMesh()
    {
        const float w = 0.4f;
        const float h = 0.5f;
        const float cut = 0.16f;
        const float b = 0.04f;
        Vector2[] outline =
        [
            new(-w, h - cut), new(-w, -h + b), new(-w + b, -h), new(w - b, -h), new(w, -h + b),
            new(w, h - b), new(w - b, h), new(-w + cut, h),
        ];
        return Extrude(outline, 0.16f);
    }

    protected override Rect2 ViewRect =>
        _clip != null && IsInstanceValid(_clip) ? new Rect2(_clip.GlobalPosition - GlobalPosition, _clip.Size) : new Rect2(Vector2.Zero, Size);

    /// <summary>Spins the tile's cart toward the screen, then runs <paramref name="done"/>.</summary>
    public void Launch(Control tile, Action done)
    {
        var cart = _carts.FirstOrDefault(c => c.Tile == tile);
        if (cart == null || !Anim.Enabled || Paused)
        {
            done();
            return;
        }
        if (cart.Launch >= 0)
        {
            return;
        }
        cart.Launch = 0;
        cart.OnLaunched = done;
        // The spin runs in the per-frame update; if that ever stalls (stage held, cart scrolled
        // out of view), the game still opens.
        GetTree().CreateTimer(0.6).Timeout += () =>
        {
            if (cart.OnLaunched == done && IsInstanceValid(this) && IsInstanceValid(tile))
            {
                cart.Launch = -1;
                cart.OnLaunched = null;
                done();
            }
        };
    }

    protected override void Tick(float dt)
    {
    }

    protected override void Follow()
    {
        var dt = (float)GetProcessDeltaTime();
        var vr = ViewRect;
        var upp = UnitsPerPixel(CamDistance);
        foreach (var c in _carts)
        {
            Vector2 center;
            float heightPx;
            if (_single)
            {
                center = vr.GetCenter();
                heightPx = vr.Size.Y * 0.86f;
            }
            else
            {
                if (c.Tile == null || !IsInstanceValid(c.Tile) || !c.Tile.IsVisibleInTree())
                {
                    c.Root.Visible = false;
                    continue;
                }
                var r = c.Tile.GetGlobalRect();
                r.Position -= GlobalPosition;
                // Off-screen rows: skip.
                if (r.End.Y < vr.Position.Y - 40 || r.Position.Y > vr.End.Y + 40)
                {
                    c.Root.Visible = false;
                    continue;
                }
                center = new Vector2(r.GetCenter().X, r.Position.Y + r.Size.Y * 0.37f);
                heightPx = r.Size.Y * 0.64f;
            }
            c.Root.Visible = true;
            var focused = _single || c.Tile!.HasFocus();
            var target = focused ? 1f : 0f;
            c.Focus = Anim.Enabled ? Mathf.MoveToward(c.Focus, target, dt * 5) : target;
            var f = Mathf.SmoothStep(0, 1, c.Focus);
            var basePos = ToWorld(center, CamDistance);
            var s = heightPx * upp;
            var motion = Anim.Enabled ? 1f : 0f;
            var t = T + c.Phase;
            Vector3 rot;
            var pos = basePos;
            if (_single)
            {
                // Showcase: turns side to side so the label stays in view, with a slow bob.
                rot = new Vector3(-6 + Mathf.Sin(T * 0.7f) * 4 * motion, -18 + Mathf.Sin(T * 0.55f) * 34 * motion, Mathf.Sin(T * 0.4f) * 3 * motion);
                pos += new Vector3(0, Mathf.Sin(T * 1.1f) * s * 0.02f * motion, 0);
            }
            else
            {
                // Idle carts lean back a touch and breathe; the focused one comes forward and sways.
                rot = new Vector3(
                    -10 * (1 - f) + Mathf.Sin(t * 1.3f) * 4 * f * motion,
                    Mathf.Sin(t * 0.6f) * 3 * (1 - f) * motion + Mathf.Sin(t * 1.7f) * 22 * f * motion,
                    Mathf.Sin(t * 0.8f) * 1.5f * motion);
                // Toward the camera along its line of sight, so it grows in place instead of drifting.
                pos = Camera.Position + (pos - Camera.Position) * (1 - s * 0.6f * f / CamDistance);
            }
            if (c.Launch >= 0)
            {
                c.Launch += dt;
                var a = Mathf.Clamp(c.Launch / 0.32f, 0, 1);
                rot.Y += 360 * a * a;
                pos = Camera.Position + (pos - Camera.Position) * (1 - s * 1.6f * a * a / CamDistance);
                if (a >= 1)
                {
                    var done = c.OnLaunched;
                    c.Launch = -1;
                    c.OnLaunched = null;
                    done?.Invoke();
                }
            }
            c.Root.Position = pos;
            c.Root.RotationDegrees = rot;
            c.Root.Scale = Vector3.One * s;
        }
    }

    /// <summary>Cover labels, drawn once into the atlas: era-style printed art with the game's monogram.</summary>
    private partial class LabelArt : Control
    {
        private readonly List<string> _titles;
        private readonly Font? _font;
        private readonly ImageTexture?[] _icons;

        public LabelArt(List<string> titles, Font? font, Image? icon = null)
        {
            _titles = titles;
            _font = font;
            _icons = new ImageTexture?[titles.Count];
            // 32x32, drawn with nearest filtering: chunky pixels that suit the era.
            TextureFilter = TextureFilterEnum.Nearest;
            if (icon != null && !icon.IsEmpty() && titles.Count > 0)
            {
                SetIcon(0, icon);
            }
            Size = new Vector2(AtlasCols * CellW, Math.Max(1, (titles.Count + AtlasCols - 1) / AtlasCols) * CellH);
        }

        public void SetIcon(int index, Image icon)
        {
            var small = (Image)icon.Duplicate();
            // Wide icons (PS3's ICON0 is 320x176): keep the middle square instead of squashing it.
            if (small.GetWidth() != small.GetHeight())
            {
                var side = Math.Min(small.GetWidth(), small.GetHeight());
                small = small.GetRegion(new Rect2I((small.GetWidth() - side) / 2, (small.GetHeight() - side) / 2, side, side));
            }
            if (small.GetWidth() != 32 || small.GetHeight() != 32)
            {
                small.Resize(32, 32, Image.Interpolation.Bilinear);
            }
            _icons[index] = ImageTexture.CreateFromImage(small);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var font = _font ?? UiTheme.CurrentFont;
            for (var i = 0; i < _titles.Count; i++)
            {
                var o = new Vector2(i % AtlasCols * CellW, i / AtlasCols * CellH);
                Paint(o, _titles[i], font, _icons[i]);
            }
        }

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (var ch in s)
            {
                h = (h ^ ch) * 16777619;
            }
            return h;
        }

        private void Paint(Vector2 o, string title, Font font, ImageTexture? icon)
        {
            if (icon != null)
            {
                // Fill the whole label: the quad is 0.62 x 0.56 (wider than tall), so the square icon is
                // cropped top and bottom to that shape, centred, then stretched over the 64 x 80 cell.
                const float labelAspect = 0.62f / 0.56f;
                var iconSize = icon.GetSize();
                var srcH = iconSize.Y / labelAspect;
                DrawTextureRectRegion(icon, new Rect2(o, new Vector2(CellW, CellH)), new Rect2(0, (iconSize.Y - srcH) / 2, iconSize.X, srcH));
                return;
            }
            var h = Hash(title);
            var hue = h % 360 / 360f;
            var top = Color.FromHsv(hue, 0.55f, 0.85f);
            var bottom = Color.FromHsv(Mathf.PosMod(hue + 0.08f, 1), 0.75f, 0.35f);
            for (var y = 0; y < CellH; y++)
            {
                DrawRect(new Rect2(o.X, o.Y + y, CellW, 1), top.Lerp(bottom, y / (float)CellH));
            }
            var ink = Color.FromHsv(Mathf.PosMod(hue + 0.5f, 1), 0.5f, 1f, 0.35f);
            var c = o + new Vector2(CellW / 2f, CellH * 0.45f);
            switch (h / 360 % 5)
            {
                case 0:  // diagonal stripes
                    for (var k = -CellH; k < CellW; k += 10)
                    {
                        DrawLine(o + new Vector2(k, CellH), o + new Vector2(k + CellH, 0), ink, 3);
                    }
                    break;
                case 1:  // rings
                    for (var r = 6; r < 50; r += 8)
                    {
                        DrawArc(c, r, 0, Mathf.Tau, 20, ink, 2);
                    }
                    break;
                case 2:  // sunset: sun and horizon lines
                    DrawCircle(c + new Vector2(0, -4), 16, Color.FromHsv(0.1f, 0.6f, 1f, 0.6f));
                    for (var k = 0; k < 6; k++)
                    {
                        DrawRect(new Rect2(o.X, c.Y + 4 + k * 5, CellW, 1 + k * 0.6f), bottom with { A = 0.9f });
                    }
                    break;
                case 3:  // perspective grid
                    for (var k = 0; k <= 8; k++)
                    {
                        DrawLine(c + new Vector2(0, 2), o + new Vector2(k * CellW / 8f, CellH), ink, 1);
                    }
                    for (var k = 1; k < 6; k++)
                    {
                        var yy = c.Y + 2 + k * k * 1.2f;
                        DrawLine(new Vector2(o.X, yy), new Vector2(o.X + CellW, yy), ink, 1);
                    }
                    break;
                default:  // starburst
                    for (var k = 0; k < 12; k++)
                    {
                        var a = k * Mathf.Tau / 12;
                        DrawLine(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 60, ink, 3);
                    }
                    break;
            }
            // Monogram, big and bold with a hard drop shadow.
            var mono = Luma.App.Ui.Tiles.Monogram(title);
            const int size = 30;
            var ms = font.GetStringSize(mono, HorizontalAlignment.Left, -1, size);
            var at = new Vector2(c.X - ms.X / 2, c.Y - ms.Y / 2 + font.GetAscent(size));
            DrawString(font, at + new Vector2(2, 2), mono, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, 0.7f));
            DrawString(font, at, mono, HorizontalAlignment.Left, -1, size, Colors.White);
            // Printed title band along the bottom, and a thin stripe along the top.
            DrawRect(new Rect2(o.X, o.Y + CellH - 15, CellW, 15), new Color(0.05f, 0.05f, 0.08f, 0.85f));
            DrawString(font, new Vector2(o.X + 3, o.Y + CellH - 4), title.ToUpperInvariant(), HorizontalAlignment.Left, CellW - 6, 9, Colors.White);
            DrawRect(new Rect2(o.X, o.Y + 3, CellW, 3), Color.FromHsv(Mathf.PosMod(hue + 0.5f, 1), 0.7f, 0.95f));
        }
    }
}
