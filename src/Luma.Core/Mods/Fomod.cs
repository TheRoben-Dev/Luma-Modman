using System.Xml.Linq;

namespace Luma.Core.Mods;

/// <summary>A file or folder a FOMOD installs: source inside the archive, destination inside the mods folder.</summary>
public sealed record FomodFile(string Source, string Destination, bool Folder, int Priority, int Index);

/// <summary>
/// A FOMOD condition: an "And"/"Or" of children, or a leaf testing a flag set by an earlier
/// choice or a plugin file's state (Active, Inactive, Missing).
/// </summary>
public sealed record FomodCondition(string Operator, List<FomodCondition> Children, string? Flag = null, string? Value = null, string? File = null, string? State = null)
{
    public bool Test(IReadOnlyDictionary<string, string> flags, Func<string, string> fileState)
    {
        if (Flag != null)
        {
            return string.Equals(flags.GetValueOrDefault(Flag, ""), Value ?? "", StringComparison.OrdinalIgnoreCase);
        }
        if (File != null)
        {
            return string.Equals(fileState(File), State, StringComparison.OrdinalIgnoreCase);
        }
        if (Children.Count == 0)
        {
            return true;  // game and mod-manager version checks: Luma doesn't hold anyone back on those
        }
        return Operator.Equals("Or", StringComparison.OrdinalIgnoreCase)
            ? Children.Any(c => c.Test(flags, fileState))
            : Children.All(c => c.Test(flags, fileState));
    }
}

/// <summary>Required, Recommended, Optional, CouldBeUsable or NotUsable, possibly depending on conditions.</summary>
public sealed record FomodType(string Default, List<(FomodCondition When, string Type)> Patterns)
{
    public string Resolve(IReadOnlyDictionary<string, string> flags, Func<string, string> fileState) =>
        Patterns.FirstOrDefault(p => p.When.Test(flags, fileState)).Type ?? Default;
}

public sealed record FomodOption(string Name, string Description, string? Image, List<FomodFile> Files, Dictionary<string, string> Flags, FomodType Type);

/// <summary>A group of options: SelectExactlyOne, SelectAtMostOne, SelectAtLeastOne, SelectAll or SelectAny.</summary>
public sealed record FomodGroup(string Name, string Type, List<FomodOption> Options)
{
    public bool Single => Type is "SelectExactlyOne" or "SelectAtMostOne";
}

public sealed record FomodStep(string Name, FomodCondition? Visible, List<FomodGroup> Groups);

public sealed record FomodConfig(string Name, string? Image, List<FomodFile> Required, List<FomodStep> Steps, List<(FomodCondition When, List<FomodFile> Files)> Conditional);

/// <summary>The options picked in one group of one step, saved with the mod so updates install the same way.</summary>
public sealed record FomodChoice(string Step, string Group, List<string> Options);

/// <summary>An archive with a FOMOD installer was installed without choices: the player has to pick them first.</summary>
public sealed class FomodNeededException(string mod) : Exception($"{mod} has install options to choose first.");

/// <summary>
/// FOMOD installers (fomod/ModuleConfig.xml), the scripted installers many Nexus Mods mods use:
/// steps of option groups, flags that later steps and conditional files depend on, and the files
/// each option installs. Luma reads the XML (never the C# script kind) and builds the mod from it.
/// </summary>
public static class Fomod
{
    /// <summary>The folder holding fomod/ModuleConfig.xml (the archive root or a wrapper folder), or null.</summary>
    public static string? FindRoot(string dir)
    {
        foreach (var d in new[] { dir }.Concat(Directory.GetDirectories(dir)))
        {
            var fomod = Directory.GetDirectories(d).FirstOrDefault(x => Path.GetFileName(x).Equals("fomod", StringComparison.OrdinalIgnoreCase));
            if (fomod != null && Directory.GetFiles(fomod).Any(f => Path.GetFileName(f).Equals("ModuleConfig.xml", StringComparison.OrdinalIgnoreCase)))
            {
                return d;
            }
        }
        return null;
    }

    public static FomodConfig Load(string root)
    {
        var fomod = Directory.GetDirectories(root).First(x => Path.GetFileName(x).Equals("fomod", StringComparison.OrdinalIgnoreCase));
        var file = Directory.GetFiles(fomod).First(f => Path.GetFileName(f).Equals("ModuleConfig.xml", StringComparison.OrdinalIgnoreCase));
        XDocument doc;
        try
        {
            doc = XDocument.Load(file);
        }
        catch (System.Xml.XmlException e)
        {
            throw new InvalidDataException($"The mod's installer (ModuleConfig.xml) is damaged: {e.Message}");
        }
        return Parse(doc);
    }

