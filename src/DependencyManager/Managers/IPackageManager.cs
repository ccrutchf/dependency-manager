using DependencyManager.Config;

namespace DependencyManager.Managers;

public interface IPackageManager
{
    ManagerKind Kind { get; }
    bool IsAvailable();
    Task BootstrapAsync(CancellationToken ct);
    Task<bool> IsInstalledAsync(ResolvedPackage pkg, CancellationToken ct);
    Task InstallAsync(ResolvedPackage pkg, CancellationToken ct);
}
