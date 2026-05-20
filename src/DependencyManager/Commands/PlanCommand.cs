using DependencyManager.Config;
using DependencyManager.Runner;
using DependencyManager.Util;

namespace DependencyManager.Commands;

public static class PlanCommand
{
    public static int Run(string configPath)
    {
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"config file not found: {configPath}");
            return 1;
        }

        var config = ConfigLoader.Load(configPath);
        var platform = PlatformInfo.Current();
        var plan = Planner.Plan(config, platform);

        Console.WriteLine($"platform: {platform.Os}/{platform.Architecture} ({platform.Version})");
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
            Console.WriteLine("note: this plan will invoke sudo to install (apt/snap/PPAs or system-scope packages present)");

        return 0;
    }
}
