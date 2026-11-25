using System.Text;
using Luma.Core.Steam;
using Xunit;

namespace Luma.Core.Tests;

public class SteamShortcutsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "luma-steam-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    /// <summary>A shortcuts.vdf the way Steam writes it, with one shortcut someone else made.</summary>
    private static byte[] SteamFile()
    {
        var b = new List<byte>();
        void Str(string s) { b.AddRange(Encoding.UTF8.GetBytes(s)); b.Add(0); }
        b.Add(0); Str("shortcuts");
        b.Add(0); Str("0");
        b.Add(2); Str("appid"); b.AddRange(BitConverter.GetBytes(unchecked((int)0x8badf00d)));
        b.Add(1); Str("AppName"); Str("Some Other Game");
        b.Add(1); Str("Exe"); Str("\"/usr/bin/other\"");
        b.Add(0); Str("tags"); b.Add(1); Str("0"); Str("Favorite"); b.Add(8);
        b.Add(8);
        b.Add(8);
        b.Add(8);
        return [.. b];
    }

    [Fact]
    public void BinaryVdfRoundTripsSteamsBytesExactly()
    {
        var bytes = SteamFile();
        var map = BinaryVdf.Read(bytes);
        Assert.Equal("Some Other Game", map.Map("shortcuts")!.Map("0")!.Str("AppName"));
        Assert.Equal(unchecked((int)0x8badf00d), map.Map("shortcuts")!.Map("0")!.Int("appid"));
        Assert.Equal(bytes, BinaryVdf.Write(map));
    }

    [Fact]
    public void AppIdIsCrc32OfQuotedExeAndNameWithTheTopBitSet()
    {
        Assert.Equal(0xCBF43926u, SteamShortcut.ComputeAppId("123456789", ""));
        Assert.Equal(0x80000000u, SteamShortcut.ComputeAppId("", "") & 0x80000000u);
    }

    private string Account(string id = "12345")
    {
        var config = Path.Combine(_home, ".local", "share", "Steam", "userdata", id, "config");
        Directory.CreateDirectory(config);
        return config;
    }

    [Fact]
    public void AddsUpdatesAndRemovesItsOwnShortcutAndKeepsOthers()
    {
        var config = Account();
        File.WriteAllBytes(Path.Combine(config, "shortcuts.vdf"), SteamFile());
        var shortcut = new SteamShortcut
        {
            AppName = "Castle Defenders",
            Exe = "/home/deck/Applications/Luma/luma",
            StartDir = "/home/deck/Applications/Luma",
            LaunchOptions = "launch eden:0100000000070000",
            Tags = ["Luma"],
        };

        var s = SteamShortcuts.Load(config);
        Assert.True(s.AddOrUpdate(shortcut));
        s.Save();
        Assert.True(File.Exists(Path.Combine(config, "shortcuts.vdf.luma-bak")));

        s = SteamShortcuts.Load(config);
        Assert.Equal(2, s.Entries.Count());
        var mine = s.Find(shortcut.AppId)!;
        Assert.Equal("\"/home/deck/Applications/Luma/luma\"", mine.Str("Exe"));
        Assert.Equal("launch eden:0100000000070000", mine.Str("LaunchOptions"));
        Assert.Equal(unchecked((int)shortcut.AppId), mine.Int("appid"));
        Assert.Equal("Luma", mine.Map("tags")!.Str("0"));

        // Same exe and name again: updated in place, not duplicated; the user's own flags are kept.
        mine["AllowOverlay"] = 0;
        Assert.False(s.AddOrUpdate(shortcut with { LaunchOptions = "launch eden:0100000000070000 --x" }));
        Assert.Equal(2, s.Entries.Count());
        Assert.Equal(0, s.Find(shortcut.AppId)!.Int("AllowOverlay"));

        Assert.True(s.Remove(shortcut.AppId));
        s.Save();
        s = SteamShortcuts.Load(config);
        Assert.Single(s.Entries);
        Assert.Equal("Some Other Game", s.Entries.Single().Str("AppName"));
        Assert.Equal(SteamFile(), File.ReadAllBytes(Path.Combine(config, "shortcuts.vdf")));
    }

    [Fact]
    public void FindsSignedInAccountsOnceAndStartsAnEmptyFile()
    {
        Assert.Empty(SteamShortcuts.AccountConfigDirs(_home));
        var config = Account("777");
        Account("0");
        Directory.CreateDirectory(Path.Combine(_home, ".steam"));
        Directory.CreateSymbolicLink(Path.Combine(_home, ".steam", "steam"), Path.Combine(_home, ".local", "share", "Steam"));
        Assert.Equal([config], SteamShortcuts.AccountConfigDirs(_home));
        var s = SteamShortcuts.Load(config);
        Assert.Empty(s.Entries);
        Assert.False(SteamShortcuts.SteamRunning(_home));
    }

    [Fact]
    public void InstallsArtworkWithoutReplacingThePlayersOwn()
    {
        var art = Path.Combine(_home, "steam-art");
        var config = Path.Combine(_home, "config");
        Directory.CreateDirectory(art);
        foreach (var (source, _) in SteamShortcuts.ArtworkFiles)
        {
            File.WriteAllText(Path.Combine(art, source), "ours " + source);
        }
        const uint id = 0x80001234;
        var grid = SteamShortcuts.GridDir(config);
        Assert.Equal(4, SteamShortcuts.InstallArtwork(config, id, art));
        Assert.Equal("ours capsule.png", File.ReadAllText(Path.Combine(grid, $"{id}p.png")));
        Assert.Equal("ours hero.png", File.ReadAllText(Path.Combine(grid, $"{id}_hero.png")));
        Assert.Equal(0, SteamShortcuts.InstallArtwork(config, id, art));  // nothing changed

        // The player swaps the hero in Steam; a newer Luma updates only its own files.
        File.WriteAllText(Path.Combine(grid, $"{id}_hero.png"), "player's hero");
        File.WriteAllText(Path.Combine(art, "capsule.png"), "ours v2");
        Assert.Equal(1, SteamShortcuts.InstallArtwork(config, id, art));
        Assert.Equal("ours v2", File.ReadAllText(Path.Combine(grid, $"{id}p.png")));
        Assert.Equal("player's hero", File.ReadAllText(Path.Combine(grid, $"{id}_hero.png")));

        // Removing takes away only what Luma put there.
        Assert.Equal(3, SteamShortcuts.InstallArtwork(config, id, art, remove: true));
        Assert.False(File.Exists(Path.Combine(grid, $"{id}p.png")));
        Assert.Equal("player's hero", File.ReadAllText(Path.Combine(grid, $"{id}_hero.png")));
        Assert.False(File.Exists(Path.Combine(grid, $".luma-art-{id}")));
    }
}
