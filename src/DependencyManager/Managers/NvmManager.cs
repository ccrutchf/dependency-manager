using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class NvmManager : IPackageManager
{
    private const string NvmShell = """
        if [ -z "$NVM_DIR" ]; then export NVM_DIR="$HOME/.nvm"; fi
        if [ ! -s "$NVM_DIR/nvm.sh" ]; then echo "nvm.sh not found in $NVM_DIR" >&2; exit 1; fi
        . "$NVM_DIR/nvm.sh" --no-use
        nvm "$@"
        """;

    public ManagerKind Kind => ManagerKind.Nvm;

    public bool IsAvailable() => File.Exists(NvmScriptPath());

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var version = pkg.Spec.Name ?? pkg.Id;
        var result = await RunNvmAsync(["version", version], ct);
        if (result.ExitCode != 0) return false;
        var resolved = result.StdOut.Trim();
        return resolved.Length > 0 && resolved != "N/A";
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var version = pkg.Spec.Name ?? pkg.Id;
        var result = await RunNvmAsync(["install", version], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"nvm install {version} failed: {result.StdErr.Trim()}");
    }

    public Task UpdateAllAsync(CancellationToken ct) => Task.CompletedTask;

    private static string NvmDir() =>
        Environment.GetEnvironmentVariable("NVM_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".nvm");

    private static string NvmScriptPath() => Path.Combine(NvmDir(), "nvm.sh");

    private static Task<ProcessResult> RunNvmAsync(IReadOnlyList<string> nvmArgs, CancellationToken ct)
    {
        var args = new List<string> { "-c", NvmShell, "depend-nvm" };
        args.AddRange(nvmArgs);
        return ProcessRunner.RunAsync("bash", args, ct);
    }
}
