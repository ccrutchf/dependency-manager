using DependencyManager.Managers;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class FlatpakBundleTests
{
    // A real single-file bundle (~2 KB) made with `flatpak build-bundle` from a
    // throwaway app: ref app/org.depend.Throwaway/x86_64/stable on the
    // org.freedesktop.Platform 25.08 runtime.
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "throwaway.flatpak");

    private static FlatpakBundleHeader ReadFixture()
    {
        using var stream = File.OpenRead(FixturePath);
        return FlatpakBundle.ReadHeader(stream);
    }

    [Fact]
    public void Reads_the_ref_from_a_real_bundle()
    {
        var header = ReadFixture();

        header.Ref.ShouldBe("app/org.depend.Throwaway/x86_64/stable");
        header.IsApp.ShouldBeTrue();
        header.Id.ShouldBe("org.depend.Throwaway");
    }

    [Fact]
    public void Reads_the_commit_from_a_real_bundle()
    {
        // Matches `flatpak info --show-commit org.depend.Throwaway` after installing it.
        ReadFixture().Commit.ShouldBe("cc789fc43eb29ae40d9988ae1bc5b358fda0cbd41681d760fa5fc671b882a75e");
    }

    [Fact]
    public void Reads_the_runtime_from_the_embedded_metadata()
    {
        ReadFixture().Runtime.ShouldBe("org.freedesktop.Platform/x86_64/25.08");
    }

    [Fact]
    public void Reads_a_large_bundle_whose_metadata_carries_inline_delta_parts()
    {
        // Real bundles (e.g. CrealityPrint, 190 MB) store the delta parts inline in
        // the metadata dict, so it spans nearly the whole file and the framing
        // offsets are 4 bytes wide. The small fixture exercises neither.
        var commit = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var bundle = GVariantWriter.Superblock(
            [
                ("ref", GVariantWriter.Str("app/org.example.Big/x86_64/master"), "s"),
                ("0", new byte[2 * 1024 * 1024], "ay"),
                ("metadata", GVariantWriter.Str("[Application]\nname=org.example.Big\nruntime=org.gnome.Platform/x86_64/50\n"), "s"),
            ],
            commit);

        using var stream = new MemoryStream(bundle);
        var header = FlatpakBundle.ReadHeader(stream);

        header.Id.ShouldBe("org.example.Big");
        header.Runtime.ShouldBe("org.gnome.Platform/x86_64/50");
        header.Commit.ShouldBe(Convert.ToHexString(commit).ToLowerInvariant());
    }

    [Fact]
    public void Rejects_a_file_that_is_not_a_bundle()
    {
        using var stream = new MemoryStream("<html>404 not found</html>"u8.ToArray());

        Should.Throw<InvalidDataException>(() => FlatpakBundle.ReadHeader(stream));
    }

    [Fact]
    public void Rejects_an_empty_file()
    {
        using var stream = new MemoryStream();

        Should.Throw<InvalidDataException>(() => FlatpakBundle.ReadHeader(stream));
    }

    [Fact]
    public void App_id_check_passes_when_the_bundle_matches_the_yaml_key()
    {
        Should.NotThrow(() => FlatpakBundle.EnsureAppId(ReadFixture(), "org.depend.Throwaway", "https://x/t.flatpak"));
    }

    [Fact]
    public void App_id_check_fails_with_a_clear_error_on_mismatch()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            FlatpakBundle.EnsureAppId(ReadFixture(), "io.github.crealityofficial.CrealityPrint", "https://x/t.flatpak"));

        ex.Message.ShouldContain("org.depend.Throwaway");
        ex.Message.ShouldContain("io.github.crealityofficial.CrealityPrint");
        ex.Message.ShouldContain("https://x/t.flatpak");
    }

    [Fact]
    public void App_id_check_rejects_a_runtime_bundle()
    {
        var runtime = new FlatpakBundleHeader("runtime/org.example.Platform/x86_64/1", "c0ffee", null);

        var ex = Should.Throw<InvalidOperationException>(() =>
            FlatpakBundle.EnsureAppId(runtime, "org.example.Platform", "https://x/r.flatpak"));
        ex.Message.ShouldContain("runtime");
    }
}

/// <summary>
/// Just enough GVariant serialization (little-endian) to build a bundle
/// superblock <c>(a{sv} t ay ay (commit) ay a(…) a(…))</c> for tests.
/// </summary>
internal static class GVariantWriter
{
    public static byte[] Str(string s) => [.. System.Text.Encoding.UTF8.GetBytes(s), 0];

    public static byte[] Superblock((string Key, byte[] Value, string Type)[] metadata, byte[] toCommit)
    {
        var dict = Array(metadata.Select(e => Entry(e.Key, e.Value, e.Type)).ToList(), alignment: 8);
        var body = new List<byte>(dict);
        var ends = new List<long> { body.Count };                    // a{sv}
        Pad(body, 8); body.AddRange(new byte[8]);                    // t (timestamp)
        ends.Add(body.Count);                                        // ay from (empty)
        body.AddRange(toCommit); ends.Add(body.Count);               // ay to
        ends.Add(body.Count);                                        // (commit) — empty is fine here
        ends.Add(body.Count);                                        // ay prerequisites
        ends.Add(body.Count);                                        // a(…) part headers
        // last element (fallbacks) is empty and needs no framing offset
        ends.Reverse();                                              // tuple offsets are stored reversed
        return Frame(body, ends);
    }

    private static byte[] Entry(string key, byte[] value, string type)
    {
        var body = new List<byte>(Str(key));
        var keyEnd = body.Count;
        Pad(body, 8);
        body.AddRange(value);
        body.Add(0);
        body.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
        return Frame(body, [keyEnd]);
    }

    private static byte[] Array(List<byte[]> elements, int alignment)
    {
        var body = new List<byte>();
        var ends = new List<long>();
        foreach (var e in elements)
        {
            Pad(body, alignment);
            body.AddRange(e);
            ends.Add(body.Count);
        }
        return Frame(body, ends);
    }

    private static byte[] Frame(List<byte> body, List<long> offsets)
    {
        var width = new[] { 1, 2, 4, 8 }.First(w => body.Count + offsets.Count * w <= (w == 8 ? long.MaxValue : (1L << (8 * w)) - 1));
        foreach (var o in offsets)
            for (var i = 0; i < width; i++) body.Add((byte)(o >> (8 * i)));
        return body.ToArray();
    }

    private static void Pad(List<byte> body, int alignment)
    {
        while (body.Count % alignment != 0) body.Add(0);
    }
}
