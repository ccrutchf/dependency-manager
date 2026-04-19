using System.Runtime.InteropServices;

namespace DependencyManager.Util;

public sealed record PlatformInfo(string Os, string Architecture, string Version)
{
    public static PlatformInfo Current()
    {
        string os;
        if (OperatingSystem.IsLinux()) os = "linux";
        else if (OperatingSystem.IsWindows()) os = "windows";
        else if (OperatingSystem.IsMacOS()) os = "osx";
        else os = "unknown";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "amd64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };

        var version = Environment.OSVersion.Version.ToString();
        return new PlatformInfo(os, arch, version);
    }
}
