using System.Runtime.InteropServices;
using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

/// <summary>
/// Installs browser extensions by writing enterprise-policy files. Browsers
/// ship no extension-install CLI (unlike VS Code), so the supported mechanism
/// is a managed policy that declares each extension. One instance handles one
/// browser; the per-browser paths and policy family come from <see cref="BrowserCatalog"/>.
///
/// The depend process always runs as the normal user (install refuses root) and
/// escalates individual writes through sudo, so per-user flatpak policy files are
/// written directly and system paths (/etc, &lt;install&gt;/distribution, /var/snap,
/// /var/lib/flatpak) go through <c>sudo install</c>.
/// </summary>
public sealed class BrowserExtensionManager : IPackageManager
{
    private static readonly string[] GenericBinDirs =
        ["/usr/bin", "/bin", "/usr/local/bin", "/sbin", "/usr/sbin", "/usr/local/sbin"];

    private readonly ManagerKind _kind;

    public BrowserExtensionManager(ManagerKind kind) => _kind = kind;

    public ManagerKind Kind => _kind;

    public bool IsAvailable()
    {
        if (!OperatingSystem.IsLinux()) return false;
        var spec = Spec();
        if (NativePresent(spec)) return true;
        if (spec.SnapName is not null && Directory.Exists($"/var/snap/{spec.SnapName}")) return true;
        if (spec.FlatpakAppId is not null)
        {
            if (Directory.Exists($"/var/lib/flatpak/app/{spec.FlatpakAppId}")) return true;
            if (Directory.Exists(Path.Combine(Home(), ".local/share/flatpak/app", spec.FlatpakAppId))) return true;
        }
        return false;
    }

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var spec = Spec();
        var resolved = BrowserPolicy.ResolveExtension(spec.Family, pkg.Spec);
        if (!resolved.Valid) return Task.FromResult(false);

        var targets = ResolveTargets(spec);
        if (targets.Count == 0) return Task.FromResult(false);

