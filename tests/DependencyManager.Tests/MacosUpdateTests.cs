using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class MacosUpdateTests
{
    [Fact]
    public void Plan_installs_all_software_updates_via_sudo()
    {
        var steps = MacosUpdate.Plan();

        steps.Count.ShouldBe(1);
        steps[0].Command.ShouldBe("softwareupdate");
        steps[0].Args.ShouldBe(["--install", "--all"]);
        steps[0].Sudo.ShouldBeTrue();
    }

    [Fact]
    public void Plan_appends_restart_when_includeRestart_is_true()
    {
        var steps = MacosUpdate.Plan(includeRestart: true);

        steps.Count.ShouldBe(1);
        steps[0].Args.ShouldBe(["--install", "--all", "--restart"]);
        steps[0].Sudo.ShouldBeTrue();
    }

    [Fact]
    public void ParsePendingUpdates_returns_empty_when_no_updates_available()
    {
        var output = """
            Software Update Tool

            Finding available software
            No new software available.
            """;

        MacosUpdate.ParsePendingUpdates(output).ShouldBeEmpty();
    }

    [Fact]
    public void ParsePendingUpdates_returns_empty_for_empty_input()
    {
        MacosUpdate.ParsePendingUpdates(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void ParsePendingUpdates_returns_labels_when_updates_pending()
    {
        var output = """
            Software Update Tool

            Finding available software
            Software Update found the following new or updated software:
            * Label: Command Line Tools for Xcode 26.5-26.5
            	Title: Command Line Tools for Xcode 26.5, Version: 26.5, Size: 920416KiB, Recommended: YES,
            * Label: macOS Tahoe 26.5.1-25F80
            	Title: macOS Tahoe 26.5.1, Version: 26.5.1, Size: 3691689KiB, Recommended: YES, Action: restart,
            """;

        MacosUpdate.ParsePendingUpdates(output).ShouldBe([
            "Command Line Tools for Xcode 26.5-26.5",
            "macOS Tahoe 26.5.1-25F80",
        ]);
    }

    [Fact]
    public void ParsePendingUpdates_trims_trailing_whitespace_from_labels()
    {
        // softwareupdate has historically emitted trailing whitespace on the Label line.
        MacosUpdate.ParsePendingUpdates("* Label: foo  \r\n").ShouldBe(["foo"]);
    }

    [Fact]
    public void ParsePendingUpdates_handles_crlf_line_endings()
    {
        var output = "* Label: a\r\n* Label: b\r\n";
        MacosUpdate.ParsePendingUpdates(output).ShouldBe(["a", "b"]);
    }
}
