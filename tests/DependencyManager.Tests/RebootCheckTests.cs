using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class RebootCheckTests
{
    // --- Debian/Ubuntu: /var/run/reboot-required ---

    [Fact]
    public void Debian_returns_null_when_flag_file_missing()
    {
        var signal = RebootCheck.Debian(
            fileExists: _ => false,
            readText: _ => null);

        signal.ShouldBeNull();
    }

    [Fact]
    public void Debian_returns_signal_when_flag_file_present_without_pkgs_list()
    {
        var signal = RebootCheck.Debian(
            fileExists: p => p == "/var/run/reboot-required",
            readText: _ => null);

        signal.ShouldNotBeNull();
        signal.Source.ShouldBe("apt/dpkg");
    }

    [Fact]
    public void Debian_includes_package_names_in_reason_when_pkgs_list_present()
    {
        var signal = RebootCheck.Debian(
            fileExists: _ => true,
            readText: _ => "linux-image-generic\nsystemd\n");

        signal.ShouldNotBeNull();
        signal.Reason.ShouldContain("linux-image-generic");
        signal.Reason.ShouldContain("systemd");
    }

    [Fact]
    public void Debian_treats_whitespace_only_pkgs_list_as_empty()
    {
        var signal = RebootCheck.Debian(
            fileExists: _ => true,
            readText: _ => "   \n\n");

        signal.ShouldNotBeNull();
        signal.Reason.ShouldNotContain(","); // no joined list when nothing parseable
    }

    // --- NixOS: booted-system vs current-system kernel diff ---

    [Fact]
    public void Nixos_returns_null_when_booted_and_current_kernels_match()
    {
        var signal = RebootCheck.Nixos(
            resolveSymlink: _ => "/nix/store/abc-linux-6.6/bzImage");

        signal.ShouldBeNull();
    }

    [Fact]
    public void Nixos_returns_signal_when_booted_and_current_kernels_differ()
    {
        var signal = RebootCheck.Nixos(
            resolveSymlink: p => p == "/run/booted-system/kernel"
                ? "/nix/store/abc-linux-6.6/bzImage"
                : "/nix/store/def-linux-6.7/bzImage");

        signal.ShouldNotBeNull();
        signal.Source.ShouldBe("nixos");
        signal.Reason.ShouldContain("kernel");
    }

    [Fact]
    public void Nixos_returns_null_when_booted_path_unresolvable()
    {
        var signal = RebootCheck.Nixos(
            resolveSymlink: p => p == "/run/current-system/kernel"
                ? "/nix/store/abc/bzImage"
                : null);

        signal.ShouldBeNull();
    }

    [Fact]
    public void Nixos_returns_null_when_current_path_unresolvable()
    {
        var signal = RebootCheck.Nixos(
            resolveSymlink: p => p == "/run/booted-system/kernel"
                ? "/nix/store/abc/bzImage"
                : null);

        signal.ShouldBeNull();
    }
}
