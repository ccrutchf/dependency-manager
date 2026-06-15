using DependencyManager.Config;
using DependencyManager.Managers;
using DependencyManager.Util;

namespace DependencyManager.Commands;

public static class UpdateCommand
{
    public static async Task<int> RunAsync(bool restart, CancellationToken ct)
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
        var macosAvailable = OperatingSystem.IsMacOS() && PathLookup.Exists("softwareupdate");

        if (available.Count == 0 && !nixosAvailable && !macosAvailable)
        {
            Console.WriteLine("no package managers available on this machine");
            return 0;
        }

        var providerLabels = new List<string>();
        if (nixosAvailable) providerLabels.Add("nixos");
        if (macosAvailable) providerLabels.Add("macos");
        providerLabels.AddRange(available.Select(m => m.Kind.ToString().ToLowerInvariant()));
        Console.WriteLine($"available providers: {string.Join(", ", providerLabels)}");

        if (nixosAvailable || macosAvailable || available.Any(RequiresSudo))
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
                var flakeRef = Environment.GetEnvironmentVariable("DEPEND_NIXOS_FLAKE");
                foreach (var step in NixosUpdate.Plan(flakeRef))
                {
                    var label = $"{step.Command} {string.Join(' ', step.Args)}";
                    var exitCode = step.Sudo
                        ? await Sudo.RunStreamingAsync(step.Command, step.Args, ct)
                        : await ProcessRunner.RunStreamingAsync(step.Command, step.Args, ct);
                    if (exitCode != 0)
                        throw new InvalidOperationException($"{label} exited with code {exitCode}");
                }
                succeeded++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL]    nixos  {ex.Message}");
                failures.Add(("nixos", ex.Message));
            }
        }

        if (macosAvailable)
        {
            Console.WriteLine("  [update]  macos");
            try
            {
                foreach (var step in MacosUpdate.Plan(includeRestart: restart))
                {
                    var label = $"{step.Command} {string.Join(' ', step.Args)}";
                    var exitCode = step.Sudo
                        ? await Sudo.RunStreamingAsync(step.Command, step.Args, ct)
                        : await ProcessRunner.RunStreamingAsync(step.Command, step.Args, ct);
                    if (exitCode != 0)
                        throw new InvalidOperationException($"{label} exited with code {exitCode}");
                }

                // softwareupdate exits 0 even when it silently skipped updates
                // (insufficient space, restart-required without --restart, license-gated).
                // Re-scan and fail loudly if anything is still pending.
                Console.WriteLine("  [verify]  softwareupdate --list");
                var listResult = await ProcessRunner.RunAsync("softwareupdate", ["--list"], ct);
                var pending = MacosUpdate.ParsePendingUpdates(listResult.StdOut + "\n" + listResult.StdErr);
                if (pending.Count > 0)
                    throw new InvalidOperationException(
                        $"{pending.Count} update(s) still pending after install: {string.Join("; ", pending)}");

                succeeded++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL]    macos  {ex.Message}");
                failures.Add(("macos", ex.Message));
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
        }

        var rebootSignals = RebootCheck.Check();
        if (rebootSignals.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("a reboot is required:");
            foreach (var s in rebootSignals)
                Console.WriteLine($"  {s.Source,-8}: {s.Reason}");

            if (failures.Count > 0)
            {
                Console.WriteLine("not rebooting because some updates failed; resolve those first.");
            }
            else if (restart)
            {
                Console.WriteLine("rebooting now (--restart was set)...");
                var rebootExit = await Sudo.RunStreamingAsync("reboot", Array.Empty<string>(), ct);
                if (rebootExit != 0)
                {
                    Console.Error.WriteLine($"reboot exited with code {rebootExit}");
                    return 1;
                }
            }
            else
            {
                Console.WriteLine("re-run with --restart to apply, or reboot manually.");
            }
        }

        return failures.Count > 0 ? 1 : 0;
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
        new BrowserExtensionManager(ManagerKind.Firefox),
        new BrowserExtensionManager(ManagerKind.Zen),
        new BrowserExtensionManager(ManagerKind.Chrome),
        new BrowserExtensionManager(ManagerKind.Chromium),
        new BrowserExtensionManager(ManagerKind.Brave),
        new BrewManager(cask: false),
        new BrewManager(cask: true),
        new MasManager(),
    ];

    private static bool RequiresSudo(IPackageManager manager) =>
        manager.Kind is ManagerKind.Apt or ManagerKind.Snap or ManagerKind.Deb;
}
