using System.Security.Cryptography;

namespace DependencyManager.Util;

/// <summary>
/// Download-to-temp-file and sha256 helpers shared by the url-installing
/// providers (deb, flatpak bundles, vscode .vsix files).
/// </summary>
public static class Downloads
{
    private static readonly HttpClient Http = new();

    /// <summary>A fresh temp path ending in <paramref name="extension"/> (e.g. ".deb").</summary>
    public static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"depend-{Guid.NewGuid():N}{extension}");

    public static async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"download {url} failed: HTTP {(int)response.StatusCode}");

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destination);
        await src.CopyToAsync(dst, ct);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string NormalizeSha256(string sha256) => sha256.Replace("-", "");

    public static void VerifySha256(string actual, string expected)
    {
        if (!string.Equals(actual, NormalizeSha256(expected), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"sha256 mismatch: expected {expected}, got {actual}");
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destination"/>, verifies it
    /// against <paramref name="expectedSha256"/> when one is given, and returns its sha256.
    /// </summary>
    public static async Task<string> DownloadVerifiedAsync(
        string url, string destination, string? expectedSha256, CancellationToken ct)
    {
        await DownloadAsync(url, destination, ct);
        var sha256 = await ComputeSha256Async(destination, ct);
        if (!string.IsNullOrWhiteSpace(expectedSha256))
            VerifySha256(sha256, expectedSha256);
        return sha256;
    }

    public static void TryDelete(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); } catch { }
    }
}
