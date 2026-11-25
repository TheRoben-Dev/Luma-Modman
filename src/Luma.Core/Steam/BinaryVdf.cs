using System.Text;

namespace Luma.Core.Steam;

/// <summary>
/// Steam's binary KeyValues format, as used by <c>userdata/&lt;id&gt;/config/shortcuts.vdf</c>:
/// a nested map where each entry is a type byte (0 = map, 1 = string, 2 = int32), a NUL-terminated
/// name, then the value; 8 ends a map. Strings are UTF-8. Key order is kept, so a file we didn't
/// change round-trips byte for byte.
/// </summary>
public sealed class VdfMap
{
    private readonly List<KeyValuePair<string, object>> _items = [];

    public IReadOnlyList<KeyValuePair<string, object>> Items => _items;

    public object? this[string key]
    {
        get => _items.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        set
        {
            var i = _items.FindIndex(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
            if (value == null)
            {
                if (i >= 0)
                {
                    _items.RemoveAt(i);
                }
                return;
            }
            if (value is not (string or int or VdfMap))
            {
                throw new ArgumentException($"VDF values are strings, ints or maps, not {value.GetType().Name}");
            }
            if (i >= 0)
            {
                _items[i] = new(_items[i].Key, value);
            }
            else
            {
                _items.Add(new(key, value));
            }
        }
    }

    public string? Str(string key) => this[key] as string;

    public int? Int(string key) => this[key] as int?;

    public VdfMap? Map(string key) => this[key] as VdfMap;

    public void Remove(string key) => this[key] = null;
}

public static class BinaryVdf
{
    private const byte TMap = 0, TString = 1, TInt = 2, TEnd = 8;

    public static VdfMap Read(byte[] data)
    {
        var pos = 0;
        var root = ReadMap(data, ref pos);
        return root;
    }

    public static byte[] Write(VdfMap root)
    {
        using var ms = new MemoryStream();
        WriteMap(ms, root);
        return ms.ToArray();
    }

    private static VdfMap ReadMap(byte[] d, ref int pos)
    {
        var map = new VdfMap();
        while (true)
        {
            if (pos >= d.Length)
            {
                return map;  // tolerate a missing trailing end marker
            }
            var type = d[pos++];
            if (type == TEnd)
            {
                return map;
            }
            var key = ReadCString(d, ref pos);
            switch (type)
            {
                case TMap:
                    map[key] = ReadMap(d, ref pos);
                    break;
                case TString:
                    map[key] = ReadCString(d, ref pos);
                    break;
                case TInt:
                    if (pos + 4 > d.Length)
                    {
                        throw new InvalidDataException("shortcuts.vdf ends inside a number");
                    }
                    map[key] = BitConverter.ToInt32(d, pos);
                    pos += 4;
                    break;
                default:
                    throw new InvalidDataException($"shortcuts.vdf has an unknown value type {type} at byte {pos - 1}");
            }
        }
    }

    private static string ReadCString(byte[] d, ref int pos)
    {
        var end = Array.IndexOf(d, (byte)0, pos);
        if (end < 0)
        {
            throw new InvalidDataException("shortcuts.vdf ends inside a name");
        }
        var s = Encoding.UTF8.GetString(d, pos, end - pos);
        pos = end + 1;
        return s;
    }

    private static void WriteMap(Stream s, VdfMap map)
    {
        foreach (var (key, value) in map.Items)
        {
            switch (value)
            {
                case VdfMap m:
                    s.WriteByte(TMap);
                    WriteCString(s, key);
                    WriteMap(s, m);
                    break;
                case string str:
                    s.WriteByte(TString);
                    WriteCString(s, key);
                    WriteCString(s, str);
                    break;
                case int i:
                    s.WriteByte(TInt);
                    WriteCString(s, key);
                    s.Write(BitConverter.GetBytes(i));
                    break;
            }
        }
        s.WriteByte(TEnd);
    }

    private static void WriteCString(Stream s, string v)
    {
        s.Write(Encoding.UTF8.GetBytes(v));
        s.WriteByte(0);
    }
}
