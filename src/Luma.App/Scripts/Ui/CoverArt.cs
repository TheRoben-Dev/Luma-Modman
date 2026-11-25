using Godot;
using Luma.Host;

namespace Luma.App.Ui;

/// <summary>
/// A game's portrait cover (Steam's 600x900 library art), smoothly scaled into a rounded rectangle.
/// Without art it shows a plain card in the game's colour with its initials, so every game still
/// has a recognisable face.
/// </summary>
public partial class CoverArt : Control
{
    private static readonly Dictionary<string, ImageTexture?> Cache = new();

    private readonly string _title;
    private readonly ImageTexture? _tex;
    private readonly float _radius;

    public CoverArt(string title, string? path, Vector2 size, float radius = 8)
    {
        _title = title;
        _radius = radius;
        _tex = Load(path);
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        Resized += QueueRedraw;
    }

    private static ImageTexture? Load(string? path)
    {
        if (path == null)
        {
            return null;
        }
        if (Cache.TryGetValue(path, out var t))
        {
            return t;
        }
        ImageTexture? tex = null;
        try
        {
            var img = Image.LoadFromFile(path);
            if (img != null && !img.IsEmpty())
            {
                img.GenerateMipmaps();
                tex = ImageTexture.CreateFromImage(img);
            }
        }
        catch (Exception e)
        {
            AppLog.Warn($"cover art {path}: {e.Message}");
        }
        Cache[path] = tex;
        return tex;
    }

    /// <summary>Frees cached textures while .NET is still running (see Main._ExitTree).</summary>
    public static void ReleaseShared() => Cache.Clear();

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        var pts = Rounded(r, Mathf.Min(_radius, Mathf.Min(Size.X, Size.Y) / 2));
        if (_tex != null)
        {
            DrawPolygon(pts, new[] { Colors.White }, CoverUvs(pts, r, _tex.GetSize()), _tex);
        }
        else
        {
            var tile = UiTheme.TileColor(_title);
            DrawColoredPolygon(pts, tile);
            var font = GetThemeDefaultFont();
            var initials = Initials(_title);
            var size = (int)(Mathf.Min(Size.X, Size.Y) * 0.36f);
            var ts = font.GetStringSize(initials, HorizontalAlignment.Left, -1, size);
            DrawString(font, new Vector2((Size.X - ts.X) / 2, (Size.Y - ts.Y) / 2 + font.GetAscent(size)), initials,
                HorizontalAlignment.Left, -1, size, tile.Lightened(0.55f));
        }
        DrawPolyline(pts.Append(pts[0]).ToArray(), UiTheme.Line with { A = 0.6f }, 1, true);
    }

    /// <summary>First letters of the first two words that start with a letter or digit ("Baldur's Gate 3" → "BG").</summary>
    public static string Initials(string title)
    {
        var words = title.Split(new[] { ' ', ':', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]) && w is not ("The" or "of" or "the"))
            .ToList();
        return words.Count switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0]))),
        };
    }

    private static Vector2[] Rounded(Rect2 r, float radius)
    {
        var pts = new List<Vector2>();
        void Arc(Vector2 c, float from)
        {
            for (var i = 0; i <= 6; i++)
            {
                var a = from + i * Mathf.Pi / 2 / 6;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }
        Arc(r.Position + new Vector2(radius, radius), Mathf.Pi);
        Arc(new Vector2(r.End.X - radius, r.Position.Y + radius), -Mathf.Pi / 2);
        Arc(r.End - new Vector2(radius, radius), 0);
        Arc(new Vector2(r.Position.X + radius, r.End.Y - radius), Mathf.Pi / 2);
        return pts.ToArray();
    }

    /// <summary>UVs that fill <paramref name="r"/> with the texture, cropping the overflow evenly (CSS "cover").</summary>
    internal static Vector2[] CoverUvs(Vector2[] pts, Rect2 r, Vector2 texSize)
    {
        var rectAspect = r.Size.X / r.Size.Y;
        var texAspect = texSize.X / texSize.Y;
        var span = rectAspect >= texAspect ? new Vector2(1, texAspect / rectAspect) : new Vector2(rectAspect / texAspect, 1);
        var start = (Vector2.One - span) / 2;
        return pts.Select(p => start + (p - r.Position) / r.Size * span).ToArray();
    }
}
