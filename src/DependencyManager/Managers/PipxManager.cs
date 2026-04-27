using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class PipxManager : IPackageManager
{
    private readonly bool _userScope;

    public PipxManager() : this(userScope: true) { }

    public PipxManager(bool userScope) => _userScope = userScope;

    public ManagerKind Kind => ManagerKind.Pipx;

    public bool IsAvailable() => ProcessRunner.OnPath("pipx");

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("pipx", ["list", "--short"], ct);
        if (result.ExitCode != 0) return false;
        foreach (var line in result.StdOut.Split('\n'))
        {
            var name = line.Trim().Split(' ', 2)[0];
            if (string.Equals(name, pkg.Id, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var userScope = pkg.Spec.UserScope ?? _userScope;
        var args = new List<string> { "install" };
        if (!userScope) args.Add("--global");
        args.Add(pkg.Spec.Url ?? pkg.Id);

        var result = userScope
            ? await ProcessRunner.RunAsync("pipx", args, ct)
            : await Sudo.RunAsync("pipx", args, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"pipx install {pkg.Id} failed: {result.StdErr.Trim()}");
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var args = new List<string> { "upgrade-all" };
        if (!_userScope) args.Add("--global");

        var result = _userScope
            ? await ProcessRunner.RunAsync("pipx", args, ct)
            : await Sudo.RunAsync("pipx", args, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"pipx upgrade-all failed: {result.StdErr.Trim()}");
    }
}
