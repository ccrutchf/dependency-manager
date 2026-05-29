namespace DependencyManager.Util;

/// <summary>
/// Pure planner for the system-wide NixOS update. A channel-based system upgrades
/// channels in place (<c>nixos-rebuild switch --upgrade</c>); a flake-based system
/// has no channels, so its config lives in a flake repo identified by
/// <c>DEPEND_NIXOS_FLAKE</c> (e.g. <c>/path/to/repo#hostname</c>) and is upgraded by
/// bumping its inputs then rebuilding from the flake.
/// </summary>
public static class NixosUpdate
{
    public sealed record Step(string Command, IReadOnlyList<string> Args, bool Sudo);

    /// <param name="flakeRef">
    /// The <c>DEPEND_NIXOS_FLAKE</c> value: an absolute flake path optionally suffixed
    /// with <c>#hostname</c>. Null/blank means the system is channel-based.
    /// </param>
    public static IReadOnlyList<Step> Plan(string? flakeRef)
    {
        if (string.IsNullOrWhiteSpace(flakeRef))
            return [new Step("nixos-rebuild", ["switch", "--upgrade"], Sudo: true)];

        flakeRef = flakeRef.Trim();
        var dir = flakeRef.Split('#', 2)[0];
        return
        [
            // The flake repo is user-owned; writing flake.lock as root would break it.
            new Step("nix", ["flake", "update", "--flake", dir], Sudo: false),
            new Step("nixos-rebuild", ["switch", "--flake", flakeRef], Sudo: true),
        ];
    }
}
