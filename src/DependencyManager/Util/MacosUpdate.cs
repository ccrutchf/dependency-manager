namespace DependencyManager.Util;

/// <summary>
/// Pure planner and result-parser for the macOS system-wide update.
/// <c>softwareupdate --install --all</c> installs every available update except
/// those that require restart on Apple Silicon (OS upgrades); pass
/// <paramref name="includeRestart"/> to opt into <c>--restart</c> for those.
/// softwareupdate is known to exit 0 even when it silently skipped updates
/// (insufficient space, missing flag, license-gated), so the verification step
/// re-runs <c>softwareupdate --list</c> and surfaces anything still pending.
/// </summary>
public static class MacosUpdate
{
    public sealed record Step(string Command, IReadOnlyList<string> Args, bool Sudo);

    public static IReadOnlyList<Step> Plan(bool includeRestart = false)
    {
        var args = new List<string> { "--install", "--all" };
        if (includeRestart) args.Add("--restart");
        return [new Step("softwareupdate", args, Sudo: true)];
    }

    /// <summary>
    /// Returns the labels of updates still pending in the given
    /// <c>softwareupdate --list</c> output. Empty list means no updates pending.
    /// </summary>
    public static IReadOnlyList<string> ParsePendingUpdates(string listOutput)
    {
        const string prefix = "* Label: ";
        var labels = new List<string>();
        if (string.IsNullOrEmpty(listOutput)) return labels;

        foreach (var rawLine in listOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                labels.Add(line[prefix.Length..].TrimEnd());
        }
        return labels;
    }
}
