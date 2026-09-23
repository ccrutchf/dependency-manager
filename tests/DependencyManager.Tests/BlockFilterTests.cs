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

    [Fact]
    public void Architecture_comparison_is_case_insensitive()
    {
        var block = new Block { Platform = "linux", Architecture = "AMD64" };
        BlockFilter.Matches(block, Linux64).ShouldBeTrue();
    }

    [Fact]
    public void Version_prefix_match_is_case_insensitive()
    {
        var block = new Block { Platform = "windows", Version = "10.0.22631" };
        BlockFilter.Matches(block, WindowsArm).ShouldBeTrue();

        var upper = new Block { Platform = "windows", Version = "10.0" };
        BlockFilter.Matches(upper, WindowsArm).ShouldBeTrue();
    }

    [Fact]
    public void All_wildcard_on_block_side_matches_specific_platform()
    {
        var block = new Block { Platform = "all", Architecture = "amd64" };
        BlockFilter.Matches(block, Linux64).ShouldBeTrue();
        BlockFilter.Matches(block, WindowsArm).ShouldBeFalse();
    }

    [Fact]
    public void All_wildcard_is_case_insensitive()
    {
        var block = new Block { Platform = "ALL", Architecture = "All" };
        BlockFilter.Matches(block, Linux64).ShouldBeTrue();
        BlockFilter.Matches(block, WindowsArm).ShouldBeTrue();
    }
}

public class BlockFilterTagTests
{
    private static PlatformInfo Linux64 => new("linux", "amd64", "6.6.0");
    private static PlatformInfo Mac => new("osx", "arm64", "15.0");

    private static ActiveTags Tags(params string[] names) => ActiveTags.Resolve(cli: names, env: null);

    [Fact]
    public void Untagged_block_matches_regardless_of_active_tags()
    {
        var block = new Block { Platform = "linux" };
        BlockFilter.Matches(block, Linux64, ActiveTags.None).ShouldBeTrue();
        BlockFilter.Matches(block, Linux64, Tags("desktop")).ShouldBeTrue();
    }

    [Fact]
    public void Tags_match_when_any_listed_tag_is_active()
    {
        var block = new Block { Tags = ["desktop", "laptop"] };
        BlockFilter.Matches(block, Linux64, Tags("laptop")).ShouldBeTrue();
        BlockFilter.Matches(block, Linux64, Tags("crostini")).ShouldBeFalse();
    }

    [Fact]
    public void Tagged_block_does_not_match_when_no_tags_active()
    {
        var block = new Block { Tags = ["desktop"] };
        BlockFilter.Matches(block, Linux64, ActiveTags.None).ShouldBeFalse();
        BlockFilter.Matches(block, Linux64).ShouldBeFalse();
    }

    [Fact]
    public void Exclude_tags_skip_when_any_listed_tag_is_active()
    {
        var block = new Block { ExcludeTags = ["crostini"] };
        BlockFilter.Matches(block, Linux64, Tags("crostini")).ShouldBeFalse();
        BlockFilter.Matches(block, Linux64, Tags("desktop")).ShouldBeTrue();
        BlockFilter.Matches(block, Linux64, ActiveTags.None).ShouldBeTrue();
    }

    [Fact]
    public void Exclude_wins_over_tags()
    {
        var block = new Block { Tags = ["desktop"], ExcludeTags = ["crostini"] };
        BlockFilter.Matches(block, Linux64, Tags("desktop", "crostini")).ShouldBeFalse();
        BlockFilter.Matches(block, Linux64, Tags("desktop")).ShouldBeTrue();
    }

    [Fact]
    public void Tag_comparison_is_case_insensitive()
    {
        BlockFilter.Matches(new Block { Tags = ["Desktop"] }, Linux64, Tags("DESKTOP")).ShouldBeTrue();
        BlockFilter.Matches(new Block { ExcludeTags = ["CROSTINI"] }, Linux64, Tags("crostini")).ShouldBeFalse();
    }

    [Fact]
    public void Tags_combine_with_platform_filters()
    {
        var block = new Block { Platform = "linux", Tags = ["desktop"] };
        BlockFilter.Matches(block, Linux64, Tags("desktop")).ShouldBeTrue();
        BlockFilter.Matches(block, Mac, Tags("desktop")).ShouldBeFalse();
    }

    [Fact]
    public void Empty_tag_lists_behave_like_absent_keys()
    {
        var block = new Block { Tags = [], ExcludeTags = [] };
        BlockFilter.Matches(block, Linux64, ActiveTags.None).ShouldBeTrue();
    }

    [Fact]
    public void Tag_skip_reason_explains_why()
    {
        BlockFilter.TagSkipReason(new Block { Tags = ["desktop"] }, ActiveTags.None)
            .ShouldBe("requires one of tags [desktop]; none active");
        BlockFilter.TagSkipReason(new Block { Tags = ["desktop", "laptop"] }, Tags("crostini"))
            .ShouldBe("requires one of tags [desktop, laptop]; active: [crostini]");
        BlockFilter.TagSkipReason(new Block { ExcludeTags = ["crostini"] }, Tags("Crostini"))
            .ShouldBe("excluded by active tag(s) [crostini]");
        BlockFilter.TagSkipReason(new Block { Tags = ["desktop"] }, Tags("desktop")).ShouldBeNull();
    }
}
