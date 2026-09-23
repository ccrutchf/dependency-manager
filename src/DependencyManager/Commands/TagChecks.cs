using DependencyManager.Config;

namespace DependencyManager.Commands;

internal static class TagChecks
{
    /// <summary>
    /// Reports unknown active tags. Returns false when the run must stop (destructive
    /// runs refuse; see <see cref="TagGuard"/>).
    /// </summary>
    public static bool Enforce(ConfigFile config, ActiveTags tags, bool destructive)
    {
        var check = TagGuard.Check(config, tags, destructive);
        if (check.Message is not null) Console.Error.WriteLine(check.Message);
        return !check.Refuse;
    }
}
