using System.Security.Cryptography;
using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class DebManager : IPackageManager
{
    private static readonly HttpClient Http = new();

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

            if (!string.IsNullOrWhiteSpace(pkg.Spec.Sha256))
                await VerifySha256Async(tempPath, pkg.Spec.Sha256, ct);

            var install = await Sudo.RunAsync("dpkg", ["-i", tempPath], ct);
            if (install.ExitCode == 0) return;

            var fix = await Sudo.RunAsync("apt-get", ["install", "-y", "-f"], ct);
            if (fix.ExitCode != 0)
                throw new InvalidOperationException(
                    $"dpkg -i {pkg.Id} failed: {install.StdErr.Trim()}; apt-get install -f also failed: {fix.StdErr.Trim()}");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
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

    private static async Task VerifySha256Async(string path, string expected, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        var actual = Convert.ToHexString(hash);
        if (!string.Equals(actual, expected.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"sha256 mismatch for {path}: expected {expected}, got {actual.ToLowerInvariant()}");
    }
}
