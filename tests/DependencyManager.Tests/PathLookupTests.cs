using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

[Collection("PathLookup")]
public class PathLookupTests : IDisposable
{
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");
    private readonly string? _originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("PATHEXT", _originalPathExt);
    }

    [Fact]
    public void Default_probe_returns_false_for_empty_name()
    {
        PathLookup.Exists("").ShouldBeFalse();
    }

    [Fact]
    public void Default_probe_returns_false_for_whitespace_name()
    {
        PathLookup.Exists("   ").ShouldBeFalse();
    }

    [Fact]
    public void Default_probe_resolves_existing_absolute_path()
    {
        var existing = typeof(PathLookupTests).Assembly.Location;
        File.Exists(existing).ShouldBeTrue();
        PathLookup.Exists(existing).ShouldBeTrue();
    }

    [Fact]
    public void Default_probe_returns_false_for_missing_absolute_path()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"depend-{Guid.NewGuid():N}.absent");
        PathLookup.Exists(missing).ShouldBeFalse();
    }

    [Fact]
    public void Default_probe_walks_PATH_for_bare_names()
    {
        var dir = Directory.CreateTempSubdirectory("depend-pathlookup-");
        try
        {
            var binaryName = "depend-test-tool" + (OperatingSystem.IsWindows() ? ".exe" : "");
            File.WriteAllText(Path.Combine(dir.FullName, binaryName), string.Empty);

            Environment.SetEnvironmentVariable("PATH", dir.FullName);
            if (OperatingSystem.IsWindows())
                Environment.SetEnvironmentVariable("PATHEXT", ".EXE");

            PathLookup.Exists("depend-test-tool").ShouldBeTrue();
            PathLookup.Exists("definitely-not-on-path").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Fact]
    public void Default_probe_returns_false_when_PATH_is_empty()
    {
        Environment.SetEnvironmentVariable("PATH", string.Empty);
        PathLookup.Exists("anything").ShouldBeFalse();
    }
}
