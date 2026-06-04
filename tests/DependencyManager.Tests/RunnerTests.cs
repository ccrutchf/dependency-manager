using DependencyManager.Config;
using DependencyManager.Managers;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class RunnerTests
{
    private static ResolvedPackage Pkg(ManagerKind kind, string id) =>
        new(kind, id, new PackageSpec(), "block");

    [Fact]
    public async Task FindMissingAsync_returns_packages_with_no_available_manager()
    {
        var runner = new Runner.Runner(new IPackageManager[]
        {
            new FakeManager { Kind = ManagerKind.Apt, Available = false },
        });

        var missing = await runner.FindMissingAsync(
            new[] { Pkg(ManagerKind.Apt, "curl") }, CancellationToken.None);

        missing.Count.ShouldBe(1);
        missing[0].Id.ShouldBe("curl");
    }

    [Fact]
    public async Task FindMissingAsync_filters_out_installed_packages()
    {
        var runner = new Runner.Runner(new IPackageManager[]
        {
            new FakeManager { Kind = ManagerKind.VsCode, InstalledCheck = p => p.Id == "already" },
        });

        var missing = await runner.FindMissingAsync(
            new[] { Pkg(ManagerKind.VsCode, "already"), Pkg(ManagerKind.VsCode, "missing") },
            CancellationToken.None);

        missing.Count.ShouldBe(1);
        missing[0].Id.ShouldBe("missing");
    }

    [Fact]
    public async Task InstallAsync_bootstraps_each_manager_kind_once()
    {
        var fake = new FakeManager { Kind = ManagerKind.VsCode };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.InstallAsync(
            new[]
            {
                Pkg(ManagerKind.VsCode, "a"),
                Pkg(ManagerKind.VsCode, "b"),
                Pkg(ManagerKind.VsCode, "c"),
            },
            failFast: false, CancellationToken.None));

        rc.ShouldBe(0);
        fake.BootstrapCalls.ShouldBe(1);
        fake.Installed.Count.ShouldBe(3);
    }

    [Fact]
    public async Task InstallAsync_skips_packages_already_installed()
    {
        var fake = new FakeManager
        {
            Kind = ManagerKind.VsCode,
            InstalledCheck = p => p.Id == "already",
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.InstallAsync(
            new[] { Pkg(ManagerKind.VsCode, "already"), Pkg(ManagerKind.VsCode, "missing") },
            failFast: false, CancellationToken.None));

        rc.ShouldBe(0);
        fake.Installed.Count.ShouldBe(1);
        fake.Installed[0].Id.ShouldBe("missing");
    }

    [Fact]
    public async Task InstallAsync_reports_failure_when_no_manager_available_for_kind()
    {
        var fake = new FakeManager { Kind = ManagerKind.Apt, Available = false };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.InstallAsync(
            new[] { Pkg(ManagerKind.Apt, "curl") },
            failFast: false, CancellationToken.None));

        rc.ShouldBe(1);
        fake.Installed.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_stops_on_first_failure_when_failFast_is_true()
    {
        var fake = new FakeManager
        {
            Kind = ManagerKind.VsCode,
            InstallShouldThrow = p => p.Id == "first" ? new InvalidOperationException("boom") : null,
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.InstallAsync(
            new[] { Pkg(ManagerKind.VsCode, "first"), Pkg(ManagerKind.VsCode, "second") },
            failFast: true, CancellationToken.None));

        rc.ShouldBe(1);
        fake.Installed.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_continues_past_failure_when_failFast_is_false()
    {
        var fake = new FakeManager
        {
            Kind = ManagerKind.VsCode,
            InstallShouldThrow = p => p.Id == "first" ? new InvalidOperationException("boom") : null,
        };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.InstallAsync(
            new[] { Pkg(ManagerKind.VsCode, "first"), Pkg(ManagerKind.VsCode, "second") },
            failFast: false, CancellationToken.None));

        rc.ShouldBe(1);
        fake.Installed.Count.ShouldBe(1);
        fake.Installed[0].Id.ShouldBe("second");
    }

    [Fact]
    public async Task ManagerFor_picks_first_matching_available_manager_of_kind()
    {
        var unavailable = new FakeManager { Kind = ManagerKind.VsCode, Available = false };
        var available = new FakeManager { Kind = ManagerKind.VsCode };
        var runner = new Runner.Runner(new IPackageManager[] { unavailable, available });

        var rc = await SilentRun(() => runner.InstallAsync(
            new[] { Pkg(ManagerKind.VsCode, "x") },
            failFast: false, CancellationToken.None));

        rc.ShouldBe(0);
        unavailable.Installed.ShouldBeEmpty();
        available.Installed.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TestAsync_returns_zero_when_all_installed()
    {
        var fake = new FakeManager { Kind = ManagerKind.VsCode, InstalledCheck = _ => true };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.TestAsync(
            new[] { Pkg(ManagerKind.VsCode, "x") }, CancellationToken.None));

        rc.ShouldBe(0);
    }

    [Fact]
    public async Task TestAsync_returns_one_when_any_missing()
    {
        var fake = new FakeManager { Kind = ManagerKind.VsCode, InstalledCheck = _ => false };
        var runner = new Runner.Runner(new IPackageManager[] { fake });

        var rc = await SilentRun(() => runner.TestAsync(
            new[] { Pkg(ManagerKind.VsCode, "x") }, CancellationToken.None));

        rc.ShouldBe(1);
    }

    private static async Task<int> SilentRun(Func<Task<int>> action)
    {
        var prev = Console.Out;
        Console.SetOut(TextWriter.Null);
        try { return await action(); }
        finally { Console.SetOut(prev); }
    }

    private sealed class FakeManager : IPackageManager
    {
        public ManagerKind Kind { get; init; }
        public bool Available { get; init; } = true;
        public Func<ResolvedPackage, bool> InstalledCheck { get; init; } = _ => false;
        public Func<ResolvedPackage, Exception?>? InstallShouldThrow { get; init; }

        public int BootstrapCalls { get; private set; }
        public List<ResolvedPackage> Installed { get; } = new();

        public bool IsAvailable() => Available;

        public Task BootstrapAsync(CancellationToken ct)
        {
            BootstrapCalls++;
            return Task.CompletedTask;
        }

        public Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct) =>
            Task.FromResult(InstalledCheck(pkg));

        public Task InstallAsync(ResolvedPackage pkg, CancellationToken ct)
        {
            var ex = InstallShouldThrow?.Invoke(pkg);
            if (ex is not null) throw ex;
            Installed.Add(pkg);
            return Task.CompletedTask;
        }

        public Task UpdateAllAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
