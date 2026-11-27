using Luma.Core.Mods;
using Xunit;

namespace Luma.Core.Tests;

public sealed class ToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "luma-tools-" + Guid.NewGuid().ToString("N")[..8]);

    public ToolsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ArgumentsExpandToWinePaths()
    {
        var args = Tools.Expand(Tools.Known.Single(k => k.Name == "LOOT").Args, "/home/deck/games/Skyrim", "/home/deck/games/Skyrim/Data", "Skyrim Special Edition", Tools.WinePath);
        Assert.Equal(["--game=Skyrim Special Edition", "--game-path=Z:\\home\\deck\\games\\Skyrim"], Tools.SplitArgs(args));
        Assert.Equal(["-D:Z:\\x\\Data"], Tools.SplitArgs(Tools.Expand("-D:\"{data}\"", "/x", "/x/Data", "", Tools.WinePath)));
        Assert.Equal("FalloutNV", Tools.LootName("newvegas", "Fallout: New Vegas"));
    }

    [Theory]
    [InlineData("proton_9", "Proton 9.0")]
    [InlineData("proton_10", "Proton 10.0")]
    [InlineData("proton_63", "Proton 6.3")]
    [InlineData("proton_513", "Proton 5.13")]
    [InlineData("proton_experimental", "Proton - Experimental")]
    public void ProtonFolderNames(string id, string folder) => Assert.Contains(folder, ProtonFinder.FolderNames(id));

    [Fact]
    public void ProtonIsFoundForTheGameElseTheNewest()
    {
        var steam = Path.Combine(_dir, "Steam");
        foreach (var p in new[] { "Proton 8.0", "Proton 9.0", "Proton - Experimental" })
        {
            Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common", p));
            File.WriteAllText(Path.Combine(steam, "steamapps", "common", p, "proton"), "");
        }
        Directory.CreateDirectory(Path.Combine(steam, "compatibilitytools.d", "GE-Proton9-20"));
        File.WriteAllText(Path.Combine(steam, "compatibilitytools.d", "GE-Proton9-20", "proton"), "");
        Assert.EndsWith("Proton 8.0/proton", ProtonFinder.Find("proton_8", [steam], [], _dir)!.Replace('\\', '/'));
        Assert.EndsWith("GE-Proton9-20/proton", ProtonFinder.Find("GE-Proton9-20", [steam], [], _dir)!.Replace('\\', '/'));
        Assert.EndsWith("Proton 9.0/proton", ProtonFinder.Find(null, [steam], [], _dir)!.Replace('\\', '/'));
    }

    [Fact]
    public void KnownToolsAreFoundInsideMods()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var mod = Path.Combine(store.ModDir("nexus:201"), "mods", "CalienteTools", "BodySlide");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "BodySlide x64.exe"), "");
        store.Save(store.State with { Mods = [new InstalledMod { Id = "nexus:201", Name = "BodySlide" }] });
        var downloads = Path.Combine(_dir, "Downloads", "SSEEdit 4.1");
        Directory.CreateDirectory(downloads);
        File.WriteAllText(Path.Combine(downloads, "SSEEdit.exe"), "");
        var found = Tools.Find(store, Path.Combine(_dir, "game"), Path.Combine(_dir, "game", "Data"), [Path.Combine(_dir, "Downloads")]);
        Assert.Contains(found, t => t.Name == "BodySlide" && t.Exe == "{data}/CalienteTools/BodySlide/BodySlide x64.exe");
        Assert.Contains(found, t => t.Name == "xEdit" && t.Args.Contains("{data}"));
        Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "game", "Data", "CalienteTools", "BodySlide", "BodySlide x64.exe")),
            Tools.ExePath(found.First(t => t.Name == "BodySlide"), Path.Combine(_dir, "game"), Path.Combine(_dir, "game", "Data")));
    }
}
