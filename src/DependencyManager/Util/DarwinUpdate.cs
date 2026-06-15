namespace DependencyManager.Util;

/// <summary>
/// Pure planner for the nix-darwin system update — the macOS analogue of
/// <see cref="NixosUpdate"/>. A flake-based darwin system's config lives in a flake
/// repo identified by <c>DEPEND_DARWIN_FLAKE</c> (e.g. <c>/path/to/repo#hostname</c>);
/// updating it means bumping the inputs then <c>darwin-rebuild switch --flake</c>.
/// With no flake ref it falls back to a bare <c>darwin-rebuild switch</c>.
///
/// This drives the *Nix* layer on macOS; Apple's own updates are handled separately
/// by <see cref="MacosUpdate"/> (softwareupdate).
/// </summary>
public static class DarwinUpdate
{
    public sealed record Step(string Command, IReadOnlyList<string> Args, bool Sudo);

    /// <param name="flakeRef">
    /// The <c>DEPEND_DARWIN_FLAKE</c> value: an absolute flake path optionally suffixed
    /// with <c>#hostname</c>. Null/blank falls back to a bare switch.
    /// </param>
    public static IReadOnlyList<Step> Plan(string? flakeRef)
    {
        if (string.IsNullOrWhiteSpace(flakeRef))
            return [new Step("darwin-rebuild", ["switch"], Sudo: true)];

        flakeRef = flakeRef.Trim();
        var dir = flakeRef.Split('#', 2)[0];
        return
        [
            // The flake repo is user-owned; writing flake.lock as root would break it.
            new Step("nix", ["flake", "update", "--flake", dir], Sudo: false),
            new Step("darwin-rebuild", ["switch", "--flake", flakeRef], Sudo: true),
        ];
    }
}
