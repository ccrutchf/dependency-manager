using System.Diagnostics;

namespace DependencyManager.Util;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new ProcessResult(
            process.ExitCode,
            await stdOutTask,
            await stdErrTask);
    }

    /// <summary>
    /// Inherits the parent's stdio so child output streams live to the terminal.
    /// Use for long-running, chatty commands (system updaters) where capturing
    /// silently is worse UX than showing progress. Returns the exit code only —
    /// the user has already seen any error text on screen.
    /// </summary>
    public static async Task<int> RunStreamingAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    public static bool OnPath(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return true;
        }
        return false;
    }
}
