using System.Diagnostics;
using System.IO.Compression;

namespace Luma.Core.Mods;

/// <summary>
/// Unpacks mod archives. Zip is built in; 7z, rar and tar archives use bsdtar (libarchive, part
/// of SteamOS and Bazzite) or 7-Zip when installed; on Windows 7-Zip or Windows' own tar.exe. Entries that would land outside the target
/// folder are refused.
/// </summary>
public static class Archive
{
    public static readonly string[] Extensions = [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".xz", ".bz2"];

    /// <summary>True for files installed as they are (a single .pak, .esp, .dll...).</summary>
    public static bool IsArchive(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static void Extract(string archive, string dest)
    {
        Directory.CreateDirectory(dest);
        var full = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        if (!IsArchive(archive))
        {
            File.Copy(archive, Path.Combine(dest, Path.GetFileName(archive)), overwrite: true);
            return;
        }
        if (IsZip(archive))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var e in zip.Entries)
            {
                var name = e.FullName.Replace('\\', '/');
                var target = Path.GetFullPath(Path.Combine(dest, name));
                if (!target.StartsWith(full, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"{Path.GetFileName(archive)} has a file that would land outside its folder ({e.FullName}). Luma won't install it.");
                }
                if (name.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, overwrite: true);
            }
            return;
        }
        RunTool(archive, dest);
        // External tools are trusted to stay inside dest, but check anyway (symlinks pointing out are removed).
        foreach (var f in Directory.EnumerateFileSystemEntries(dest, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(f);
            if (info.LinkTarget != null)
            {
                File.Delete(f);
            }
        }
    }

    private static bool IsZip(string path)
    {
        using var s = File.OpenRead(path);
        Span<byte> head = stackalloc byte[4];
        return s.Read(head) == 4 && head[0] == 'P' && head[1] == 'K';
    }

    private static void RunTool(string archive, string dest)
    {
        var tools = OperatingSystem.IsWindows()
            ? new List<(string Exe, string[] Args)>
            {
                // 7-Zip where installed, then the tar.exe built into Windows (libarchive: zip, and 7z/rar on Windows 11).
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"), ["x", "-y", "-o" + dest, archive]),
                ("7z", ["x", "-y", "-o" + dest, archive]),
                (Path.Combine(Environment.SystemDirectory, "tar.exe"), ["-xf", archive, "-C", dest]),
            }
            : new List<(string Exe, string[] Args)>
            {
                ("bsdtar", ["-xf", archive, "-C", dest]),
                ("7z", ["x", "-y", "-o" + dest, archive]),
                ("7za", ["x", "-y", "-o" + dest, archive]),
                ("7zz", ["x", "-y", "-o" + dest, archive]),
            };
        string? lastError = null;
        foreach (var (exe, args) in tools)
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
            try
            {
                using var p = Process.Start(psi)!;
                var err = p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0)
                {
                    return;
                }
                lastError = err.Result.Trim();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // not installed; try the next one
            }
        }
        throw new InvalidDataException(lastError != null
            ? $"Couldn't unpack {Path.GetFileName(archive)}: {lastError}"
            : OperatingSystem.IsWindows()
                ? $"Couldn't unpack {Path.GetFileName(archive)}: install 7-Zip (7-zip.org) and try again."
                : $"Couldn't unpack {Path.GetFileName(archive)}: it needs bsdtar or 7-Zip, and neither is installed.");
    }
}
