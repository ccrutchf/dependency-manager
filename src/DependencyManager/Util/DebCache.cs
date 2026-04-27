using System.Text.Json;
using System.Text.Json.Serialization;

namespace DependencyManager.Util;

public sealed record DebCacheEntry(string Sha256, DateTimeOffset InstalledAt);

public sealed class DebCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    public DebCache(string? path = null)
    {
        _path = path ?? DefaultPath();
    }

    public string Path => _path;

    public static string DefaultPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var baseDir = !string.IsNullOrEmpty(xdg)
            ? xdg
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache");
        return System.IO.Path.Combine(baseDir, "depend", "debs.json");
    }

    public async Task<Dictionary<string, DebCacheEntry>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return new Dictionary<string, DebCacheEntry>(StringComparer.Ordinal);

        await using var stream = File.OpenRead(_path);
        var entries = await JsonSerializer.DeserializeAsync<Dictionary<string, DebCacheEntry>>(
            stream, JsonOptions, ct);
        return entries is null
            ? new Dictionary<string, DebCacheEntry>(StringComparer.Ordinal)
            : new Dictionary<string, DebCacheEntry>(entries, StringComparer.Ordinal);
    }

    public async Task SaveAsync(
        IReadOnlyDictionary<string, DebCacheEntry> entries,
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

    public async Task RecordAsync(string url, string sha256, CancellationToken ct)
    {
        var entries = await LoadAsync(ct);
        entries[url] = new DebCacheEntry(sha256, DateTimeOffset.UtcNow);
        await SaveAsync(entries, ct);
    }
}
