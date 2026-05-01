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

    private IPackageManager? ManagerFor(ResolvedPackage pkg)
    {
        foreach (var m in _managers)
        {
            if (m.Kind == pkg.Manager && m.IsAvailable()) return m;
        }
        return null;
    }
}
