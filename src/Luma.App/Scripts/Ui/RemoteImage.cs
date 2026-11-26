using System.Security.Cryptography;
using System.Text;
using Luma.Core.Sources;
using Luma.Host;
using Godot;

namespace Luma.App.Ui;

/// <summary>
/// A picture from a mod site (icon, screenshot). Shows a soft placeholder with the initials while
/// it loads, then fades the image in. Files are cached on disk so lists don't download the same
/// icon twice, and at most four pictures download at once.
/// </summary>
public partial class RemoteImage : Control
{
    private static readonly SemaphoreSlim Slots = new(4);
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<string, ImageTexture> Memory = new();
    private const int MemoryLimit = 160;

    private readonly string? _url;
    private readonly string _initials;
    private readonly float _radius;
    private TextureRect? _image;

    public RemoteImage(string? url, string name, Vector2 size, float radius = 10)
    {
        _url = url;
        _radius = radius;
        _initials = string.Concat(name.Split(' ', '-', '_').Where(w => w.Length > 0).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public static string CacheDir => Path.Combine(Main.Instance.Host!.Paths.ArtCacheDir, "remote");

    public override void _Ready()
    {
        var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bg.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.TileColor(_initials + _url).Darkened(0.35f), 0, (int)_radius));
        AddChild(bg);
        var label = UiTheme.Label(_initials, (int)Mathf.Clamp(CustomMinimumSize.Y * 0.32f, 12, 40), UiTheme.Bright);
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(label);
        _image = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _image.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_image);
        if (_url is { Length: > 0 })
        {
            Load(_url);
        }
    }

    private async void Load(string url)
    {
        if (Memory.TryGetValue(url, out var cached))
        {
            Show(cached, fade: false);
            return;
        }
        byte[]? bytes;
        try
        {
            bytes = await Task.Run(() => Fetch(url));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            return;  // the placeholder stays
        }
        if (bytes == null || !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }
        var img = Decode(bytes);
        if (img == null)
        {
            return;
        }
        // Keep memory small: icons are shown at ~64-200 px.
        var max = Mathf.Max(256, (int)(Mathf.Max(CustomMinimumSize.X, CustomMinimumSize.Y) * 2));
        if (img.GetWidth() > max || img.GetHeight() > max)
        {
            var scale = (float)max / Mathf.Max(img.GetWidth(), img.GetHeight());
            img.Resize(Mathf.Max(1, (int)(img.GetWidth() * scale)), Mathf.Max(1, (int)(img.GetHeight() * scale)), Image.Interpolation.Bilinear);
        }
        var tex = ImageTexture.CreateFromImage(img);
        if (Memory.Count > MemoryLimit)
        {
            Memory.Clear();
        }
        Memory[url] = tex;
        Show(tex, fade: true);
    }

    private void Show(Texture2D tex, bool fade)
    {
        if (_image == null)
        {
            return;
        }
        _image.Texture = tex;
        if (fade && Anim.Enabled)
        {
            CreateTween().TweenProperty(_image, "modulate:a", 1f, Anim.Slow);
        }
        else
        {
            _image.Modulate = Colors.White;
        }
    }

    private static async Task<byte[]?> Fetch(string url)
    {
        var file = Path.Combine(CacheDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant());
        if (File.Exists(file))
        {
            return await File.ReadAllBytesAsync(file);
        }
        await Slots.WaitAsync();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", HttpGate.UserAgent);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 8 * 1024 * 1024)
            {
                return null;
            }
            Directory.CreateDirectory(CacheDir);
            await File.WriteAllBytesAsync(file + ".tmp", bytes);
            File.Move(file + ".tmp", file, overwrite: true);
            return bytes;
        }
        finally
        {
            Slots.Release();
        }
    }

    /// <summary>PNG, JPEG or WebP, by their first bytes (sites don't always name files truthfully).</summary>
    private static Image? Decode(byte[] b)
    {
        var img = new Image();
        Error err;
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50)
        {
            err = img.LoadPngFromBuffer(b);
        }
        else if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8)
        {
            err = img.LoadJpgFromBuffer(b);
        }
        else if (b.Length > 12 && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
        {
            err = img.LoadWebpFromBuffer(b);
        }
        else
        {
            return null;
        }
        return err == Error.Ok && !img.IsEmpty() ? img : null;
    }

    /// <summary>Size of the picture cache on disk, for Settings.</summary>
    public static long CacheBytes() =>
        Directory.Exists(CacheDir) ? new DirectoryInfo(CacheDir).EnumerateFiles().Sum(f => f.Length) : 0;

    public static void ClearCache()
    {
        Memory.Clear();
        if (Directory.Exists(CacheDir))
        {
            Directory.Delete(CacheDir, recursive: true);
        }
    }
}
