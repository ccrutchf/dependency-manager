using DependencyManager.Config;
using DependencyManager.Managers;
using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class VsCodeExtensionsTests
{
    [Fact]
    public void ParseList_trims_and_drops_blank_lines()
    {
        var stdout = "ms-python.python\n  rust-lang.rust-analyzer  \n\nmkhl.direnv\n";
        VsCodeExtensions.ParseList(stdout)
            .ShouldBe(["ms-python.python", "rust-lang.rust-analyzer", "mkhl.direnv"]);
    }

    [Fact]
    public void ParsePulledIn_reads_extension_pack()
    {
        var json = """
        { "name": "python",
          "extensionPack": ["ms-python.vscode-pylance", "ms-python.debugpy", "ms-python.vscode-python-envs"] }
        """;
        VsCodeExtensions.ParsePulledIn(json)
            .ShouldBe(["ms-python.vscode-pylance", "ms-python.debugpy", "ms-python.vscode-python-envs"]);
    }

    [Fact]
    public void ParsePulledIn_unions_pack_and_dependencies()
    {
        var json = """
        { "extensionPack": ["a.one"], "extensionDependencies": ["b.two"] }
        """;
        VsCodeExtensions.ParsePulledIn(json).ShouldBe(["a.one", "b.two"], ignoreOrder: true);
    }

    [Fact]
    public void ParsePulledIn_empty_for_a_leaf_extension()
    {
        VsCodeExtensions.ParsePulledIn("""{ "name": "direnv" }""").ShouldBeEmpty();
    }

    [Fact]
    public void ParsePulledIn_tolerates_garbage()
    {
        VsCodeExtensions.ParsePulledIn("not json").ShouldBeEmpty();
        VsCodeExtensions.ParsePulledIn("").ShouldBeEmpty();
        VsCodeExtensions.ParsePulledIn("[1,2,3]").ShouldBeEmpty();   // array, not an object
    }

    [Fact]
    public void Leaves_excludes_pack_members_keeps_top_level()
    {
        // ms-python.python (declared/installed) pulls in pylance + debugpy; only the
        // top-level extensions that nothing pulls in are leaves.
        var installed = new[]
        {
            "ms-python.python", "ms-python.vscode-pylance", "ms-python.debugpy",
            "rust-lang.rust-analyzer",
        };
        var pulledIn = new[] { "ms-python.vscode-pylance", "ms-python.debugpy" };

        VsCodeExtensions.Leaves(installed, pulledIn)
            .ShouldBe(["ms-python.python", "rust-lang.rust-analyzer"]);
    }

    [Fact]
    public void Leaves_is_case_insensitive()
    {
        var installed = new[] { "Pub.Tool", "Pub.Dep" };
        var pulledIn = new[] { "pub.dep" };
        VsCodeExtensions.Leaves(installed, pulledIn).ShouldBe(["Pub.Tool"]);
    }

    [Fact]
    public void Leaves_with_no_packs_returns_everything()
    {
        var installed = new[] { "a.one", "b.two" };
        VsCodeExtensions.Leaves(installed, System.Array.Empty<string>())
            .ShouldBe(["a.one", "b.two"]);
    }

    private static Dictionary<string, DownloadCacheEntry> VsixCache(params (string Location, string Id)[] rows) =>
        rows.ToDictionary(r => r.Location, r => new DownloadCacheEntry("aaa", DateTimeOffset.UtcNow, r.Id));

    [Fact]
    public void Updatable_skips_extensions_installed_from_a_vsix()
    {
        var installed = new[] { "ms-python.python", "PhoneDash.phone-dash-focus", "mkhl.direnv" };
        var cache = VsixCache(("/home/alice/ext.vsix", "phonedash.phone-dash-focus"));

        VsCodeExtensions.Updatable(installed, cache).ShouldBe(["ms-python.python", "mkhl.direnv"]);
    }

    [Fact]
    public void Updatable_is_every_installed_extension_when_nothing_came_from_a_vsix()
    {
        var installed = new[] { "ms-python.python", "mkhl.direnv" };

        VsCodeExtensions.Updatable(installed, new Dictionary<string, DownloadCacheEntry>())
            .ShouldBe(["ms-python.python", "mkhl.direnv"]);
    }

    [Fact]
    public void Updatable_ignores_cache_entries_without_an_id()
    {
        var cache = new Dictionary<string, DownloadCacheEntry>
        {
            ["/x.vsix"] = new("aaa", DateTimeOffset.UtcNow, PackageId: null),
        };

        VsCodeExtensions.Updatable(["a.one"], cache).ShouldBe(["a.one"]);
    }

    [Fact]
    public void Marketplace_entry_is_installed_when_listed()
    {
        var none = new Dictionary<string, DownloadCacheEntry>();

        VsCodeExtensions.IsMarketplaceInstalled(["ms-python.python"], "MS-Python.Python", none).ShouldBeTrue();
        VsCodeExtensions.IsMarketplaceInstalled(["mkhl.direnv"], "ms-python.python", none).ShouldBeFalse();
    }

    [Fact]
    public void Marketplace_entry_previously_installed_from_a_vsix_is_reinstalled()
    {
        // Dropping url: from an entry switches it back to the Marketplace; until that
        // reinstall happens, update would keep skipping it as a vsix install.
        var cache = VsixCache(("/home/alice/ext.vsix", "a.one"));

        VsCodeExtensions.IsMarketplaceInstalled(["a.one"], "a.one", cache).ShouldBeFalse();
    }
}

public class VsCodeManagerTests
{
    [Fact]
    public void Entry_without_url_installs_from_the_marketplace_by_id()
    {
        VsCodeManager.IsVsix(new PackageSpec()).ShouldBeFalse();
        VsCodeManager.IsVsix(new PackageSpec { Url = " " }).ShouldBeFalse();
        VsCodeManager.InstallArgs("ms-python.python")
            .ShouldBe(["--install-extension", "ms-python.python", "--force"]);
    }

    [Fact]
    public void Entry_without_url_uses_the_source_binary_when_given()
    {
        VsCodeManager.Binary(new PackageSpec()).ShouldBe("code");
        VsCodeManager.Binary(new PackageSpec { Source = "codium" }).ShouldBe("codium");
    }

    [Fact]
    public void Entry_with_url_installs_the_vsix_file()
    {
        VsCodeManager.IsVsix(new PackageSpec { Url = "~/ext.vsix" }).ShouldBeTrue();
        VsCodeManager.InstallArgs("/home/alice/ext.vsix")
            .ShouldBe(["--install-extension", "/home/alice/ext.vsix", "--force"]);
    }
}
