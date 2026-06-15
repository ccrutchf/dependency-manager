using DependencyManager.Managers;
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
}
