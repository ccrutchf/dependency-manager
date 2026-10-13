using DependencyManager.Commands;
using DependencyManager.Config;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class PlanCommandTests
{
    [Fact]
    public void Package_line_shows_the_url_for_url_backed_entries()
    {
        var pkg = new ResolvedPackage(
            ManagerKind.Flatpak,
            "io.github.crealityofficial.CrealityPrint",
            new PackageSpec { Url = "https://example.com/CrealityPrint.flatpak" },
            "linux-desktop-flatpaks");

        var line = PlanCommand.FormatPackage(pkg);

        line.ShouldContain("io.github.crealityofficial.CrealityPrint");
        line.ShouldContain("url=https://example.com/CrealityPrint.flatpak");
    }

    [Fact]
    public void Package_line_shows_the_path_for_vsix_entries()
    {
        var pkg = new ResolvedPackage(
            ManagerKind.VsCode,
            "phonedash.phone-dash-focus",
            new PackageSpec { Url = "~/.local/share/phone-dash/phone-dash-focus.vsix" },
            "kairos-vscode");

        PlanCommand.FormatPackage(pkg).ShouldContain("url=~/.local/share/phone-dash/phone-dash-focus.vsix");
    }

    [Fact]
    public void Package_line_has_no_url_for_remote_entries()
    {
        var pkg = new ResolvedPackage(ManagerKind.Flatpak, "org.gimp.GIMP", new PackageSpec(), "b");

        PlanCommand.FormatPackage(pkg).ShouldNotContain("url=");
    }
}
