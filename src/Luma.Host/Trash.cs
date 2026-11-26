using System.Diagnostics;

namespace Luma.Host;

/// <summary>Moves files to the desktop trash (gio trash; the Recycle Bin on Windows), so a removal can still be undone.</summary>
public static class Trash
{
    /// <summary>True when files can go to the trash on this system (otherwise removal is permanent).</summary>
    public static bool Available => OperatingSystem.IsWindows() || Find("gio") != null;

    /// <summary>Moves each file to the trash, or deletes it when there is no trash. Returns true if trashed.</summary>
    public static bool Remove(IReadOnlyList<string> files)
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var f in files.Where(File.Exists))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                AppLog.Info($"recycled {f}");
            }
            return true;
        }
        var gio = Find("gio");
        foreach (var f in files.Where(File.Exists))
        {
            if (gio != null)
            {
                var psi = new ProcessStartInfo(gio) { RedirectStandardError = true, UseShellExecute = false };
                psi.ArgumentList.Add("trash");
                psi.ArgumentList.Add("--");
                psi.ArgumentList.Add(f);
                using var p = Process.Start(psi)!;
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    throw new IOException($"Couldn't move {Path.GetFileName(f)} to the trash: {err.Trim()}");
                }
                AppLog.Info($"trashed {f}");
            }
            else
            {
                File.Delete(f);
                AppLog.Info($"deleted {f} (no trash available)");
            }
        }
        return gio != null;
    }

    private static string? Find(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
}
