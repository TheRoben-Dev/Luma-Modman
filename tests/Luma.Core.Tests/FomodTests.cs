using System.IO.Compression;
using System.Xml.Linq;
using Luma.Core.Games;
using Luma.Core.Mods;
using Xunit;

namespace Luma.Core.Tests;

public sealed class FomodTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "luma-fomod-" + Guid.NewGuid().ToString("N")[..8]);

    public FomodTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <config xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <moduleName>Cool Armor</moduleName>
          <requiredInstallFiles>
            <folder source="Core" destination="" />
          </requiredInstallFiles>
          <installSteps order="Explicit">
            <installStep name="Body">
              <optionalFileGroups order="Explicit">
                <group name="Body type" type="SelectExactlyOne">
                  <plugins order="Explicit">
                    <plugin name="Vanilla">
                      <description>Fits the default body.</description>
                      <files><folder source="Bodies\Vanilla" destination="meshes" /></files>
                      <conditionFlags><flag name="body">vanilla</flag></conditionFlags>
                      <typeDescriptor><type name="Recommended" /></typeDescriptor>
                    </plugin>
                    <plugin name="CBBE">
                      <description>For CBBE.</description>
                      <image path="fomod\cbbe.png" />
                      <files><folder source="bodies\cbbe" destination="meshes" /></files>
                      <conditionFlags><flag name="body">cbbe</flag></conditionFlags>
                      <typeDescriptor>
                        <dependencyType>
                          <defaultType name="Optional" />
                          <patterns><pattern><dependencies><fileDependency file="CBBE.esp" state="Missing" /></dependencies><type name="NotUsable" /></pattern></patterns>
                        </dependencyType>
                      </typeDescriptor>
                    </plugin>
                  </plugins>
                </group>
              </optionalFileGroups>
            </installStep>
            <installStep name="CBBE extras">
              <visible><flagDependency flag="body" value="cbbe" /></visible>
              <optionalFileGroups>
                <group name="Extras" type="SelectAny">
                  <plugins>
                    <plugin name="Physics">
                      <description>Bouncy.</description>
                      <files><file source="Extras\physics.esp" destination="Cool Physics.esp" /></files>
                      <typeDescriptor><type name="Optional" /></typeDescriptor>
                    </plugin>
                  </plugins>
                </group>
              </optionalFileGroups>
            </installStep>
          </installSteps>
          <conditionalFileInstalls>
            <patterns>
              <pattern>
                <dependencies operator="And"><flagDependency flag="body" value="cbbe" /></dependencies>
                <files><file source="Patches\cbbe patch.esp" destination="" priority="1" /></files>
              </pattern>
            </patterns>
          </conditionalFileInstalls>
        </config>
        """;

    private string Archive()
    {
        var path = Path.Combine(_dir, "armor.zip");
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            using var w = new StreamWriter(z.CreateEntry(name).Open());
            w.Write(text);
        }
        Add("Cool Armor/fomod/info.xml", "<fomod/>");
        Add("Cool Armor/fomod/ModuleConfig.xml", Xml);
        Add("Cool Armor/Core/CoolArmor.esp", "esp");
        Add("Cool Armor/Core/textures/armor.dds", "tex");
        Add("Cool Armor/Bodies/Vanilla/armor.nif", "vanilla");
        Add("Cool Armor/Bodies/CBBE/armor.nif", "cbbe");
        Add("Cool Armor/Extras/Physics.esp", "physics");
        Add("Cool Armor/Patches/CBBE Patch.esp", "patch");
        return path;
    }

    [Fact]
    public void ParsesStepsGroupsTypesAndConditions()
    {
        var c = Fomod.Parse(XDocument.Parse(Xml));
        Assert.Equal("Cool Armor", c.Name);
        Assert.Equal(["Body", "CBBE extras"], c.Steps.Select(s => s.Name));
        var g = c.Steps[0].Groups.Single();
        Assert.True(g.Single);
        Assert.Equal("fomod/cbbe.png", g.Options[1].Image);
        Assert.Equal("NotUsable", g.Options[1].Type.Resolve(new Dictionary<string, string>(), _ => "Missing"));
        Assert.Equal("Optional", g.Options[1].Type.Resolve(new Dictionary<string, string>(), _ => "Active"));
        Assert.Equal(["Vanilla"], Fomod.Defaults(g, new Dictionary<string, string>(), _ => "Missing"));

        var (visible, flags, _) = Fomod.Run(c, [], _ => "Missing");
        Assert.Equal(["Body"], visible.Select(s => s.Name));   // the CBBE step hides with the default pick
        Assert.Equal("vanilla", flags["body"]);
        Assert.Equal(2, Fomod.Run(c, [new FomodChoice("Body", "Body type", ["CBBE"])], _ => "Active").Visible.Count);
    }

    [Fact]
    public void InstallNeedsChoicesThenBuildsTheModFromThem()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var p = new GameProfile { Id = "g", Name = "G", Steam = [1], ModsPath = "Data", Sites = new SiteIds { Nexus = "g" } };
        var archive = Archive();
        Assert.Throws<FomodNeededException>(() => store.Install(archive, new InstalledMod { Id = "nexus:5", Name = "Cool Armor" }, p));
        Assert.Empty(store.State.Mods);

        var choices = new List<FomodChoice> { new("Body", "Body type", ["CBBE"]), new("CBBE extras", "Extras", ["Physics"]) };
        var mod = store.Install(archive, new InstalledMod { Id = "nexus:5", Name = "Cool Armor", Fomod = choices }, p);
        var files = Directory.EnumerateFiles(Path.Combine(store.ModDir(mod.Id), "mods"), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Path.Combine(store.ModDir(mod.Id), "mods"), f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["CBBE Patch.esp", "Cool Physics.esp", "CoolArmor.esp", "meshes/armor.nif", "textures/armor.dds"], files);
        Assert.Equal("cbbe", File.ReadAllText(Path.Combine(store.ModDir(mod.Id), "mods", "meshes", "armor.nif")));
        Assert.Equal(choices, store.State.Find("nexus:5")!.Fomod);
    }

    [Fact]
    public void DestinationsOutsideTheModAreSkipped()
    {
        var root = Path.Combine(_dir, "src");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.esp"), "a");
        var dest = Path.Combine(_dir, "out");
        var n = Fomod.Assemble(root, [new FomodFile("a.esp", "../../evil.esp", false, 0, 0), new FomodFile("A.ESP", "ok/a.esp", false, 0, 1)], dest);
        Assert.Equal(1, n);
        Assert.False(File.Exists(Path.Combine(_dir, "evil.esp")));
        Assert.True(File.Exists(Path.Combine(dest, "ok", "a.esp")));
    }
}
