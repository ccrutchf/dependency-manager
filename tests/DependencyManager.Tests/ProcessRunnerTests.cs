using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class ProcessRunnerTests
{
    // These spawn a real child via /bin/sh — the Unix hosts depend targets (CI is
    // ubuntu + macos). Guard Windows out; it has no /bin/sh. Only shell builtins
    // (printf, while) are used so the tests also pass in the Nix checkPhase sandbox,
    // which provides /bin/sh but not external coreutils (seq/yes/head).

    [Fact]
    public async Task RunAsync_captures_stdout_stderr_and_exit_code()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await ProcessRunner.RunAsync(
            "/bin/sh",
            ["-c", "printf 'hello-out'; printf 'hello-err' 1>&2; exit 3"]);

        result.ExitCode.ShouldBe(3);
        result.StdOut.ShouldBe("hello-out");
        result.StdErr.ShouldBe("hello-err");
    }

    // RunAsync must drain stdout and stderr CONCURRENTLY while the child runs. Here
    // the child writes ~100 KB to each stream — far past the ~64 KB pipe buffer —
    // before it exits. A stdout fd stays open until the process exits, so a reader
    // that drains the streams one-after-the-other (rather than concurrently) would
    // block: the child stalls writing the second stream while the reader still waits
    // on EOF of the first, and neither side can advance. Concurrent draining keeps it
    // flowing and captures everything; a regression to sequential draining deadlocks
    // and trips the timeout.
    [Fact]
    public async Task RunAsync_drains_large_output_on_both_streams_without_deadlock()
    {
        if (OperatingSystem.IsWindows()) return;

        const int lines = 1000; // 1000 * ~101 B ≈ 101 KB per stream, well over the pipe buffer
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var script =
            "big=$(printf '%100d' 1); " +
            $"i=0; while [ $i -lt {lines} ]; do printf '%s\\n' \"$big\"; i=$((i+1)); done; " +
            $"i=0; while [ $i -lt {lines} ]; do printf '%s\\n' \"$big\" 1>&2; i=$((i+1)); done; " +
            "exit 7";

        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", script], cts.Token);

        result.ExitCode.ShouldBe(7);
        LineCount(result.StdOut).ShouldBe(lines);
        LineCount(result.StdErr).ShouldBe(lines);
    }

    private static int LineCount(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
}
