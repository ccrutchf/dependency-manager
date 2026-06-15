using DependencyManager.Managers;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class BrewPlanTests
{
    [Fact]
    public void List_formula_full_uses_list_formula()
    {
        BrewPlan.ListArgs(cask: false, explicitOnly: false)
            .ShouldBe(["list", "--formula", "-1"]);
    }

    [Fact]
    public void List_formula_explicit_uses_leaves_installed_on_request()
    {
        // The prune candidate set MUST be leaves-only, never the dependency closure.
        BrewPlan.ListArgs(cask: false, explicitOnly: true)
            .ShouldBe(["leaves", "--installed-on-request"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void List_cask_is_the_same_regardless_of_explicit(bool explicitOnly)
    {
        // Casks have no dependency concept, so the full list IS the explicit set.
        BrewPlan.ListArgs(cask: true, explicitOnly)
            .ShouldBe(["list", "--cask", "-1"]);
    }

    [Fact]
    public void Install_formula_omits_cask_flag()
    {
        BrewPlan.InstallArgs(cask: false, "swiftlint").ShouldBe(["install", "swiftlint"]);
    }

    [Fact]
    public void Install_cask_adds_cask_flag()
    {
        BrewPlan.InstallArgs(cask: true, "visual-studio-code")
            .ShouldBe(["install", "--cask", "visual-studio-code"]);
    }

    [Fact]
    public void Uninstall_formula_and_cask()
    {
        BrewPlan.UninstallArgs(cask: false, "htop").ShouldBe(["uninstall", "htop"]);
        BrewPlan.UninstallArgs(cask: true, "macfuse").ShouldBe(["uninstall", "--cask", "macfuse"]);
    }

    [Theory]
    [InlineData("homebrew/cask-fonts", true)]   // owner/repo → tap
    [InlineData("some/other/tap", true)]
    [InlineData("flathub", false)]              // bare word → not a brew tap
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTap_only_when_source_has_a_slash(string? source, bool expected)
    {
        BrewPlan.IsTap(source).ShouldBe(expected);
    }

    [Fact]
    public void ParseNames_trims_and_drops_blank_lines()
    {
        var stdout = "cocoapods\n  fastlane  \n\nrbenv\n";
        BrewPlan.ParseNames(stdout).ShouldBe(["cocoapods", "fastlane", "rbenv"]);
    }

    [Fact]
    public void ParseNames_empty_is_empty()
    {
        BrewPlan.ParseNames("").ShouldBeEmpty();
    }
}