    public static FomodConfig Parse(XDocument doc)
    {
        var config = doc.Root ?? throw new InvalidDataException("ModuleConfig.xml is empty.");
        var index = 0;
        List<FomodFile> Files(XElement? list) =>
            list?.Elements().Where(e => E(e, "file") || E(e, "folder")).Select(e => new FomodFile(
                Norm(A(e, "source") ?? ""),
                Norm(A(e, "destination") ?? (E(e, "file") ? A(e, "source") ?? "" : A(e, "source") ?? "")),
                E(e, "folder"),
                int.TryParse(A(e, "priority"), out var p) ? p : 0,
                index++)).ToList() ?? new();

        var steps = Ordered(Child(config, "installSteps"), Children(Child(config, "installSteps"), "installStep"), e => A(e, "name") ?? "")
            .Select(s => new FomodStep(
                A(s, "name") ?? "",
                Child(s, "visible") is { } v ? Condition(v) : null,
                Ordered(Child(s, "optionalFileGroups"), Children(Child(s, "optionalFileGroups"), "group"), g => A(g, "name") ?? "")
                    .Select(g => new FomodGroup(A(g, "name") ?? "", A(g, "type") ?? "SelectAny",
                        Ordered(Child(g, "plugins"), Children(Child(g, "plugins"), "plugin"), p => A(p, "name") ?? "")
                            .Select(p => new FomodOption(
                                A(p, "name") ?? "",
                                (Child(p, "description")?.Value ?? "").Trim(),
                                Child(p, "image") is { } img ? Norm(A(img, "path") ?? "") : null,
                                Files(Child(p, "files")),
                                Children(Child(p, "conditionFlags"), "flag").ToDictionary(f => A(f, "name") ?? "", f => f.Value, StringComparer.OrdinalIgnoreCase),
                                Type(Child(p, "typeDescriptor"))))
                            .ToList()))
                    .ToList()))
            .ToList();
        var conditional = Children(Child(Child(config, "conditionalFileInstalls"), "patterns"), "pattern")
            .Select(p => (Condition(Child(p, "dependencies")), Files(Child(p, "files"))))
            .ToList();
        return new FomodConfig(
            (Child(config, "moduleName")?.Value ?? "").Trim(),
            Child(config, "moduleImage") is { } mi ? Norm(A(mi, "path") ?? "") : null,
            Files(Child(config, "requiredInstallFiles")),
            steps,
            conditional);
    }

    private static FomodType Type(XElement? descriptor)
    {
        if (descriptor == null)
        {
            return new FomodType("Optional", new());
        }
        if (Child(descriptor, "type") is { } t)
        {
            return new FomodType(A(t, "name") ?? "Optional", new());
        }
        var dep = Child(descriptor, "dependencyType");
        var patterns = Children(Child(dep, "patterns"), "pattern")
            .Select(p => (Condition(Child(p, "dependencies")), A(Child(p, "type")!, "name") ?? "Optional"))
            .ToList();
        return new FomodType(Child(dep, "defaultType") is { } d ? A(d, "name") ?? "Optional" : "Optional", patterns);
    }

    private static FomodCondition Condition(XElement? e)
    {
        if (e == null)
        {
            return new FomodCondition("And", new());
        }
        var children = new List<FomodCondition>();
        foreach (var c in e.Elements())
        {
            switch (c.Name.LocalName)
            {
                case "flagDependency":
                    children.Add(new FomodCondition("And", new(), Flag: A(c, "flag") ?? "", Value: A(c, "value") ?? ""));
                    break;
                case "fileDependency":
                    children.Add(new FomodCondition("And", new(), File: A(c, "file") ?? "", State: A(c, "state") ?? "Active"));
                    break;
                case "dependencies":
                    children.Add(Condition(c));
                    break;
            }
        }
        return new FomodCondition(A(e, "operator") ?? "And", children);
    }

    /// <summary>FOMOD lists are sorted by name unless the author asks for their own order ("Explicit").</summary>
    private static IEnumerable<XElement> Ordered(XElement? parent, IEnumerable<XElement> items, Func<XElement, string> name) =>
        (parent == null ? null : A(parent, "order")) switch
        {
            "Explicit" => items,
            "Descending" => items.OrderByDescending(name, StringComparer.OrdinalIgnoreCase),
            _ => items.OrderBy(name, StringComparer.OrdinalIgnoreCase),
        };

    private static bool E(XElement e, string name) => e.Name.LocalName == name;

    private static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(c => c.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement? e, string name) => e?.Elements().Where(c => c.Name.LocalName == name) ?? [];

    private static string? A(XElement e, string name) => e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    private static string Norm(string p) => p.Replace('\\', '/').Trim().TrimStart('/');

