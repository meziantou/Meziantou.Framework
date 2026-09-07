using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.Http;

// The preload list is a large immutable data set: ~95,000 host names that never expire and carry a single
// bit of information each. Materializing it as a dictionary of policy objects cost about 28 MB of
// permanently rooted heap for 1.3 MB of names, so it is kept in the shape it was compiled in: per label
// count, one blob of lower-case ASCII names and a bitmap holding the includeSubdomains flags. Lookups
// binary-search the blob in place and allocate nothing.
//
// A lookup is an exact match, never a range query, so the names do not have to be in one ordinal sequence:
// they are grouped by length, each group ordinally sorted. That makes every group a run of fixed-width
// records, which costs nothing to index — no length byte per name in the resource and no offset table on the
// heap — and lets a probe be compared as a whole span against only the names it could possibly equal.
internal sealed partial class HstsPreloadList
{
    // A host name is at most 253 bytes, so the probe a lookup folds onto the stack is bounded by it and a
    // resource claiming to hold anything longer is rejected rather than trusted.
    private const int MaxHostNameLength = 253;

    // The resources are committed in two compressed forms and each target framework embeds only the one it can
    // read, so neither the assembly nor the package carries the other's copy. Zstandard arrived in the BCL with
    // .NET 11 and is worth the split: it holds this data about 13% smaller than gzip and the list loads about
    // 20% faster from it. See tools/Meziantou.Framework.Http.Hsts.Generator, which writes both.
#if NET11_0_OR_GREATER
    private const string ResourceExtension = ".zst";
#else
    private const string ResourceExtension = ".bin";
#endif

    private readonly Bucket[] _buckets;

    private HstsPreloadList(Bucket[] buckets) => _buckets = buckets;

    /// <summary>Gets the instance shared by every collection built with the preload list.</summary>
    /// <remarks>
    /// Loading costs a few milliseconds and a couple of megabytes, so it is paid once per process and only
    /// if something asks for it. The data is immutable, so sharing it is safe.
    /// </remarks>
    public static HstsPreloadList Shared => SharedHolder.Instance;

    /// <summary>Gets a value indicating whether the preload data is available in this application.</summary>
    /// <remarks>
    /// An application can stop the preload list being loaded at all by setting the
    /// <c>Meziantou.Framework.Http.Hsts.IncludePreloadList</c> feature switch to <see langword="false"/>, which
    /// saves the memory and the startup cost of materializing it. The embedded resources still ship in the
    /// assembly either way. See readme.md.
    /// </remarks>
    public static bool IsSupported
        => !AppContext.TryGetSwitch("Meziantou.Framework.Http.Hsts.IncludePreloadList", out var enabled) || enabled;

    /// <summary>Gets the highest number of labels any preloaded host name has.</summary>
    public int MaxLabelCount => _buckets.Length;

    /// <summary>Looks a host name up. <paramref name="host"/> must be the canonicalized, ASCII form.</summary>
    public bool TryGetValue(ReadOnlySpan<char> host, int labelCount, out bool includeSubdomains)
    {
        includeSubdomains = false;

        // The names in the blob are ASCII, so a name that is not cannot be in the list. Checking once here
        // keeps the per-comparison loop free of the test.
        if (labelCount < 1 || labelCount > _buckets.Length || !Ascii.IsValid(host))
            return false;

        return _buckets[labelCount - 1].TryGetValue(host, out includeSubdomains);
    }

    /// <summary>Enumerates every preloaded entry, materializing the host names on demand.</summary>
    /// <remarks>The entries come out grouped by label count and then by name length, not in one sorted order.</remarks>
    public IEnumerable<(string Host, bool IncludeSubdomains)> GetEntries()
    {
        foreach (var bucket in _buckets)
        {
            foreach (var entry in bucket.GetEntries())
            {
                yield return entry;
            }
        }
    }

    private static HstsPreloadList Load()
    {
        var resources = GetResources();
        var buckets = new Bucket[resources.Length];
        for (var i = 0; i < resources.Length; i++)
        {
            buckets[i] = Bucket.Load(resources[i].ResourceBaseName, resources[i].EntryCount);
        }

        return new HstsPreloadList(buckets);
    }

    // A nested type so the data is loaded the first time it is used and not when the containing type is
    // first touched for any other reason
    private static class SharedHolder
    {
        public static readonly HstsPreloadList Instance = Load();
    }

