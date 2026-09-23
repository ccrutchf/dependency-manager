using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Commands;

internal static class TagChecks
{
    /// <summary>
    /// Reports unknown or missing active tags. Returns false when the run must stop (destructive
    /// runs refuse; see <see cref="TagGuard"/>).
    /// </summary>
    public static bool Enforce(ConfigFile config, PlatformInfo platform, ActiveTags tags, bool destructive)
    {
        var check = TagGuard.Check(config, platform, tags, destructive);
        if (check.Message is not null) Console.Error.WriteLine(check.Message);
        return !check.Refuse;
    }
}
