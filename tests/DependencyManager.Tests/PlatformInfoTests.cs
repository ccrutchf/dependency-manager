using System.Runtime.InteropServices;
using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class PlatformInfoTests
{
    [Fact]
    public void Os_is_one_of_the_documented_labels()
    {
        new[] { "linux", "osx", "windows", "unknown" }
            .ShouldContain(PlatformInfo.Current().Os);
    }

    [Fact]
    public void Os_matches_running_platform()
    {
        var os = PlatformInfo.Current().Os;
        if (OperatingSystem.IsLinux()) os.ShouldBe("linux");
        else if (OperatingSystem.IsMacOS()) os.ShouldBe("osx");
        else if (OperatingSystem.IsWindows()) os.ShouldBe("windows");
    }

    [Fact]
    public void Architecture_maps_known_runtime_archs_to_documented_labels()
    {
        var info = PlatformInfo.Current();
        switch (RuntimeInformation.ProcessArchitecture)
        {
            case Architecture.X64: info.Architecture.ShouldBe("amd64"); break;
            case Architecture.Arm64: info.Architecture.ShouldBe("arm64"); break;
            case Architecture.X86: info.Architecture.ShouldBe("x86"); break;
        }
    }

    [Fact]
    public void Architecture_is_lowercase()
    {
        var arch = PlatformInfo.Current().Architecture;
        arch.ShouldBe(arch.ToLowerInvariant());
    }

    [Fact]
    public void Version_is_populated()
    {
        PlatformInfo.Current().Version.ShouldNotBeNullOrWhiteSpace();
    }
}
