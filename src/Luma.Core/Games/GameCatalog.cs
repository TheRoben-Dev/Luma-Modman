using System.Reflection;
using System.Text.Json;

namespace Luma.Core.Games;

/// <summary>
/// The supported games and loaders. Built-in profiles ship inside this assembly (data/games);
/// profiles in the user's own folder (~/.config/luma/games/*.json) are added on top and win for
/// the same id, so a game can be added or fixed without a new release.
/// </summary>
public sealed class GameCatalog
{
    public IReadOnlyList<GameProfile> Games { get; }
    public IReadOnlyDictionary<string, LoaderInfo> Loaders { get; }

    /// <summary>Profiles that failed to load or validate, with why (shown in the log, never fatal).</summary>
    public IReadOnlyList<string> Problems { get; }

    private readonly Dictionary<long, GameProfile> _bySteam = new();

    public GameCatalog(IEnumerable<GameProfile> games, IEnumerable<LoaderInfo> loaders, IReadOnlyList<string>? problems = null)
    {
        Loaders = loaders.ToDictionary(l => l.Id);
        var ids = Loaders.Keys.ToHashSet();
        var list = new Dictionary<string, GameProfile>();
        var issues = problems?.ToList() ?? new List<string>();
        foreach (var g in games)
        {
            var errors = g.Validate(ids);
            if (errors.Count > 0)
            {
                issues.Add($"{(g.Id.Length > 0 ? g.Id : g.Name)}: {string.Join("; ", errors)}");
                continue;
            }
            list[g.Id] = g;  // later ones (user folder) replace built-in ones
        }
        Games = list.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var g in Games)
        {
            foreach (var id in g.Steam)
            {
                _bySteam[id] = g;
            }
        }
        Problems = issues;
    }

    public GameProfile? BySteamId(long appId) => _bySteam.GetValueOrDefault(appId);

    public GameProfile? ById(string id) => Games.FirstOrDefault(g => g.Id == id);

    /// <summary>Built-in profiles plus the user's own folder (if it exists).</summary>
    public static GameCatalog Load(string? userDir = null)
    {
        var problems = new List<string>();
        var asm = typeof(GameCatalog).Assembly;
        var games = new List<GameProfile>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("games/", StringComparison.Ordinal)).Order())
        {
            using var s = asm.GetManifestResourceStream(name)!;
            Read(s, name, games, problems);
        }
        if (userDir != null && Directory.Exists(userDir))
        {
            foreach (var file in Directory.EnumerateFiles(userDir, "*.json").Order())
            {
                try
                {
                    using var s = File.OpenRead(file);
                    Read(s, file, games, problems);
                }
                catch (IOException e)
                {
                    problems.Add($"{file}: {e.Message}");
                }
            }
        }
        using var ls = asm.GetManifestResourceStream("loaders.json") ?? throw new InvalidDataException("loaders.json is missing from the build.");
        var loaders = JsonSerializer.Deserialize<List<LoaderInfo>>(ls, GameProfile.Json) ?? [];
        return new GameCatalog(games, loaders, problems);
    }

    private static void Read(Stream s, string name, List<GameProfile> into, List<string> problems)
    {
        try
        {
            var g = JsonSerializer.Deserialize<GameProfile>(s, GameProfile.Json);
            if (g == null)
            {
                problems.Add($"{name}: empty");
                return;
            }
            // The file name is the id unless the file says otherwise.
            if (g.Id.Length == 0)
            {
                g = g with { Id = Path.GetFileNameWithoutExtension(name.Replace("games/", "")) };
            }
            into.Add(g);
        }
        catch (JsonException e)
        {
            problems.Add($"{name}: {e.Message}");
        }
    }
}
