using System.IO.Compression;
using System.Text.Json;
using DependencyManager.Util;

namespace DependencyManager.Managers;

/// <summary>
/// Where a vscode entry's <c>url:</c> points: an http(s) url to download, or an
/// absolute local path (after expanding <c>~/</c> and <c>file://</c>). The
/// location is also the key the install is recorded under in the vsix cache.
/// </summary>
public sealed record VsixSource(string Location, bool IsRemote);

/// <summary>
/// Pure helpers for installing VS Code extensions from a <c>.vsix</c> file, kept
/// separate from <see cref="VsCodeManager"/> so they are unit-testable without
/// `code` (same pattern as FlatpakBundle).
/// </summary>
public static class Vsix
{
    private const string ManifestEntry = "extension/package.json";

    private static readonly JsonDocumentOptions LenientJson = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Normalises a <c>url:</c> value: http(s) urls are kept as-is; <c>file://</c>
    /// urls, <c>~/</c> paths (expanded against <paramref name="home"/>) and absolute
    /// paths become a full local path. Anything else, including relative paths, is rejected.
    /// </summary>
    public static VsixSource Resolve(string url, string home)
    {
        var value = url.Trim();

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new VsixSource(value, IsRemote: true);

        string? path = null;
        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(value, UriKind.Absolute, out var uri))
            path = uri.LocalPath;
        else if (value == "~" || value.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(home, value.Length > 2 ? value[2..] : string.Empty);
        else if (Path.IsPathFullyQualified(value))
            path = value;

        if (path is null || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException(
                $"vsix url '{url}' must be an http(s) url, a file:// url, an absolute path or a ~/ path");

        return new VsixSource(Path.GetFullPath(path), IsRemote: false);
    }

    /// <summary>
    /// The extension id (<c>publisher.name</c>) from the <c>extension/package.json</c>
    /// inside a .vsix (a zip). Throws <see cref="InvalidDataException"/> when the
    /// file isn't a zip or the manifest is missing or lacks publisher/name.
    /// </summary>
    public static string ReadId(Stream vsix)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(vsix, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new InvalidDataException("not a .vsix (zip) file"); }

        using (zip)
        {
            var entry = zip.GetEntry(ManifestEntry)
                ?? throw new InvalidDataException($"no {ManifestEntry} in the .vsix");

            JsonDocument doc;
            try
            {
                using var stream = entry.Open();
                doc = JsonDocument.Parse(stream, LenientJson);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{ManifestEntry} is not valid json: {ex.Message}");
            }

            using (doc)
            {
                var publisher = StringProperty(doc.RootElement, "publisher");
                var name = StringProperty(doc.RootElement, "name");
                if (publisher is null || name is null)
                    throw new InvalidDataException($"{ManifestEntry} has no publisher and name");
                return $"{publisher}.{name}";
            }
        }
    }

    private static string? StringProperty(JsonElement root, string field) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(field, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>
    /// Refuses a .vsix whose <c>publisher.name</c> isn't the config key. Compared
    /// case-insensitively, as VS Code compares extension ids.
    /// </summary>
    public static void EnsureId(string actualId, string expectedId, string source)
    {
        if (!string.Equals(actualId, expectedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{source} contains extension {actualId}, not {expectedId}; the vscode key must be the extension's publisher.name");
    }

    /// <summary>
    /// Whether an installed (listed) extension from a .vsix is current: recorded
    /// under this location and id, with the pinned sha256 if one is given, else the
    /// sha256 of the local file as it is now (so a rebuilt file reinstalls).
    /// <paramref name="fileSha256"/> is null for a remote url, or for a local file
    /// that is missing — which counts as current, since there is nothing to install.
    /// </summary>
    public static bool IsCurrent(
        IReadOnlyDictionary<string, DownloadCacheEntry> entries,
        VsixSource source,
        string id,
        string? pinnedSha256,
        string? fileSha256)
    {
        if (!source.IsRemote && fileSha256 is null) return true;
        var expected = string.IsNullOrWhiteSpace(pinnedSha256) ? fileSha256 : pinnedSha256;
        return DownloadCache.IsCurrent(entries, source.Location, expected, id);
    }
}
