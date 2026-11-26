using System.Runtime.InteropServices;

namespace Luma.Core;

/// <summary>
/// Placing a mod file in a game folder without copying it. Linux: a symbolic link. Windows: a
/// symbolic link when the user may make one (Developer Mode or admin), otherwise a hard link
/// (needs no rights, but only works on the same drive). Null when neither works and the caller
/// should copy.
/// </summary>
public static class FileLinks
{
    public enum Kind
    {
        Symlink,
        Hardlink,
    }

    public static Kind? TryLink(string target, string source)
    {
        try
        {
            File.CreateSymbolicLink(target, source);
            return Kind.Symlink;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(target) || new FileInfo(target).LinkTarget != null)
            {
                throw;  // something else is in the way: let the caller see it
            }
        }
        if (OperatingSystem.IsWindows() && CreateHardLinkW(target, source, IntPtr.Zero))
        {
            return Kind.Hardlink;
        }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);
}
