using DependencyManager.Config;
using DependencyManager.Managers;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class BrowserPolicyTests
{
    [Theory]
    [InlineData(null, "normal_installed")]
    [InlineData("", "normal_installed")]
    [InlineData("normal", "normal_installed")]
    [InlineData("normal_installed", "normal_installed")]
    [InlineData("force", "force_installed")]
    [InlineData("force_installed", "force_installed")]
    [InlineData("ALLOWED", "allowed")]
    [InlineData("blocked", "blocked")]
    [InlineData("nonsense", "normal_installed")]
    public void Resolves_installation_mode(string? input, string expected) =>
        BrowserPolicy.ResolveInstallationMode(input).ShouldBe(expected);

    [Theory]
    [InlineData("force_installed", true)]
    [InlineData("normal_installed", true)]
    [InlineData("allowed", false)]
    [InlineData("blocked", false)]
    public void Auto_install_modes_need_a_url(string mode, bool needsUrl) =>
        BrowserPolicy.ModeNeedsUrl(mode).ShouldBe(needsUrl);

    [Fact]
    public void Firefox_url_prefers_explicit_url_then_amo_slug()
    {
        BrowserPolicy.FirefoxInstallUrl(new PackageSpec { Url = "https://example/x.xpi" })
            .ShouldBe("https://example/x.xpi");

        BrowserPolicy.FirefoxInstallUrl(new PackageSpec { Source = "ublock-origin" })
            .ShouldBe("https://addons.mozilla.org/firefox/downloads/latest/ublock-origin/latest.xpi");

        BrowserPolicy.FirefoxInstallUrl(new PackageSpec()).ShouldBeNull();
    }

    [Fact]
    public void Chromium_url_defaults_to_chrome_web_store()
    {
        BrowserPolicy.ChromiumUpdateUrl(new PackageSpec()).ShouldBe(BrowserPolicy.ChromeWebStoreUpdateUrl);
        BrowserPolicy.ChromiumUpdateUrl(new PackageSpec { Url = "https://example/u" }).ShouldBe("https://example/u");
    }

    [Fact]
    public void Chromium_file_round_trips_through_has_check()
    {
        var json = BrowserPolicy.BuildChromiumExtensionFile(
            "cjpalhdlnbpafiamejdnhcphjbkeiagm", "normal_installed", BrowserPolicy.ChromeWebStoreUpdateUrl);

        BrowserPolicy.ChromiumFileHasExtension(
            json, "cjpalhdlnbpafiamejdnhcphjbkeiagm", "normal_installed", BrowserPolicy.ChromeWebStoreUpdateUrl)
            .ShouldBeTrue();

        // Different mode means a rewrite is needed.
        BrowserPolicy.ChromiumFileHasExtension(
            json, "cjpalhdlnbpafiamejdnhcphjbkeiagm", "force_installed", BrowserPolicy.ChromeWebStoreUpdateUrl)
            .ShouldBeFalse();

        // Different update url means a rewrite is needed.
        BrowserPolicy.ChromiumFileHasExtension(
            json, "cjpalhdlnbpafiamejdnhcphjbkeiagm", "normal_installed", "https://other/u")
            .ShouldBeFalse();
    }

    [Fact]
    public void Chromium_allowed_mode_omits_update_url()
    {
        var json = BrowserPolicy.BuildChromiumExtensionFile("someid", "allowed", null);
        json.ShouldNotContain("update_url");
        BrowserPolicy.ChromiumFileHasExtension(json, "someid", "allowed", null).ShouldBeTrue();
    }

    [Fact]
    public void Firefox_merge_creates_policies_and_round_trips()
    {
        var json = BrowserPolicy.MergeFirefoxPolicies(
            existing: null,
            "uBlock0@raymondhill.net",
            "normal_installed",
            "https://addons.mozilla.org/firefox/downloads/latest/ublock-origin/latest.xpi");

        BrowserPolicy.FirefoxPoliciesHasExtension(
            json, "uBlock0@raymondhill.net", "normal_installed",
            "https://addons.mozilla.org/firefox/downloads/latest/ublock-origin/latest.xpi")
            .ShouldBeTrue();
    }

    [Fact]
    public void Firefox_merge_preserves_existing_policies_and_other_extensions()
    {
        const string existing = """
            {
              "policies": {
                "DisableTelemetry": true,
                "ExtensionSettings": {
                  "other@example.com": { "installation_mode": "blocked" }
                }
              }
            }
            """;

        var json = BrowserPolicy.MergeFirefoxPolicies(
            existing, "new@example.com", "force_installed", "https://example/new.xpi");

        // New extension present.
        BrowserPolicy.FirefoxPoliciesHasExtension(json, "new@example.com", "force_installed", "https://example/new.xpi")
            .ShouldBeTrue();
        // Pre-existing extension preserved.
        BrowserPolicy.FirefoxPoliciesHasExtension(json, "other@example.com", "blocked", null)
            .ShouldBeTrue();
        // Unrelated policy preserved.
        json.ShouldContain("DisableTelemetry");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Has_checks_are_false_on_missing_or_invalid_documents(string? json)
    {
        BrowserPolicy.ChromiumFileHasExtension(json, "id", "normal_installed", null).ShouldBeFalse();
        BrowserPolicy.FirefoxPoliciesHasExtension(json, "id", "normal_installed", null).ShouldBeFalse();
    }
}

public class BrowserCatalogTests
{
    [Fact]
    public void Firefox_is_a_firefox_family_browser_with_etc_target()
    {
        var spec = BrowserCatalog.For(ManagerKind.Firefox, "x86_64", "/home/u");
        spec.Family.ShouldBe(BrowserPolicyFamily.Firefox);
        spec.NativeTargets.ShouldContain(t => t.Path == "/etc/firefox/policies/policies.json");
    }

    [Fact]
    public void Chrome_is_a_chromium_family_browser_with_managed_dir()
    {
        var spec = BrowserCatalog.For(ManagerKind.Chrome, "x86_64", "/home/u");
        spec.Family.ShouldBe(BrowserPolicyFamily.Chromium);
        spec.NativeTargets.ShouldContain(t => t.Path == "/etc/opt/chrome/policies/managed");
    }

    [Fact]
    public void Flatpak_leaf_differs_by_family()
    {
        BrowserCatalog.FlatpakLeaf(BrowserPolicyFamily.Firefox).ShouldBe("policies/policies.json");
        BrowserCatalog.FlatpakLeaf(BrowserPolicyFamily.Chromium).ShouldBe("policies/managed");
    }

    [Fact]
    public void Builds_firefox_flatpak_extension_point_path()
    {
        var spec = BrowserCatalog.For(ManagerKind.Firefox, "x86_64", "/home/u");
        var target = BrowserCatalog.FlatpakTarget(spec, "/var/lib/flatpak", "x86_64", requiresRoot: true);

        target.Path.ShouldBe(
            "/var/lib/flatpak/extension/org.mozilla.firefox.systemconfig/x86_64/stable/policies/policies.json");
        target.RequiresRoot.ShouldBeTrue();
    }

    [Fact]
    public void Builds_chromium_flatpak_extension_point_path_for_user_install()
    {
        var spec = BrowserCatalog.For(ManagerKind.Chromium, "aarch64", "/home/u");
        var target = BrowserCatalog.FlatpakTarget(
            spec, "/home/u/.local/share/flatpak", "aarch64", requiresRoot: false);

        target.Path.ShouldBe(
            "/home/u/.local/share/flatpak/extension/org.chromium.Chromium.Extension.system-policies/aarch64/stable/policies/managed");
        target.RequiresRoot.ShouldBeFalse();
    }
}
