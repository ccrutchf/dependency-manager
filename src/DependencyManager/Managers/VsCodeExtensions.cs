using System.Text.Json;

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
}
