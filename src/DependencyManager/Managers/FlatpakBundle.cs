using System.Buffers.Binary;
using System.Text;

namespace DependencyManager.Managers;

/// <summary>What a single-file <c>.flatpak</c> bundle says it contains.</summary>
/// <param name="Ref">e.g. <c>app/org.example.App/x86_64/stable</c>.</param>
/// <param name="Commit">The OSTree commit the bundle installs (hex), as <c>flatpak info --show-commit</c> reports it.</param>
/// <param name="Runtime">The app's runtime ref from its metadata, if present.</param>
public sealed record FlatpakBundleHeader(string Ref, string Commit, string? Runtime)
{
    public bool IsApp => Ref.StartsWith("app/", StringComparison.Ordinal);

    public string Id => Ref.Split('/') is { Length: > 1 } parts ? parts[1] : Ref;
}

/// <summary>
/// Reads the header of a <c>flatpak build-bundle</c> file without flatpak, so
/// depend can check the app id before installing anything.
/// </summary>
/// <remarks>
/// A bundle is an OSTree static-delta superblock: the GVariant
/// <c>(a{sv} t ay ay (commit) ay a(…) a(…))</c>. Element 0 is the metadata dict
/// (flatpak puts the <c>ref</c> and the app's <c>metadata</c> keyfile there) and
/// element 3 is the target commit checksum. Only those are decoded; the
/// serialization is little-endian on every platform flatpak bundles are built on.
/// </remarks>
public static class FlatpakBundle
{
    // Superblock tuple: 8 elements; the variable-size ones before the last
    // (0, 2, 3, 4, 5, 6) each have a framing offset at the end of the file.
    private const int TupleFramingOffsets = 6;
    private const int ChecksumLength = 32;
    // The metadata dict also carries the delta parts inline (most of the file),
    // so entries are read one at a time and anything this large is skipped:
    // only the small string entries (ref, metadata) are wanted.
    private const int MaxEntryBytes = 1024 * 1024;

