using System.Text;

namespace Luma.Core.Mods;

/// <summary>What a plugin's header says: whether it's a master or light plugin, and which plugins it needs.</summary>
public sealed record PluginHeader(bool Master, bool Light, IReadOnlyList<string> Masters)
{
    public static readonly PluginHeader None = new(false, false, []);
}

/// <summary>
/// Reads the header record (TES4) at the start of a Bethesda plugin: its flags (master, light)
/// and its MAST subrecords, the plugins it needs loaded before it. Works for Oblivion (20-byte
/// record headers) and everything after it (24 bytes). Only the first record is read.
/// </summary>
public static class PluginFile
{
    public const uint MasterFlag = 0x1;

    /// <summary>The light (ESL) flag for Skyrim SE and Fallout 4; Starfield's is 0x100 (see <see cref="Games.PluginSettings.LightFlag"/>).</summary>
    public const uint DefaultLightFlag = 0x200;

    /// <param name="lightFlag">The game's light-plugin flag, or 0 for games without light plugins.</param>
    public static PluginHeader Read(string path, uint lightFlag)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var byName = new PluginHeader(ext is ".esm" or ".esl", ext == ".esl" && lightFlag != 0, []);
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[24];
            if (f.Read(head, 0, 24) < 20 || Encoding.ASCII.GetString(head, 0, 4) != "TES4")
            {
                return byName;
            }
            var size = BitConverter.ToUInt32(head, 4);
            var flags = BitConverter.ToUInt32(head, 8);
            if (size > 16 * 1024 * 1024)
            {
                return byName;
            }
            // Subrecords follow the record header: 24 bytes, or 20 in Oblivion (where HEDR starts at 20).
            var start = Encoding.ASCII.GetString(head, 20, 4) == "HEDR" ? 20 : 24;
            f.Position = start;
            var data = new byte[size];
            var read = 0;
            while (read < size && f.Read(data, read, (int)size - read) is var n and > 0)
            {
                read += n;
            }
            var masters = new List<string>();
            for (var i = 0; i + 6 <= read;)
            {
                var type = Encoding.ASCII.GetString(data, i, 4);
                int len = BitConverter.ToUInt16(data, i + 4);
                i += 6;
                if (i + len > read)
                {
                    break;
                }
                if (type == "MAST")
                {
                    masters.Add(Encoding.Latin1.GetString(data, i, len).TrimEnd('\0'));
                }
                i += len;
            }
            return new PluginHeader(byName.Master || (flags & MasterFlag) != 0, byName.Light || (lightFlag != 0 && (flags & lightFlag) != 0), masters);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return byName;
        }
    }

    /// <summary>A minimal plugin file (for tests and the UI smoke test): a TES4 header with these flags and masters.</summary>
    public static byte[] Make(uint flags, params string[] masters)
    {
        using var sub = new MemoryStream();
        void Sub(string type, byte[] body)
        {
            sub.Write(Encoding.ASCII.GetBytes(type));
            sub.Write(BitConverter.GetBytes((ushort)body.Length));
            sub.Write(body);
        }
        Sub("HEDR", new byte[12]);
        foreach (var m in masters)
        {
            Sub("MAST", Encoding.Latin1.GetBytes(m + "\0"));
            Sub("DATA", new byte[8]);
        }
        using var o = new MemoryStream();
        o.Write(Encoding.ASCII.GetBytes("TES4"));
        o.Write(BitConverter.GetBytes((uint)sub.Length));
        o.Write(BitConverter.GetBytes(flags));
        o.Write(new byte[12]);
        sub.Position = 0;
        sub.CopyTo(o);
        return o.ToArray();
    }
}
