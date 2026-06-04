using System.Diagnostics;

namespace DependencyManager.Util;

public static class Sudo
{
    public static Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken ct = default)
    {
        if (RootCheck.IsRoot())
            return ProcessRunner.RunAsync(fileName, arguments, ct);

        var wrapped = new List<string> { fileName };
        wrapped.AddRange(arguments);
        return ProcessRunner.RunAsync("sudo", wrapped, ct);
    }

    public static Task<int> RunStreamingAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken ct = default)
    {
        if (RootCheck.IsRoot())
            return ProcessRunner.RunStreamingAsync(fileName, arguments, ct);

        var wrapped = new List<string> { fileName };
        wrapped.AddRange(arguments);
        return ProcessRunner.RunStreamingAsync("sudo", wrapped, ct);
    }

    public static async Task<bool> PrimeAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-v");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start sudo");
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0;
    }
}
