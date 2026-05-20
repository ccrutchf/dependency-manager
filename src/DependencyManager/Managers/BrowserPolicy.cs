using System.Text.Json;
using System.Text.Json.Nodes;
using DependencyManager.Config;

namespace DependencyManager.Managers;

/// <summary>
/// The two enterprise-policy dialects browsers use to declare extensions.
/// Firefox-family browsers merge a single <c>policies.json</c> with a
/// <c>policies.ExtensionSettings</c> map; Chromium-family browsers read every
/// JSON file in a <c>policies/managed/</c> directory and merge their top-level
/// <c>ExtensionSettings</c> maps.
/// </summary>
public enum BrowserPolicyFamily
{
    Firefox,
    Chromium,
}

/// <summary>
/// A single location to write an extension policy. For Firefox-family browsers
/// <see cref="Path"/> is the full <c>policies.json</c> file (merged in place);
/// for Chromium-family browsers it is the <c>policies/managed/</c> directory
/// (one file per extension is written into it).
/// </summary>
/// <param name="RequiresRoot">Whether writing needs sudo (system path) or not (per-user flatpak).</param>
/// <param name="RequiresDir">If set, the target is only used when this directory already exists.</param>
public sealed record PolicyTarget(string Path, bool RequiresRoot, string? RequiresDir = null);

/// <summary>
/// Pure, filesystem-free helpers for building and inspecting browser extension
/// policy documents. Kept separate from <see cref="BrowserExtensionManager"/>
/// so the JSON/mode/url logic is unit-testable without touching disk.
/// </summary>
public static class BrowserPolicy
{
    public const string ChromeWebStoreUpdateUrl = "https://clients2.google.com/service/update2/crx";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>
    /// Maps a config <c>mode:</c> value to a policy installation_mode string.
    /// Both families share the same vocabulary. Defaults to normal_installed.
    /// </summary>
    public static string ResolveInstallationMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "force" or "force_installed" => "force_installed",
        "allowed" => "allowed",
        "blocked" => "blocked",
        "normal" or "normal_installed" or null or "" => "normal_installed",
        _ => "normal_installed",
    };

    /// <summary>Whether a mode auto-installs the extension and therefore needs a download url.</summary>
    public static bool ModeNeedsUrl(string installationMode) =>
        installationMode is "force_installed" or "normal_installed";

    /// <summary>
    /// Firefox install_url: explicit <c>url:</c>, else built from an AMO slug in
    /// <c>source:</c>, else null (only valid for allowed/blocked).
    /// </summary>
    public static string? FirefoxInstallUrl(PackageSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.Url)) return spec.Url;
        if (!string.IsNullOrWhiteSpace(spec.Source))
            return $"https://addons.mozilla.org/firefox/downloads/latest/{spec.Source}/latest.xpi";
        return null;
    }

    /// <summary>Chromium update_url: explicit <c>url:</c>, else the Chrome Web Store endpoint.</summary>
    public static string ChromiumUpdateUrl(PackageSpec spec) =>
        !string.IsNullOrWhiteSpace(spec.Url) ? spec.Url : ChromeWebStoreUpdateUrl;

    /// <summary>Builds the contents of a single Chromium <c>managed/depend-&lt;id&gt;.json</c> file.</summary>
    public static string BuildChromiumExtensionFile(string id, string installationMode, string? updateUrl)
    {
        var entry = new JsonObject { ["installation_mode"] = installationMode };
        if (updateUrl is not null) entry["update_url"] = updateUrl;
        var root = new JsonObject { ["ExtensionSettings"] = new JsonObject { [id] = entry } };
        return root.ToJsonString(Pretty);
    }

    /// <summary>
    /// Merges one extension into a Firefox <c>policies.json</c>, preserving any
    /// existing policies and other managed extensions. Returns the new contents.
    /// </summary>
    public static string MergeFirefoxPolicies(string? existing, string addonId, string installationMode, string? installUrl)
    {
        var root = TryParseObject(existing) ?? new JsonObject();

        if (root["policies"] is not JsonObject policies)
        {
            policies = new JsonObject();
            root["policies"] = policies;
        }
        if (policies["ExtensionSettings"] is not JsonObject extensions)
        {
            extensions = new JsonObject();
            policies["ExtensionSettings"] = extensions;
        }

        var entry = new JsonObject { ["installation_mode"] = installationMode };
        if (installUrl is not null) entry["install_url"] = installUrl;
        extensions[addonId] = entry;

        return root.ToJsonString(Pretty);
    }

    /// <summary>True if a Chromium managed file already declares the extension with the same mode and url.</summary>
    public static bool ChromiumFileHasExtension(string? json, string id, string installationMode, string? updateUrl)
    {
        if (TryParseObject(json) is not { } root) return false;
        if (root["ExtensionSettings"] is not JsonObject extensions) return false;
        if (extensions[id] is not JsonObject entry) return false;
        if ((string?)entry["installation_mode"] != installationMode) return false;
        if (updateUrl is not null && (string?)entry["update_url"] != updateUrl) return false;
        return true;
    }

    /// <summary>True if a Firefox policies.json already declares the extension with the same mode and url.</summary>
    public static bool FirefoxPoliciesHasExtension(string? json, string addonId, string installationMode, string? installUrl)
    {
        if (TryParseObject(json) is not { } root) return false;
        if (root["policies"] is not JsonObject policies) return false;
        if (policies["ExtensionSettings"] is not JsonObject extensions) return false;
        if (extensions[addonId] is not JsonObject entry) return false;
        if ((string?)entry["installation_mode"] != installationMode) return false;
        if (installUrl is not null && (string?)entry["install_url"] != installUrl) return false;
        return true;
    }

    private static JsonObject? TryParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
