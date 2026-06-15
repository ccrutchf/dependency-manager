using DependencyManager.Config;
using DependencyManager.Managers;

namespace DependencyManager.Runner;

public sealed class Runner
{
    private readonly IReadOnlyList<IPackageManager> _managers;

    public Runner(IEnumerable<IPackageManager> managers) => _managers = managers.ToList();

    public async Task<int> InstallAsync(
        IReadOnlyList<ResolvedPackage> plan,
        bool failFast,
        CancellationToken ct)
    {
        var installed = 0;
        var skipped = 0;
        var failures = new List<(ResolvedPackage Pkg, string Reason)>();
        var bootstrapped = new HashSet<ManagerKind>();

        foreach (var pkg in plan)
        {
            var manager = ManagerFor(pkg);
            if (manager is null)
            {
                failures.Add((pkg, $"no manager available for {pkg.Manager}"));
                if (failFast) break;
                continue;
            }

            try
            {
                if (bootstrapped.Add(manager.Kind))
                    await manager.BootstrapAsync(ct);

                if (await manager.IsInstalledAsync(pkg, ct))
                {
                    Console.WriteLine($"  [skip]    {pkg.Manager,-8} {pkg.Id}  (already installed)");
                    skipped++;
                    continue;
                }

                Console.WriteLine($"  [install] {pkg.Manager,-8} {pkg.Id}");
                await manager.InstallAsync(pkg, ct);
                installed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL]    {pkg.Manager,-8} {pkg.Id}  {ex.Message}");
                failures.Add((pkg, ex.Message));
                if (failFast) break;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"installed: {installed}  skipped: {skipped}  failed: {failures.Count}");
        if (failures.Count > 0)
        {
            Console.WriteLine("failures:");
            foreach (var (pkg, reason) in failures)
                Console.WriteLine($"  {pkg.Manager,-8} {pkg.Id}: {reason}");
            return 1;
        }
        return 0;
    }

    public async Task<int> TestAsync(IReadOnlyList<ResolvedPackage> plan, CancellationToken ct)
    {
        var missing = await FindMissingAsync(plan, ct);

        if (missing.Count == 0)
        {
            Console.WriteLine($"all {plan.Count} package(s) installed");
            return 0;
        }

        Console.WriteLine($"{missing.Count} of {plan.Count} package(s) missing:");
        foreach (var pkg in missing)
            Console.WriteLine($"  {pkg.Manager,-8} {pkg.Id}");
        return 1;
    }

    public async Task<IReadOnlyList<ResolvedPackage>> FindMissingAsync(
        IReadOnlyList<ResolvedPackage> plan,
        CancellationToken ct)
    {
        var missing = new List<ResolvedPackage>();
        foreach (var pkg in plan)
        {
            var manager = ManagerFor(pkg);
            if (manager is null || !await manager.IsInstalledAsync(pkg, ct))
                missing.Add(pkg);
        }
        return missing;
    }

    /// <summary>
    /// Converge each prunable provider's installed set down to the declared set:
    /// anything explicitly installed but not in <paramref name="declared"/> is
    /// removed (apply=true) or just listed (apply=false, the default drift report).
    /// </summary>
    public async Task<int> PruneAsync(
        IReadOnlyList<ResolvedPackage> declared,
        bool apply,
        CancellationToken ct)
    {
        // Declared ids grouped by manager kind (case-insensitive).
        var declaredByKind = declared
            .GroupBy(p => p.Manager)
            .ToDictionary(
                g => g.Key,
                g => new HashSet<string>(g.Select(p => p.Id), StringComparer.OrdinalIgnoreCase));

        var planned = 0;
        var removed = 0;
        var reportOnly = 0;
        var failures = new List<(string Target, string Reason)>();

        foreach (var manager in _managers)
        {
            if (manager is not IPrunableManager prunable) continue;
            if (!manager.IsAvailable()) continue;

            // SAFETY RAIL: never prune a provider that declares nothing on this
            // platform. An empty or partial config must not be read as "remove
            // everything this provider has installed".
            if (!declaredByKind.TryGetValue(manager.Kind, out var keep) || keep.Count == 0)
                continue;

            IReadOnlyList<string> installed;
            try
            {
                installed = await prunable.ListExplicitAsync(ct);
            }
            catch (Exception ex)
            {
                failures.Add((manager.Kind.ToString(), $"list failed: {ex.Message}"));
                continue;
            }

            var extra = installed.Where(id => !keep.Contains(id)).ToList();
            if (extra.Count == 0) continue;

            var canRemove = apply && prunable.MaxPolicy == PrunePolicy.Zap;
            var removedHere = 0;
            foreach (var id in extra)
            {
                planned++;
                if (!canRemove)
                {
                    var note = prunable.MaxPolicy == PrunePolicy.ReportOnly
                        ? "  (manual — provider cannot auto-remove)"
                        : "  (dry run; pass --prune to apply)";
                    Console.WriteLine($"  [prune?]  {manager.Kind,-8} {id}{note}");
                    if (prunable.MaxPolicy == PrunePolicy.ReportOnly) reportOnly++;
                    continue;
                }

                Console.WriteLine($"  [remove]  {manager.Kind,-8} {id}");
                try
                {
                    await prunable.UninstallAsync(id, ct);
                    removed++;
                    removedHere++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL]    {manager.Kind,-8} {id}  {ex.Message}");
                    failures.Add(($"{manager.Kind}/{id}", ex.Message));
                }
            }

            if (removedHere > 0)
            {
                try { await prunable.CollectGarbageAsync(ct); }
                catch (Exception ex) { failures.Add(($"{manager.Kind} gc", ex.Message)); }
            }
        }

        Console.WriteLine();
        if (planned == 0)
        {
            Console.WriteLine("no undeclared packages to prune.");
            return 0;
        }

        if (!apply)
        {
            Console.WriteLine($"prune (dry run): {planned} undeclared package(s); re-run with --prune to remove.");
            return 0;
        }

        Console.WriteLine($"pruned: {removed}  report-only: {reportOnly}  failed: {failures.Count}");
        if (failures.Count > 0)
        {
            Console.WriteLine("failures:");
            foreach (var (target, reason) in failures)
                Console.WriteLine($"  {target}: {reason}");
            return 1;
        }
        return 0;
    }

    private IPackageManager? ManagerFor(ResolvedPackage pkg)
    {
        foreach (var m in _managers)
        {
            if (m.Kind == pkg.Manager && m.IsAvailable()) return m;
        }
        return null;
    }
}
