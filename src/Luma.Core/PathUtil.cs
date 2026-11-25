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
            return full;
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

    [DllImport("libc", CharSet = CharSet.Ansi)]
    private static extern IntPtr realpath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);

    [DllImport("libc")]
    private static extern void free(IntPtr p);
}
