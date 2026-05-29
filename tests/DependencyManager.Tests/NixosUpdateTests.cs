using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class NixosUpdateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_flake_ref_upgrades_channels(string? flakeRef)
    {
        var steps = NixosUpdate.Plan(flakeRef);

        steps.Count.ShouldBe(1);
        steps[0].Command.ShouldBe("nixos-rebuild");
        steps[0].Args.ShouldBe(["switch", "--upgrade"]);
        steps[0].Sudo.ShouldBeTrue();
    }

    [Fact]
    public void Flake_ref_updates_inputs_then_switches_flake()
    {
        var steps = NixosUpdate.Plan("/home/chris/Repos/personal/laptop#chris-laptop");

        steps.Count.ShouldBe(2);

        // Lock-file update runs as the normal user (the flake repo is user-owned).
        steps[0].Command.ShouldBe("nix");
        steps[0].Args.ShouldBe(["flake", "update", "--flake", "/home/chris/Repos/personal/laptop"]);
        steps[0].Sudo.ShouldBeFalse();

        // The rebuild needs root and takes the full ref (path#host).
        steps[1].Command.ShouldBe("nixos-rebuild");
        steps[1].Args.ShouldBe(["switch", "--flake", "/home/chris/Repos/personal/laptop#chris-laptop"]);
        steps[1].Sudo.ShouldBeTrue();
    }

    [Fact]
    public void Flake_ref_without_hostname_uses_dir_for_both()
    {
        var steps = NixosUpdate.Plan("  /etc/nixos  ");

        steps[0].Args.ShouldBe(["flake", "update", "--flake", "/etc/nixos"]);
        steps[1].Args.ShouldBe(["switch", "--flake", "/etc/nixos"]);
    }
}
