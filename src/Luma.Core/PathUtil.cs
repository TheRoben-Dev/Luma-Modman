using System.Runtime.InteropServices;

namespace Luma.Core;

public static class PathUtil
{
    /// <summary>Absolute path with every symlink resolved (realpath(3) on Unix); the full path otherwise.</summary>
    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // Windows: "D:\SteamLibrary\" and "D:\SteamLibrary" are the same folder.
            return full.Length > 3 ? full.TrimEnd('\\', '/') : full;
        }
        try
        {
            var p = realpath(full, IntPtr.Zero);
            if (p == IntPtr.Zero)
            {
                return full;
            }
            try
            {
                return Marshal.PtrToStringUTF8(p) ?? full;
            }
            finally
            {
                free(p);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return full;
        }
    }

    /// <summary>
    /// Resolves <paramref name="relative"/> under <paramref name="root"/> ignoring case per segment
    /// (Windows games on a Linux file system: "Data" vs "data", "SkyrimSE.exe" vs "skyrimse.exe").
    /// Returns the real path, or null if any segment is missing.
    /// </summary>
    public static string? FindCaseInsensitive(string root, string relative)
    {
        var cur = root;
        foreach (var part in relative.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var exact = Path.Combine(cur, part);
            if (File.Exists(exact) || Directory.Exists(exact))
            {
                cur = exact;
                continue;
            }
            try
            {
                var match = Directory.Exists(cur)
                    ? Directory.EnumerateFileSystemEntries(cur).FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (match == null)
                {
                    return null;
                }
                cur = match;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        return cur;
    }

    [DllImport("libc", CharSet = CharSet.Ansi)]
    private static extern IntPtr realpath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);

    [DllImport("libc")]
    private static extern void free(IntPtr p);
}
