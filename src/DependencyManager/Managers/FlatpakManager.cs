using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class FlatpakManager : IPrunableManager
{
    private readonly bool _userScope;
    private readonly DownloadCache _cache;
    private readonly HashSet<bool> _remoteAdded = new();

    public FlatpakManager() : this(userScope: false) { }

    public FlatpakManager(bool userScope, DownloadCache? cache = null)
    {
        _userScope = userScope;
        _cache = cache ?? new DownloadCache(DownloadCache.DefaultPath(DownloadCache.FlatpaksFile));
    }

    public ManagerKind Kind => ManagerKind.Flatpak;

    public bool IsAvailable() => OperatingSystem.IsLinux() && ProcessRunner.OnPath("flatpak");

    public Task BootstrapAsync(CancellationToken ct) => EnsureFlathubAsync(_userScope, ct);

    /// <summary>An entry with a <c>url:</c> installs from a downloaded <c>.flatpak</c> bundle, not a remote.</summary>
    public static bool IsBundle(PackageSpec spec) => !string.IsNullOrWhiteSpace(spec.Url);

    public static string[] RemoteInstallArgs(PackageSpec spec, string id, bool userScope) =>
        ["install", ScopeFlag(userScope), "-y", spec.Source ?? "flathub", id];

    // No --reinstall: installing a different commit over an existing install is
    // flatpak's normal upgrade/downgrade path and keeps ~/.var/app/<id> (checked on
    // 1.14 and 1.18). Installing the *same* commit fails on 1.14 even with
    // --reinstall, so InstallBundleAsync skips that case instead.
    public static string[] BundleInstallArgs(string bundlePath, bool userScope) =>
        ["install", ScopeFlag(userScope), "-y", "--bundle", bundlePath];

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("flatpak", ["list", "--columns=application"], ct);
        if (result.ExitCode != 0) return false;
        var present = result.StdOut.Split('\n')
            .Any(line => string.Equals(line.Trim(), pkg.Id, StringComparison.OrdinalIgnoreCase));
        if (!present || !IsBundle(pkg.Spec)) return present;

        // A bundle is current only if it came from this url (and sha256), so
        // changing either in the config reinstalls it.
        var entries = await _cache.LoadAsync(ct);
        return DownloadCache.IsCurrent(entries, pkg.Spec.Url!, pkg.Spec.Sha256, pkg.Id);
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var userScope = pkg.Spec.UserScope ?? _userScope;
        // Also needed for bundles: their runtime is pulled from Flathub.
        await EnsureFlathubAsync(userScope, ct);

        if (IsBundle(pkg.Spec))
        {
            await InstallBundleAsync(pkg, userScope, ct);
            return;
        }

        var remote = pkg.Spec.Source ?? "flathub";
        var result = await RunScopedAsync(userScope, RemoteInstallArgs(pkg.Spec, pkg.Id, userScope), ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"flatpak install {remote} {pkg.Id} failed: {result.StdErr.Trim()}");
    }

    private async Task InstallBundleAsync(ResolvedPackage pkg, bool userScope, CancellationToken ct)
    {
        var url = pkg.Spec.Url!;
        var tempPath = Downloads.TempPath(".flatpak");
        try
        {
            var sha256 = await Downloads.DownloadVerifiedAsync(url, tempPath, pkg.Spec.Sha256, ct);

            FlatpakBundleHeader header;
            try
            {
                await using var stream = File.OpenRead(tempPath);
                header = FlatpakBundle.ReadHeader(stream);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException($"{url}: {ex.Message}");
            }
            FlatpakBundle.EnsureAppId(header, pkg.Id, url);

            if (await InstalledCommitAsync(header.Id, userScope, ct) != header.Commit)
            {
                var remotesBefore = await ListRemotesAsync(userScope, ct);
                var result = await RunScopedAsync(userScope, BundleInstallArgs(tempPath, userScope), ct);
                if (result.ExitCode != 0)
                {
                    await RemoveAddedRemotesAsync(remotesBefore, userScope, ct);
                    throw new InvalidOperationException(
                        $"flatpak install --bundle {url} failed: {result.StdErr.Trim()}");
                }
            }

            await _cache.RecordAsync(url, sha256, header.Id, ct);
        }
        finally
        {
            Downloads.TryDelete(tempPath);
        }
    }

    public static IReadOnlyList<string> ParseRemoteNames(string stdout) =>
        stdout.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public static IReadOnlyList<string> AddedRemotes(IEnumerable<string> before, IEnumerable<string> after) =>
        after.Except(before, StringComparer.Ordinal).ToList();

    private static async Task<IReadOnlyList<string>?> ListRemotesAsync(bool userScope, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(
            "flatpak", ["remotes", ScopeFlag(userScope), "--show-disabled", "--columns=name"], ct);
        return result.ExitCode == 0 ? ParseRemoteNames(result.StdOut) : null;
    }

    // A failed bundle install leaves the disabled <name>-origin remote it created
    // (flatpak only removes an origin remote when its last ref is uninstalled), and
    // every retry adds another <name>N-origin. Delete just the ones this attempt
    // added; without --force, remote-delete refuses any remote with refs installed.
    private static async Task RemoveAddedRemotesAsync(IReadOnlyList<string>? before, bool userScope, CancellationToken ct)
    {
        var after = await ListRemotesAsync(userScope, ct);
        if (before is null || after is null) return;
        foreach (var remote in AddedRemotes(before, after))
            await RunScopedAsync(userScope, ["remote-delete", ScopeFlag(userScope), remote], ct);
    }

    private static async Task<string?> InstalledCommitAsync(string id, bool userScope, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("flatpak", ["info", ScopeFlag(userScope), "--show-commit", id], ct);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
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

    private static string ScopeFlag(bool userScope) => userScope ? "--user" : "--system";

    private static Task<ProcessResult> RunScopedAsync(bool userScope, string[] args, CancellationToken ct) =>
        userScope
            ? ProcessRunner.RunAsync("flatpak", args, ct)
            : Sudo.RunAsync("flatpak", args, ct);
}