    public static FlatpakBundleHeader ReadHeader(Stream stream)
    {
        try
        {
            return ReadHeaderCore(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or EndOfStreamException or OverflowException)
        {
            throw NotABundle(ex.Message);
        }
    }

    /// <summary>
    /// Refuses a bundle that isn't an app or whose app id differs from the YAML
    /// key, so depend never installs something other than what it declares.
    /// </summary>
    public static void EnsureAppId(FlatpakBundleHeader header, string expectedId, string url)
    {
        if (!header.IsApp)
            throw new InvalidOperationException(
                $"flatpak bundle {url} contains a runtime ({header.Ref}), not an app");
        if (!string.Equals(header.Id, expectedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"flatpak bundle {url} contains app '{header.Id}', but the config declares '{expectedId}'; " +
                "fix the key (or the url) so it names the app the bundle installs");
    }

    private static FlatpakBundleHeader ReadHeaderCore(Stream stream)
    {
        var size = stream.Length;
        var osz = OffsetSize(size);
        if (size < TupleFramingOffsets * osz + ChecksumLength)
            throw NotABundle("file too small");

        var tail = ReadAt(stream, size - TupleFramingOffsets * osz, TupleFramingOffsets * osz);
        // Tuple framing offsets are stored in reverse: element 0's end is the last word.
        long End(int i) => ReadOffset(tail, (TupleFramingOffsets - 1 - i) * osz, osz);
        long metadataEnd = End(0), fromEnd = End(1), toEnd = End(2);

        if (metadataEnd <= 0 || fromEnd < metadataEnd || toEnd - fromEnd != ChecksumLength || toEnd > size)
            throw NotABundle("unexpected superblock layout");

        var metadata = ReadDictionary(stream, metadataEnd);
        var commit = Convert.ToHexString(ReadAt(stream, fromEnd, ChecksumLength)).ToLowerInvariant();

        if (!metadata.TryGetValue("ref", out var flatpakRef) || flatpakRef.Split('/').Length != 4)
            throw NotABundle("no flatpak ref in bundle metadata");

        var runtime = metadata.TryGetValue("metadata", out var keyfile) ? RuntimeFromKeyfile(keyfile) : null;
        return new FlatpakBundleHeader(flatpakRef, commit, runtime);
    }

    /// <summary>
    /// Decodes the small string-valued entries of the <c>a{sv}</c> occupying
    /// bytes [0, <paramref name="dictLength"/>) of the stream.
    /// </summary>
    private static Dictionary<string, string> ReadDictionary(Stream stream, long dictLength)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var osz = OffsetSize(dictLength);

        // Array of variable-size elements: element end offsets follow the data, in
        // order, and the last one (the end of the data) is the array's final word.
        var offsetsStart = ReadOffset(ReadAt(stream, dictLength - osz, osz), 0, osz);
        if (offsetsStart > dictLength || (dictLength - offsetsStart) % osz != 0
            || dictLength - offsetsStart > MaxEntryBytes)
            throw NotABundle("malformed metadata dictionary");
        var offsets = ReadAt(stream, offsetsStart, (int)(dictLength - offsetsStart));

        long start = 0;
        for (var at = 0; at < offsets.Length; at += osz)
        {
            var end = ReadOffset(offsets, at, osz);
            if (end < start || end > offsetsStart) throw NotABundle("malformed metadata entry");
            if (end - start <= MaxEntryBytes
                && TryReadStringEntry(ReadAt(stream, start, (int)(end - start)), out var key, out var value))
                result[key] = value;
            start = Align(end, 8);
        }
        return result;
    }

    /// <summary>A <c>{sv}</c> entry: key string, framing offset at the end, variant aligned to 8.</summary>
    private static bool TryReadStringEntry(ReadOnlySpan<byte> entry, out string key, out string value)
    {
        key = value = "";
        var osz = OffsetSize(entry.Length);
        if (osz == 0 || entry.Length < osz) return false;
        var keyEnd = (int)ReadOffset(entry, entry.Length - osz, osz);
        var valueStart = (int)Align(keyEnd, 8);
        var valueEnd = entry.Length - osz;
        if (keyEnd < 1 || valueStart > valueEnd) return false;

        key = Encoding.UTF8.GetString(entry[..(keyEnd - 1)]);

        // A variant is its value, a NUL, then its type signature.
        var variant = entry[valueStart..valueEnd];
        var sep = variant.LastIndexOf((byte)0);
        if (sep < 1 || !variant[(sep + 1)..].SequenceEqual("s"u8)) return false;
        value = Encoding.UTF8.GetString(variant[..(sep - 1)]); // drop the string's own NUL
        return true;
    }

    private static string? RuntimeFromKeyfile(string keyfile)
    {
        var inApplication = false;
        foreach (var raw in keyfile.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) inApplication = line == "[Application]";
            else if (inApplication && line.StartsWith("runtime=", StringComparison.Ordinal))
                return line["runtime=".Length..];
        }
        return null;
    }

    // GVariant picks a container's framing-offset width from the container's size.
    private static int OffsetSize(long containerSize) => containerSize switch
    {
        0 => 0,
        <= byte.MaxValue => 1,
        <= ushort.MaxValue => 2,
        <= uint.MaxValue => 4,
        _ => 8,
    };

    private static long ReadOffset(ReadOnlySpan<byte> buffer, int at, int width) => width switch
    {
        1 => buffer[at],
        2 => BinaryPrimitives.ReadUInt16LittleEndian(buffer[at..]),
        4 => BinaryPrimitives.ReadUInt32LittleEndian(buffer[at..]),
        _ => checked((long)BinaryPrimitives.ReadUInt64LittleEndian(buffer[at..])),
    };

    private static long Align(long offset, int alignment) => (offset + alignment - 1) & ~(long)(alignment - 1);

    private static byte[] ReadAt(Stream stream, long position, int count)
    {
        var buffer = new byte[count];
        stream.Position = position;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static InvalidDataException NotABundle(string detail) =>
        new($"not a flatpak bundle ({detail})");
}
