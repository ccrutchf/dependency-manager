using System.Security.Cryptography;
using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class DebManager : IPackageManager
{
    private static readonly HttpClient Http = new();

    private readonly DebCache _cache;

    public DebManager(DebCache? cache = null)
    {
        _cache = cache ?? new DebCache();
    }

    public ManagerKind Kind => ManagerKind.Deb;

    public bool IsAvailable() =>
        OperatingSystem.IsLinux() && File.Exists("/usr/bin/dpkg") && File.Exists("/usr/bin/apt-get");

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("dpkg", ["-s", pkg.Id], ct);
        return result.ExitCode == 0 && result.StdOut.Contains("Status: install ok installed");
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var url = pkg.Spec.Url
            ?? throw new InvalidOperationException($"deb package {pkg.Id} is missing 'url'");

        var tempPath = Path.Combine(Path.GetTempPath(), $"depend-{Guid.NewGuid():N}.deb");
        try
        {
            await DownloadAsync(url, tempPath, ct);
            var sha256 = await ComputeSha256Async(tempPath, ct);

            if (!string.IsNullOrWhiteSpace(pkg.Spec.Sha256))
                VerifySha256(sha256, pkg.Spec.Sha256);

            await DpkgInstallAsync(pkg.Id, tempPath, ct);
            await _cache.RecordAsync(url, sha256, ct);
        }
        finally
        {
            TryDelete(tempPath);
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
            var tempPath = Path.Combine(Path.GetTempPath(), $"depend-{Guid.NewGuid():N}.deb");
            try
            {
                await DownloadAsync(url, tempPath, ct);
                var sha256 = await ComputeSha256Async(tempPath, ct);

                if (string.Equals(sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  [ok]      deb       unchanged: {url}");
                    continue;
                }

                Console.WriteLine($"  [update]  deb       sha256 changed: {url}");
                await DpkgInstallAsync(url, tempPath, ct);
                entries[url] = new DebCacheEntry(sha256, DateTimeOffset.UtcNow);
                updated = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL]    deb       {url}: {ex.Message}");
                failures.Add($"{url}: {ex.Message}");
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        if (updated)
            await _cache.SaveAsync(entries, ct);

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"{failures.Count} deb update(s) failed: {string.Join("; ", failures)}");
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

    private static async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"download {url} failed: HTTP {(int)response.StatusCode}");

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destination);
        await src.CopyToAsync(dst, ct);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void VerifySha256(string actual, string expected)
    {
        var normalized = expected.Replace("-", "");
        if (!string.Equals(actual, normalized, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"sha256 mismatch: expected {expected}, got {actual}");
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); } catch { }
    }
}
