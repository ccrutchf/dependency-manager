using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class FlatpakManager : IPackageManager
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
