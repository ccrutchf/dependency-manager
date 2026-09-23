using DependencyManager.Util;

namespace DependencyManager.Config;

public static class BlockFilter
{
    public static bool Matches(Block block, PlatformInfo platform) =>
        Matches(block, platform, ActiveTags.None);

    public static bool Matches(Block block, PlatformInfo platform, ActiveTags tags) =>
        MatchesPlatform(block, platform) && TagSkipReason(block, tags) is null;

    public static bool MatchesPlatform(Block block, PlatformInfo platform)
    {
        if (!MatchesField(block.Platform, platform.Os)) return false;
        if (!MatchesField(block.Architecture, platform.Architecture)) return false;
        if (!string.IsNullOrEmpty(block.Version) && !VersionMatches(block.Version, platform.Version)) return false;
        return true;
    }

    /// <summary>
    /// Null when the block's <c>tags</c>/<c>exclude_tags</c> admit it; otherwise a
    /// human-readable reason it was skipped. Absent or empty lists impose no constraint.
    /// </summary>
    public static string? TagSkipReason(Block block, ActiveTags tags)
    {
        var excludedBy = (block.ExcludeTags ?? []).Where(tags.Contains).ToList();
        if (excludedBy.Count > 0)
            return $"excluded by active tag(s) [{string.Join(", ", excludedBy)}]";

        if (block.Tags is { Count: > 0 } required && !required.Any(tags.Contains))
        {
            var active = tags.Names.Count == 0
                ? "none active"
                : $"active: [{string.Join(", ", tags.Names.Order(StringComparer.OrdinalIgnoreCase))}]";
            return $"requires one of tags [{string.Join(", ", required)}]; {active}";
        }

        return null;
    }

    private static bool MatchesField(string blockValue, string platformValue) =>
        blockValue.Equals("all", StringComparison.OrdinalIgnoreCase)
        || blockValue.Equals(platformValue, StringComparison.OrdinalIgnoreCase);

    private static bool VersionMatches(string blockVersion, string platformVersion) =>
        platformVersion.StartsWith(blockVersion, StringComparison.OrdinalIgnoreCase);
}
