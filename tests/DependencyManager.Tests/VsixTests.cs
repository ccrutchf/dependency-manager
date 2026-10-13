using System.IO.Compression;
using System.Text;
using DependencyManager.Managers;
using DependencyManager.Util;
using Shouldly;
using Xunit;

namespace DependencyManager.Tests;

public class VsixResolveTests
{
    private const string Home = "/home/alice";

    [Fact]
    public void Https_url_is_remote_and_kept_as_is()
    {
        Vsix.Resolve("https://example.com/ext-1.2.3.vsix", Home)
            .ShouldBe(new VsixSource("https://example.com/ext-1.2.3.vsix", IsRemote: true));
    }

    [Fact]
    public void Http_url_is_remote_case_insensitively()
    {
        Vsix.Resolve("HTTP://example.com/ext.vsix", Home).IsRemote.ShouldBeTrue();
    }

    [Fact]
    public void Absolute_path_is_local()
    {
        Vsix.Resolve("/opt/exts/ext.vsix", Home)
            .ShouldBe(new VsixSource("/opt/exts/ext.vsix", IsRemote: false));
    }

    [Fact]
    public void Absolute_path_is_normalised()
    {
        Vsix.Resolve("/opt/exts/../other/./ext.vsix", Home).Location.ShouldBe("/opt/other/ext.vsix");
    }

    [Fact]
    public void Tilde_path_expands_to_home()
    {
        Vsix.Resolve("~/.local/share/phone-dash/phone-dash-focus.vsix", Home)
            .ShouldBe(new VsixSource("/home/alice/.local/share/phone-dash/phone-dash-focus.vsix", IsRemote: false));
    }

    [Fact]
    public void File_url_is_a_local_path_with_percent_decoding()
    {
        Vsix.Resolve("file:///home/alice/my%20exts/ext.vsix", Home)
            .ShouldBe(new VsixSource("/home/alice/my exts/ext.vsix", IsRemote: false));
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Vsix.Resolve("  /opt/ext.vsix ", Home).Location.ShouldBe("/opt/ext.vsix");
    }

    [Theory]
    [InlineData("exts/ext.vsix")]
    [InlineData("./ext.vsix")]
    [InlineData("~alice/ext.vsix")]
    [InlineData("ftp://example.com/ext.vsix")]
    [InlineData("")]
    public void Unsupported_forms_are_rejected_with_the_value_in_the_message(string url)
    {
        var ex = Should.Throw<InvalidOperationException>(() => Vsix.Resolve(url, Home));
        ex.Message.ShouldContain($"'{url}'");
    }
}

public class VsixReadIdTests
{
    internal static MemoryStream BuildVsix(string? packageJson, string entryName = "extension/package.json")
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("extension.vsixmanifest");
            if (packageJson is not null)
            {
                using var w = new StreamWriter(zip.CreateEntry(entryName).Open(), Encoding.UTF8);
                w.Write(packageJson);
            }
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Reads_publisher_dot_name_from_the_manifest()
    {
        using var vsix = BuildVsix("""{ "publisher": "phonedash", "name": "phone-dash-focus", "version": "0.1.0" }""");

        Vsix.ReadId(vsix).ShouldBe("phonedash.phone-dash-focus");
    }

    [Fact]
    public void Missing_manifest_is_invalid_data()
    {
        using var vsix = BuildVsix(null);

        Should.Throw<InvalidDataException>(() => Vsix.ReadId(vsix))
            .Message.ShouldContain("extension/package.json");
    }

    [Fact]
    public void Manifest_without_publisher_is_invalid_data()
    {
        using var vsix = BuildVsix("""{ "name": "phone-dash-focus" }""");

        Should.Throw<InvalidDataException>(() => Vsix.ReadId(vsix)).Message.ShouldContain("publisher");
    }

