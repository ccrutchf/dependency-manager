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
        var result = await ProcessRunner.RunAsync("mas", ["list"], ct);
        return result.ExitCode != 0 ? [] : ParseInstalledIds(result.StdOut);
    }

    /// <summary>
    /// Pure parser for <c>mas list</c> output: lines look like
    /// <c>"497799835  Xcode (15.4)"</c> — the id is the first whitespace-delimited
    /// token. Extracted so it is unit-testable without the App Store.
    /// </summary>
    public static IReadOnlyList<string> ParseInstalledIds(string masListOutput)
    {
        var ids = new List<string>();
        if (string.IsNullOrEmpty(masListOutput)) return ids;
        foreach (var line in masListOutput.Split('\n'))
        {
            var id = line.Trim().Split(' ', 2)[0];
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }

    public Task UninstallAsync(string id, CancellationToken ct) =>
        throw new NotSupportedException("mas cannot uninstall App Store apps; remove them manually.");
}
