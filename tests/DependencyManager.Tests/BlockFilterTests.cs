using DependencyManager.Config;
using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class BlockFilterTests
{
    private static PlatformInfo Linux64 => new("linux", "amd64", "5.15.0.1054");
    private static PlatformInfo WindowsArm => new("windows", "arm64", "10.0.22631");

    [Fact]
    public void All_wildcards_match_everything()
    {
        BlockFilter.Matches(new Block(), Linux64).ShouldBeTrue();
        BlockFilter.Matches(new Block(), WindowsArm).ShouldBeTrue();
    }

    [Fact]
    public void Platform_mismatch_fails()
    {
        var block = new Block { Platform = "windows" };
        BlockFilter.Matches(block, Linux64).ShouldBeFalse();
        BlockFilter.Matches(block, WindowsArm).ShouldBeTrue();
    }

    [Fact]
    public void Architecture_mismatch_fails()
    {
        var block = new Block { Platform = "linux", Architecture = "arm64" };
        BlockFilter.Matches(block, Linux64).ShouldBeFalse();
    }

    [Fact]
    public void Version_is_prefix_match()
    {
        var block = new Block { Platform = "linux", Version = "5.15" };
        BlockFilter.Matches(block, Linux64).ShouldBeTrue();

        var mismatch = new Block { Platform = "linux", Version = "6.0" };
        BlockFilter.Matches(mismatch, Linux64).ShouldBeFalse();
    }

    [Fact]
    public void Platform_comparison_is_case_insensitive()
    {
        var block = new Block { Platform = "LINUX" };
        BlockFilter.Matches(block, Linux64).ShouldBeTrue();
    }
}
