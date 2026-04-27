using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class AptManager : IPackageManager
{
    private readonly IReadOnlyList<string> _ppas;
    private bool _bootstrapped;

    public AptManager() : this(Array.Empty<string>()) { }

    public AptManager(IReadOnlyList<string> ppas) => _ppas = ppas;

    public ManagerKind Kind => ManagerKind.Apt;

    public bool IsAvailable() => OperatingSystem.IsLinux() && File.Exists("/usr/bin/apt-get");

    public async Task BootstrapAsync(CancellationToken ct)
    {
        if (_bootstrapped) return;

        foreach (var ppa in _ppas)
        {
            var add = await Sudo.RunAsync("add-apt-repository", ["-y", ppa], ct);
            if (add.ExitCode != 0)
                throw new InvalidOperationException(
                    $"add-apt-repository {ppa} failed: {add.StdErr.Trim()} (install software-properties-common if missing)");
        }

        var result = await Sudo.RunAsync("apt-get", ["update"], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"apt-get update failed: {result.StdErr.Trim()}");
        _bootstrapped = true;
    }

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("dpkg", ["-s", pkg.Id], ct);
        return result.ExitCode == 0 && result.StdOut.Contains("Status: install ok installed");
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await Sudo.RunAsync("apt-get", ["install", "-y", pkg.Id], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"apt-get install {pkg.Id} failed: {result.StdErr.Trim()}");
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var refresh = await Sudo.RunAsync("apt-get", ["update"], ct);
        if (refresh.ExitCode != 0)
            throw new InvalidOperationException($"apt-get update failed: {refresh.StdErr.Trim()}");

        var upgrade = await Sudo.RunAsync("apt-get", ["upgrade", "-y"], ct);
        if (upgrade.ExitCode != 0)
            throw new InvalidOperationException($"apt-get upgrade failed: {upgrade.StdErr.Trim()}");
    }
}
