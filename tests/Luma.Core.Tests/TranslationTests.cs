using Luma.Host;
using Xunit;

namespace Luma.Core.Tests;

public sealed class TranslationTests
{
    private static readonly Translator Sv = new(new Dictionary<string, string>
    {
        ["Uninstall"] = "Avinstallera",
        ["Installed {0}."] = "Installerade {0}.",
        ["{0} file{1} in Overwrite"] = "{0} filer i Overwrite",
        ["Move {0} to {1}?"] = "Flytta {1} till {0}?",
        ["On"] = "På",
        ["{0}: {1}"] = "{1}: {0}",
    });

    [Fact]
    public void ExactTextAndTextWithValuesAreTranslated()
    {
        Assert.Equal("Avinstallera", Sv.Translate("Uninstall"));
        Assert.Equal("Installerade SkyUI 5.2.", Sv.Translate("Installed SkyUI 5.2."));
        Assert.Equal("3 filer i Overwrite", Sv.Translate("3 files in Overwrite"));
        Assert.Equal("Flytta b till a?", Sv.Translate("Move a to b?"));  // values can change places
        Assert.Null(Sv.Translate("Something else"));
        Assert.Null(Sv.Translate("Installed"));
        Assert.Null(Sv.Translate("x: y"));  // too little fixed text to be a safe match
    }

    [Fact]
    public void LinesBulletsAndCapitalsAreTranslatedPieceByPiece()
    {
        Assert.Equal("Avinstallera\nInstallerade X.", Sv.Translate("Uninstall\nInstalled X."));
        Assert.Equal("Avinstallera\nunknown", Sv.Translate("Uninstall\nunknown"));
        Assert.Equal("•  Avinstallera", Sv.Translate("•  Uninstall"));
        Assert.Equal("AVINSTALLERA", Sv.Translate("UNINSTALL"));
        Assert.Null(Sv.Translate("•  nothing known"));
    }

    [Fact]
    public void ValuesThatAreThemselvesKnownTextAreTranslatedToo() =>
        Assert.Equal("Installerade På.", Sv.Translate("Installed On."));

    [Fact]
    public void BuiltInLanguagesLoadAndEnglishNeedsNoTranslator()
    {
        Assert.Equal("en", Translations.Available[0].Code);
        Assert.Null(Translations.For("en"));
        Assert.Contains(Translations.Available, l => l.Code == Translations.Pick("xx-not-a-language"));  // unknown: the system's or English
        foreach (var l in Translations.Available.Skip(1))
        {
            Assert.True(Translations.For(l.Code)!.Count > 100, $"{l.Code} has few strings");
        }
    }
}
