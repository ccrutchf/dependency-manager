using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class VsCodeManager : IPrunableManager
{
    private const string DefaultBinary = "code";

    private readonly DownloadCache _cache;

    public VsCodeManager(DownloadCache? cache = null)
    {
        _cache = cache ?? new DownloadCache(DownloadCache.DefaultPath(DownloadCache.VsixFile));
    }

    public ManagerKind Kind => ManagerKind.VsCode;

    public bool IsAvailable() => ProcessRunner.OnPath(DefaultBinary);

    public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>An entry with a <c>url:</c> installs from a .vsix file (http(s), file://, absolute or ~/ path).</summary>
    public static bool IsVsix(PackageSpec spec) => !string.IsNullOrWhiteSpace(spec.Url);

    public static string Binary(PackageSpec spec) => spec.Source ?? DefaultBinary;

    /// <summary>Args for a Marketplace id or a .vsix path; `code` tells them apart by the .vsix suffix.</summary>
    public static string[] InstallArgs(string extension) => ["--install-extension", extension, "--force"];

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(Binary(pkg.Spec), ["--list-extensions"], ct);
        if (result.ExitCode != 0) return false;
        var listed = VsCodeExtensions.ParseList(result.StdOut);
        var entries = await _cache.LoadAsync(ct);

        if (!IsVsix(pkg.Spec))
            return VsCodeExtensions.IsMarketplaceInstalled(listed, pkg.Id, entries);
        if (!listed.Contains(pkg.Id, StringComparer.OrdinalIgnoreCase)) return false;

        VsixSource source;
        try { source = Vsix.Resolve(pkg.Spec.Url!, Home); }
        catch (InvalidOperationException) { return false; }   // InstallAsync reports the bad url

        // Hash the local file as it is now, so a rebuilt .vsix reinstalls.
        string? fileSha256 = null;
        if (!source.IsRemote && File.Exists(source.Location))
            fileSha256 = await Downloads.ComputeSha256Async(source.Location, ct);
        return Vsix.IsCurrent(entries, source, pkg.Id, pkg.Spec.Sha256, fileSha256);
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var binary = Binary(pkg.Spec);
        if (IsVsix(pkg.Spec))
        {
            await InstallVsixAsync(pkg, binary, ct);
            return;
        }

        var result = await ProcessRunner.RunAsync(binary, InstallArgs(pkg.Id), ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"{binary} --install-extension {pkg.Id} failed: {result.StdErr.Trim()}");
        // Now a Marketplace install: stop `update` skipping it as a .vsix one.
        await _cache.ForgetAsync(pkg.Id, ct);
    }

    private async Task InstallVsixAsync(ResolvedPackage pkg, string binary, CancellationToken ct)
    {
        var source = Vsix.Resolve(pkg.Spec.Url!, Home);
        // A local file is copied first, so the hash recorded is of the exact bytes
        // installed even if the file is rebuilt meanwhile.
        var tempPath = Downloads.TempPath(".vsix");
        try
        {
            string sha256;
            if (source.IsRemote)
            {
                sha256 = await Downloads.DownloadVerifiedAsync(source.Location, tempPath, pkg.Spec.Sha256, ct);
            }
            else
            {
                if (!File.Exists(source.Location))
                    throw new InvalidOperationException($".vsix not found: {source.Location}");
                File.Copy(source.Location, tempPath);
                sha256 = await Downloads.ComputeSha256Async(tempPath, ct);
                if (!string.IsNullOrWhiteSpace(pkg.Spec.Sha256))
                    Downloads.VerifySha256(sha256, pkg.Spec.Sha256);
            }

            string id;
            try
            {
                await using var stream = File.OpenRead(tempPath);
                id = Vsix.ReadId(stream);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException($"{source.Location}: {ex.Message}");
            }
            Vsix.EnsureId(id, pkg.Id, source.Location);

            var result = await ProcessRunner.RunAsync(binary, InstallArgs(tempPath), ct);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{binary} --install-extension {source.Location} failed: {result.StdErr.Trim()}");

            await _cache.RecordAsync(source.Location, sha256, pkg.Id, ct);
        }
        finally
        {
            Downloads.TryDelete(tempPath);
        }
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var list = await ProcessRunner.RunAsync(DefaultBinary, ["--list-extensions"], ct);
        if (list.ExitCode != 0)
            throw new InvalidOperationException($"{DefaultBinary} --list-extensions failed: {list.StdErr.Trim()}");

        // Extensions installed from a .vsix aren't on the Marketplace (or would be
        // replaced by a different build); `install` keeps them current instead.
        var installed = VsCodeExtensions.ParseList(list.StdOut);
        var updatable = VsCodeExtensions.Updatable(installed, await _cache.LoadAsync(ct));
        foreach (var id in installed.Except(updatable))
            Console.WriteLine($"  [skip]    vscode    {id}  (installed from .vsix)");

        var failures = new List<string>();
        foreach (var id in updatable)
        {
            var result = await ProcessRunner.RunAsync(DefaultBinary, InstallArgs(id), ct);
            if (result.ExitCode != 0)
                failures.Add($"{id}: {result.StdErr.Trim()}");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"{DefaultBinary} --install-extension failed for {failures.Count} extension(s): {string.Join("; ", failures)}");
    }

    // --- prune ---------------------------------------------------------------

    public PrunePolicy MaxPolicy => PrunePolicy.Zap;

    public async Task<IReadOnlyList<string>> ListExplicitAsync(CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(DefaultBinary, ["--list-extensions"], ct);
        if (result.ExitCode != 0) return [];
        var installed = VsCodeExtensions.ParseList(result.StdOut);

        // Reduce to leaves: read each installed extension's manifest and subtract any
        // id pulled in via extensionPack / extensionDependencies, so prune never
        // removes a pack member of a declared extension (declaring ms-python.python
        // protects pylance/debugpy/python-envs). Built-in defaults are already
        // absent — `code --list-extensions` doesn't list them.
        var extDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".vscode", "extensions");
        var pulledIn = new List<string>();
        if (Directory.Exists(extDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(extDir))
            {
                var manifest = Path.Combine(dir, "package.json");
                if (!File.Exists(manifest)) continue;
                try { pulledIn.AddRange(VsCodeExtensions.ParsePulledIn(File.ReadAllText(manifest))); }
                catch (IOException) { /* unreadable manifest → treat as pulling nothing in */ }
            }
        }
        return VsCodeExtensions.Leaves(installed, pulledIn);
    }

    public async Task UninstallAsync(string id, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(DefaultBinary, ["--uninstall-extension", id], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"{DefaultBinary} --uninstall-extension {id} failed: {result.StdErr.Trim()}");
        await _cache.ForgetAsync(id, ct);
    }
}
