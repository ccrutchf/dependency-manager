using DependencyManager.Config;
using DependencyManager.Managers;
using DependencyManager.Runner;
using DependencyManager.Util;

namespace DependencyManager.Commands;

public static class InstallCommand
{
    public static async Task<int> RunAsync(string configPath, bool failFast, CancellationToken ct)
    {
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"config file not found: {configPath}");
            return 1;
        }

        var config = ConfigLoader.Load(configPath);
        var platform = PlatformInfo.Current();
        var plan = Planner.Plan(config, platform);

        var unsatisfied = plan.Requirements.Where(r => !r.Satisfied).ToList();
        if (unsatisfied.Count > 0)
        {
            Console.Error.WriteLine($"missing {unsatisfied.Count} required prerequisite(s):");
            foreach (var r in unsatisfied)
                Console.Error.WriteLine($"  {r.Name}  (required by block '{r.BlockName}')");
            return 1;
        }

        if (plan.Packages.Count == 0)
        {
            Console.WriteLine("no packages match this platform");
            return 0;
        }

        if (RootCheck.IsRoot())
        {
            Console.Error.WriteLine(
                "depend install must not be run as root. Run it as your normal user; it will invoke sudo for privileged operations.");
            return 1;
        }

        Console.WriteLine($"platform: {platform.Os}/{platform.Architecture} ({platform.Version})");
        Console.WriteLine($"plan: {plan.Packages.Count} package(s)");
        if (plan.AptPpas.Count > 0)
            Console.WriteLine($"ppas:  {string.Join(", ", plan.AptPpas)}");
        if (plan.AptSources.Count > 0)
            Console.WriteLine($"apt sources: {string.Join(", ", plan.AptSources.Select(s => s.Name))}");

        var managers = BuildManagers(plan);
        var runner = new Runner.Runner(managers);
        var missing = await runner.FindMissingAsync(plan.Packages, ct);

        if (missing.Count == 0)
        {
            Console.WriteLine("all packages already installed; nothing to do.");
            return 0;
        }

        Console.WriteLine($"missing: {missing.Count} package(s)");

        var effectivePlan = EffectivePlan(plan, missing);
        if (RootCheck.PlanRequiresSudo(effectivePlan))
        {
            Console.WriteLine("this plan includes privileged operations — priming sudo...");
            if (!await Sudo.PrimeAsync(ct))
            {
                Console.Error.WriteLine("sudo authentication failed; aborting.");
                return 1;
            }
        }
        Console.WriteLine();

        return await runner.InstallAsync(missing, failFast, ct);
    }

    private static ResolvedPlan EffectivePlan(ResolvedPlan plan, IReadOnlyList<ResolvedPackage> missing)
    {
        var hasMissingApt = missing.Any(p => p.Manager == ManagerKind.Apt);
        return new ResolvedPlan(
            missing,
            hasMissingApt ? plan.AptPpas : Array.Empty<string>(),
            hasMissingApt ? plan.AptSources : Array.Empty<ResolvedAptSource>(),
            plan.Requirements);
    }

    internal static IPackageManager[] BuildManagers(ResolvedPlan plan) =>
    [
        new AptManager(plan.AptPpas, plan.AptSources),
        new SnapManager(),
        new FlatpakManager(userScope: true),
        new DebManager(),
        new PipManager(userScope: true),
        new PipxManager(userScope: true),
        new ScriptManager(),
        new VsCodeManager(),
        new CargoManager(),
        new NvmManager(),
        new BrowserExtensionManager(ManagerKind.Firefox),
        new BrowserExtensionManager(ManagerKind.Zen),
        new BrowserExtensionManager(ManagerKind.Chrome),
        new BrowserExtensionManager(ManagerKind.Chromium),
        new BrowserExtensionManager(ManagerKind.Brave),
    ];
}
