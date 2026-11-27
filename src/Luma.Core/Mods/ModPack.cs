using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luma.Core.Games;
using Luma.Core.Sources;

namespace Luma.Core.Mods;

/// <summary>One mod in a modpack, in load order.</summary>
public sealed record PackMod
{
    /// <summary>"site:modId", "local:name" or "loader:id", as in <see cref="InstalledMod.Id"/>.</summary>
    public required string Id { get; init; }
    public string Site { get; init; } = "";
    public string ModId { get; init; } = "";
    public string FileId { get; init; } = "";
    public required string Name { get; init; }
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string? PageUrl { get; init; }
    public bool Enabled { get; init; } = true;
    public List<ModDependency> Dependencies { get; init; } = new();

    /// <summary>The options picked in the mod's FOMOD installer, so the pack installs it the same way.</summary>
    public List<FomodChoice>? Fomod { get; init; }

    /// <summary>The mod's archive inside the pack ("mods/…"), or null when it's downloaded from its site.</summary>
    public string? File { get; init; }
}

/// <summary>pack.json: what a .lumapack holds. Paths inside it always use "/".</summary>
public sealed record PackManifest
{
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;
    public string Name { get; init; } = "";
    public string CreatedBy { get; init; } = "";
    public DateTime Created { get; init; } = DateTime.UtcNow;

    /// <summary>The game's profile id (same on every platform) and its name.</summary>
    public string Game { get; init; } = "";
    public string GameName { get; init; } = "";
    public bool Full { get; init; }
    public List<PackMod> Mods { get; init; } = new();

    /// <summary>Conflict winners picked by hand: file key → mod id.</summary>
    public Dictionary<string, string> Winners { get; init; } = new();

    /// <summary>Plugins turned off by hand.</summary>
    public List<string> PluginsOff { get; init; } = new();

    /// <summary>The plugin load order set by hand or by Sort (empty: the mod order).</summary>
    public List<string> PluginOrder { get; init; } = new();
}

public sealed record PackExport(string File, int Bundled, int Listed, IReadOnlyList<string> NotBundled);

/// <summary>
/// Modpacks (.lumapack, a zip): a game profile's mods in load order, either as a download list or
/// with the mod archives inside. Archives are stored as downloaded, so a pack made on Linux installs
/// on Windows and the other way round. Only mods whose source allows passing them on are bundled;
/// the rest stay as download entries.
/// </summary>
public static class ModPack
{
    public const string Extension = ".lumapack";
    private const string ManifestName = "pack.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Whether Luma may put this mod's file in a pack for others. Thunderstore packages and the
    /// open-source loaders (BepInEx, MelonLoader, UE4SS) may be passed on, and so may files the
    /// player added by hand (their own call). Nexus Mods, CurseForge and mod.io files are fetched
    /// from their site by each player, as those sites and many authors require; script extenders
    /// ask not to be redistributed.
    /// </summary>
    public static bool CanBundle(InstalledMod m) => m.Site switch
    {
        "thunderstore" or "local" => true,
        "loader" => m.ModId is "bepinex" or "bepinex-il2cpp" or "melonloader" or "ue4ss",
        _ => false,
    };

    /// <summary>Writes the active profile of <paramref name="store"/> as a pack.</summary>
    public static PackExport Export(ModStore store, GameProfile game, string name, bool full, string file, string createdBy)
    {
        var state = store.State;
        var mods = new List<PackMod>();
        var notBundled = new List<string>();
        var tmp = file + ".tmp";
        var bundled = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var entry in state.Order)
            {
                if (state.Find(entry.Id) is not { } m)
                {
                    continue;
                }
                string? inside = null;
                if (full && CanBundle(m) && m.Archive != null && System.IO.File.Exists(m.Archive))
                {
                    inside = $"mods/{ModStore.Safe(m.Id)}/{ModStore.Safe(Path.GetFileName(m.Archive))}";
                    zip.CreateEntryFromFile(m.Archive, inside, CompressionLevel.NoCompression);
                    bundled++;
                }
                else if (full)
                {
                    notBundled.Add(m.Name);
                }
                mods.Add(new PackMod
                {
                    Id = m.Id, Site = m.Site, ModId = m.ModId, FileId = m.FileId, Name = m.Name, Author = m.Author,
                    Version = m.Version, PageUrl = m.PageUrl, Enabled = entry.Enabled, Dependencies = m.Dependencies, File = inside, Fomod = m.Fomod,
                });
            }
            var manifest = new PackManifest { Name = name, CreatedBy = createdBy, Game = game.Id, GameName = game.Name, Full = full, Mods = mods,
                Winners = store.State.Winners.TryGetValue(store.State.Active, out var picks) ? picks.Where(p => p.Value != ModStore.OverwriteId).ToDictionary(p => p.Key, p => p.Value) : new(),
                PluginsOff = store.State.PluginsOff.TryGetValue(store.State.Active, out var off) ? off.ToList() : new(),
                PluginOrder = store.State.PluginOrder.TryGetValue(store.State.Active, out var po) ? po.ToList() : new() };
            using var w = new StreamWriter(zip.CreateEntry(ManifestName).Open());
            w.Write(JsonSerializer.Serialize(manifest, Json));
        }
        System.IO.File.Move(tmp, file, overwrite: true);
        return new PackExport(file, bundled, mods.Count - bundled, notBundled);
    }

    /// <summary>Reads a pack's manifest, refusing newer formats and unsafe paths.</summary>
    public static PackManifest Read(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        var entry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException($"{Path.GetFileName(file)} isn't a Luma modpack (no {ManifestName}).");
        PackManifest? m;
        try
        {
            using var s = entry.Open();
            m = JsonSerializer.Deserialize<PackManifest>(s, Json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{Path.GetFileName(file)} has a damaged {ManifestName}: {e.Message}");
        }
        if (m == null || m.Game.Length == 0)
        {
            throw new InvalidDataException($"{Path.GetFileName(file)} doesn't say which game it's for.");
        }
        if (m.Format > PackManifest.CurrentFormat)
        {
            throw new InvalidDataException($"{Path.GetFileName(file)} was made by a newer Luma ({m.CreatedBy}). Update Luma to import it.");
        }
        foreach (var mod in m.Mods)
        {
            if (mod.File != null && (!mod.File.StartsWith("mods/", StringComparison.Ordinal) || mod.File.Contains("..") || mod.File.Contains('\\') || zip.GetEntry(mod.File) == null))
            {
                throw new InvalidDataException($"{Path.GetFileName(file)}: the file for {mod.Name} is missing or outside the pack's mods folder.");
            }
        }
        return m;
    }

    /// <summary>Copies a bundled mod's archive out of the pack into <paramref name="dir"/>.</summary>
    public static string ExtractMod(string file, PackMod mod, string dir)
    {
        using var zip = ZipFile.OpenRead(file);
        var entry = zip.GetEntry(mod.File ?? throw new ArgumentException($"{mod.Name} isn't bundled.")) ?? throw new InvalidDataException($"{mod.File} is missing.");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, ModStore.Safe(Path.GetFileName(mod.File)));
        entry.ExtractToFile(dest, overwrite: true);
        return dest;
    }

    /// <summary>A file name for a pack: "Lethal Company - Default.lumapack".</summary>
    public static string FileName(string gameName, string profile, bool full) =>
        string.Concat($"{gameName} - {profile}{(full ? " (full)" : "")}".Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is ':' or '/' or '\\' ? '_' : c)) + Extension;
}
