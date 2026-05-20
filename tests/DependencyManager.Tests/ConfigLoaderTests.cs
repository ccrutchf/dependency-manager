using DependencyManager.Config;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Parses_multiple_blocks_with_filters_and_provider_sections()
    {
        const string yaml = """
            all:
              platform: all
              architecture: all
              flatpak:
                org.mozilla.firefox:

            ubuntu:
              platform: linux
              architecture: amd64
              apt:
                ripgrep:
                  dependencies: [curl]
                curl:
              snap:
                code:
                  classic: true
            """;

        var config = ConfigLoader.Parse(yaml);

        config.Blocks.Count.ShouldBe(2);

        var all = config.Blocks["all"];
        all.Platform.ShouldBe("all");
        all.Architecture.ShouldBe("all");
        all.Flatpak.ShouldNotBeNull();
        all.Flatpak.ShouldContainKey("org.mozilla.firefox");

        var ubuntu = config.Blocks["ubuntu"];
        ubuntu.Platform.ShouldBe("linux");
        ubuntu.Architecture.ShouldBe("amd64");
        ubuntu.Apt.ShouldNotBeNull();
        ubuntu.Apt["ripgrep"].Dependencies.ShouldBe(new[] { "curl" });
        ubuntu.Snap.ShouldNotBeNull();
        ubuntu.Snap["code"].Classic.ShouldBeTrue();
    }

    [Fact]
    public void Defaults_platform_and_architecture_to_all_when_omitted()
    {
        const string yaml = """
            minimal:
              apt:
                vim:
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["minimal"];
        block.Platform.ShouldBe("all");
        block.Architecture.ShouldBe("all");
        block.Version.ShouldBeNull();
    }

    [Fact]
    public void Parses_block_level_ppas_list()
    {
        const string yaml = """
            ubuntu:
              platform: linux
              ppas:
                - ppa:neovim-ppa/unstable
                - ppa:git-core/ppa
              apt:
                neovim:
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["ubuntu"];
        block.Ppas.ShouldBe(new[] { "ppa:neovim-ppa/unstable", "ppa:git-core/ppa" });
    }

    [Fact]
    public void Parses_deb_block_with_url_and_sha256()
    {
        const string yaml = """
            desktop:
              platform: linux
              deb:
                slack-desktop:
                  url: https://example.com/slack.deb
                  sha256: abc123
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["desktop"];
        block.Deb.ShouldNotBeNull();
        block.Deb["slack-desktop"].Url.ShouldBe("https://example.com/slack.deb");
        block.Deb["slack-desktop"].Sha256.ShouldBe("abc123");
    }

    [Fact]
    public void Parses_pip_block()
    {
        const string yaml = """
            linux:
              platform: linux
              pip:
                httpie:
                ruff:
                  dependencies: [httpie]
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["linux"];
        block.Pip.ShouldNotBeNull();
        block.Pip.ShouldContainKey("httpie");
        block.Pip["ruff"].Dependencies.ShouldBe(new[] { "httpie" });
    }

    [Fact]
    public void Parses_pipx_block()
    {
        const string yaml = """
            linux:
              platform: linux
              pipx:
                httpie:
                ruff:
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["linux"];
        block.Pipx.ShouldNotBeNull();
        block.Pipx.ShouldContainKey("httpie");
        block.Pipx.ShouldContainKey("ruff");
    }

    [Fact]
    public void Parses_script_block_with_check_and_install()
    {
        const string yaml = """
            linux:
              platform: linux
              script:
                uv:
                  check: command -v uv
                  install: curl -LsSf https://astral.sh/uv/install.sh | sh
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["linux"];
        block.Script.ShouldNotBeNull();
        block.Script["uv"].Check.ShouldBe("command -v uv");
        block.Script["uv"].Install.ShouldBe("curl -LsSf https://astral.sh/uv/install.sh | sh");
    }

    [Fact]
    public void Parses_vscode_block()
    {
        const string yaml = """
            linux:
              platform: linux
              vscode:
                ms-python.python:
                rust-lang.rust-analyzer:
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["linux"];
        block.Vscode.ShouldNotBeNull();
        block.Vscode.ShouldContainKey("ms-python.python");
        block.Vscode.ShouldContainKey("rust-lang.rust-analyzer");
    }

    [Fact]
    public void Parses_scope_on_package_spec()
    {
        const string yaml = """
            linux:
              platform: linux
              pip:
                httpie:
                  scope: user
              flatpak:
                org.mozilla.firefox:
                  scope: system
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["linux"];
        block.Pip!["httpie"].Scope.ShouldBe("user");
        block.Pip["httpie"].UserScope.ShouldBe(true);
        block.Flatpak!["org.mozilla.firefox"].Scope.ShouldBe("system");
        block.Flatpak["org.mozilla.firefox"].UserScope.ShouldBe(false);
    }

    [Fact]
    public void Parses_nvm_block()
    {
        const string yaml = """
            linux:
              platform: linux
              nvm:
                "20":
                "lts/iron":
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["linux"];
        block.Nvm.ShouldNotBeNull();
        block.Nvm.ShouldContainKey("20");
        block.Nvm.ShouldContainKey("lts/iron");
    }

    [Fact]
    public void Parses_apt_sources_block()
    {
        const string yaml = """
            docker:
              platform: linux
              apt_sources:
                docker:
                  keyUrl: https://download.docker.com/linux/ubuntu/gpg
                  uri: https://download.docker.com/linux/ubuntu
                  components: stable
                  signedBy: /etc/apt/keyrings/docker.asc
              apt:
                docker-ce:
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["docker"];
        block.AptSources.ShouldNotBeNull();
        var src = block.AptSources["docker"];
        src.KeyUrl.ShouldBe("https://download.docker.com/linux/ubuntu/gpg");
        src.Uri.ShouldBe("https://download.docker.com/linux/ubuntu");
        src.Components.ShouldBe("stable");
        src.SignedBy.ShouldBe("/etc/apt/keyrings/docker.asc");
    }

    [Fact]
    public void Parses_block_level_requires_list()
    {
        const string yaml = """
            vscode-extensions:
              platform: all
              requires:
                - code
                - git
              vscode:
                ms-python.python:
            """;

        var config = ConfigLoader.Parse(yaml);
        config.Blocks["vscode-extensions"].Requires.ShouldBe(new[] { "code", "git" });
    }

    [Fact]
    public void Accepts_version_filter()
    {
        const string yaml = """
            focal:
              platform: linux
              version: "5.15"
              apt:
                tmux:
            """;

        var config = ConfigLoader.Parse(yaml);
        config.Blocks["focal"].Version.ShouldBe("5.15");
    }

    [Fact]
    public void Parses_browser_extension_blocks_with_mode_and_source()
    {
        const string yaml = """
            browser-extensions:
              platform: linux
              zen:
                uBlock0@raymondhill.net:
                  source: ublock-origin
              brave:
                cjpalhdlnbpafiamejdnhcphjbkeiagm:
                  mode: force
              firefox:
                uBlock0@raymondhill.net:
                  url: https://example/ublock.xpi
            """;

        var config = ConfigLoader.Parse(yaml);
        var block = config.Blocks["browser-extensions"];

        block.Zen.ShouldNotBeNull();
        block.Zen["uBlock0@raymondhill.net"].Source.ShouldBe("ublock-origin");

        block.Brave.ShouldNotBeNull();
        block.Brave["cjpalhdlnbpafiamejdnhcphjbkeiagm"].Mode.ShouldBe("force");

        block.Firefox.ShouldNotBeNull();
        block.Firefox["uBlock0@raymondhill.net"].Url.ShouldBe("https://example/ublock.xpi");
    }
}
