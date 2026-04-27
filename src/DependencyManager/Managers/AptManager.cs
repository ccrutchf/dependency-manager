using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Managers;

public sealed class AptManager : IPackageManager
{
    private static readonly HttpClient Http = new();

    private readonly IReadOnlyList<string> _ppas;
    private readonly IReadOnlyList<ResolvedAptSource> _sources;
    private bool _bootstrapped;

    public AptManager() : this(Array.Empty<string>(), Array.Empty<ResolvedAptSource>()) { }

    public AptManager(IReadOnlyList<string> ppas)
        : this(ppas, Array.Empty<ResolvedAptSource>()) { }

    public AptManager(IReadOnlyList<string> ppas, IReadOnlyList<ResolvedAptSource> sources)
    {
        _ppas = ppas;
        _sources = sources;
    }

    public ManagerKind Kind => ManagerKind.Apt;

    public bool IsAvailable() => OperatingSystem.IsLinux() && File.Exists("/usr/bin/apt-get");

    public async Task BootstrapAsync(CancellationToken ct)
    {
        if (_bootstrapped) return;

        foreach (var src in _sources)
            await InstallSourceAsync(src, ct);

        foreach (var ppa in _ppas)
        {
            var add = await Sudo.RunAsync("add-apt-repository", ["-y", ppa], ct);
            if (add.ExitCode != 0)
                throw new InvalidOperationException(
                    $"add-apt-repository {ppa} failed: {add.StdErr.Trim()} (install software-properties-common if missing)");
        }

        var result = await Sudo.RunAsync("apt-get", ["update"], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"apt-get update failed: {result.StdErr.Trim()}");
        _bootstrapped = true;
    }

    public async Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("dpkg", ["-s", pkg.Id], ct);
        return result.ExitCode == 0 && result.StdOut.Contains("Status: install ok installed");
    }

    public async Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
    {
        var result = await Sudo.RunAsync("apt-get", ["install", "-y", pkg.Id], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"apt-get install {pkg.Id} failed: {result.StdErr.Trim()}");
    }

    public async Task UpdateAllAsync(CancellationToken ct)
    {
        var refresh = await Sudo.RunAsync("apt-get", ["update"], ct);
        if (refresh.ExitCode != 0)
            throw new InvalidOperationException($"apt-get update failed: {refresh.StdErr.Trim()}");

        var upgrade = await Sudo.RunAsync("apt-get", ["upgrade", "-y"], ct);
        if (upgrade.ExitCode != 0)
            throw new InvalidOperationException($"apt-get upgrade failed: {upgrade.StdErr.Trim()}");
    }

    private static async Task InstallSourceAsync(ResolvedAptSource src, CancellationToken ct)
    {
        var name = src.Name;
        var source = src.Source;
        if (string.IsNullOrWhiteSpace(source.KeyUrl))
            throw new InvalidOperationException($"apt source '{name}' is missing 'keyUrl'");
        if (string.IsNullOrWhiteSpace(source.Uri))
            throw new InvalidOperationException($"apt source '{name}' is missing 'uri'");

        var keyringPath = string.IsNullOrWhiteSpace(source.SignedBy)
            ? $"/etc/apt/keyrings/{name}.asc"
            : source.SignedBy!;
        var sourcesPath = $"/etc/apt/sources.list.d/{name}.sources";
        var suite = !string.IsNullOrWhiteSpace(source.Suite) ? source.Suite! : await DetectCodenameAsync(ct);
        var components = !string.IsNullOrWhiteSpace(source.Components) ? source.Components! : "stable";
        var architectures = !string.IsNullOrWhiteSpace(source.Architectures)
            ? source.Architectures!
            : await DetectArchitectureAsync(ct);

        await EnsureDirAsync(Path.GetDirectoryName(keyringPath)!, ct);
        await DownloadKeyAsync(source.KeyUrl!, keyringPath, ct);

        var deb822 = $"""
            Types: deb
            URIs: {source.Uri}
            Suites: {suite}
            Components: {components}
            Architectures: {architectures}
            Signed-By: {keyringPath}

            """;
        await WriteRootFileAsync(sourcesPath, deb822, "0644", ct);
    }

    private static async Task EnsureDirAsync(string path, CancellationToken ct)
    {
        var result = await Sudo.RunAsync("install", ["-d", "-m", "0755", path], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"failed to create {path}: {result.StdErr.Trim()}");
    }

    private static async Task DownloadKeyAsync(string url, string destination, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"download {url} failed: HTTP {(int)response.StatusCode}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"depend-key-{Guid.NewGuid():N}.asc");
        try
        {
            await using (var src = await response.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tempPath))
                await src.CopyToAsync(dst, ct);

            var install = await Sudo.RunAsync(
                "install", ["-m", "0644", "-o", "root", "-g", "root", tempPath, destination], ct);
            if (install.ExitCode != 0)
                throw new InvalidOperationException(
                    $"failed to install keyring at {destination}: {install.StdErr.Trim()}");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static async Task WriteRootFileAsync(string destination, string contents, string mode, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"depend-src-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, ct);
            var install = await Sudo.RunAsync(
                "install", ["-m", mode, "-o", "root", "-g", "root", tempPath, destination], ct);
            if (install.ExitCode != 0)
                throw new InvalidOperationException(
                    $"failed to write {destination}: {install.StdErr.Trim()}");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static async Task<string> DetectCodenameAsync(CancellationToken ct)
    {
        const string osRelease = "/etc/os-release";
        if (!File.Exists(osRelease))
            throw new InvalidOperationException("cannot determine codename: /etc/os-release not found (set 'suite' explicitly)");

        string? ubuntu = null;
        string? version = null;
        foreach (var raw in await File.ReadAllLinesAsync(osRelease, ct))
        {
            var line = raw.Trim();
            if (line.StartsWith("UBUNTU_CODENAME=", StringComparison.Ordinal))
                ubuntu = Unquote(line["UBUNTU_CODENAME=".Length..]);
            else if (line.StartsWith("VERSION_CODENAME=", StringComparison.Ordinal))
                version = Unquote(line["VERSION_CODENAME=".Length..]);
        }

        var codename = !string.IsNullOrWhiteSpace(ubuntu) ? ubuntu : version;
        if (string.IsNullOrWhiteSpace(codename))
            throw new InvalidOperationException(
                "cannot determine codename from /etc/os-release (set 'suite' explicitly)");
        return codename!;
    }

    private static async Task<string> DetectArchitectureAsync(CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("dpkg", ["--print-architecture"], ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
            throw new InvalidOperationException(
                $"dpkg --print-architecture failed: {result.StdErr.Trim()}");
        return result.StdOut.Trim();
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            return value[1..^1];
        return value;
    }
}
