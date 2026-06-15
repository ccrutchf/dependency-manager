using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class FlatpakManager : IPrunableManager
{
    private readonly bool _userScope;
    private readonly HashSet<bool> _remoteAdded = new();

    public FlatpakManager() : this(userScope: false) { }

    public FlatpakManager(bool userScope) => _userScope = userScope;

    public ManagerKind Kind => ManagerKind.Flatpak;

    public bool IsAvailable() => OperatingSystem.IsLinux() && ProcessRunner.OnPath("flatpak");

    public Task BootstrapAsync(CancellationToken ct) => EnsureFlathubAsync(_userScope, ct);

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("flatpak", ["list", "--columns=application"], ct);
        if (result.ExitCode != 0) return false;
        foreach (var line in result.StdOut.Split('\n'))
        {
            if (string.Equals(line.Trim(), pkg.Id, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var userScope = pkg.Spec.UserScope ?? _userScope;
        await EnsureFlathubAsync(userScope, ct);

        var remote = pkg.Spec.Source ?? "flathub";
        var scopeFlag = userScope ? "--user" : "--system";
        var args = new[] { "install", scopeFlag, "-y", remote, pkg.Id };
        var result = userScope
            ? await ProcessRunner.RunAsync("flatpak", args, ct)
            : await Sudo.RunAsync("flatpak", args, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"flatpak install {remote} {pkg.Id} failed: {result.StdErr.Trim()}");
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var scopeFlag = _userScope ? "--user" : "--system";
        var args = new[] { "update", scopeFlag, "-y" };
        var result = _userScope
            ? await ProcessRunner.RunAsync("flatpak", args, ct)
            : await Sudo.RunAsync("flatpak", args, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"flatpak update {scopeFlag} failed: {result.StdErr.Trim()}");
    }

    // --- prune ---------------------------------------------------------------

    public PrunePolicy MaxPolicy => PrunePolicy.Zap;

    public async Task<IReadOnlyList<string>> ListExplicitAsync(CancellationToken ct)
    {
        // Apps only (--app), never runtimes — runtimes are dependencies. Scope to
        // the manager's own scope so a user-scope run never lists system apps.
        var scopeFlag = _userScope ? "--user" : "--system";
        var ids = new List<string>();
        var result = await ProcessRunner.RunAsync("flatpak", ["list", "--app", scopeFlag, "--columns=application"], ct);
        if (result.ExitCode != 0) return ids;
        foreach (var line in result.StdOut.Split('\n'))
        {
            var id = line.Trim();
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }

    public async Task UninstallAsync(string id, CancellationToken ct)
    {
        var scopeFlag = _userScope ? "--user" : "--system";
        var args = new[] { "uninstall", scopeFlag, "-y", id };
        var result = _userScope
            ? await ProcessRunner.RunAsync("flatpak", args, ct)
            : await Sudo.RunAsync("flatpak", args, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"flatpak uninstall {id} failed: {result.StdErr.Trim()}");
    }

    public async Task CollectGarbageAsync(CancellationToken ct)
    {
        var scopeFlag = _userScope ? "--user" : "--system";
        var args = new[] { "uninstall", scopeFlag, "--unused", "-y" };
        _ = _userScope
            ? await ProcessRunner.RunAsync("flatpak", args, ct)
            : await Sudo.RunAsync("flatpak", args, ct);
    }

    private async Task EnsureFlathubAsync(bool userScope, CancellationToken ct)
    {
        if (!_remoteAdded.Add(userScope)) return;
        var scopeFlag = userScope ? "--user" : "--system";
        var args = new[] { "remote-add", scopeFlag, "--if-not-exists", "flathub", "https://flathub.org/repo/flathub.flatpakrepo" };
        var result = userScope
            ? await ProcessRunner.RunAsync("flatpak", args, ct)
            : await Sudo.RunAsync("flatpak", args, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"flatpak remote-add flathub failed: {result.StdErr.Trim()}");
    }
}
