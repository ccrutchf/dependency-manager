using DependencyManager.Config;
using DependencyManager.Managers;
using DependencyManager.Util;

namespace DependencyManager.Commands;

public static class UpdateCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        if (RootCheck.IsRoot())
        {
            Console.Error.WriteLine(
                "depend update must not be run as root. Run it as your normal user; it will invoke sudo for privileged operations.");
            return 1;
        }

        var managers = BuildManagers();
        var available = managers.Where(m => m.IsAvailable()).ToList();
        var nixosAvailable = OperatingSystem.IsLinux() && PathLookup.Exists("nixos-rebuild");

        if (available.Count == 0 && !nixosAvailable)
        {
            Console.WriteLine("no package managers available on this machine");
            return 0;
        }

        var providerLabels = new List<string>();
        if (nixosAvailable) providerLabels.Add("nixos");
        providerLabels.AddRange(available.Select(m => m.Kind.ToString().ToLowerInvariant()));
        Console.WriteLine($"available providers: {string.Join(", ", providerLabels)}");

        if (nixosAvailable || available.Any(RequiresSudo))
        {
            Console.WriteLine("this update includes privileged operations — priming sudo...");
            if (!await Sudo.PrimeAsync(ct))
            {
                Console.Error.WriteLine("sudo authentication failed; aborting.");
                return 1;
            }
        }
        Console.WriteLine();

        var succeeded = 0;
        var failures = new List<(string Kind, string Reason)>();

        if (nixosAvailable)
        {
            Console.WriteLine("  [update]  nixos");
            try
            {
                var result = await Sudo.RunAsync("nixos-rebuild", ["switch", "--upgrade"], ct);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"nixos-rebuild switch --upgrade failed: {(result.StdErr.Length > 0 ? result.StdErr.Trim() : result.StdOut.Trim())}");
                succeeded++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL]    nixos  {ex.Message}");
                failures.Add(("nixos", ex.Message));
            }
        }

        foreach (var manager in available)
        {
            var label = manager.Kind.ToString().ToLowerInvariant();
            Console.WriteLine($"  [update]  {label}");
            try
            {
                await manager.UpdateAllAsync(ct);
                succeeded++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL]    {label}  {ex.Message}");
                failures.Add((label, ex.Message));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"updated: {succeeded}  failed: {failures.Count}");
        if (failures.Count > 0)
        {
            Console.WriteLine("failures:");
            foreach (var (kind, reason) in failures)
                Console.WriteLine($"  {kind,-8}: {reason}");
            return 1;
        }
        return 0;
    }

    internal static IPackageManager[] BuildManagers() =>
    [
        new AptManager(),
        new SnapManager(),
        new FlatpakManager(userScope: true),
        new DebManager(),
        new PipManager(userScope: true),
        new PipxManager(userScope: true),
        new ScriptManager(),
        new VsCodeManager(),
        new CargoManager(),
        new NvmManager(),
    ];

    private static bool RequiresSudo(IPackageManager manager) =>
        manager.Kind is ManagerKind.Apt or ManagerKind.Snap or ManagerKind.Deb;
}
