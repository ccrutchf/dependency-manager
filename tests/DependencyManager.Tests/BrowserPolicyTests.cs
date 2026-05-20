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

    [Fact]
    public void Firefox_normal_mode_builds_url_from_source_slug()
    {
        var r = BrowserPolicy.ResolveExtension(
            BrowserPolicyFamily.Firefox, new PackageSpec { Source = "ublock-origin" });

        r.Valid.ShouldBeTrue();
        r.Mode.ShouldBe("normal_installed");
        r.Url.ShouldBe("https://addons.mozilla.org/firefox/downloads/latest/ublock-origin/latest.xpi");
    }

    [Fact]
    public void Firefox_auto_install_without_a_url_is_invalid()
    {
        var r = BrowserPolicy.ResolveExtension(BrowserPolicyFamily.Firefox, new PackageSpec());

        r.Valid.ShouldBeFalse();
        r.Mode.ShouldBe("normal_installed");
        r.Url.ShouldBeNull();
    }

    [Fact]
    public void Firefox_allowed_mode_is_valid_without_a_url()
    {
        var r = BrowserPolicy.ResolveExtension(
            BrowserPolicyFamily.Firefox, new PackageSpec { Mode = "allowed" });

        r.Valid.ShouldBeTrue();
        r.Mode.ShouldBe("allowed");
        r.Url.ShouldBeNull();
    }

    [Fact]
    public void Chromium_auto_install_defaults_to_store_url_so_it_is_always_valid()
    {
        var r = BrowserPolicy.ResolveExtension(BrowserPolicyFamily.Chromium, new PackageSpec());

        r.Valid.ShouldBeTrue();
        r.Mode.ShouldBe("normal_installed");
        r.Url.ShouldBe(BrowserPolicy.ChromeWebStoreUpdateUrl);
    }

    [Fact]
    public void Chromium_force_mode_honors_explicit_url()
    {
        var r = BrowserPolicy.ResolveExtension(
            BrowserPolicyFamily.Chromium, new PackageSpec { Mode = "force", Url = "https://example/u" });

        r.Valid.ShouldBeTrue();
        r.Mode.ShouldBe("force_installed");
        r.Url.ShouldBe("https://example/u");
    }

    [Fact]
    public void Chromium_blocked_mode_carries_no_url()
    {
        var r = BrowserPolicy.ResolveExtension(
            BrowserPolicyFamily.Chromium, new PackageSpec { Mode = "blocked" });

        r.Valid.ShouldBeTrue();
        r.Mode.ShouldBe("blocked");
        r.Url.ShouldBeNull();
    }

    [Fact]
    public void Chromium_file_name_is_per_extension()
    {
        BrowserPolicy.ChromiumFileName("cjpalhdlnbpafiamejdnhcphjbkeiagm")
            .ShouldBe("depend-cjpalhdlnbpafiamejdnhcphjbkeiagm.json");
    }

    [Fact]
    public void Chromium_file_name_sanitizes_unsafe_characters()
    {
        // Defensive: an id is part of a filesystem path, so path/separator chars must not leak through.
        BrowserPolicy.ChromiumFileName("a/b c@d").ShouldBe("depend-a-b-c-d.json");
    }

    [Fact]
    public void Firefox_blocked_extension_is_written_without_an_install_url()
    {
        var json = BrowserPolicy.MergeFirefoxPolicies(existing: null, "ext@x", "blocked", installUrl: null);

        json.ShouldNotContain("install_url");
        BrowserPolicy.FirefoxPoliciesHasExtension(json, "ext@x", "blocked", null).ShouldBeTrue();
    }

    [Fact]
    public void Firefox_re_merge_updates_the_same_extension_in_place()
    {
        var first = BrowserPolicy.MergeFirefoxPolicies(null, "ext@x", "normal_installed", "https://example/x.xpi");
        var second = BrowserPolicy.MergeFirefoxPolicies(first, "ext@x", "force_installed", "https://example/x.xpi");

        BrowserPolicy.FirefoxPoliciesHasExtension(second, "ext@x", "force_installed", "https://example/x.xpi")
            .ShouldBeTrue();
        // The old mode is gone, not duplicated.
        BrowserPolicy.FirefoxPoliciesHasExtension(second, "ext@x", "normal_installed", "https://example/x.xpi")
            .ShouldBeFalse();
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

    [Fact]
    public void Zen_is_a_firefox_family_flatpak_browser()
    {
        var spec = BrowserCatalog.For(ManagerKind.Zen, "x86_64", "/home/u");

        spec.Family.ShouldBe(BrowserPolicyFamily.Firefox);
        spec.FlatpakAppId.ShouldBe("app.zen_browser.zen");
        spec.FlatpakExtensionPoint.ShouldBe("app.zen_browser.zen.systemconfig");
        spec.SnapName.ShouldBeNull();

        // Zen ships no /etc target; its policies live next to the binary.
        spec.NativeTargets.ShouldAllBe(t => t.Path.EndsWith("distribution/policies.json"));
    }

    [Fact]
    public void Chromium_native_and_snap_targets_are_known()
    {
        var spec = BrowserCatalog.For(ManagerKind.Chromium, "x86_64", "/home/u");

        spec.Family.ShouldBe(BrowserPolicyFamily.Chromium);
        spec.NativeTargets.ShouldContain(t => t.Path == "/etc/chromium/policies/managed");
        spec.SnapName.ShouldBe("chromium");
        spec.SnapTargets.ShouldContain(t => t.Path == "/var/snap/chromium/current/policies/managed");
    }

    [Fact]
    public void Brave_is_a_chromium_family_browser_with_etc_brave_target()
    {
        var spec = BrowserCatalog.For(ManagerKind.Brave, "x86_64", "/home/u");

        spec.Family.ShouldBe(BrowserPolicyFamily.Chromium);
        spec.NativeTargets.ShouldContain(t => t.Path == "/etc/brave/policies/managed");
    }

    [Theory]
    [InlineData(ManagerKind.Apt)]
    [InlineData(ManagerKind.Snap)]
    [InlineData(ManagerKind.VsCode)]
    public void For_throws_on_non_browser_kinds(ManagerKind kind) =>
        Should.Throw<ArgumentOutOfRangeException>(() => BrowserCatalog.For(kind, "x86_64", "/home/u"));
}
