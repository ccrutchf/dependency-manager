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

        var managers = InstallCommand.BuildManagers(plan);
        var runner = new Runner.Runner(managers);
        return await runner.TestAsync(plan.Packages, ct);
    }
}
