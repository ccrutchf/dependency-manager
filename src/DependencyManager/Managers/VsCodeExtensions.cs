using System.Text.Json;
using DependencyManager.Util;

namespace DependencyManager.Managers;

/// <summary>
/// Pure parsing + leaves computation for VSCode extensions, kept separate from
/// <see cref="VsCodeManager"/> so it is unit-testable without `code` or the
/// filesystem (same pattern as BrewPlan).
///
/// Prune must operate on "leaves" — the manually-installed, top-level extensions —
/// never on members an extension pack / dependency pulled in (e.g. ms-python.python
/// pulls ms-python.vscode-pylance + ms-python.debugpy). Built-in defaults are a
/// non-issue: `code --list-extensions` doesn't list them, so they never appear here.
/// </summary>
public static class VsCodeExtensions
{
    /// <summary>Parse `code --list-extensions` output: one id per line, trimmed.</summary>
    public static IReadOnlyList<string> ParseList(string stdout)
    {
        var ids = new List<string>();
        if (string.IsNullOrEmpty(stdout)) return ids;
        foreach (var line in stdout.Split('\n'))
        {
            var id = line.Trim();
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }

    private static readonly JsonDocumentOptions LenientJson = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The extension ids a single extension's manifest (package.json) pulls in:
    /// the union of its <c>extensionPack</c> and <c>extensionDependencies</c> arrays.
    /// Returns empty for a manifest with neither (or unparseable input — callers
    /// treat a bad manifest as "pulls nothing in", the conservative choice).
    /// </summary>
    public static IReadOnlyList<string> ParsePulledIn(string packageJson)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(packageJson)) return ids;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(packageJson, LenientJson); }
        catch (JsonException) { return ids; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return ids;
            foreach (var field in (ReadOnlySpan<string>)["extensionPack", "extensionDependencies"])
            {
                if (doc.RootElement.TryGetProperty(field, out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in arr.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String)
                        {
                            var id = e.GetString();
                            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                        }
                }
            }
        }
        return ids;
    }

    /// <summary>
    /// The installed ids that no OTHER installed extension pulls in — the leaves,
    /// i.e. the only safe prune candidates. Case-insensitive (extension ids are).
    /// </summary>
    public static IReadOnlyList<string> Leaves(
        IEnumerable<string> installed, IEnumerable<string> pulledIn)
    {
        var pulled = new HashSet<string>(pulledIn, StringComparer.OrdinalIgnoreCase);
        return installed.Where(id => !pulled.Contains(id)).ToList();
    }

    /// <summary>
    /// The installed ids `update` may reinstall from the Marketplace: everything
    /// except extensions the vsix cache records as installed from a .vsix, which
    /// the Marketplace doesn't have (or would replace with a different build).
    /// </summary>
    public static IReadOnlyList<string> Updatable(
        IEnumerable<string> installed, IReadOnlyDictionary<string, DownloadCacheEntry> vsixCache)
    {
        var fromVsix = FromVsix(vsixCache);
        return installed.Where(id => !fromVsix.Contains(id)).ToList();
    }

    /// <summary>
    /// A Marketplace (no url) entry is installed when listed — unless the vsix cache
    /// says it came from a .vsix, in which case it reinstalls from the Marketplace
    /// (and leaves the cache), so `update` stops skipping it.
    /// </summary>
    public static bool IsMarketplaceInstalled(
        IEnumerable<string> listed, string id, IReadOnlyDictionary<string, DownloadCacheEntry> vsixCache) =>
        listed.Contains(id, StringComparer.OrdinalIgnoreCase) && !FromVsix(vsixCache).Contains(id);

    private static HashSet<string> FromVsix(IReadOnlyDictionary<string, DownloadCacheEntry> vsixCache) =>
        new(vsixCache.Values.Select(e => e.PackageId).OfType<string>(), StringComparer.OrdinalIgnoreCase);
}