    /// <summary>The options picked by default: required and recommended ones, else the first that may be picked where one is needed.</summary>
    public static List<string> Defaults(FomodGroup g, IReadOnlyDictionary<string, string> flags, Func<string, string> fileState)
    {
        var types = g.Options.Select(o => o.Type.Resolve(flags, fileState)).ToList();
        if (g.Type == "SelectAll")
        {
            return g.Options.Select(o => o.Name).ToList();
        }
        var picked = g.Options.Where((o, i) => types[i] is "Required" or "Recommended").Select(o => o.Name).ToList();
        if (g.Single && picked.Count > 1)
        {
            picked = picked.Take(1).ToList();
        }
        if (picked.Count == 0 && g.Type is "SelectExactlyOne" or "SelectAtLeastOne")
        {
            var first = g.Options.Where((o, i) => types[i] != "NotUsable").FirstOrDefault();
            if (first != null)
            {
                picked.Add(first.Name);
            }
        }
        return picked;
    }

    /// <summary>Whether a group's picks are allowed by its type.</summary>
    public static bool Valid(FomodGroup g, IReadOnlyCollection<string> picked) => g.Type switch
    {
        "SelectExactlyOne" => picked.Count == 1,
        "SelectAtMostOne" => picked.Count <= 1,
        "SelectAtLeastOne" => picked.Count >= 1,
        "SelectAll" => picked.Count == g.Options.Count,
        _ => true,
    };

    /// <summary>
    /// Walks the steps with the given choices (defaults where a group has none): the visible
    /// steps, the flags the picks set, and the files to install, in priority order (later wins).
    /// </summary>
    public static (List<FomodStep> Visible, Dictionary<string, string> Flags, List<FomodFile> Files) Run(FomodConfig c, IReadOnlyList<FomodChoice> choices, Func<string, string> fileState)
    {
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FomodFile>(c.Required);
        var visible = new List<FomodStep>();
        foreach (var step in c.Steps)
        {
            if (step.Visible != null && !step.Visible.Test(flags, fileState))
            {
                continue;
            }
            visible.Add(step);
            foreach (var g in step.Groups)
            {
                var picked = Picked(choices, step, g) ?? Defaults(g, flags, fileState);
                foreach (var o in g.Options.Where(o => picked.Contains(o.Name, StringComparer.OrdinalIgnoreCase)))
                {
                    files.AddRange(o.Files);
                    foreach (var (k, v) in o.Flags)
                    {
                        flags[k] = v;
                    }
                }
            }
        }
        foreach (var (when, add) in c.Conditional)
        {
            if (when.Test(flags, fileState))
            {
                files.AddRange(add);
            }
        }
        return (visible, flags, files.OrderBy(f => f.Priority).ThenBy(f => f.Index).ToList());
    }

    public static List<string>? Picked(IReadOnlyList<FomodChoice> choices, FomodStep step, FomodGroup g) =>
        choices.FirstOrDefault(x => x.Step == step.Name && x.Group == g.Name)?.Options;

    /// <summary>Copies the chosen files from the unpacked archive into <paramref name="dest"/>, as they'll sit in the mods folder.</summary>
    public static int Assemble(string root, IEnumerable<FomodFile> files, string dest)
    {
        Directory.CreateDirectory(dest);
        var n = 0;
        foreach (var f in files)
        {
            var src = Find(root, f.Source);
            if (src == null)
            {
                continue;  // authors' typos: skip what isn't there, like other mod managers do
            }
            if (f.Folder || Directory.Exists(src))
            {
                foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    n += Copy(file, dest, Path.Combine(f.Destination, Path.GetRelativePath(src, file)));
                }
            }
            else
            {
                var target = f.Destination.Length == 0 || f.Destination.EndsWith('/') ? f.Destination + Path.GetFileName(src) : f.Destination;
                n += Copy(src, dest, target);
            }
        }
        return n;
    }

    /// <summary>Copies one file to dest/relative, refusing destinations outside dest.</summary>
    private static int Copy(string src, string dest, string relative)
    {
        var baseDir = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(dest, relative));
        if (!target.StartsWith(baseDir, StringComparison.Ordinal))
        {
            return 0;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(src, target, overwrite: true);
        return 1;
    }

    /// <summary>A path inside the archive, matched without case (installers are written on Windows).</summary>
    public static string? Find(string root, string relative)
    {
        var cur = root;
        foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".." || part == ".")
            {
                return null;
            }
            var next = Directory.Exists(cur)
                ? Directory.EnumerateFileSystemEntries(cur).FirstOrDefault(e => Path.GetFileName(e).Equals(part, StringComparison.OrdinalIgnoreCase))
                : null;
            if (next == null)
            {
                return null;
            }
            cur = next;
        }
        return cur;
    }
}
