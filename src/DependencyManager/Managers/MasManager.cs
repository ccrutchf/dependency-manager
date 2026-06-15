using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

/// <summary>
/// Mac App Store provider via the `mas` CLI (itself installable as a brew
/// formula). Keys are the numeric App Store id. `mas` can install and upgrade
/// but CANNOT uninstall, so prune is ReportOnly — drift is surfaced, never
/// auto-removed.
/// </summary>
public sealed class MasManager : IPrunableManager
{
    public ManagerKind Kind => ManagerKind.Mas;

    public bool IsAvailable() => OperatingSystem.IsMacOS() && ProcessRunner.OnPath("mas");

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var installed = await ListExplicitAsync(ct);
        return installed.Contains(pkg.Id);
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("mas", ["install", pkg.Id], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"mas install {pkg.Id} failed: {result.StdErr.Trim()}");
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("mas", ["upgrade"], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"mas upgrade failed: {result.StdErr.Trim()}");
    }

    // --- prune (report-only) -------------------------------------------------

    public PrunePolicy MaxPolicy => PrunePolicy.ReportOnly;

    public async Task<IReadOnlyList<string>> ListExplicitAsync(CancellationToken ct)
    {
        // `mas list` lines look like: "497799835  Xcode (15.4)" — id is token 0.
        var ids = new List<string>();
        var result = await ProcessRunner.RunAsync("mas", ["list"], ct);
        if (result.ExitCode != 0) return ids;
        foreach (var line in result.StdOut.Split('\n'))
        {
            var id = line.Trim().Split(' ', 2)[0];
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }

    public Task UninstallAsync(string id, CancellationToken ct) =>
        throw new NotSupportedException("mas cannot uninstall App Store apps; remove them manually.");
}
