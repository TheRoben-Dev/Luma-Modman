using Luma.Host;
using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Controller layout (full screen, sized for a TV or a Steam Deck) or desktop layout (a resizable
/// window, denser, with right-click and keyboard shortcuts). "auto" picks the controller layout in
/// Steam's Game Mode / Big Picture and the desktop layout everywhere else, Windows included.
/// </summary>
public static class UiLayout
{
    public const string Auto = "auto";
    public const string Controller = "controller";
    public const string DesktopId = "desktop";

    /// <summary>Desktop layout: UI scale relative to the controller layout.</summary>
    private const float DesktopScale = 0.9f;

    /// <summary>True while the desktop layout is in use.</summary>
    public static bool Desktop { get; private set; }

    public static readonly (string Id, string Label)[] Choices =
    [
        (Auto, "Automatic"),
        (Controller, "Controller"),
        (DesktopId, "Desktop"),
    ];

    public static string Describe(string id) => id switch
    {
        Controller => "Full screen with big rows, made for a controller or touch. Like in Game Mode.",
        DesktopId => "A resizable window with more on screen, right-click for options and keyboard shortcuts.",
        _ => $"Controller layout in Game Mode, desktop layout on the desktop. Right now: {(Resolve(Auto) ? "desktop" : "controller")}.",
    };

    /// <summary>Whether <paramref name="setting"/> means the desktop layout on this session.</summary>
    public static bool Resolve(string? setting) => setting switch
    {
        Controller => false,
        DesktopId => true,
        _ => !SteamSetup.InGameMode && !OS.GetCmdlineUserArgs().Contains("--smoke"),
    };

    /// <summary>Sets up the window for the layout. <paramref name="windowed"/> keeps the controller layout in a window (testing).</summary>
    public static void Apply(Window window, string? setting, bool windowed = false)
    {
        var desktop = Resolve(setting);
        var changed = desktop != Desktop;
        Desktop = desktop;
        if (desktop)
        {
            var scale = DesktopScale * Math.Max(1f, DisplayServer.ScreenGetScale());
            window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
            window.ContentScaleFactor = scale;
            window.MinSize = new Vector2I((int)(1280 * scale), (int)(800 * scale));
            if (window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen)
            {
                window.Mode = Window.ModeEnum.Windowed;
                var screen = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
                var size = new Vector2I(Math.Min(screen.Size.X, (int)(1600 * scale)), Math.Min(screen.Size.Y, (int)(1000 * scale)));
                window.Size = size;
                window.Position = screen.Position + (screen.Size - size) / 2;
            }
            window.Unresizable = false;
        }
        else
        {
            window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
            window.ContentScaleFactor = 1f;
            window.MinSize = Vector2I.Zero;
            if (!windowed && changed)
            {
                window.Mode = Window.ModeEnum.Fullscreen;
            }
        }
    }
}
