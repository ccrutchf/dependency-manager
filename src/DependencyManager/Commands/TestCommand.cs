using DependencyManager.Config;
using DependencyManager.Runner;
using DependencyManager.Util;

namespace DependencyManager.Commands;

public static class TestCommand
{
    public static async Task<int> RunAsync(string configPath, CancellationToken ct)
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
        var managers = InstallCommand.BuildManagers(plan);
        var runner = new Runner.Runner(managers);
        var packageResult = await runner.TestAsync(plan.Packages, ct);

        if (unsatisfied.Count > 0)
        {
            Console.WriteLine($"{unsatisfied.Count} missing prerequisite(s):");
            foreach (var r in unsatisfied)
                Console.WriteLine($"  {r.Name}  (required by block '{r.BlockName}')");
            return 1;
        }

        return packageResult;
    }
}
