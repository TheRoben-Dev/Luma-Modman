using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Luma.Core.Mods;

/// <summary>One module (mod) as Larian games list it in modsettings.lsx.</summary>
public sealed record LarianModule(string Uuid, string Folder, string Name, string Version64 = "36028797018963968", string Md5 = "", string PublishHandle = "0");

/// <summary>
/// Larian games (Baldur's Gate 3) load .pak mods listed in modsettings.lsx, in its order. A mod's
/// identity comes from the info.json mod managers ship next to the .pak, or from the meta.lsx inside it.
/// </summary>
public static class Larian
{
    /// <summary>The module a .pak holds: from an info.json beside it, else its meta.lsx. Null when neither says.</summary>
    public static LarianModule? Module(string pak, string? infoJson = null)
    {
        if (infoJson != null && File.Exists(infoJson) && FromInfo(File.ReadAllText(infoJson), Path.GetFileNameWithoutExtension(pak)) is { } m)
        {
            return m;
        }
        try
        {
            return ReadMeta(pak) is { } xml ? FromMeta(xml) : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Xml.XmlException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The module described by a BG3 Mod Manager info.json ({"Mods":[{"UUID","Folder","Name","Version"}]}).</summary>
    public static LarianModule? FromInfo(string json, string pakName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Mods", out var mods) || mods.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var all = mods.EnumerateArray().ToList();
            var pick = all.FirstOrDefault(m => Str(m, "Folder").Equals(pakName, StringComparison.OrdinalIgnoreCase));
            if (pick.ValueKind != JsonValueKind.Object)
            {
                if (all.Count == 0)
                {
                    return null;
                }
                pick = all[0];
            }
            var uuid = Str(pick, "UUID");
            if (uuid.Length == 0)
            {
                return null;
            }
            var version = Str(pick, "Version64") is { Length: > 0 } v64 ? v64 : Str(pick, "Version") is { Length: > 0 } v ? v : "36028797018963968";
            return new LarianModule(uuid, Str(pick, "Folder") is { Length: > 0 } f ? f : pakName, Str(pick, "Name") is { Length: > 0 } n ? n : pakName, version,
                doc.RootElement.TryGetProperty("MD5", out var md5) && md5.ValueKind == JsonValueKind.String ? md5.GetString() ?? "" : "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Number => v.GetRawText(), _ => "" } : "";

    /// <summary>The module described by a meta.lsx (its ModuleInfo node).</summary>
    public static LarianModule? FromMeta(string xml)
    {
        var doc = XDocument.Parse(xml);
        var info = doc.Descendants("node").FirstOrDefault(n => (string?)n.Attribute("id") == "ModuleInfo");
        if (info == null)
        {
            return null;
        }
        string A(string id) => info.Elements("attribute").FirstOrDefault(a => (string?)a.Attribute("id") == id)?.Attribute("value")?.Value ?? "";
        var uuid = A("UUID");
        if (uuid.Length == 0)
        {
            return null;
        }
        var version = A("Version64") is { Length: > 0 } v64 ? v64 : A("Version") is { Length: > 0 } v ? v : "36028797018963968";
        return new LarianModule(uuid, A("Folder"), A("Name"), version, A("MD5"), A("PublishHandle") is { Length: > 0 } ph ? ph : "0");
    }

    /// <summary>
    /// Reads Mods/&lt;Folder&gt;/meta.lsx out of a .pak (LSPK version 15 to 18). Null when the package has none.
    /// </summary>
    public static string? ReadMeta(string pak)
    {
        using var fs = File.OpenRead(pak);
        using var r = new BinaryReader(fs);
        if (fs.Length < 40 || Encoding.ASCII.GetString(r.ReadBytes(4)) != "LSPK")
        {
            return null;
        }
        var version = r.ReadUInt32();
        if (version is < 15 or > 18)
        {
            throw new InvalidDataException($"Package version {version} isn't supported.");
        }
        var listOffset = (long)r.ReadUInt64();
        fs.Position = listOffset;
        var count = r.ReadInt32();
        var compressed = r.ReadInt32();
        var entrySize = version == 18 ? 272 : 296;
        var list = Lz4Block(r.ReadBytes(compressed), count * entrySize);
        for (var i = 0; i < count; i++)
        {
            var e = list.AsSpan(i * entrySize, entrySize);
            var name = Encoding.UTF8.GetString(e[..256]).TrimEnd('\0');
            var end = name.IndexOf('\0');
            if (end >= 0)
            {
                name = name[..end];
            }
            if (!(name.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) && name.EndsWith("/meta.lsx", StringComparison.OrdinalIgnoreCase) && name.Count(c => c == '/') == 2))
            {
                continue;
            }
            long offset;
            int flags, disk, size;
            if (version == 18)
            {
                offset = BitConverter.ToUInt32(e[256..]) | ((long)BitConverter.ToUInt16(e[260..]) << 32);
                flags = e[263];
                disk = BitConverter.ToInt32(e[264..]);
                size = BitConverter.ToInt32(e[268..]);
            }
            else
            {
                // Version 15/16: offset, size on disk and size as 64-bit values, then part, flags, CRC.
                offset = (long)BitConverter.ToUInt64(e[256..]);
                disk = (int)BitConverter.ToUInt64(e[264..]);
                size = (int)BitConverter.ToUInt64(e[272..]);
                flags = (int)BitConverter.ToUInt32(e[284..]);
            }
            fs.Position = offset;
            var data = r.ReadBytes(disk);
            var bytes = (flags & 0x0F) switch
            {
                0 => data,
                1 => Inflate(data),
                2 => Lz4Block(data, size),
                _ => throw new InvalidDataException("The package uses a compression Luma can't read."),
            };
            return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        }
        return null;
    }

    private static byte[] Inflate(byte[] data)
    {
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    /// <summary>Decompresses an LZ4 block (no frame header) of known size.</summary>
    public static byte[] Lz4Block(byte[] src, int size)
    {
        var dst = new byte[size];
        int s = 0, d = 0;
        while (s < src.Length)
        {
            var token = src[s++];
            var lit = token >> 4;
            if (lit == 15)
            {
                byte b;
                do
                {
                    b = src[s++];
                    lit += b;
                } while (b == 255);
            }
            if (d + lit > size || s + lit > src.Length)
            {
                throw new InvalidDataException("Damaged package.");
            }
            Buffer.BlockCopy(src, s, dst, d, lit);
            s += lit;
            d += lit;
            if (s >= src.Length)
            {
                break;
            }
            var off = src[s] | (src[s + 1] << 8);
            s += 2;
            var len = token & 15;
            if (len == 15)
            {
                byte b;
                do
                {
                    b = src[s++];
                    len += b;
                } while (b == 255);
            }
            len += 4;
            if (off == 0 || off > d || d + len > size)
            {
                throw new InvalidDataException("Damaged package.");
            }
            for (var i = 0; i < len; i++, d++)
            {
                dst[d] = dst[d - off];
            }
        }
        return dst;
    }

    /// <summary>
    /// The new modsettings.lsx: what the existing one lists that Luma doesn't manage (the game's own
    /// module, mods from the in-game manager) stays first, in its order; Luma's enabled mods follow in
    /// load order. A missing file starts from the game's base module.
    /// </summary>
    public static string ModSettings(string? existing, IReadOnlyList<LarianModule> mods, IReadOnlySet<string> managed)
    {
        XDocument doc;
        try
        {
            doc = existing is { Length: > 0 } ? XDocument.Parse(existing) : XDocument.Parse(Empty);
        }
        catch (System.Xml.XmlException)
        {
            doc = XDocument.Parse(Empty);
        }
        var modsNode = doc.Descendants("node").FirstOrDefault(n => (string?)n.Attribute("id") == "Mods");
        if (modsNode == null)
        {
            doc = XDocument.Parse(Empty);
            modsNode = doc.Descendants("node").First(n => (string?)n.Attribute("id") == "Mods");
        }
        var children = modsNode.Element("children");
        if (children == null)
        {
            children = new XElement("children");
            modsNode.Add(children);
        }
        static string Uuid(XElement n) => n.Elements("attribute").FirstOrDefault(a => (string?)a.Attribute("id") == "UUID")?.Attribute("value")?.Value ?? "";
        foreach (var n in children.Elements("node").ToList())
        {
            if (managed.Contains(Uuid(n).ToLowerInvariant()))
            {
                n.Remove();
            }
        }
        var have = children.Elements("node").Select(Uuid).Select(u => u.ToLowerInvariant()).ToHashSet();
        foreach (var m in mods.Where(m => !have.Contains(m.Uuid.ToLowerInvariant())))
        {
            children.Add(new XElement("node", new XAttribute("id", "ModuleShortDesc"),
                Attr("Folder", "LSString", m.Folder),
                Attr("MD5", "LSString", m.Md5),
                Attr("Name", "LSString", m.Name),
                Attr("PublishHandle", "uint64", m.PublishHandle),
                Attr("UUID", "guid", m.Uuid),
                Attr("Version64", "int64", m.Version64)));
            have.Add(m.Uuid.ToLowerInvariant());
        }
        // Older files also keep a ModOrder list; keep it in step when it's there.
        if (doc.Descendants("node").FirstOrDefault(n => (string?)n.Attribute("id") == "ModOrder") is { } order)
        {
            var oc = order.Element("children") ?? new XElement("children");
            if (oc.Parent == null)
            {
                order.Add(oc);
            }
            oc.RemoveNodes();
            foreach (var u in children.Elements("node").Select(Uuid))
            {
                oc.Add(new XElement("node", new XAttribute("id", "Module"), Attr("UUID", "FixedString", u)));
            }
        }
        var sb = new StringBuilder();
        using (var w = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = Encoding.UTF8, OmitXmlDeclaration = true }))
        {
            doc.Root!.WriteTo(w);
        }
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + sb + "\n";
    }

    private static XElement Attr(string id, string type, string value) =>
        new("attribute", new XAttribute("id", id), new XAttribute("type", type), new XAttribute("value", value));

    /// <summary>A fresh modsettings.lsx with BG3's base module (GustavX, patch 8).</summary>
    private const string Empty = """
        <?xml version="1.0" encoding="UTF-8"?>
        <save>
            <version major="4" minor="8" revision="0" build="500"/>
            <region id="ModuleSettings">
                <node id="root">
                    <children>
                        <node id="Mods">
                            <children>
                                <node id="ModuleShortDesc">
                                    <attribute id="Folder" type="LSString" value="GustavX"/>
                                    <attribute id="MD5" type="LSString" value=""/>
                                    <attribute id="Name" type="LSString" value="GustavX"/>
                                    <attribute id="PublishHandle" type="uint64" value="0"/>
                                    <attribute id="UUID" type="guid" value="cb555efe-2d9e-131f-8195-a89329d218ea"/>
                                    <attribute id="Version64" type="int64" value="36028797018963968"/>
                                </node>
                            </children>
                        </node>
                    </children>
                </node>
            </region>
        </save>
        """;
}
