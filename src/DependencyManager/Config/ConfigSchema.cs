namespace DependencyManager.Config;

public enum ManagerKind
{
    Apt,
    Snap,
    Flatpak,
    Deb,
    Pip,
    Pipx,
    Script,
    VsCode,
    Cargo,
}

public sealed record ConfigFile(Dictionary<string, Block> Blocks);

public sealed record Block
{
    public string Platform { get; init; } = "all";
    public string Architecture { get; init; } = "all";
    public string? Version { get; init; }

    public List<string>? Ppas { get; init; }
    public Dictionary<string, AptSource>? AptSources { get; init; }

    public Dictionary<string, PackageSpec>? Apt { get; init; }
    public Dictionary<string, PackageSpec>? Snap { get; init; }
    public Dictionary<string, PackageSpec>? Flatpak { get; init; }
    public Dictionary<string, PackageSpec>? Deb { get; init; }
    public Dictionary<string, PackageSpec>? Pip { get; init; }
    public Dictionary<string, PackageSpec>? Pipx { get; init; }
    public Dictionary<string, PackageSpec>? Script { get; init; }
    public Dictionary<string, PackageSpec>? Vscode { get; init; }
    public Dictionary<string, PackageSpec>? Cargo { get; init; }
}

public sealed record PackageSpec
{
    public string? Name { get; init; }
    public List<string>? Dependencies { get; init; }
    public string? Source { get; init; }
    public bool Classic { get; init; }
    public string? Url { get; init; }
    public string? Sha256 { get; init; }
    public string? Scope { get; init; }
    public string? Check { get; init; }
    public string? Install { get; init; }

    public bool? UserScope => Scope?.ToLowerInvariant() switch
    {
        "user" => true,
        "system" => false,
        _ => null,
    };
}

public sealed record AptSource
{
    public string? KeyUrl { get; init; }
    public string? Uri { get; init; }
    public string? Suite { get; init; }
    public string? Components { get; init; }
    public string? Architectures { get; init; }
    public string? SignedBy { get; init; }
}

public sealed record ResolvedPackage(
    ManagerKind Manager,
    string Id,
    PackageSpec Spec,
    string BlockName);

public sealed record ResolvedAptSource(string Name, AptSource Source, string BlockName);

public sealed record ResolvedPlan(
    IReadOnlyList<ResolvedPackage> Packages,
    IReadOnlyList<string> AptPpas,
    IReadOnlyList<ResolvedAptSource> AptSources);
