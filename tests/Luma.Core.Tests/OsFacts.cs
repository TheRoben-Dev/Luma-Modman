using Xunit;

namespace Luma.Core.Tests;

/// <summary>A test of Linux behaviour (XDG folders, Proton prefixes, Flatpak): skipped on Windows.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Linux only";
        }
    }
}

/// <summary>A test of Windows behaviour (registry, known folders): skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only";
        }
    }
}
