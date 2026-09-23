using DependencyManager.Config;
using DependencyManager.Runner;
using DependencyManager.Util;

namespace DependencyManager.Commands;

public static class PlanCommand
{
    /// <summary>
    /// Prints the resolved plan and, when <paramref name="prune"/> is set, also
    /// previews the removals that `install --prune` would make — a dry-run prune,
    /// so `plan --prune` is a complete preview of `install --prune`.
    /// </summary>
    public static async Task<int> RunAsync(string configPath, ActiveTags tags, bool prune, CancellationToken ct)
    {
        var rc = PrintPlan(configPath, tags, out var plan);
        if (rc != 0 || !prune || plan is null) return rc;

        Console.WriteLine();
        Console.WriteLine("prune preview (installed but not declared; install --prune would remove these):");
        var runner = new Runner.Runner(InstallCommand.BuildManagers(plan));
        return await runner.PruneAsync(plan.Packages, apply: false, ct);
    }

    private static int PrintPlan(string configPath, ActiveTags tags, out ResolvedPlan? resolved)
    {
        resolved = null;
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"config file not found: {configPath}");
            return 1;
        }

        var config = ConfigLoader.Load(configPath);
        var platform = PlatformInfo.Current();
        TagChecks.Enforce(config, tags, destructive: false);
        var plan = Planner.Plan(config, platform, tags);

        Console.WriteLine($"platform: {platform.Os}/{platform.Architecture} ({platform.Version})");
        Console.WriteLine($"tags: {tags.Describe()}");
        var tagSkipped = Planner.TagSkippedBlocks(config, platform, tags);
        if (tagSkipped.Count > 0)
        {
            Console.WriteLine($"skipped by tags ({tagSkipped.Count}):");
            foreach (var s in tagSkipped)
                Console.WriteLine($"  {s.BlockName}  ({s.Reason})");
        }
        if (plan.Requirements.Count > 0)
        {
            Console.WriteLine($"requires ({plan.Requirements.Count}):");
            foreach (var r in plan.Requirements)
            {
                var mark = r.Satisfied ? "ok     " : "MISSING";
                Console.WriteLine($"  [{mark}] {r.Name}  (from {r.BlockName})");
            }
        }
        if (plan.AptPpas.Count > 0)
        {
            Console.WriteLine($"ppas ({plan.AptPpas.Count}):");
            foreach (var ppa in plan.AptPpas)
                Console.WriteLine($"  {ppa}");
        }
        if (plan.AptSources.Count > 0)
        {
            Console.WriteLine($"apt sources ({plan.AptSources.Count}):");
            foreach (var s in plan.AptSources)
                Console.WriteLine($"  {s.Name} -> {s.Source.Uri ?? "?"}  (from {s.BlockName})");
        }
        Console.WriteLine($"resolved {plan.Packages.Count} package(s):");
        foreach (var pkg in plan.Packages)
        {
            var deps = pkg.Spec.Dependencies is { Count: > 0 }
                ? $"  deps=[{string.Join(", ", pkg.Spec.Dependencies)}]"
                : string.Empty;
            Console.WriteLine($"  {pkg.Manager,-8} {pkg.Id}  (from {pkg.BlockName}){deps}");
        }

        if (RootCheck.PlanRequiresSudo(plan))
            Console.WriteLine("note: this plan will invoke sudo to install (apt/snap/PPAs, browser extension policies, or system-scope packages present)");

        resolved = plan;
        return 0;
    }
}
