using DependencyManager.Managers;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class MasManagerTests
{
    [Fact]
    public void ParseInstalledIds_takes_the_first_token_of_each_line()
    {
        var output = "497799835  Xcode (15.4)\n1295203466  Microsoft Remote Desktop (10.9)\n";
        MasManager.ParseInstalledIds(output).ShouldBe(["497799835", "1295203466"]);
    }

    [Fact]
    public void ParseInstalledIds_skips_blank_lines()
    {
        var output = "\n  \n497799835  Xcode (15.4)\n\n";
        MasManager.ParseInstalledIds(output).ShouldBe(["497799835"]);
    }

    [Fact]
    public void ParseInstalledIds_empty_is_empty()
    {
        MasManager.ParseInstalledIds("").ShouldBeEmpty();
    }
}