    private readonly struct Bucket
    {
        // The decompressed payload, whole: the names, ordered by length and then ordinally within a length,
        // followed by the include_subdomains bits in the same order. Nothing is copied out of it.
        private readonly byte[] _payload;

        // Where each name length starts, indexed by that length and running one past the longest one so a
        // group's size is the difference with the next: _firstIndex in entries, _firstByte in payload bytes.
        private readonly int[] _firstIndex;
        private readonly int[] _firstByte;

        // Equal to the end of the names, which is where the bit for entry 0 lives
        private readonly int _includeSubdomainsOffset;

        private Bucket(byte[] payload, int[] firstIndex, int[] firstByte, int includeSubdomainsOffset)
        {
            _payload = payload;
            _firstIndex = firstIndex;
            _firstByte = firstByte;
            _includeSubdomainsOffset = includeSubdomainsOffset;
        }

        public int Count => _firstIndex[^1];

        private int LongestNameLength => _firstIndex.Length - 2;

        public bool TryGetValue(ReadOnlySpan<char> host, out bool includeSubdomains)
        {
            includeSubdomains = false;

            // Only names of exactly this length can match, and if the resource holds none there is nothing
            // to search: both checks come before anything is written to the stack.
            if (host.Length == 0 || host.Length > LongestNameLength)
                return false;

            var count = _firstIndex[host.Length + 1] - _firstIndex[host.Length];
            if (count == 0)
                return false;

            // The blob holds lower-case names, so the probe is folded once here rather than a character at a
            // time inside every comparison: a host name is matched case-insensitively and the comparisons
            // themselves are whole-span compares.
            Span<byte> probe = stackalloc byte[host.Length];
            for (var i = 0; i < host.Length; i++)
            {
                probe[i] = (byte)(host[i] is >= 'A' and <= 'Z' ? host[i] + ' ' : host[i]);
            }

            var names = _payload.AsSpan(_firstByte[host.Length], count * host.Length);
            var low = 0;
            var high = count - 1;
            while (low <= high)
            {
                var middle = (int)(((uint)low + (uint)high) / 2);
                var comparison = probe.SequenceCompareTo(names.Slice(middle * host.Length, host.Length));
                if (comparison == 0)
                {
                    includeSubdomains = GetIncludeSubdomains(_firstIndex[host.Length] + middle);
                    return true;
                }

                if (comparison < 0)
                {
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return false;
        }

        public IEnumerable<(string Host, bool IncludeSubdomains)> GetEntries()
        {
            for (var length = 1; length <= LongestNameLength; length++)
            {
                var start = _firstByte[length];
                var count = _firstIndex[length + 1] - _firstIndex[length];
                for (var i = 0; i < count; i++)
                {
                    yield return (Encoding.ASCII.GetString(_payload, start + (i * length), length), GetIncludeSubdomains(_firstIndex[length] + i));
                }
            }
        }

        private bool GetIncludeSubdomains(int index)
            => (_payload[_includeSubdomainsOffset + (index >> 3)] & (1 << (index & 7))) != 0;

        // The resource is read in three bulk reads rather than one call per entry: at ~86,000 entries for the
        // largest bucket, pulling a table through the decompression stream a byte at a time cost more than
        // decompressing it.
        public static Bucket Load(string? resourceBaseName, int entryCount)
        {
            if (resourceBaseName is null || entryCount == 0)
                return new Bucket([], [0, 0], [0, 0], 0);

            // The resource name and the entry count come from the generated file: a mismatch means the
            // package was built from an inconsistent tree, so say which resource is at fault instead of
            // failing inside the decompression stream.
            var resourceName = resourceBaseName + ResourceExtension;
            using var stream = typeof(HstsPreloadList).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"The embedded resource '{resourceName}' is missing from the assembly.");

#if NET11_0_OR_GREATER
            using Stream decompressed = new ZstandardStream(stream, CompressionMode.Decompress);
#else
            using Stream decompressed = new GZipStream(stream, CompressionMode.Decompress);
#endif
            try
            {
                Span<byte> header = stackalloc byte[2 * sizeof(int)];
                decompressed.ReadExactly(header);

                var count = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (count != entryCount)
                    throw new InvalidOperationException($"The embedded resource '{resourceName}' declares {count} entries but the generated code expects {entryCount}.");

                var longestNameLength = BinaryPrimitives.ReadInt32LittleEndian(header[sizeof(int)..]);
                if (longestNameLength is < 1 or > MaxHostNameLength)
                    throw new InvalidOperationException($"The embedded resource '{resourceName}' declares a longest name of {longestNameLength} bytes, outside the 1 to {MaxHostNameLength} a host name can be.");

                // How many names there are of each length, which the loop below turns into where each length
                // starts. Every group is checked against what is left of the declared entries, so a corrupt
                // table cannot make the offsets run past the payload.
                var groupSizes = new byte[longestNameLength * sizeof(int)];
                decompressed.ReadExactly(groupSizes);

                var firstIndex = new int[longestNameLength + 2];
                var firstByte = new int[longestNameLength + 2];
                var index = 0;
                var offset = 0;
                for (var length = 1; length <= longestNameLength; length++)
                {
                    firstIndex[length] = index;
                    firstByte[length] = offset;

                    var groupSize = BinaryPrimitives.ReadInt32LittleEndian(groupSizes.AsSpan((length - 1) * sizeof(int)));
                    if (groupSize < 0 || groupSize > count - index)
                        throw new InvalidOperationException($"The embedded resource '{resourceName}' declares {groupSize} names of length {length}, which does not fit its {count} entries.");

                    index += groupSize;
                    offset += groupSize * length;
                }

                firstIndex[longestNameLength + 1] = index;
                firstByte[longestNameLength + 1] = offset;

                if (index != count)
                    throw new InvalidOperationException($"The embedded resource '{resourceName}' groups {index} names by length but declares {count} entries.");

                var payload = new byte[offset + ((count + 7) / 8)];
                decompressed.ReadExactly(payload);

                // Checked in both directions: a resource holding more than the generated code expects would
                // otherwise silently protect fewer hosts than the package was built with
                if (decompressed.ReadByte() != -1)
                    throw new InvalidOperationException($"The embedded resource '{resourceName}' contains more data than the expected {entryCount} entries.");

                return new Bucket(payload, firstIndex, firstByte, offset);
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
            {
                throw new InvalidOperationException($"The embedded resource '{resourceName}' does not contain the expected {entryCount} entries.", ex);
            }
        }
    }
}
