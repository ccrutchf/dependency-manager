using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class DebCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cachePath;

    public DebCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"depend-debcache-{Guid.NewGuid():N}");
        _cachePath = Path.Combine(_tempDir, "debs.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_returns_empty_when_file_does_not_exist()
    {
        var cache = new DebCache(_cachePath);

        var entries = await cache.LoadAsync(CancellationToken.None);

        entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task RecordAsync_creates_parent_directory_and_writes_entry()
    {
        var cache = new DebCache(_cachePath);
        const string url = "https://example.com/slack.deb";
        const string sha = "abc123";

        await cache.RecordAsync(url, sha, CancellationToken.None);

        File.Exists(_cachePath).ShouldBeTrue();
        var entries = await cache.LoadAsync(CancellationToken.None);
        entries.ShouldContainKey(url);
        entries[url].Sha256.ShouldBe(sha);
    }

    [Fact]
    public async Task RecordAsync_overwrites_existing_entry_for_same_url()
    {
        var cache = new DebCache(_cachePath);
        const string url = "https://example.com/slack.deb";

        await cache.RecordAsync(url, "old-sha", CancellationToken.None);
        var firstLoad = await cache.LoadAsync(CancellationToken.None);
        var firstTimestamp = firstLoad[url].InstalledAt;

        await Task.Delay(10);
        await cache.RecordAsync(url, "new-sha", CancellationToken.None);

        var entries = await cache.LoadAsync(CancellationToken.None);
        entries.Count.ShouldBe(1);
        entries[url].Sha256.ShouldBe("new-sha");
        entries[url].InstalledAt.ShouldBeGreaterThan(firstTimestamp);
    }

    [Fact]
    public async Task RecordAsync_keeps_unrelated_entries_intact()
    {
        var cache = new DebCache(_cachePath);

        await cache.RecordAsync("https://a.example/pkg.deb", "sha-a", CancellationToken.None);
        await cache.RecordAsync("https://b.example/pkg.deb", "sha-b", CancellationToken.None);

        var entries = await cache.LoadAsync(CancellationToken.None);
        entries.Count.ShouldBe(2);
        entries["https://a.example/pkg.deb"].Sha256.ShouldBe("sha-a");
        entries["https://b.example/pkg.deb"].Sha256.ShouldBe("sha-b");
    }

    [Fact]
    public async Task SaveAsync_round_trips_multiple_entries()
    {
        var cache = new DebCache(_cachePath);
        var input = new Dictionary<string, DebCacheEntry>
        {
            ["https://a.example/pkg.deb"] = new("sha-a", DateTimeOffset.UtcNow),
            ["https://b.example/pkg.deb"] = new("sha-b", DateTimeOffset.UtcNow.AddMinutes(-5)),
        };

        await cache.SaveAsync(input, CancellationToken.None);
        var loaded = await cache.LoadAsync(CancellationToken.None);

        loaded.Count.ShouldBe(2);
        loaded["https://a.example/pkg.deb"].Sha256.ShouldBe("sha-a");
        loaded["https://b.example/pkg.deb"].Sha256.ShouldBe("sha-b");
    }

    [Fact]
    public async Task SaveAsync_does_not_leave_tmp_file_behind()
    {
        var cache = new DebCache(_cachePath);

        await cache.RecordAsync("https://example.com/pkg.deb", "sha", CancellationToken.None);

        File.Exists(_cachePath + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_emits_human_readable_json()
    {
        var cache = new DebCache(_cachePath);

        await cache.RecordAsync("https://example.com/pkg.deb", "abc", CancellationToken.None);

        var contents = await File.ReadAllTextAsync(_cachePath);
        contents.ShouldContain("\n");
        contents.ShouldContain("\"sha256\"");
        contents.ShouldContain("\"installedAt\"");
        contents.ShouldContain("abc");
    }

    [Fact]
    public void DefaultPath_honors_XDG_CACHE_HOME_when_set()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", "/tmp/xdg-cache-test");
            var path = DebCache.DefaultPath();
            path.ShouldBe(Path.Combine("/tmp/xdg-cache-test", "depend", "debs.json"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
        }
    }

    [Fact]
    public void DefaultPath_falls_back_to_user_home_cache_when_xdg_unset()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", null);
            var path = DebCache.DefaultPath();
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "depend", "debs.json");
            path.ShouldBe(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
        }
    }

    [Fact]
    public async Task RecordAsync_with_packageId_evicts_other_urls_for_same_package()
    {
        var cache = new DebCache(_cachePath);
        const string oldUrl = "https://example.com/foo-0.1.deb";
        const string newUrl = "https://example.com/foo-0.2.deb";

        await cache.RecordAsync(oldUrl, "old-sha", "foo", CancellationToken.None);
        await cache.RecordAsync(newUrl, "new-sha", "foo", CancellationToken.None);

        var entries = await cache.LoadAsync(CancellationToken.None);
        entries.Count.ShouldBe(1);
        entries.ShouldContainKey(newUrl);
        entries[newUrl].PackageId.ShouldBe("foo");
    }

    [Fact]
    public async Task RecordAsync_with_packageId_keeps_entries_for_different_packages()
    {
        var cache = new DebCache(_cachePath);

        await cache.RecordAsync("https://a.example/foo.deb", "sha-a", "foo", CancellationToken.None);
        await cache.RecordAsync("https://b.example/bar.deb", "sha-b", "bar", CancellationToken.None);

        var entries = await cache.LoadAsync(CancellationToken.None);
        entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RecordAsync_without_packageId_does_not_evict_existing_entries()
    {
        var cache = new DebCache(_cachePath);

        await cache.RecordAsync("https://a.example/foo.deb", "sha-a", "foo", CancellationToken.None);
        await cache.RecordAsync("https://b.example/foo.deb", "sha-b", CancellationToken.None);

        var entries = await cache.LoadAsync(CancellationToken.None);
        entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task LoadAsync_returns_empty_when_file_contains_null_json()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_cachePath, "null");
        var cache = new DebCache(_cachePath);

        var entries = await cache.LoadAsync(CancellationToken.None);

        entries.ShouldBeEmpty();
    }
}
