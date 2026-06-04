using DependencyManager.Config;
using DependencyManager.Managers;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class ManagerAvailabilityTests
{
    [Fact]
    public void AptManager_is_unavailable_on_non_linux()
    {
        if (OperatingSystem.IsLinux()) return;
        new AptManager().IsAvailable().ShouldBeFalse();
    }

    [Fact]
    public void SnapManager_is_unavailable_on_non_linux()
    {
        if (OperatingSystem.IsLinux()) return;
        new SnapManager().IsAvailable().ShouldBeFalse();
    }

    [Fact]
    public void FlatpakManager_is_unavailable_on_non_linux()
    {
        if (OperatingSystem.IsLinux()) return;
        new FlatpakManager(userScope: true).IsAvailable().ShouldBeFalse();
    }

    [Fact]
    public void DebManager_is_unavailable_on_non_linux()
    {
        if (OperatingSystem.IsLinux()) return;
        new DebManager().IsAvailable().ShouldBeFalse();
    }

    [Theory]
    [InlineData(ManagerKind.Firefox)]
    [InlineData(ManagerKind.Zen)]
    [InlineData(ManagerKind.Chrome)]
    [InlineData(ManagerKind.Chromium)]
    [InlineData(ManagerKind.Brave)]
    public void BrowserExtensionManager_is_unavailable_on_non_linux(ManagerKind kind)
    {
        if (OperatingSystem.IsLinux()) return;
        new BrowserExtensionManager(kind).IsAvailable().ShouldBeFalse();
    }
}