        foreach (var target in targets)
        {
            if (!TargetHasExtension(spec, target, pkg, resolved))
                return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var spec = Spec();
        var resolved = BrowserPolicy.ResolveExtension(spec.Family, pkg.Spec);
        if (!resolved.Valid)
            throw new InvalidOperationException(
                $"{_kind} extension '{pkg.Id}' with mode '{resolved.Mode}' needs an install url; set 'url:' (xpi) or 'source:' (AMO slug)");

        var targets = ResolveTargets(spec);
        if (targets.Count == 0)
            throw new InvalidOperationException(
                $"{_kind} is installed but no writable policy location was found");

        var failures = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                var (dest, content) = Render(spec, target, pkg, resolved);
                await WriteAsync(dest, content, target.RequiresRoot, ct);
            }
            catch (Exception ex)
            {
                failures.Add($"{target.Path}: {ex.Message}");
            }
        }

        if (failures.Count == targets.Count)
            throw new InvalidOperationException(
                $"failed to write policy to all {targets.Count} location(s): {string.Join("; ", failures)}");
        if (failures.Count > 0)
            Console.Error.WriteLine(
                $"  [warn]    {_kind} extension '{pkg.Id}' written to {targets.Count - failures.Count}/{targets.Count} location(s); skipped: {string.Join("; ", failures)}");
    }

    public Task UpdateAllAsync(CancellationToken ct)
    {
        Console.WriteLine(
            $"  [skip]    {_kind.ToString().ToLowerInvariant(),-8}  extensions are policy-managed; the browser auto-updates them");
        return Task.CompletedTask;
    }

    private BrowserSpec Spec() => BrowserCatalog.For(_kind, FlatpakArch(), Home());

    private List<PolicyTarget> ResolveTargets(BrowserSpec spec)
    {
        var targets = new List<PolicyTarget>();

        if (NativePresent(spec))
        {
            foreach (var target in spec.NativeTargets)
            {
                if (target.RequiresDir is null || Directory.Exists(target.RequiresDir))
                    targets.Add(target);
            }
            if (spec.Family == BrowserPolicyFamily.Firefox)
                targets.AddRange(ResolveFirefoxBinaryTargets(spec));
        }

        if (spec.SnapName is not null && Directory.Exists($"/var/snap/{spec.SnapName}"))
            targets.AddRange(spec.SnapTargets);

        if (spec.FlatpakAppId is not null)
        {
            var arch = FlatpakArch();
            if (Directory.Exists($"/var/lib/flatpak/app/{spec.FlatpakAppId}"))
                targets.Add(BrowserCatalog.FlatpakTarget(spec, "/var/lib/flatpak", arch, requiresRoot: true));

            var userFlatpak = Path.Combine(Home(), ".local/share/flatpak");
            if (Directory.Exists(Path.Combine(userFlatpak, "app", spec.FlatpakAppId)))
                targets.Add(BrowserCatalog.FlatpakTarget(spec, userFlatpak, arch, requiresRoot: false));
        }

        return targets
            .GroupBy(t => t.Path, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    // For Firefox-family browsers the canonical location is a distribution/ folder
    // next to the real binary. Resolving the binary (through symlinks) makes Zen
    // work regardless of where it was unpacked. Wrapper scripts that live directly
    // in a bin dir are skipped so we never create a stray <bindir>/distribution.
    private static IEnumerable<PolicyTarget> ResolveFirefoxBinaryTargets(BrowserSpec spec)
    {
        foreach (var binary in spec.Binaries)
        {
            var resolved = ResolveExecutable(binary);
            if (resolved is null) continue;

            var dir = Path.GetDirectoryName(ResolveSymlink(resolved));
            if (string.IsNullOrEmpty(dir) || IsGenericBinDir(dir)) continue;

            yield return new PolicyTarget(Path.Combine(dir, "distribution", "policies.json"), RequiresRoot: true);
        }
    }

    private (string Dest, string Content) Render(BrowserSpec spec, PolicyTarget target, ResolvedPackage pkg, BrowserPolicy.ResolvedExtension resolved)
    {
        if (spec.Family == BrowserPolicyFamily.Chromium)
        {
            var dest = Path.Combine(target.Path, BrowserPolicy.ChromiumFileName(pkg.Id));
            return (dest, BrowserPolicy.BuildChromiumExtensionFile(pkg.Id, resolved.Mode, resolved.Url));
        }

        var existing = TryReadFile(target.Path);
        return (target.Path, BrowserPolicy.MergeFirefoxPolicies(existing, pkg.Id, resolved.Mode, resolved.Url));
    }

    private static bool TargetHasExtension(BrowserSpec spec, PolicyTarget target, ResolvedPackage pkg, BrowserPolicy.ResolvedExtension resolved)
    {
        if (spec.Family == BrowserPolicyFamily.Chromium)
        {
            var file = Path.Combine(target.Path, BrowserPolicy.ChromiumFileName(pkg.Id));
            return BrowserPolicy.ChromiumFileHasExtension(TryReadFile(file), pkg.Id, resolved.Mode, resolved.Url);
        }
        return BrowserPolicy.FirefoxPoliciesHasExtension(TryReadFile(target.Path), pkg.Id, resolved.Mode, resolved.Url);
    }

    private static async Task WriteAsync(string dest, string content, bool requiresRoot, CancellationToken ct)
    {
        if (requiresRoot)
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"depend-policy-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(tmp, content, ct);
            try
            {
                var result = await Sudo.RunAsync("install", ["-D", "-m", "0644", tmp, dest], ct);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException(result.StdErr.Trim());
            }
            finally
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }
        }
        else
        {
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(dest, content, ct);
        }
    }

    private bool NativePresent(BrowserSpec spec) => spec.Binaries.Any(PathLookup.Exists);

    private static string? TryReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? ResolveExecutable(string nameOrPath)
    {
        if (nameOrPath.Contains('/'))
            return File.Exists(nameOrPath) ? nameOrPath : null;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, nameOrPath);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string ResolveSymlink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException) { return path; }
        catch (UnauthorizedAccessException) { return path; }
    }

    private static bool IsGenericBinDir(string dir) =>
        GenericBinDirs.Contains(dir.TrimEnd('/'), StringComparer.Ordinal);

    private static string FlatpakArch() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "aarch64",
        Architecture.X64 => "x86_64",
        _ => "x86_64",
    };

    private static string Home() =>
        Environment.GetEnvironmentVariable("HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
