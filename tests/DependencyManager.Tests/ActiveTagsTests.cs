using DependencyManager.Config;
using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class ActiveTagsTests
{
    private static PlatformInfo Linux => new("linux", "amd64", "6.6.0");
    private static PlatformInfo Mac => new("osx", "arm64", "15.0");

    [Fact]
    public void No_cli_and_no_env_resolves_to_none()
    {
        var tags = ActiveTags.Resolve(cli: null, env: null);

        tags.Names.ShouldBeEmpty();
        tags.Source.ShouldBe(TagSource.None);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ,")]
    public void Blank_env_resolves_to_none(string env)
    {
        var tags = ActiveTags.Resolve(cli: [], env: env);

        tags.Names.ShouldBeEmpty();
        tags.Source.ShouldBe(TagSource.None);
    }

    [Fact]
    public void Env_is_comma_separated_and_trimmed()
    {
        var tags = ActiveTags.Resolve(cli: [], env: " desktop , gaming,, ");

        tags.Names.ShouldBe(["desktop", "gaming"], ignoreOrder: true);
        tags.Source.ShouldBe(TagSource.Env);
    }

    [Fact]
    public void Cli_replaces_env_entirely()
    {
        var tags = ActiveTags.Resolve(cli: ["crostini"], env: "desktop");

        tags.Names.ShouldBe(["crostini"]);
        tags.Source.ShouldBe(TagSource.Cli);
    }

    [Fact]
    public void Repeated_cli_values_accumulate_and_accept_commas()
    {
        var tags = ActiveTags.Resolve(cli: ["desktop", " gaming,work "], env: null);

        tags.Names.ShouldBe(["desktop", "gaming", "work"], ignoreOrder: true);
        tags.Source.ShouldBe(TagSource.Cli);
    }

    [Fact]
    public void Blank_cli_value_still_overrides_env()
    {
        // `--tag ''` is the escape hatch for "ignore DEPEND_TAGS for this run".
        var tags = ActiveTags.Resolve(cli: [""], env: "desktop");

        tags.Names.ShouldBeEmpty();
        tags.Source.ShouldBe(TagSource.Cli);
    }

    [Fact]
    public void Names_are_case_insensitive_and_deduped()
    {
        var tags = ActiveTags.Resolve(cli: null, env: "Desktop,DESKTOP");

        tags.Names.Count.ShouldBe(1);
        tags.Names.Contains("desktop").ShouldBeTrue();
    }

    [Fact]
    public void Unknown_lists_active_tags_no_block_mentions()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["desktop"] = new() { Tags = ["desktop"] },
            ["not-crostini"] = new() { ExcludeTags = ["Crostini"] },
            ["untagged"] = new(),
        });

        var tags = ActiveTags.Resolve(cli: ["DESKTOP", "crostini", "desktp"], env: null);

        tags.UnknownIn(config).ShouldBe(["desktp"]);
    }

    [Fact]
    public void Unknown_considers_blocks_for_every_platform()
    {
        // A tag used only by a mac block is still a real tag, not a typo.
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["mac-work"] = new() { Platform = "osx", Tags = ["work"] },
        });

        ActiveTags.Resolve(cli: ["work"], env: null).UnknownIn(config).ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_is_empty_when_no_tags_active()
    {
        var config = new ConfigFile(new Dictionary<string, Block> { ["a"] = new() });

        ActiveTags.None.UnknownIn(config).ShouldBeEmpty();
    }

    [Fact]
    public void Destructive_run_with_unknown_tag_is_refused()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["desktop"] = new() { Tags = ["desktop"] },
        });
        var tags = ActiveTags.Resolve(cli: null, env: "desktp");

        var check = TagGuard.Check(config, Linux, tags, destructive: true);

        check.Refuse.ShouldBeTrue();
        check.Unknown.ShouldBe(["desktp"]);
        check.Message!.ShouldContain("desktp");
        check.Message!.ShouldContain("DEPEND_TAGS");
    }

    [Fact]
    public void Non_destructive_run_with_unknown_tag_only_warns()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["desktop"] = new() { Tags = ["desktop"] },
        });
        var tags = ActiveTags.Resolve(cli: ["desktp"], env: null);

        var check = TagGuard.Check(config, Linux, tags, destructive: false);

        check.Refuse.ShouldBeFalse();
        check.Message!.ShouldContain("desktp");
        check.Message!.ShouldContain("--tag");
    }

    [Fact]
    public void Known_tags_pass_the_guard_even_when_destructive()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["desktop"] = new() { Tags = ["desktop"] },
        });
        var tags = ActiveTags.Resolve(cli: ["desktop"], env: null);

        var check = TagGuard.Check(config, Linux, tags, destructive: true);

        check.Refuse.ShouldBeFalse();
        check.Unknown.ShouldBeEmpty();
        check.Message.ShouldBeNull();
    }

    private static ConfigFile LinuxTaggedConfig() => new(new Dictionary<string, Block>
    {
        ["linux-shared"] = new() { Platform = "linux" },
        ["linux-desktop"] = new() { Platform = "linux", Tags = ["desktop"] },
        ["crostini-bootstrap"] = new() { Platform = "linux", Tags = ["crostini"] },
        ["vscode"] = new() { ExcludeTags = ["crostini"] },
    });

    [Fact]
    public void Destructive_run_with_no_tags_set_is_refused_when_platform_has_tagged_blocks()
    {
        // DEPEND_TAGS unset (cron, un-exported shell): the tag-gated blocks silently
        // drop out, and a prune would remove their packages.
        var check = TagGuard.Check(LinuxTaggedConfig(), Linux, ActiveTags.None, destructive: true);

        check.Refuse.ShouldBeTrue();
        check.Message!.ShouldContain("no tags are active");
        check.Message!.ShouldContain("linux-desktop");
        check.Message!.ShouldContain("crostini-bootstrap");
        check.Message!.ShouldContain("--tag ''");
    }

    [Fact]
    public void Untagged_machine_whose_tagged_blocks_are_other_platforms_may_prune()
    {
        // The untagged Mac: every tag-gated block is linux-only.
        var check = TagGuard.Check(LinuxTaggedConfig(), Mac, ActiveTags.None, destructive: true);

        check.Refuse.ShouldBeFalse();
        check.Message.ShouldBeNull();
    }

    [Fact]
    public void Explicit_empty_cli_tag_is_a_deliberate_untagged_prune()
    {
        var tags = ActiveTags.Resolve(cli: [""], env: null);

        var check = TagGuard.Check(LinuxTaggedConfig(), Linux, tags, destructive: true);

        check.Refuse.ShouldBeFalse();
        check.Message.ShouldBeNull();
    }

    [Fact]
    public void Exclude_tags_alone_do_not_trigger_the_missing_tags_guard()
    {
        // Only `tags:` blocks drop out when no tags are active; exclude_tags blocks stay in.
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["vscode"] = new() { ExcludeTags = ["crostini"] },
        });

        TagGuard.Check(config, Linux, ActiveTags.None, destructive: true).Refuse.ShouldBeFalse();
    }

    [Fact]
    public void Non_destructive_run_with_no_tags_set_does_not_warn()
    {
        // plan already lists the tag-skipped blocks; an extra warning would be noise.
        var check = TagGuard.Check(LinuxTaggedConfig(), Linux, ActiveTags.None, destructive: false);

        check.Refuse.ShouldBeFalse();
        check.Message.ShouldBeNull();
    }
}
