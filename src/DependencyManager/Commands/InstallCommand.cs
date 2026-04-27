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

        if (RootCheck.PlanRequiresSudo(plan))
        {
            Console.WriteLine("this plan includes privileged operations — priming sudo...");
            if (!await Sudo.PrimeAsync(ct))
            {
                Console.Error.WriteLine("sudo authentication failed; aborting.");
                return 1;
            }
        }
        Console.WriteLine();

        var managers = BuildManagers(plan);
        var runner = new Runner.Runner(managers);
        return await runner.InstallAsync(plan.Packages, failFast, ct);
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
    ];
}
