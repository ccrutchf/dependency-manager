using DependencyManager.Config;

namespace DependencyManager.Managers;

/// <summary>
/// Where each supported browser lives and where it reads extension policies,
/// across native, snap, and flatpak installs. This is the "known paths" table
/// the browser extension managers search.
///
/// Sources for the paths below:
///  - Firefox: /etc/firefox/policies/policies.json or &lt;install&gt;/distribution/policies.json
///    (support.mozilla.org "Managing policies on Linux desktops"); snap reads
///    /var/snap/firefox/current/policies.json; the flatpak honors the
///    org.mozilla.firefox.systemconfig extension point mounted at /app/etc/firefox/policies.
///  - Zen: Firefox fork; reads &lt;install&gt;/distribution/policies.json (docs.zen-browser.app).
///  - Chrome:   /etc/opt/chrome/policies/managed (Chrome Enterprise on Linux).
///  - Chromium: /etc/chromium/policies/managed (and /etc/chromium-browser on older Ubuntu);
///    snap reads /var/snap/chromium/current/policies/managed; flatpak uses the
///    org.chromium.Chromium.Extension.system-policies extension point.
///  - Brave:    /etc/brave/policies/managed (Brave "Group Policy" help).
///
/// Flatpak extension-point and snap targets for Chrome/Brave/Zen are best-effort:
/// those packagers do not all declare a policy extension point, so writing there
/// is harmless but may have no effect.
/// </summary>
public sealed record BrowserSpec(
    ManagerKind Kind,
    BrowserPolicyFamily Family,
    IReadOnlyList<string> Binaries,
    IReadOnlyList<PolicyTarget> NativeTargets,
    string? SnapName,
    IReadOnlyList<PolicyTarget> SnapTargets,
    string? FlatpakAppId,
    string? FlatpakExtensionPoint);

public static class BrowserCatalog
{
    /// <param name="arch">flatpak arch directory, e.g. "x86_64" or "aarch64".</param>
    /// <param name="home">the invoking user's home directory.</param>
    public static BrowserSpec For(ManagerKind kind, string arch, string home) => kind switch
    {
        ManagerKind.Firefox => new BrowserSpec(
            kind,
            BrowserPolicyFamily.Firefox,
            Binaries: ["firefox", "firefox-esr", "/usr/bin/firefox", "/usr/lib/firefox/firefox", "/opt/firefox/firefox"],
            NativeTargets:
            [
                new PolicyTarget("/etc/firefox/policies/policies.json", RequiresRoot: true),
                new PolicyTarget("/usr/lib/firefox/distribution/policies.json", RequiresRoot: true, RequiresDir: "/usr/lib/firefox"),
                new PolicyTarget("/usr/lib64/firefox/distribution/policies.json", RequiresRoot: true, RequiresDir: "/usr/lib64/firefox"),
                new PolicyTarget("/opt/firefox/distribution/policies.json", RequiresRoot: true, RequiresDir: "/opt/firefox"),
            ],
            SnapName: "firefox",
            SnapTargets: [new PolicyTarget("/var/snap/firefox/current/policies.json", RequiresRoot: true)],
            FlatpakAppId: "org.mozilla.firefox",
            FlatpakExtensionPoint: "org.mozilla.firefox.systemconfig"),

        ManagerKind.Zen => new BrowserSpec(
            kind,
            BrowserPolicyFamily.Firefox,
            Binaries: ["zen", "zen-browser", "/usr/lib/zen-browser/zen-bin", "/opt/zen/zen", "/opt/zen-browser-bin/zen-bin"],
            NativeTargets:
            [
                new PolicyTarget("/usr/lib/zen-browser/distribution/policies.json", RequiresRoot: true, RequiresDir: "/usr/lib/zen-browser"),
                new PolicyTarget("/usr/lib/zen/distribution/policies.json", RequiresRoot: true, RequiresDir: "/usr/lib/zen"),
                new PolicyTarget("/opt/zen/distribution/policies.json", RequiresRoot: true, RequiresDir: "/opt/zen"),
                new PolicyTarget("/opt/zen-browser-bin/distribution/policies.json", RequiresRoot: true, RequiresDir: "/opt/zen-browser-bin"),
            ],
            SnapName: null,
            SnapTargets: [],
            FlatpakAppId: "app.zen_browser.zen",
            FlatpakExtensionPoint: "app.zen_browser.zen.systemconfig"),

        ManagerKind.Chrome => new BrowserSpec(
            kind,
            BrowserPolicyFamily.Chromium,
            Binaries: ["google-chrome", "google-chrome-stable", "/opt/google/chrome/chrome", "/usr/bin/google-chrome"],
            NativeTargets: [new PolicyTarget("/etc/opt/chrome/policies/managed", RequiresRoot: true)],
            SnapName: null,
            SnapTargets: [],
            FlatpakAppId: "com.google.Chrome",
            FlatpakExtensionPoint: "com.google.Chrome.Extension.system-policies"),

        ManagerKind.Chromium => new BrowserSpec(
            kind,
            BrowserPolicyFamily.Chromium,
            Binaries: ["chromium", "chromium-browser", "/usr/bin/chromium", "/usr/lib/chromium/chromium"],
            NativeTargets:
            [
                new PolicyTarget("/etc/chromium/policies/managed", RequiresRoot: true),
                new PolicyTarget("/etc/chromium-browser/policies/managed", RequiresRoot: true, RequiresDir: "/etc/chromium-browser"),
            ],
            SnapName: "chromium",
            SnapTargets: [new PolicyTarget("/var/snap/chromium/current/policies/managed", RequiresRoot: true)],
            FlatpakAppId: "org.chromium.Chromium",
            FlatpakExtensionPoint: "org.chromium.Chromium.Extension.system-policies"),

        ManagerKind.Brave => new BrowserSpec(
            kind,
            BrowserPolicyFamily.Chromium,
            Binaries: ["brave", "brave-browser", "/opt/brave.com/brave/brave", "/usr/bin/brave-browser"],
            NativeTargets:
            [
                new PolicyTarget("/etc/brave/policies/managed", RequiresRoot: true),
                new PolicyTarget("/etc/opt/brave/policies/managed", RequiresRoot: true, RequiresDir: "/etc/opt/brave"),
            ],
            SnapName: null,
            SnapTargets: [],
            FlatpakAppId: "com.brave.Browser",
            FlatpakExtensionPoint: "com.brave.Browser.Extension.system-policies"),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a browser-extension manager kind"),
    };

    /// <summary>The leaf path a flatpak policy extension mounts, relative to its versioned dir.</summary>
    public static string FlatpakLeaf(BrowserPolicyFamily family) =>
        family == BrowserPolicyFamily.Firefox ? "policies/policies.json" : "policies/managed";

    /// <summary>Builds a flatpak extension-point policy target rooted at a flatpak install dir.</summary>
    public static PolicyTarget FlatpakTarget(BrowserSpec spec, string flatpakRoot, string arch, bool requiresRoot)
    {
        var path = string.Join('/',
            flatpakRoot.TrimEnd('/'),
            "extension",
            spec.FlatpakExtensionPoint!,
            arch,
            "stable",
            FlatpakLeaf(spec.Family));
        return new PolicyTarget(path, requiresRoot);
    }
}
