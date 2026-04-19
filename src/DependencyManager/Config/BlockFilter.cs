using DependencyManager.Util;

namespace DependencyManager.Config;

public static class BlockFilter
{
    public static bool Matches(Block block, PlatformInfo platform)
    {
        if (!MatchesField(block.Platform, platform.Os)) return false;
        if (!MatchesField(block.Architecture, platform.Architecture)) return false;
        if (!string.IsNullOrEmpty(block.Version) && !VersionMatches(block.Version, platform.Version)) return false;
        return true;
    }

    private static bool MatchesField(string blockValue, string platformValue) =>
        blockValue.Equals("all", StringComparison.OrdinalIgnoreCase)
        || blockValue.Equals(platformValue, StringComparison.OrdinalIgnoreCase);

    private static bool VersionMatches(string blockVersion, string platformVersion) =>
        platformVersion.StartsWith(blockVersion, StringComparison.OrdinalIgnoreCase);
}
