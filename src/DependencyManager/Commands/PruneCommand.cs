using DependencyManager.Config;
using DependencyManager.Runner;
using DependencyManager.Util;

namespace DependencyManager.Commands;

public static class PruneCommand
{
    public static async Task<int> RunAsync(string configPath, bool apply, CancellationToken ct)
    {
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"config file not found: {configPath}");
            return 1;
        }

        if (RootCheck.IsRoot())
        {
            Console.Error.WriteLine(
                "depend prune must not be run as root. Run it as your normal user.");
            return 1;
        }

        var config = ConfigLoader.Load(configPath);
        var platform = PlatformInfo.Current();
        var plan = Planner.Plan(config, platform);

        Console.WriteLine($"platform: {platform.Os}/{platform.Architecture} ({platform.Version})");
        Console.WriteLine(apply
            ? "prune: removing packages not in the declared plan..."
            : "prune (dry run): packages installed but not declared...");
        Console.WriteLine();

        var managers = InstallCommand.BuildManagers(plan);
        var runner = new Runner.Runner(managers);
        return await runner.PruneAsync(plan.Packages, apply, ct);
    }
}
