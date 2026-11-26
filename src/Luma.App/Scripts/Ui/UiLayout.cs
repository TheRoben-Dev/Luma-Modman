using Luma.Host;
using Godot;

namespace Luma.App.Ui;

/// <summary>
/// Controller layout (full screen, sized for a TV or a Steam Deck) or desktop layout (a separate
/// mod manager window: menus, tables, right-click, see <see cref="Luma.App.Desktop.DesktopShell"/>).
/// "auto" picks the controller layout in Steam's Game Mode / Big Picture and the desktop layout
/// everywhere else, Windows included. Switching between them restarts the app.
/// </summary>
public static class UiLayout
{
    public const string Auto = "auto";
    public const string Controller = "controller";
    public const string DesktopId = "desktop";

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
        DesktopId => "A traditional mod manager window: menu bar, mod table with checkboxes and columns, right-click menus and keyboard shortcuts.",
        _ => $"Controller layout in Game Mode, desktop layout on the desktop. Right now: {(Resolve(Auto) ? "desktop" : "controller")}.",
    };

    /// <summary>Whether <paramref name="setting"/> means the desktop layout on this session.</summary>
    public static bool Resolve(string? setting) => setting switch
    {
        Controller => false,
        DesktopId => true,
        _ => OS.GetCmdlineUserArgs().Contains("--desktop") || (!SteamSetup.InGameMode && !OS.GetCmdlineUserArgs().Contains("--smoke")),
    };

    /// <summary>Sets up the window for the layout. <paramref name="windowed"/> keeps the controller layout in a window (testing).</summary>
    public static void Apply(Window window, string? setting, bool windowed = false)
    {
        var desktop = Resolve(setting);
        var changed = desktop != Desktop;
        Desktop = desktop;
        if (desktop)
        {
            var scale = Math.Max(1f, DisplayServer.ScreenGetScale());
            window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
            window.ContentScaleFactor = scale;
            window.MinSize = new Vector2I((int)(1000 * scale), (int)(640 * scale));
            var screen = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
            if (OS.GetCmdlineUserArgs().Contains("--smoke"))
            {
                // Screenshots of the whole virtual display.
                window.Mode = Window.ModeEnum.Windowed;
                window.Position = Vector2I.Zero;
                window.Size = DisplayServer.ScreenGetSize(window.CurrentScreen);
            }
            else if (window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen || window.Size.X < window.MinSize.X)
            {
                window.Mode = Window.ModeEnum.Windowed;
                var size = new Vector2I(Math.Min(screen.Size.X, (int)(1600 * scale)), Math.Min(screen.Size.Y, (int)(980 * scale)));
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
