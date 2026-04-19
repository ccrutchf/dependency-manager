using System.Diagnostics;
using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class ScriptManager : IPackageManager
{
    public ManagerKind Kind => ManagerKind.Script;

    public bool IsAvailable() => File.Exists("/bin/sh");

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pkg.Spec.Check)) return false;
        var result = await RunShellAsync(pkg.Spec.Check, streamToConsole: false, ct);
        return result.ExitCode == 0;
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var install = pkg.Spec.Install
            ?? throw new InvalidOperationException($"script package {pkg.Id} is missing 'install'");

        var result = await RunShellAsync(install, streamToConsole: true, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"script {pkg.Id} exited {result.ExitCode}: {result.StdErr.Trim()}");
    }

    private static async Task<ProcessResult> RunShellAsync(
        string command,
        bool streamToConsole,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start /bin/sh");

        var stdOut = streamToConsole
            ? TeeAsync(process.StandardOutput, Console.Out, ct)
            : process.StandardOutput.ReadToEndAsync(ct);
        var stdErr = streamToConsole
            ? TeeAsync(process.StandardError, Console.Error, ct)
            : process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        return new ProcessResult(process.ExitCode, await stdOut, await stdErr);
    }

    private static async Task<string> TeeAsync(
        TextReader source,
        TextWriter sink,
        CancellationToken ct)
    {
        var buffer = new char[4096];
        var captured = new System.Text.StringBuilder();
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            await sink.WriteAsync(buffer.AsMemory(0, read), ct);
            captured.Append(buffer, 0, read);
        }
        return captured.ToString();
    }
}
