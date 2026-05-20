using DependencyManager.Config;
using DependencyManager.Runner;
using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class PlannerTests
{
    private static PlatformInfo Linux => new("linux", "amd64", "5.15");

    [Fact]
    public void Skips_blocks_that_do_not_match_platform()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["windows-only"] = new()
            {
                Platform = "windows",
                Apt = new Dictionary<string, PackageSpec> { ["not-applicable"] = new() },
            },
            ["linux"] = new()
            {
                Platform = "linux",
                Apt = new Dictionary<string, PackageSpec> { ["curl"] = new() },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;

        plan.Count.ShouldBe(1);
        plan[0].Id.ShouldBe("curl");
    }

    [Fact]
    public void Topo_sort_places_dependencies_first()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Apt = new Dictionary<string, PackageSpec>
                {
                    ["ripgrep"] = new() { Dependencies = new List<string> { "curl" } },
                    ["curl"] = new(),
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;

        var ids = plan.Select(p => p.Id).ToList();
        ids.IndexOf("curl").ShouldBeLessThan(ids.IndexOf("ripgrep"));
    }

    [Fact]
    public void Topo_sort_honors_Name_override_for_dependency_lookup()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Apt = new Dictionary<string, PackageSpec>
                {
                    ["ripgrep"] = new() { Dependencies = new List<string> { "my-curl" } },
                    ["curl"] = new() { Name = "my-curl" },
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;

        var ids = plan.Select(p => p.Id).ToList();
        ids.IndexOf("curl").ShouldBeLessThan(ids.IndexOf("ripgrep"));
    }

    [Fact]
    public void Detects_dependency_cycles()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Apt = new Dictionary<string, PackageSpec>
                {
                    ["a"] = new() { Dependencies = new List<string> { "b" } },
                    ["b"] = new() { Dependencies = new List<string> { "a" } },
                },
            },
        });

        Should.Throw<InvalidOperationException>(() => Planner.Plan(config, Linux));
    }

    [Fact]
    public void Later_block_wins_on_duplicate_manager_id_pair()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["a"] = new()
            {
                Platform = "linux",
                Apt = new Dictionary<string, PackageSpec> { ["curl"] = new() { Source = "first" } },
            },
            ["b"] = new()
            {
                Platform = "linux",
                Apt = new Dictionary<string, PackageSpec> { ["curl"] = new() { Source = "second" } },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(1);
        plan[0].Spec.Source.ShouldBe("second");
    }

    [Fact]
    public void Unknown_dependency_name_is_ignored()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Apt = new Dictionary<string, PackageSpec>
                {
                    ["solo"] = new() { Dependencies = new List<string> { "does-not-exist" } },
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(1);
        plan[0].Id.ShouldBe("solo");
    }

    [Fact]
    public void Collects_ppas_from_matching_blocks_and_dedupes()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["a"] = new()
            {
                Platform = "linux",
                Ppas = new List<string> { "ppa:neovim-ppa/unstable", "ppa:git-core/ppa" },
                Apt = new Dictionary<string, PackageSpec> { ["neovim"] = new() },
            },
            ["b"] = new()
            {
                Platform = "linux",
                Ppas = new List<string> { "ppa:git-core/ppa" },
                Apt = new Dictionary<string, PackageSpec> { ["git"] = new() },
            },
            ["windows-only"] = new()
            {
                Platform = "windows",
                Ppas = new List<string> { "ppa:should-not-appear/ever" },
            },
        });

        var plan = Planner.Plan(config, Linux);

        plan.AptPpas.ShouldBe(new[] { "ppa:neovim-ppa/unstable", "ppa:git-core/ppa" });
    }

    [Fact]
    public void Deb_packages_flow_through_plan()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Deb = new Dictionary<string, PackageSpec>
                {
                    ["slack-desktop"] = new() { Url = "https://example.com/slack.deb", Sha256 = "abc" },
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(1);
        plan[0].Manager.ShouldBe(ManagerKind.Deb);
        plan[0].Id.ShouldBe("slack-desktop");
        plan[0].Spec.Url.ShouldBe("https://example.com/slack.deb");
    }

    [Fact]
    public void Pip_packages_flow_through_plan()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Pip = new Dictionary<string, PackageSpec>
                {
                    ["httpie"] = new(),
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(1);
        plan[0].Manager.ShouldBe(ManagerKind.Pip);
        plan[0].Id.ShouldBe("httpie");
    }

    [Fact]
    public void Pipx_packages_flow_through_plan()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Pipx = new Dictionary<string, PackageSpec>
                {
                    ["httpie"] = new(),
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(1);
        plan[0].Manager.ShouldBe(ManagerKind.Pipx);
        plan[0].Id.ShouldBe("httpie");
    }

    [Fact]
    public void Script_packages_flow_through_plan()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Script = new Dictionary<string, PackageSpec>
                {
                    ["uv"] = new() { Check = "command -v uv", Install = "echo install" },
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(1);
        plan[0].Manager.ShouldBe(ManagerKind.Script);
        plan[0].Id.ShouldBe("uv");
        plan[0].Spec.Check.ShouldBe("command -v uv");
        plan[0].Spec.Install.ShouldBe("echo install");
    }

    [Fact]
    public void Vscode_packages_flow_through_plan()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Vscode = new Dictionary<string, PackageSpec>
                {
                    ["ms-python.python"] = new(),
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(1);
        plan[0].Manager.ShouldBe(ManagerKind.VsCode);
        plan[0].Id.ShouldBe("ms-python.python");
    }

    [Fact]
    public void Cargo_packages_flow_through_plan()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Cargo = new Dictionary<string, PackageSpec>
                {
                    ["ripgrep"] = new(),
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(1);
        plan[0].Manager.ShouldBe(ManagerKind.Cargo);
        plan[0].Id.ShouldBe("ripgrep");
    }

    [Fact]
    public void Nvm_packages_flow_through_plan()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Nvm = new Dictionary<string, PackageSpec>
                {
                    ["20"] = new(),
                    ["lts/iron"] = new(),
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;
        plan.Count.ShouldBe(2);
        plan.ShouldAllBe(p => p.Manager == ManagerKind.Nvm);
        plan.Select(p => p.Id).ShouldBe(new[] { "20", "lts/iron" }, ignoreOrder: true);
    }

    [Fact]
    public void Topo_sort_orders_dependencies_across_manager_kinds()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Script = new Dictionary<string, PackageSpec>
                {
                    ["nvm"] = new() { Check = "test", Install = "echo" },
                },
                Nvm = new Dictionary<string, PackageSpec>
                {
                    ["20"] = new() { Dependencies = new List<string> { "nvm" } },
                },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages.ToList();
        var scriptIdx = plan.FindIndex(p => p.Manager == ManagerKind.Script && p.Id == "nvm");
        var nvmIdx = plan.FindIndex(p => p.Manager == ManagerKind.Nvm && p.Id == "20");
        scriptIdx.ShouldBeGreaterThanOrEqualTo(0);
        nvmIdx.ShouldBeGreaterThanOrEqualTo(0);
        scriptIdx.ShouldBeLessThan(nvmIdx);
    }

    [Fact]
    public void Apt_sources_flow_through_plan_in_first_seen_order()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["a"] = new()
            {
                Platform = "linux",
                AptSources = new Dictionary<string, AptSource>
                {
                    ["docker"] = new()
                    {
                        KeyUrl = "https://download.docker.com/linux/ubuntu/gpg",
                        Uri = "https://download.docker.com/linux/ubuntu",
                        Components = "stable",
                    },
                },
            },
            ["b"] = new()
            {
                Platform = "linux",
                AptSources = new Dictionary<string, AptSource>
                {
                    ["hashicorp"] = new()
                    {
                        KeyUrl = "https://apt.releases.hashicorp.com/gpg",
                        Uri = "https://apt.releases.hashicorp.com",
                    },
                    ["docker"] = new()
                    {
                        KeyUrl = "https://download.docker.com/linux/ubuntu/gpg",
                        Uri = "https://download.docker.com/linux/ubuntu",
                        Components = "edge",
                    },
                },
            },
            ["windows-only"] = new()
            {
                Platform = "windows",
                AptSources = new Dictionary<string, AptSource>
                {
                    ["should-not-appear"] = new() { KeyUrl = "x", Uri = "y" },
                },
            },
        });

        var plan = Planner.Plan(config, Linux);

        plan.AptSources.Select(s => s.Name).ShouldBe(new[] { "docker", "hashicorp" });
        plan.AptSources.First(s => s.Name == "docker").Source.Components.ShouldBe("edge");
    }

    [Fact]
    public void Requires_marked_satisfied_when_binary_resolves_on_path()
    {
        var prev = PathLookup.Probe;
        PathLookup.Probe = name => name == "code";
        try
        {
            var config = new ConfigFile(new Dictionary<string, Block>
            {
                ["vscode"] = new()
                {
                    Platform = "all",
                    Requires = new List<string> { "code" },
                    Vscode = new Dictionary<string, PackageSpec> { ["ms-python.python"] = new() },
                },
            });

            var plan = Planner.Plan(config, Linux);
            plan.Requirements.Count.ShouldBe(1);
            plan.Requirements[0].Name.ShouldBe("code");
            plan.Requirements[0].BlockName.ShouldBe("vscode");
            plan.Requirements[0].Satisfied.ShouldBeTrue();
        }
        finally
        {
            PathLookup.Probe = prev;
        }
    }

    [Fact]
    public void Requires_marked_unsatisfied_when_binary_missing()
    {
        var prev = PathLookup.Probe;
        PathLookup.Probe = _ => false;
        try
        {
            var config = new ConfigFile(new Dictionary<string, Block>
            {
                ["vscode"] = new()
                {
                    Platform = "all",
                    Requires = new List<string> { "code" },
                    Vscode = new Dictionary<string, PackageSpec> { ["ms-python.python"] = new() },
                },
            });

            var plan = Planner.Plan(config, Linux);
            plan.Requirements.Count.ShouldBe(1);
            plan.Requirements[0].Satisfied.ShouldBeFalse();
        }
        finally
        {
            PathLookup.Probe = prev;
        }
    }

    [Fact]
    public void Requires_only_collected_from_matching_blocks()
    {
        var prev = PathLookup.Probe;
        PathLookup.Probe = _ => true;
        try
        {
            var config = new ConfigFile(new Dictionary<string, Block>
            {
                ["linux-block"] = new()
                {
                    Platform = "linux",
                    Requires = new List<string> { "code" },
                },
                ["windows-block"] = new()
                {
                    Platform = "windows",
                    Requires = new List<string> { "should-not-appear" },
                },
            });

            var plan = Planner.Plan(config, Linux);
            plan.Requirements.Select(r => r.Name).ShouldBe(new[] { "code" });
        }
        finally
        {
            PathLookup.Probe = prev;
        }
    }

    [Fact]
    public void Ppas_empty_when_no_blocks_declare_them()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["linux"] = new()
            {
                Platform = "linux",
                Apt = new Dictionary<string, PackageSpec> { ["curl"] = new() },
            },
        });

        var plan = Planner.Plan(config, Linux);
        plan.AptPpas.Count.ShouldBe(0);
        plan.AptSources.Count.ShouldBe(0);
    }

    [Fact]
    public void Flattens_browser_extension_sections_into_their_manager_kinds()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["browsers"] = new()
            {
                Platform = "linux",
                Firefox = new Dictionary<string, PackageSpec> { ["uBlock0@raymondhill.net"] = new() },
                Zen = new Dictionary<string, PackageSpec> { ["uBlock0@raymondhill.net"] = new() },
                Chrome = new Dictionary<string, PackageSpec> { ["cjpalhdlnbpafiamejdnhcphjbkeiagm"] = new() },
                Chromium = new Dictionary<string, PackageSpec> { ["cjpalhdlnbpafiamejdnhcphjbkeiagm"] = new() },
                Brave = new Dictionary<string, PackageSpec> { ["cjpalhdlnbpafiamejdnhcphjbkeiagm"] = new() },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;

        plan.Select(p => p.Manager).ShouldBe(
            new[]
            {
                ManagerKind.Firefox, ManagerKind.Zen, ManagerKind.Chrome,
                ManagerKind.Chromium, ManagerKind.Brave,
            },
            ignoreOrder: true);
    }

    [Fact]
    public void Same_extension_in_two_browsers_does_not_collide()
    {
        var config = new ConfigFile(new Dictionary<string, Block>
        {
            ["browsers"] = new()
            {
                Platform = "linux",
                Firefox = new Dictionary<string, PackageSpec> { ["uBlock0@raymondhill.net"] = new() },
                Zen = new Dictionary<string, PackageSpec> { ["uBlock0@raymondhill.net"] = new() },
            },
        });

        var plan = Planner.Plan(config, Linux).Packages;

        // Distinct ManagerKinds keep the (kind, id) keys separate, so both survive.
        plan.Count.ShouldBe(2);
        plan.ShouldContain(p => p.Manager == ManagerKind.Firefox);
        plan.ShouldContain(p => p.Manager == ManagerKind.Zen);
    }
}
