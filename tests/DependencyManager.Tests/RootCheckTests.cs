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
            Array.Empty<string>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeFalse();
    }

    [Fact]
    public void Pip_package_with_scope_system_requires_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Pip, "httpie", new PackageSpec { Scope = "system" }, "b") },
            Array.Empty<string>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }

    [Fact]
    public void Script_package_does_not_require_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Script, "uv", new PackageSpec { Install = "echo hi" }, "b") },
            Array.Empty<string>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeFalse();
    }

    [Fact]
    public void Flatpak_package_with_scope_system_requires_root()
    {
        var plan = new ResolvedPlan(
            new[] { new ResolvedPackage(ManagerKind.Flatpak, "org.example", new PackageSpec { Scope = "system" }, "b") },
            Array.Empty<string>());

        RootCheck.PlanRequiresSudo(plan).ShouldBeTrue();
    }
}
