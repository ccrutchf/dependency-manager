using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class PipManager : IPackageManager
{
    private readonly bool _userScope;

    public PipManager() : this(userScope: false) { }

    public PipManager(bool userScope) => _userScope = userScope;

    public ManagerKind Kind => ManagerKind.Pip;

    public bool IsAvailable() => ResolveBinary() is not null;

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var binary = RequireBinary();
        var result = await ProcessRunner.RunAsync(binary, ["show", pkg.Id], ct);
        return result.ExitCode == 0;
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var binary = RequireBinary();
        var userScope = pkg.Spec.UserScope ?? _userScope;
        var args = new List<string> { "install" };
        if (userScope) args.Add("--user");
        args.Add(pkg.Id);

        var result = userScope
            ? await ProcessRunner.RunAsync(binary, args, ct)
            : await Sudo.RunAsync(binary, args, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"pip install {pkg.Id} failed: {result.StdErr.Trim()}");
    }

    public Task UpdateAllAsync(CancellationToken ct)
    {
        Console.WriteLine("  [skip]    pip       no bulk update (pip has no reliable upgrade-all)");
        return Task.CompletedTask;
    }

    private static string? ResolveBinary()
    {
        if (ProcessRunner.OnPath("pip3")) return "pip3";
        if (ProcessRunner.OnPath("pip")) return "pip";
        return null;
    }

    private static string RequireBinary() =>
        ResolveBinary() ?? throw new InvalidOperationException("pip is not installed or not on PATH");
}
