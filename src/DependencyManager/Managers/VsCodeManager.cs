using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class VsCodeManager : IPackageManager
{
    private const string DefaultBinary = "code";

    public ManagerKind Kind => ManagerKind.VsCode;

    public bool IsAvailable() => ProcessRunner.OnPath(DefaultBinary);

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var binary = pkg.Spec.Source ?? DefaultBinary;
        var result = await ProcessRunner.RunAsync(binary, ["--list-extensions"], ct);
        if (result.ExitCode != 0) return false;
        foreach (var line in result.StdOut.Split('\n'))
        {
            if (string.Equals(line.Trim(), pkg.Id, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var binary = pkg.Spec.Source ?? DefaultBinary;
        var result = await ProcessRunner.RunAsync(binary,
            ["--install-extension", pkg.Id, "--force"], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"{binary} --install-extension {pkg.Id} failed: {result.StdErr.Trim()}");
    }
}
