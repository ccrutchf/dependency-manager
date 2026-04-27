using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class SnapManager : IPackageManager
{
    public ManagerKind Kind => ManagerKind.Snap;

    public bool IsAvailable() => OperatingSystem.IsLinux() && ProcessRunner.OnPath("snap");

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("snap", ["list", pkg.Id], ct);
        return result.ExitCode == 0;
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var args = new List<string> { "install", pkg.Id };
        if (pkg.Spec.Classic) args.Add("--classic");

        var result = await Sudo.RunAsync("snap", args, ct);
        if (result.ExitCode != 0)
        {
            var stderr = result.StdErr.Trim();
            if (!pkg.Spec.Classic && stderr.Contains("--classic"))
                throw new InvalidOperationException(
                    $"snap install {pkg.Id} requires classic confinement — add `classic: true` to this package in your config");
            throw new InvalidOperationException($"snap install {pkg.Id} failed: {stderr}");
        }
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var result = await Sudo.RunAsync("snap", ["refresh"], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"snap refresh failed: {result.StdErr.Trim()}");
    }
}
