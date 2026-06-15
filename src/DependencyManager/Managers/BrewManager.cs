using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

/// <summary>
/// Homebrew provider. One class serves both formulae (Kind=Brew) and casks
/// (Kind=Cask) via the constructor flag, mirroring FlatpakManager(userScope).
///
/// brew REFUSES to run under sudo, so every call goes through ProcessRunner,
/// never Sudo. Casks are macOS-only; formulae also work under Linuxbrew.
/// </summary>
public sealed class BrewManager : IPrunableManager
{
    private readonly bool _cask;

    public BrewManager() : this(cask: false) { }

    public BrewManager(bool cask) => _cask = cask;

    public ManagerKind Kind => _cask ? ManagerKind.Cask : ManagerKind.Brew;

    public bool IsAvailable() =>
        ProcessRunner.OnPath("brew") && (!_cask || OperatingSystem.IsMacOS());

    public async Task BootstrapAsync(CancellationToken ct)
    {
        // Refresh formula/cask metadata once per run so a freshly-tapped machine
        // resolves names. Best-effort: a failed `brew update` shouldn't abort.
        await ProcessRunner.RunAsync("brew", ["update"], ct);
    }

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        // "Installed at all" — the FULL list (incl. formulae pulled in as deps),
        // so a declared formula already present as a dependency isn't reinstalled.
        var all = await ListAsync(explicitOnly: false, ct);
        return all.Contains(pkg.Spec.Name ?? pkg.Id);
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        // A tap given as `source:` must be tapped before an unqualified name resolves.
        if (BrewPlan.IsTap(pkg.Spec.Source))
            await ProcessRunner.RunAsync("brew", ["tap", pkg.Spec.Source!], ct);

        var name = pkg.Spec.Name ?? pkg.Id;
        var result = await ProcessRunner.RunAsync("brew", BrewPlan.InstallArgs(_cask, name), ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"brew install {name} failed: {result.StdErr.Trim()}");
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        // `brew upgrade` (no args) upgrades outdated formulae AND casks, so only
        // the formula instance runs it — the cask instance would double-work.
        if (_cask) return;
        var result = await ProcessRunner.RunAsync("brew", ["upgrade"], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"brew upgrade failed: {result.StdErr.Trim()}");
    }

    // --- prune ---------------------------------------------------------------

    public PrunePolicy MaxPolicy => PrunePolicy.Zap;

    public async Task<IReadOnlyList<string>> ListExplicitAsync(CancellationToken ct) =>
        (await ListAsync(explicitOnly: true, ct)).ToList();

    public async Task UninstallAsync(string id, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("brew", BrewPlan.UninstallArgs(_cask, id), ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"brew uninstall {id} failed: {result.StdErr.Trim()}");
    }

    public async Task CollectGarbageAsync(CancellationToken ct)
    {
        // Casks have no dependency concept; only formulae leave orphans behind.
        if (_cask) return;
        await ProcessRunner.RunAsync("brew", ["autoremove"], ct);
    }

    /// <param name="explicitOnly">
    /// true  -> formulae the user requested (`brew leaves --installed-on-request`),
    ///          the safe prune set; casks have no deps so it's the full cask list.
    /// false -> everything installed (`brew list --formula`/`--cask`).
    /// </param>
    private async Task<HashSet<string>> ListAsync(bool explicitOnly, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("brew", BrewPlan.ListArgs(_cask, explicitOnly), ct);
        if (result.ExitCode != 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(BrewPlan.ParseNames(result.StdOut), StringComparer.OrdinalIgnoreCase);
    }
}
