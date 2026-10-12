using DependencyManager.Config;
using DependencyManager.Managers;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class RunnerPruneTests
{
    private static ResolvedPackage Pkg(ManagerKind kind, string id) =>
        new(kind, id, new PackageSpec(), "block");

    [Fact]
    public async Task Prune_removes_undeclared_keeps_declared_and_collects_garbage()
    {
        var fake = new FakePrunableManager
        {
            Kind = ManagerKind.Brew,
            InstalledExplicit = new List<string> { "keep", "extra1", "extra2" },
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.PruneAsync(
            new[] { Pkg(ManagerKind.Brew, "keep") }, apply: true, CancellationToken.None));

        rc.ShouldBe(0);
        fake.Uninstalled.ShouldBe(new[] { "extra1", "extra2" }, ignoreOrder: true);
        fake.GcCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Prune_dry_run_does_not_uninstall_anything()
    {
        var fake = new FakePrunableManager
        {
            Kind = ManagerKind.Brew,
            InstalledExplicit = new List<string> { "keep", "extra" },
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.PruneAsync(
            new[] { Pkg(ManagerKind.Brew, "keep") }, apply: false, CancellationToken.None));

        rc.ShouldBe(0);
        fake.Uninstalled.ShouldBeEmpty();
        fake.GcCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Prune_skips_a_provider_that_declares_nothing_on_this_platform()
    {
        // SAFETY RAIL: brew has installed packages but the plan declares zero brew
        // packages, so brew must be left entirely untouched.
        var fake = new FakePrunableManager
        {
            Kind = ManagerKind.Brew,
            InstalledExplicit = new List<string> { "a", "b", "c" },
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.PruneAsync(
            new[] { Pkg(ManagerKind.Cargo, "ripgrep") }, apply: true, CancellationToken.None));

        rc.ShouldBe(0);
        fake.Uninstalled.ShouldBeEmpty();
    }

    [Fact]
    public async Task Prune_report_only_provider_never_uninstalls_even_on_apply()
    {
        var fake = new FakePrunableManager
        {
            Kind = ManagerKind.Mas,
            MaxPolicy = PrunePolicy.ReportOnly,
            InstalledExplicit = new List<string> { "keep", "extra" },
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.PruneAsync(
            new[] { Pkg(ManagerKind.Mas, "keep") }, apply: true, CancellationToken.None));

        rc.ShouldBe(0);
        fake.Uninstalled.ShouldBeEmpty();
        fake.GcCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Prune_ignores_unavailable_managers()
    {
        var fake = new FakePrunableManager
        {
            Kind = ManagerKind.Brew,
            Available = false,
            InstalledExplicit = new List<string> { "keep", "extra" },
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.PruneAsync(
            new[] { Pkg(ManagerKind.Brew, "keep") }, apply: true, CancellationToken.None));

        rc.ShouldBe(0);
        fake.Uninstalled.ShouldBeEmpty();
    }

    [Fact]
    public async Task Prune_does_not_remove_a_package_that_is_also_declared()
    {
        var fake = new FakePrunableManager
        {
            Kind = ManagerKind.Brew,
            InstalledExplicit = new List<string> { "keep" },
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.PruneAsync(
            new[] { Pkg(ManagerKind.Brew, "keep") }, apply: true, CancellationToken.None));

        rc.ShouldBe(0);
        fake.Uninstalled.ShouldBeEmpty();
        fake.GcCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Prune_never_removes_a_declared_flatpak_bundle_app()
    {
        // A bundle-installed app is listed by `flatpak list --app` under its app id,
        // just like a Flathub app, so declaring it by that id (with a url) keeps it.
        var fake = new FakePrunableManager
        {
            Kind = ManagerKind.Flatpak,
            InstalledExplicit = new List<string> { "io.github.crealityofficial.CrealityPrint", "org.example.Undeclared" },
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });
        var bundle = new ResolvedPackage(
            ManagerKind.Flatpak,
            "io.github.crealityofficial.CrealityPrint",
            new PackageSpec { Url = "https://example.com/CrealityPrint.flatpak", Sha256 = "abc" },
            "block");

        var rc = await SilentRun(() => runner.PruneAsync(new[] { bundle }, apply: true, CancellationToken.None));

        rc.ShouldBe(0);
        fake.Uninstalled.ShouldBe(new[] { "org.example.Undeclared" });
    }

    private static async Task<int> SilentRun(Func<Task<int>> action)
    {
        var prev = Console.Out;
        Console.SetOut(TextWriter.Null);
        try { return await action(); }
        finally { Console.SetOut(prev); }
    }

    private sealed class FakePrunableManager : IPrunableManager
    {
        public ManagerKind Kind { get; init; }
        public bool Available { get; init; } = true;
        public PrunePolicy MaxPolicy { get; init; } = PrunePolicy.Zap;
        public List<string> InstalledExplicit { get; init; } = new();

        public List<string> Uninstalled { get; } = new();
        public int GcCalls { get; private set; }

        public bool IsAvailable() => Available;
        public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct) =>
            Task.FromResult(InstalledExplicit.Contains(pkg.Id));
        public Task InstallAsync(ResolvedPackage pkg, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAllAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListExplicitAsync(CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<string>)InstalledExplicit);
        public Task UninstallAsync(string id, CancellationToken ct)
        {
            Uninstalled.Add(id);
            return Task.CompletedTask;
        }
        public Task CollectGarbageAsync(CancellationToken ct)
        {
            GcCalls++;
            return Task.CompletedTask;
        }
    }
}
