namespace Luma.Core.Detection;

/// <summary>Where an install came from.</summary>
public enum GameSource
{
    Steam,
    Epic,
    Gog,
    Amazon,
    Lutris,
}

/// <summary>One installed game as a launcher records it, before matching against the support profiles.</summary>
public sealed record InstalledGame
{
    public GameSource Source { get; init; }

    /// <summary>The launcher's id: Steam app id, Epic app name, GOG product id, Lutris slug.</summary>
    public string SourceId { get; init; } = "";

    public string Title { get; init; } = "";

    /// <summary>The game folder.</summary>
    public string InstallDir { get; init; } = "";

    /// <summary>The Wine/Proton prefix (the folder holding drive_c), when the game runs in one.</summary>
    public string? Prefix { get; init; }

    /// <summary>Steam library this game is in (Steam only), e.g. the SD card's.</summary>
    public string? Library { get; init; }

    /// <summary>Compatibility tool Steam uses for it ("proton_9", "GE-Proton9-20"), when one is set.</summary>
    public string? CompatTool { get; init; }

    /// <summary>Launch options set in Steam for this game (Steam only), as the player typed them.</summary>
    public string? LaunchOptions { get; init; }

    /// <summary>Portrait cover art on disk (Steam's library cache), when there is one.</summary>
    public string? Cover { get; init; }

    /// <summary>Where the launcher itself is installed from: "native" or "flatpak".</summary>
    public string Packaging { get; init; } = "native";
}
