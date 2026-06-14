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
    Nvm,
    Firefox,
    Zen,
    Chrome,
    Chromium,
    Brave,
    Brew,
    Cask,
    Mas,
}

public sealed record ConfigFile(Dictionary<string, Block> Blocks);

public sealed record Block
{
    public string Platform { get; init; } = "all";
    public string Architecture { get; init; } = "all";
    public string? Version { get; init; }

    public List<string>? Ppas { get; init; }
    public Dictionary<string, AptSource>? AptSources { get; init; }

    public List<string>? Requires { get; init; }

    public Dictionary<string, PackageSpec>? Apt { get; init; }
    public Dictionary<string, PackageSpec>? Snap { get; init; }
    public Dictionary<string, PackageSpec>? Flatpak { get; init; }
    public Dictionary<string, PackageSpec>? Deb { get; init; }
    public Dictionary<string, PackageSpec>? Pip { get; init; }
    public Dictionary<string, PackageSpec>? Pipx { get; init; }
    public Dictionary<string, PackageSpec>? Script { get; init; }
    public Dictionary<string, PackageSpec>? Vscode { get; init; }
    public Dictionary<string, PackageSpec>? Cargo { get; init; }
    public Dictionary<string, PackageSpec>? Nvm { get; init; }

    // Browser-extension blocks. Each maps an extension id -> spec; the id is the
    // Firefox addon id (e.g. uBlock0@raymondhill.net) or the 32-char Chrome Web
    // Store id, depending on the browser's policy family.
    public Dictionary<string, PackageSpec>? Firefox { get; init; }
    public Dictionary<string, PackageSpec>? Zen { get; init; }
    public Dictionary<string, PackageSpec>? Chrome { get; init; }
    public Dictionary<string, PackageSpec>? Chromium { get; init; }
    public Dictionary<string, PackageSpec>? Brave { get; init; }

    // macOS (and Linuxbrew) providers. brew = formulae, cask = GUI apps, mas =
    // Mac App Store apps keyed by their numeric App Store id.
    public Dictionary<string, PackageSpec>? Brew { get; init; }
    public Dictionary<string, PackageSpec>? Cask { get; init; }
    public Dictionary<string, PackageSpec>? Mas { get; init; }
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

    // Browser-extension installation mode: force | normal | allowed | blocked.
    // Defaults to normal_installed (auto-installed, user can disable).
    public string? Mode { get; init; }

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

public sealed record ResolvedRequirement(string Name, string BlockName, bool Satisfied);

public sealed record ResolvedPlan(
    IReadOnlyList<ResolvedPackage> Packages,
    IReadOnlyList<string> AptPpas,
    IReadOnlyList<ResolvedAptSource> AptSources,
    IReadOnlyList<ResolvedRequirement> Requirements);
