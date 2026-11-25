using Luma.Host.Updates;
using Xunit;

namespace Luma.Core.Tests;

public class ChangelogTests
{
    [Fact]
    public void BundledChangelogHasThisVersion()
    {
        var current = AppUpdater.CurrentAppVersion();
        Assert.Contains(Changelog.Bundled, e => e.Version.Major == current.Major && e.Version.Minor == current.Minor && e.Version.Build == current.Build);
        Assert.All(Changelog.Bundled, e => Assert.NotEmpty(e.Items));
    }

    [Fact]
    public void ParsesVersionsNewestFirstAndStripsMarkdown()
    {
        var log = Changelog.Parse("# Title\nintro\n\n## 0.2.0\n- **Bold** `code`\n  continued\n\n## v0.10.1\n- Later\n## 0.1.0\n- First\n");
        Assert.Equal(["0.10.1", "0.2.0", "0.1.0"], log.Select(e => e.Display));
        Assert.Equal("Bold code continued", log[1].Items[0]);
        var between = Changelog.Between(log, new Version(0, 1, 0), new Version(0, 2, 0));
        Assert.Equal(["0.2.0"], between.Select(e => e.Display));
    }

    [Fact]
    public void ReadsWhatsNewFromReleaseNotes()
    {
        var notes = "## What's new\r\n- One\r\n- Two\r\n\r\n---\r\n\r\nPortable Linux build\r\n- not this";
        Assert.Equal(["One", "Two"], Changelog.ItemsFromReleaseNotes(notes));
        Assert.Empty(Changelog.ItemsFromReleaseNotes("Old release notes without a changelog section"));
    }
}
