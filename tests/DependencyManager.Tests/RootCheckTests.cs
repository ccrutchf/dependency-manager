using DependencyManager.Config;
using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class RootCheckTests
{
    [Fact]
    public void Pip_package_does_not_require_root_by_default()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Pip, "httpie", new PackageSpec(), "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeFalse();
    }

    [Fact]
    public void Pip_package_with_scope_system_requires_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Pip, "httpie", new PackageSpec { Scope = "system" }, "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }

    [Fact]
    public void Script_package_does_not_require_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Script, "uv", new PackageSpec { Install = "echo hi" }, "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeFalse();
    }

    [Fact]
    public void Apt_source_in_plan_requires_root()
    {
        var plan = new ResolvedPlan(
            Array.Empty<ResolvedPackage>(),
            Array.Empty<string>(),
            new[]
            {
                new ResolvedAptSource(
                    "docker",
                    new AptSource { KeyUrl = "https://x/gpg", Uri = "https://x" },
                    "b"),
            },
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }

    [Fact]
    public void Apt_package_requires_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Apt, "curl", new PackageSpec(), "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }

    [Fact]
    public void Snap_package_requires_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Snap, "code", new PackageSpec(), "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }

    [Fact]
    public void Deb_package_requires_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Deb, "slack", new PackageSpec(), "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }

    [Fact]
    public void Apt_ppa_in_plan_requires_root()
    {
        var plan = new ResolvedPlan(
            Array.Empty<ResolvedPackage>(),
            new[] { "ppa:git-core/ppa" },
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }

    [Fact]
    public void Cargo_package_does_not_require_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Cargo, "ripgrep", new PackageSpec(), "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeFalse();
    }

    [Fact]
    public void Nvm_package_does_not_require_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Nvm, "20", new PackageSpec(), "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeFalse();
    }

    [Fact]
    public void Empty_plan_does_not_require_root()
    {
        var plan = new ResolvedPlan(
            Array.Empty<ResolvedPackage>(),
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeFalse();
    }

    [Fact]
    public void Flatpak_package_with_scope_system_requires_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Flatpak, "org.example", new PackageSpec { Scope = "system" }, "b") },
            Array.Empty<string>(),
            Array.Empty<ResolvedAptSource>(),
            Array.Empty<ResolvedRequirement>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }
}
