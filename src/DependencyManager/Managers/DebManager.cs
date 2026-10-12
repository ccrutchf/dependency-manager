using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class DebManager : IPackageManager
{
    private readonly DownloadCache _cache;

    public DebManager(DownloadCache? cache = null)
    {
        _cache = cache ?? new DownloadCache(DownloadCache.DefaultPath(DownloadCache.DebsFile));
    }

    public ManagerKind Kind => ManagerKind.Deb;

    public bool IsAvailable() =>
        OperatingSystem.IsLinux() && File.Exists("/usr/bin/dpkg") && File.Exists("/usr/bin/apt-get");

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("dpkg", ["-s", pkg.Id], ct);
        if (result.ExitCode != 0 || !result.StdOut.Contains("Status: install ok installed"))
            return false;

        var url = pkg.Spec.Url;
        if (string.IsNullOrWhiteSpace(url)) return true;

        var entries = await _cache.LoadAsync(ct);
        return DownloadCache.IsCurrent(entries, url, pkg.Spec.Sha256, packageId: null);
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var url = pkg.Spec.Url
            ?? throw new InvalidOperationException($"deb package {pkg.Id} is missing 'url'");

        var tempPath = Downloads.TempPath(".deb");
        try
        {
            var sha256 = await Downloads.DownloadVerifiedAsync(url, tempPath, pkg.Spec.Sha256, ct);
            await DpkgInstallAsync(pkg.Id, tempPath, ct);
            var packageId = await ReadPackageFieldAsync(tempPath, ct);
            await _cache.RecordAsync(url, sha256, packageId, ct);
        }
        finally
        {
            Downloads.TryDelete(tempPath);
        }
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var entries = await _cache.LoadAsync(ct);
        if (entries.Count == 0)
        {
            Console.WriteLine("  [skip]    deb       no cached .deb downloads");
            return;
        }

        var updated = false;
        var failures = new List<string>();

        foreach (var (url, entry) in entries.ToList())
        {
            var tempPath = Downloads.TempPath(".deb");
            try
            {
                var sha256 = await Downloads.DownloadVerifiedAsync(url, tempPath, expectedSha256: null, ct);

                if (string.Equals(sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  [ok]      deb       unchanged: {url}");
                    continue;
                }

                Console.WriteLine($"  [update]  deb       sha256 changed: {url}");
                await DpkgInstallAsync(url, tempPath, ct);
                var packageId = await ReadPackageFieldAsync(tempPath, ct) ?? entry.PackageId;
                entries[url] = new DownloadCacheEntry(sha256, DateTimeOffset.UtcNow, packageId);
                updated = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL]    deb       {url}: {ex.Message}");
                failures.Add($"{url}: {ex.Message}");
            }
            finally
            {
                Downloads.TryDelete(tempPath);
            }
        }

        if (updated)
            await _cache.SaveAsync(entries, ct);

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"{failures.Count} deb update(s) failed: {string.Join("; ", failures)}");
    }

    private static async Task<string?> ReadPackageFieldAsync(string debPath, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("dpkg-deb", ["-f", debPath, "Package"], ct);
        if (result.ExitCode != 0) return null;
        var name = result.StdOut.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static async Task DpkgInstallAsync(string label, string debPath, CancellationToken ct)
    {
        var install = await Sudo.RunAsync("dpkg", ["-i", debPath], ct);
        if (install.ExitCode == 0) return;

        var fix = await Sudo.RunAsync("apt-get", ["install", "-y", "-f"], ct);
        if (fix.ExitCode != 0)
            throw new InvalidOperationException(
                $"dpkg -i {label} failed: {install.StdErr.Trim()}; apt-get install -f also failed: {fix.StdErr.Trim()}");
    }
}
