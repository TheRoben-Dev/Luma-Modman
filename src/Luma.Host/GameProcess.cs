using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Luma.Host;

/// <summary>
/// Starting a game for the launch wrapper and waiting until it has really finished. On Linux that
/// is the command Steam gave (Proton waits for the game itself). On Windows many games start
/// through a launcher or script extender (skse64_loader.exe) that exits once the game is up, so
/// the game is put in a job object and Luma waits until every process in it has exited.
/// </summary>
public static class GameProcess
{
    /// <summary>Starts <paramref name="psi"/> and returns its exit code once it (and on Windows, everything it started) has exited.</summary>
    public static int Run(ProcessStartInfo psi)
    {
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"couldn't start {psi.FileName}");
        if (!OperatingSystem.IsWindows())
        {
            p.WaitForExit();
            return p.ExitCode;
        }
        var job = CreateJobObjectW(IntPtr.Zero, null);
        var tracked = job != IntPtr.Zero && AssignProcessToJobObject(job, p.Handle);
        try
        {
            p.WaitForExit();
            var code = p.ExitCode;
            while (tracked && ActiveProcesses(job) > 0)
            {
                Thread.Sleep(1000);
            }
            return code;
        }
        finally
        {
            if (job != IntPtr.Zero)
            {
                CloseHandle(job);  // no kill-on-close: anything still running keeps running
            }
        }
    }

    /// <summary>
    /// Windows: hides the console window when this process owns it alone (started by Steam or a
    /// browser link rather than from a terminal), so `luma run` and `luma nxm` don't flash a window.
    /// </summary>
    public static void HideOwnConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            var ids = new uint[2];
            if (GetConsoleProcessList(ids, (uint)ids.Length) == 1 && GetConsoleWindow() is var w && w != IntPtr.Zero)
            {
                ShowWindow(w, 0);  // SW_HIDE
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    private static uint ActiveProcesses(IntPtr job)
    {
        var info = new BasicAccounting();
        return QueryInformationJobObject(job, 1 /* JobObjectBasicAccountingInformation */, ref info, (uint)Marshal.SizeOf<BasicAccounting>(), IntPtr.Zero)
            ? info.ActiveProcesses
            : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccounting
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, ref BasicAccounting info, uint length, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList([Out] uint[] ids, uint count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
