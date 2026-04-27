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

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var list = await ProcessRunner.RunAsync(DefaultBinary, ["--list-extensions"], ct);
        if (list.ExitCode != 0)
            throw new InvalidOperationException($"{DefaultBinary} --list-extensions failed: {list.StdErr.Trim()}");

        var failures = new List<string>();
        foreach (var line in list.StdOut.Split('\n'))
        {
            var id = line.Trim();
            if (id.Length == 0) continue;

            var result = await ProcessRunner.RunAsync(DefaultBinary,
                ["--install-extension", id, "--force"], ct);
            if (result.ExitCode != 0)
                failures.Add($"{id}: {result.StdErr.Trim()}");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"{DefaultBinary} --install-extension failed for {failures.Count} extension(s): {string.Join("; ", failures)}");
    }
}
