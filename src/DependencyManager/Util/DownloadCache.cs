using System.Text.Json;
using System.Text.Json.Serialization;

namespace DependencyManager.Util;

public sealed record DownloadCacheEntry(
    string Sha256,
    DateTimeOffset InstalledAt,
    string? PackageId = null);

/// <summary>
/// Records which url (and sha256) each url-installed package came from, so a
/// changed url or hash in the YAML is seen as "not installed" and reinstalls.
/// One JSON file per provider under $XDG_CACHE_HOME/depend (debs.json, flatpaks.json, vsix.json).
/// </summary>
public sealed class DownloadCache
{
    public const string DebsFile = "debs.json";
    public const string FlatpaksFile = "flatpaks.json";
    public const string VsixFile = "vsix.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    public DownloadCache(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public static string DefaultPath(string fileName)
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var baseDir = !string.IsNullOrEmpty(xdg)
            ? xdg
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache");
        return System.IO.Path.Combine(baseDir, "depend", fileName);
    }

    /// <summary>
    /// True if <paramref name="url"/> was recorded as installed, with the pinned
    /// <paramref name="sha256"/> (when given) and for <paramref name="packageId"/>
    /// (when given and recorded).
    /// </summary>
    public static bool IsCurrent(
        IReadOnlyDictionary<string, DownloadCacheEntry> entries,
        string url,
        string? sha256,
        string? packageId)
    {
        if (!entries.TryGetValue(url, out var entry)) return false;

        if (!string.IsNullOrWhiteSpace(packageId) && entry.PackageId is not null
            && !string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(sha256)
            && !string.Equals(entry.Sha256, Downloads.NormalizeSha256(sha256), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public async Task<Dictionary<string, DownloadCacheEntry>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return new Dictionary<string, DownloadCacheEntry>(StringComparer.Ordinal);

        await using var stream = File.OpenRead(_path);
        var entries = await JsonSerializer.DeserializeAsync<Dictionary<string, DownloadCacheEntry>>(
            stream, JsonOptions, ct);
        return entries is null
            ? new Dictionary<string, DownloadCacheEntry>(StringComparer.Ordinal)
            : new Dictionary<string, DownloadCacheEntry>(entries, StringComparer.Ordinal);
    }

    public async Task SaveAsync(
        IReadOnlyDictionary<string, DownloadCacheEntry> entries,
        CancellationToken ct)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, ct);
        }
        File.Move(tmp, _path, overwrite: true);
    }

    public Task RecordAsync(string url, string sha256, CancellationToken ct) =>
        RecordAsync(url, sha256, packageId: null, ct);

    public async Task RecordAsync(string url, string sha256, string? packageId, CancellationToken ct)
    {
        var entries = await LoadAsync(ct);
        if (!string.IsNullOrEmpty(packageId))
        {
            var stale = entries
                .Where(kv => kv.Key != url
                    && string.Equals(kv.Value.PackageId, packageId, StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in stale) entries.Remove(key);
        }
        entries[url] = new DownloadCacheEntry(sha256, DateTimeOffset.UtcNow, packageId);
        await SaveAsync(entries, ct);
    }

    /// <summary>Drops every entry recorded for <paramref name="packageId"/>; writes only if one existed.</summary>
    public async Task ForgetAsync(string packageId, CancellationToken ct)
    {
        var entries = await LoadAsync(ct);
        var stale = entries
            .Where(kv => string.Equals(kv.Value.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();
        if (stale.Count == 0) return;
        foreach (var key in stale) entries.Remove(key);
        await SaveAsync(entries, ct);
    }
}
