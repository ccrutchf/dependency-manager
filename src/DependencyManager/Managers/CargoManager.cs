using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class CargoManager : IPackageManager
{
    public ManagerKind Kind => ManagerKind.Cargo;

    public bool IsAvailable() => ProcessRunner.OnPath("cargo");

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var installed = await ListInstalledAsync(ct);
        return installed.Contains(pkg.Id);
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var args = new List<string> { "install" };
        if (pkg.Spec.Url is { } url)
        {
            args.Add("--git");
            args.Add(url);
        }
        args.Add(pkg.Spec.Name ?? pkg.Id);

        var result = await ProcessRunner.RunAsync("cargo", args, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"cargo install {pkg.Id} failed: {result.StdErr.Trim()}");
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var installed = await ListInstalledAsync(ct);

        var failures = new List<string>();
        foreach (var crate in installed)
        {
            var result = await ProcessRunner.RunAsync("cargo", ["install", crate], ct);
            if (result.ExitCode != 0)
                failures.Add($"{crate}: {result.StdErr.Trim()}");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"cargo install failed for {failures.Count} crate(s): {string.Join("; ", failures)}");
    }

    private static async Task<HashSet<string>> ListInstalledAsync(CancellationToken ct)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = await ProcessRunner.RunAsync("cargo", ["install", "--list"], ct);
        if (result.ExitCode != 0) return installed;

        foreach (var line in result.StdOut.Split('\n'))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) continue;
            var spaceIdx = line.IndexOf(' ');
            var name = spaceIdx < 0 ? line : line[..spaceIdx];
            if (name.Length > 0) installed.Add(name);
        }
        return installed;
    }
}
