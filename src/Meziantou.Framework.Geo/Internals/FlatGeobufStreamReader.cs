using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>
/// Reads the features of FlatGeobuf data from a stream, one at a time. With a box, a spatial index and a seekable stream,
/// only the nodes of the index that the search visits and the features it finds are read.
/// </summary>
internal static class FlatGeobufStreamReader
{
    // The content of a feature is read in chunks, so a hostile size does not allocate more than the data the stream has
    private const int ChunkLength = 1 << 20;

    public static async IAsyncEnumerable<GeoFeature> ReadAsync(Stream stream, GeoBounds? bounds, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var origin = stream.CanSeek ? stream.Position : 0;
        var buffer = new byte[256];
        if (!await TryReadAsync(stream, buffer.AsMemory(0, FlatGeobufReader.PrefixLength), cancellationToken).ConfigureAwait(false))
            throw new FormatException("The data is not FlatGeobuf: it does not start with the signature of the format.");

        var headerSize = FlatGeobufReader.ReadHeaderLength(buffer);
        buffer = await ReadBlockAsync(stream, buffer, headerSize, "The header of the FlatGeobuf data is truncated.", cancellationToken).ConfigureAwait(false);
        var header = FlatGeobufReader.ReadHeader(buffer.AsSpan(0, headerSize));
        var count = header.FeatureCount;
        var dataStart = origin + FlatGeobufReader.PrefixLength + headerSize;
        if (header.HasIndex)
        {
            var indexSize = PackedRTree.GetSize(count, header.IndexNodeSize);
            var treeStart = dataStart;
            dataStart += indexSize;
            if (stream.CanSeek)
            {
                if (bounds is { } box)
                {
                    var offsets = await PackedRTree.SearchAsync(stream, treeStart, count, header.IndexNodeSize, box, cancellationToken).ConfigureAwait(false);
                    foreach (var offset in offsets)
                    {
                        if (offset < 0)
                            throw new FormatException("The index of the FlatGeobuf data refers to a feature outside of the data.");

                        stream.Seek(dataStart + offset, SeekOrigin.Begin);
                        (var feature, buffer) = await ReadFeatureAsync(stream, buffer, header, cancellationToken).ConfigureAwait(false);
                        yield return feature ?? throw new FormatException("The index of the FlatGeobuf data refers to a feature outside of the data.");
                    }

                    yield break;
                }

                stream.Seek(dataStart, SeekOrigin.Begin);
            }
            else
            {
                // The index of a stream that cannot seek is read and dropped, a chunk at a time
                var skip = new byte[Math.Min(indexSize, 81920)];
                for (var remaining = indexSize; remaining > 0; remaining -= skip.Length)
                {
                    var length = (int)Math.Min(remaining, skip.Length);
                    if (!await TryReadAsync(stream, skip.AsMemory(0, length), cancellationToken).ConfigureAwait(false))
                        throw new FormatException("The index of the FlatGeobuf data is truncated.");
                }
            }
        }

        var read = 0UL;
        while (count == 0 || read < count)
        {
            (var feature, buffer) = await ReadFeatureAsync(stream, buffer, header, cancellationToken).ConfigureAwait(false);
            if (feature is null)
                break;

            read++;
            if (bounds is { } filter && !FlatGeobufReader.Intersects(filter, feature))
                continue;

            yield return feature;
        }

        if (count != 0 && read < count)
            throw new FormatException("The FlatGeobuf data has fewer features than its header declares.");
    }

    // Reads a size-prefixed feature, or returns null at the end of the stream
    private static async ValueTask<(GeoFeature? Feature, byte[] Buffer)> ReadFeatureAsync(Stream stream, byte[] buffer, FlatGeobufReader.Header header, CancellationToken cancellationToken)
    {
        var read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, 4), 4, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (read is 0)
            return (null, buffer);

        if (read < 4)
            throw new FormatException("A feature of the FlatGeobuf data is truncated.");

        var size = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        if (size > Array.MaxLength)
            throw new FormatException("A feature of the FlatGeobuf data is truncated.");

        buffer = await ReadBlockAsync(stream, buffer, (int)size, "A feature of the FlatGeobuf data is truncated.", cancellationToken).ConfigureAwait(false);
        return (FlatGeobufReader.ReadFeature(buffer.AsSpan(0, (int)size), header), buffer);
    }

    // Reads a block of data at the start of the buffer, growing it as the data is read
    private static async ValueTask<byte[]> ReadBlockAsync(Stream stream, byte[] buffer, int length, string message, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < length)
        {
            var chunk = Math.Min(length - read, ChunkLength);
            if (buffer.Length < read + chunk)
            {
                Array.Resize(ref buffer, (int)Math.Min(length, Math.Max(read + chunk, buffer.Length * 2L)));
            }

            if (!await TryReadAsync(stream, buffer.AsMemory(read, chunk), cancellationToken).ConfigureAwait(false))
                throw new FormatException(message);

            read += chunk;
        }

        return buffer;
    }

    private static async ValueTask<bool> TryReadAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken) =>
        await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) == buffer.Length;
}
