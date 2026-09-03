using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.Http;

// The preload list is a large immutable data set: ~95,000 host names that never expire and carry a single
// bit of information each. Materializing it as a dictionary of policy objects cost about 28 MB of
// permanently rooted heap for 1.3 MB of names, so it is kept in the shape it was compiled in: per label
// count, one blob of ordinally sorted lower-case ASCII names, an offset table, and a bitmap holding the
// includeSubdomains flags. Lookups binary-search the blob in place and allocate nothing.
internal sealed partial class HstsPreloadList
{
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
    public IEnumerable<(string Host, bool IncludeSubdomains)> GetEntries()
    {
        foreach (var bucket in _buckets)
        {
            for (var i = 0; i < bucket.Count; i++)
            {
                yield return bucket.GetEntry(i);
            }
        }
    }

    private static HstsPreloadList Load()
    {
        var resources = GetResources();
        var buckets = new Bucket[resources.Length];
        for (var i = 0; i < resources.Length; i++)
        {
            buckets[i] = Bucket.Load(resources[i].ResourceName, resources[i].EntryCount);
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
        private readonly byte[] _names;
        private readonly int[] _offsets;
        private readonly byte[] _includeSubdomains;

        private Bucket(byte[] names, int[] offsets, byte[] includeSubdomains)
        {
            _names = names;
            _offsets = offsets;
            _includeSubdomains = includeSubdomains;
        }

        public int Count => _offsets.Length - 1;

        public bool TryGetValue(ReadOnlySpan<char> host, out bool includeSubdomains)
        {
            var low = 0;
            var high = Count - 1;
            while (low <= high)
            {
                var middle = (int)(((uint)low + (uint)high) / 2);
                var comparison = Compare(host, GetName(middle));
                if (comparison == 0)
                {
                    includeSubdomains = GetIncludeSubdomains(middle);
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

            includeSubdomains = false;
            return false;
        }

        public (string Host, bool IncludeSubdomains) GetEntry(int index)
            => (Encoding.ASCII.GetString(GetName(index)), GetIncludeSubdomains(index));

        private ReadOnlySpan<byte> GetName(int index)
            => _names.AsSpan(_offsets[index], _offsets[index + 1] - _offsets[index]);

        private bool GetIncludeSubdomains(int index)
            => (_includeSubdomains[index >> 3] & (1 << (index & 7))) != 0;

        // The blob holds lower-case names sorted with an ordinal comparison, so the probe is folded to
        // lower case as it is compared: a host name is matched case-insensitively.
        private static int Compare(ReadOnlySpan<char> probe, ReadOnlySpan<byte> name)
        {
            var length = Math.Min(probe.Length, name.Length);
            for (var i = 0; i < length; i++)
            {
                var left = (byte)(probe[i] is >= 'A' and <= 'Z' ? probe[i] + ' ' : probe[i]);
                var right = name[i];
                if (left != right)
                    return left - right;
            }

            return probe.Length - name.Length;
        }

        public static Bucket Load(string? resourceName, int entryCount)
        {
            if (resourceName is null || entryCount == 0)
                return new Bucket([], [0], []);

            // The resource name and the entry count come from the generated file: a mismatch means the
            // package was built from an inconsistent tree, so say which resource is at fault instead of
            // failing inside the decompression stream.
            using var stream = typeof(HstsPreloadList).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"The embedded resource '{resourceName}' is missing from the assembly.");

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            try
            {
                var count = reader.ReadInt32();
                if (count != entryCount)
                    throw new InvalidOperationException($"The embedded resource '{resourceName}' declares {count} entries but the generated code expects {entryCount}.");

                var offsets = new int[count + 1];
                var total = 0;
                for (var i = 0; i < count; i++)
                {
                    offsets[i] = total;
                    total += reader.ReadByte();
                }

                offsets[count] = total;

                var names = ReadExactly(reader, total, resourceName);
                var includeSubdomains = ReadExactly(reader, (count + 7) / 8, resourceName);

                // Checked in both directions: a resource holding more than the generated code expects would
                // otherwise silently protect fewer hosts than the package was built with
                if (reader.BaseStream.ReadByte() != -1)
                    throw new InvalidOperationException($"The embedded resource '{resourceName}' contains more data than the expected {entryCount} entries.");

                return new Bucket(names, offsets, includeSubdomains);
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
            {
                throw new InvalidOperationException($"The embedded resource '{resourceName}' does not contain the expected {entryCount} entries.", ex);
            }
        }

        private static byte[] ReadExactly(BinaryReader reader, int count, string resourceName)
        {
            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
                throw new InvalidOperationException($"The embedded resource '{resourceName}' ended after {bytes.Length} of the {count} bytes that were expected.");

            return bytes;
        }
    }
}
