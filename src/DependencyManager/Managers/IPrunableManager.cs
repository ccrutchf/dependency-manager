using DependencyManager.Config;

namespace DependencyManager.Managers;

/// <summary>
/// How aggressively a provider may be pruned (have undeclared packages removed).
/// </summary>
public enum PrunePolicy
{
    /// <summary>
    /// Drift can be listed but never auto-removed — the provider has no safe
    /// uninstall path (e.g. <c>mas</c>: the Mac App Store CLI cannot uninstall).
    /// </summary>
    ReportOnly,

    /// <summary>
    /// Only packages depend itself installed may be removed. Requires state
    /// tracking; not yet implemented (reserved so apt/snap can opt in later
    /// without their base-system packages ever being eligible).
    /// </summary>
    Tracked,

    /// <summary>
    /// The provider's full explicit/top-level set is depend's to own: anything
    /// installed-but-undeclared is eligible for removal (nix-darwin's "zap").
    /// Only safe where the explicit set genuinely equals "things the user asked
    /// for" — brew leaves, casks, flatpak apps, pipx, cargo, vscode.
    /// </summary>
    Zap,
}

/// <summary>
/// Opt-in capability for managers that can converge their installed set down to
/// the declared set. A manager implements this ONLY if it can enumerate its
/// explicit/top-level installs (never the dependency closure) and remove them
/// safely. Managers that can't (apt/snap, whose "manual" set includes the base
/// system; script/deb, which have no generic uninstall) simply don't implement
/// it and are skipped by the prune phase.
/// </summary>
public interface IPrunableManager : IPackageManager
{
    /// <summary>The strongest prune action permitted for this provider.</summary>
    PrunePolicy MaxPolicy { get; }

    /// <summary>
    /// The explicitly/top-level installed ids — what the user asked for, NOT the
    /// full dependency closure. Pruning the closure would orphan declared
    /// packages, so this MUST be leaves-only (brew leaves, flatpak --app, …).
    /// </summary>
    Task<IReadOnlyList<string>> ListExplicitAsync(CancellationToken ct);

    /// <summary>Remove a single explicitly-installed id.</summary>
    Task UninstallAsync(string id, CancellationToken ct);

    /// <summary>
    /// Best-effort garbage collection of newly-orphaned dependencies after a
    /// prune (e.g. <c>brew autoremove</c>, <c>flatpak uninstall --unused</c>).
    /// No-op by default.
    /// </summary>
    Task CollectGarbageAsync(CancellationToken ct) => Task.CompletedTask;
}
