using DependencyManager.Config;
using DependencyManager.Managers;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class FlatpakManagerTests
{
    [Fact]
    public void Entry_without_url_installs_from_flathub_by_default()
    {
        var spec = new PackageSpec();

        FlatpakManager.IsBundle(spec).ShouldBeFalse();
        FlatpakManager.RemoteInstallArgs(spec, "org.gimp.GIMP", userScope: true)
            .ShouldBe(["install", "--user", "-y", "flathub", "org.gimp.GIMP"]);
    }

    [Fact]
    public void Entry_without_url_installs_from_its_source_remote()
    {
        var spec = new PackageSpec { Source = "fedora" };

        FlatpakManager.IsBundle(spec).ShouldBeFalse();
        FlatpakManager.RemoteInstallArgs(spec, "org.gimp.GIMP", userScope: false)
            .ShouldBe(["install", "--system", "-y", "fedora", "org.gimp.GIMP"]);
    }

    [Fact]
    public void Entry_with_url_is_a_bundle()
    {
        FlatpakManager.IsBundle(new PackageSpec { Url = "https://x/app.flatpak" }).ShouldBeTrue();
        FlatpakManager.IsBundle(new PackageSpec { Url = "  " }).ShouldBeFalse();
    }

    [Fact]
    public void Parses_remote_names_from_flatpak_remotes_output()
    {
        FlatpakManager.ParseRemoteNames("flathub\nfoo-origin\n\n  bar-origin  \n")
            .ShouldBe(["flathub", "foo-origin", "bar-origin"]);
    }

    [Fact]
    public void Remotes_added_by_a_failed_attempt_are_only_the_new_ones()
    {
        // A failed `flatpak install --bundle` leaves behind the <name>-origin remote
        // it created (and a new <name>N-origin on each retry); only those get cleaned.
        var before = new[] { "flathub", "creality-origin" };
        var after = new[] { "flathub", "creality-origin", "crealityprint1-origin" };

        FlatpakManager.AddedRemotes(before, after).ShouldBe(["crealityprint1-origin"]);
    }

    [Fact]
    public void Bundle_install_uses_the_bundle_flag_in_the_requested_scope()
    {
        // No --reinstall: a newer/older commit over an existing install is flatpak's
        // normal upgrade path and keeps ~/.var/app data (verified on 1.14 and 1.18).
        FlatpakManager.BundleInstallArgs("/tmp/x.flatpak", userScope: true)
            .ShouldBe(["install", "--user", "-y", "--bundle", "/tmp/x.flatpak"]);
        FlatpakManager.BundleInstallArgs("/tmp/x.flatpak", userScope: false)
            .ShouldBe(["install", "--system", "-y", "--bundle", "/tmp/x.flatpak"]);
    }
}
