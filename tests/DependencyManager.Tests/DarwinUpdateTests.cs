using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class DarwinUpdateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_flake_ref_does_a_bare_switch(string? flakeRef)
    {
        var steps = DarwinUpdate.Plan(flakeRef);

        steps.Count.ShouldBe(1);
        steps[0].Command.ShouldBe("darwin-rebuild");
        steps[0].Args.ShouldBe(["switch"]);
        steps[0].Sudo.ShouldBeTrue();
    }

    [Fact]
    public void Flake_ref_updates_inputs_then_switches_flake()
    {
        var steps = DarwinUpdate.Plan("/Users/chris/Repos/personal/laptop#chris-macbook");

        steps.Count.ShouldBe(2);

        // Lock-file update runs as the normal user (the flake repo is user-owned).
        steps[0].Command.ShouldBe("nix");
        steps[0].Args.ShouldBe(["flake", "update", "--flake", "/Users/chris/Repos/personal/laptop"]);
        steps[0].Sudo.ShouldBeFalse();

        // The rebuild needs root and takes the full ref (path#host).
        steps[1].Command.ShouldBe("darwin-rebuild");
        steps[1].Args.ShouldBe(["switch", "--flake", "/Users/chris/Repos/personal/laptop#chris-macbook"]);
        steps[1].Sudo.ShouldBeTrue();
    }

    [Fact]
    public void Flake_ref_without_hostname_uses_dir_for_both()
    {
        var steps = DarwinUpdate.Plan("  /Users/chris/Repos/personal/laptop  ");

        steps[0].Args.ShouldBe(["flake", "update", "--flake", "/Users/chris/Repos/personal/laptop"]);
        steps[1].Args.ShouldBe(["switch", "--flake", "/Users/chris/Repos/personal/laptop"]);
    }
}
