namespace DependencyManager.Managers;

/// <summary>
/// Pure argument-building and output-parsing for Homebrew, kept separate from
/// <see cref="BrewManager"/> so it is unit-testable without shelling out (the same
/// pattern as BrowserPolicy / MacosUpdate). BrewManager is the thin shell that
/// runs these against the real <c>brew</c>.
/// </summary>
public static class BrewPlan
{
    /// <summary>
    /// Args to enumerate installed packages.
    /// <list type="bullet">
    /// <item>cask → <c>brew list --cask -1</c> (casks have no dependency concept).</item>
    /// <item>formula, explicit → <c>brew leaves --installed-on-request</c> — the
    ///   user-requested top-level set, the ONLY safe prune candidates (never the
    ///   dependency closure, which would orphan declared formulae).</item>
    /// <item>formula, full → <c>brew list --formula -1</c> — everything installed,
    ///   used to answer "is it present at all?".</item>
    /// </list>
    /// </summary>
    public static string[] ListArgs(bool cask, bool explicitOnly) =>
        (cask, explicitOnly) switch
        {
            (true, _) => ["list", "--cask", "-1"],
            (false, true) => ["leaves", "--installed-on-request"],
            (false, false) => ["list", "--formula", "-1"],
        };

    public static string[] InstallArgs(bool cask, string name) =>
        cask ? ["install", "--cask", name] : ["install", name];

    public static string[] UninstallArgs(bool cask, string id) =>
        cask ? ["uninstall", "--cask", id] : ["uninstall", id];

    /// <summary>
    /// A <c>source:</c> is a Homebrew tap (<c>owner/repo</c>) when it contains a
    /// slash; a bare word is a flatpak-style remote that doesn't apply to brew.
    /// </summary>
    public static bool IsTap(string? source) =>
        !string.IsNullOrEmpty(source) && source.Contains('/');

    /// <summary>
    /// Parse brew's one-name-per-line list output (<c>-1</c> / <c>leaves</c>),
    /// trimming blanks.
    /// </summary>
    public static IReadOnlyList<string> ParseNames(string stdout)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(stdout)) return names;
        foreach (var line in stdout.Split('\n'))
        {
            var name = line.Trim();
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }
}