    [Fact]
    public void Unparseable_manifest_is_invalid_data()
    {
        using var vsix = BuildVsix("not json");

        Should.Throw<InvalidDataException>(() => Vsix.ReadId(vsix));
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_invalid_data()
    {
        using var notZip = new MemoryStream(Encoding.UTF8.GetBytes("<html>404</html>"));

        Should.Throw<InvalidDataException>(() => Vsix.ReadId(notZip));
    }

    [Fact]
    public void Matching_id_is_accepted_case_insensitively()
    {
        Should.NotThrow(() => Vsix.EnsureId("PhoneDash.Phone-Dash-Focus", "phonedash.phone-dash-focus", "/x.vsix"));
    }

    [Fact]
    public void Mismatched_id_is_refused_naming_both_ids_and_the_source()
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => Vsix.EnsureId("someone.other-ext", "phonedash.phone-dash-focus", "/x.vsix"));

        ex.Message.ShouldContain("someone.other-ext");
        ex.Message.ShouldContain("phonedash.phone-dash-focus");
        ex.Message.ShouldContain("/x.vsix");
    }
}

public class VsixIsCurrentTests
{
    private const string Id = "phonedash.phone-dash-focus";
    private static readonly VsixSource Local = new("/home/alice/ext.vsix", IsRemote: false);
    private static readonly VsixSource Remote = new("https://example.com/ext.vsix", IsRemote: true);

    private static Dictionary<string, DownloadCacheEntry> Recorded(VsixSource source, string sha, string id = Id) =>
        new() { [source.Location] = new DownloadCacheEntry(sha, DateTimeOffset.UtcNow, id) };

    [Fact]
    public void Local_file_unchanged_since_install_is_current()
    {
        Vsix.IsCurrent(Recorded(Local, "aaa"), Local, Id, pinnedSha256: null, fileSha256: "aaa").ShouldBeTrue();
    }

    [Fact]
    public void Rebuilt_local_file_is_not_current()
    {
        Vsix.IsCurrent(Recorded(Local, "aaa"), Local, Id, pinnedSha256: null, fileSha256: "bbb").ShouldBeFalse();
    }

    [Fact]
    public void Never_recorded_is_not_current()
    {
        Vsix.IsCurrent(new Dictionary<string, DownloadCacheEntry>(), Local, Id, pinnedSha256: null, fileSha256: "aaa")
            .ShouldBeFalse();
    }

    [Fact]
    public void Recorded_from_a_different_path_is_not_current()
    {
        var other = new VsixSource("/home/alice/old.vsix", IsRemote: false);

        Vsix.IsCurrent(Recorded(other, "aaa"), Local, Id, pinnedSha256: null, fileSha256: "aaa").ShouldBeFalse();
    }

    [Fact]
    public void Recorded_for_a_different_id_is_not_current()
    {
        Vsix.IsCurrent(Recorded(Local, "aaa", id: "x.y"), Local, Id, pinnedSha256: null, fileSha256: "aaa")
            .ShouldBeFalse();
    }

    [Fact]
    public void Missing_local_file_counts_as_current()
    {
        // Nothing to install from, and the extension is present: don't fail.
        Vsix.IsCurrent(new Dictionary<string, DownloadCacheEntry>(), Local, Id, pinnedSha256: null, fileSha256: null)
            .ShouldBeTrue();
    }

    [Fact]
    public void Pinned_sha_is_compared_instead_of_the_file()
    {
        Vsix.IsCurrent(Recorded(Local, "aaa"), Local, Id, pinnedSha256: "AAA", fileSha256: "bbb").ShouldBeTrue();
        Vsix.IsCurrent(Recorded(Local, "aaa"), Local, Id, pinnedSha256: "ccc", fileSha256: "aaa").ShouldBeFalse();
    }

    [Fact]
    public void Remote_without_pin_is_current_once_its_url_is_recorded()
    {
        // Like deb/flatpak bundles: a remote url isn't re-downloaded just to check it.
        Vsix.IsCurrent(Recorded(Remote, "aaa"), Remote, Id, pinnedSha256: null, fileSha256: null).ShouldBeTrue();
        Vsix.IsCurrent(new Dictionary<string, DownloadCacheEntry>(), Remote, Id, pinnedSha256: null, fileSha256: null)
            .ShouldBeFalse();
    }

    [Fact]
    public void Remote_with_pin_must_match_the_recorded_sha()
    {
        Vsix.IsCurrent(Recorded(Remote, "aaa"), Remote, Id, pinnedSha256: "bbb", fileSha256: null).ShouldBeFalse();
    }
}
