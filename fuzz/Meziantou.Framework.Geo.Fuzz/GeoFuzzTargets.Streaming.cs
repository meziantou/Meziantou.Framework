using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Geo.Fuzz;

public static partial class GeoFuzzTargets
{
    /// <summary>
    /// <c>chunk, text</c>: the GeoJSON document, read by a stream whose reads return at most <c>1 + chunk % 64</c> bytes
    /// (or any number when <c>chunk</c> is 255). <see cref="GeoJson.ReadFeaturesAsync"/> returns the features that
    /// <see cref="GeoJson.Read"/> returns, and fails when it fails, with a <see cref="FormatException"/>. The members of the
    /// root object can be in any order; the streaming reader only differs when the root has several <c>type</c>,
    /// <c>features</c> or <c>name</c> members, or a <c>features</c> member and another type than <c>FeatureCollection</c>.
    /// </summary>
    private static void GeoJsonStreamTarget(ReadOnlySpan<byte> data)
    {
        var chunk = FuzzInput.ReadByte(ref data);
        var bytes = data.ToArray();
        var expected = Read(() => GeoJson.Read(new MemoryStream(bytes, writable: false)));
        var (actual, error) = ReadAll(() => GeoJson.ReadFeaturesAsync(new ShortReadStream(bytes, GetChunkLength(chunk), canSeek: false)));
        if (IsAmbiguousGeoJson(bytes))
            return;

        FuzzAssert.Equal(expected is null, error is not null, $"GeoJson.Read and GeoJson.ReadFeaturesAsync disagree on whether the document is valid: {error?.Message}");
        if (expected is not null)
        {
            expected.Name = null;
            FuzzAssert.Equal(Describe(expected), Describe(actual), "GeoJson.ReadFeaturesAsync returns other features than GeoJson.Read");
        }
    }

    /// <summary>
    /// <c>chunk</c>, then the input of the shapefile target: <see cref="Shapefile.ReadFeaturesAsync(Stream, Stream?, ShapefileReaderOptions?, CancellationToken)"/>
    /// and <see cref="Shapefile.ReadZipFeaturesAsync"/> return the features that <see cref="Shapefile.Read(Stream, Stream?, ShapefileReaderOptions?)"/>
    /// and <see cref="Shapefile.ReadZip(Stream, ShapefileReaderOptions?)"/> return, from streams whose reads return at most
    /// <c>1 + chunk % 64</c> bytes, and fail when they fail.
    /// </summary>
    private static void ShapefileStreamTarget(ReadOnlySpan<byte> data)
    {
        var chunk = FuzzInput.ReadByte(ref data);
        var flags = FuzzInput.ReadByte(ref data);
        var length = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var shp = data[..Math.Min(length, data.Length)].ToArray();
        var dbf = (flags & 1) is 0 ? null : data[shp.Length..].ToArray();
        var options = new ShapefileReaderOptions { Encoding = (flags & 4) is 0 ? null : Encoding.Latin1 };
        var chunkLength = GetChunkLength(chunk);

        GeoFeatureCollection? expected;
        List<GeoFeature> actual;
        Exception? error;
        if ((flags & 2) is 0)
        {
            expected = Read(() => Shapefile.Read(new MemoryStream(shp, writable: false), dbf is null ? null : new MemoryStream(dbf, writable: false), options));
            (actual, error) = ReadAll(() => Shapefile.ReadFeaturesAsync(new ShortReadStream(shp, chunkLength, canSeek: false), dbf is null ? null : new ShortReadStream(dbf, chunkLength, canSeek: false), options));
        }
        else
        {
            var zip = Zip(shp, dbf);
            expected = Read(() => Shapefile.ReadZip(new MemoryStream(zip, writable: false), options));
            (actual, error) = ReadAll(() => Shapefile.ReadZipFeaturesAsync(new ShortReadStream(zip, chunkLength, canSeek: true), options: options));
        }

        FuzzAssert.Equal(expected is null, error is not null, $"Shapefile.Read and Shapefile.ReadFeaturesAsync disagree on whether the files are valid: {error?.Message}");
        if (expected is not null)
        {
            expected.Name = null;
            FuzzAssert.Equal(Describe(expected), Describe(actual), "Shapefile.ReadFeaturesAsync returns other features than Shapefile.Read");
        }
    }

    /// <summary>
    /// <c>chunk</c>, then the input of the FlatGeobuf target. <see cref="FlatGeobuf.ReadFeaturesAsync(Stream, FlatGeobufReaderOptions?, CancellationToken)"/>
    /// returns the features of <see cref="FlatGeobuf.Read(ReadOnlySpan{byte}, FlatGeobufReaderOptions?)"/>, from a stream that
    /// cannot seek and whose reads return at most <c>1 + chunk % 64</c> bytes, and fails when it fails. With a box (bit 0 of
    /// <c>options</c>), a seekable stream reads the features of the spatial index, as the reader of memory does, and a
    /// stream that cannot seek reads the features whose bounds intersect the box. A read that only visits a part of the
    /// data may succeed where a read of the whole data fails.
    /// </summary>
    private static void FlatGeobufStreamTarget(ReadOnlySpan<byte> data)
    {
        var chunk = FuzzInput.ReadByte(ref data);
        var options = FuzzInput.ReadByte(ref data);
        var box = ReadBox(ref data);
        var bytes = data.ToArray();
        var chunkLength = GetChunkLength(chunk);

        var expected = Read(() => FlatGeobuf.Read(bytes));
        var (actual, error) = ReadAll(() => FlatGeobuf.ReadFeaturesAsync(new ShortReadStream(bytes, chunkLength, canSeek: false)));
        FuzzAssert.Equal(expected is null, error is not null, $"FlatGeobuf.Read and FlatGeobuf.ReadFeaturesAsync disagree on whether the data is valid: {error?.Message}");
        if (expected is not null)
        {
            expected.Name = null;
            FuzzAssert.Equal(Describe(expected), Describe(actual), "FlatGeobuf.ReadFeaturesAsync returns other features than FlatGeobuf.Read");
        }

        if ((options & 1) is 0)
            return;

        var readerOptions = new FlatGeobufReaderOptions { Bounds = box };
        var bounded = Read(() => FlatGeobuf.Read(bytes, readerOptions));
        var (seekable, seekableError) = ReadAll(() => FlatGeobuf.ReadFeaturesAsync(new ShortReadStream(bytes, chunkLength, canSeek: true), readerOptions));
        if (bounded is not null)
        {
            bounded.Name = null;
            FuzzAssert.True(seekableError is null, $"FlatGeobuf.ReadFeaturesAsync fails on a seekable stream with a box, where FlatGeobuf.Read succeeds: {seekableError?.Message}");
            FuzzAssert.Equal(Describe(bounded), Describe(seekable), "FlatGeobuf.ReadFeaturesAsync with a box returns other features than FlatGeobuf.Read with the box");
        }

        var (scanned, scannedError) = ReadAll(() => FlatGeobuf.ReadFeaturesAsync(new ShortReadStream(bytes, chunkLength, canSeek: false), readerOptions));
        if (expected is not null)
        {
            FuzzAssert.True(scannedError is null, $"FlatGeobuf.ReadFeaturesAsync fails on a stream that cannot seek with a box, where FlatGeobuf.Read succeeds: {scannedError?.Message}");
            var filtered = new GeoFeatureCollection(expected.Where(feature => feature.Geometry?.GetBounds() is { } bounds && Intersects(box, bounds)));
            FuzzAssert.Equal(Describe(filtered), Describe(scanned), "FlatGeobuf.ReadFeaturesAsync with a box on a stream that cannot seek does not return the features that intersect the box");
        }
    }

    // The seeds of the targets that read the whole data, read 1, 7 and any number of bytes at a time
    private static IEnumerable<FuzzSeed> GetGeoJsonStreamSeeds() => WithChunks(GetGeoJsonSeeds());

    private static IEnumerable<FuzzSeed> GetShapefileStreamSeeds() => WithChunks(GetShapefileSeeds());

    private static IEnumerable<FuzzSeed> GetFlatGeobufStreamSeeds() => WithChunks(GetFlatGeobufSeeds());

    private static IEnumerable<FuzzSeed> WithChunks(IEnumerable<FuzzSeed> seeds)
    {
        byte[] chunks = [0, 6, 255];
        var i = 0;
        foreach (var seed in seeds)
        {
            var chunk = chunks[i++ % chunks.Length];
            yield return new FuzzSeed(seed.Name, (byte[])[chunk, .. seed.Data.ToArray()]);
        }
    }

    private static int GetChunkLength(byte chunk) => chunk is 255 ? int.MaxValue : 1 + (chunk % 64);

    /// <summary>Enumerates the features of a streaming reader: the features read, and the <see cref="FormatException"/> that stopped it.</summary>
    private static (List<GeoFeature> Features, FormatException? Error) ReadAll(Func<IAsyncEnumerable<GeoFeature>> read)
    {
        var features = new List<GeoFeature>();
        try
        {
            var enumerator = read().GetAsyncEnumerator();
            try
            {
                while (WaitMoveNext(enumerator.MoveNextAsync()))
                {
                    features.Add(enumerator.Current);
                }
            }
            finally
            {
                WaitDispose(enumerator.DisposeAsync());
            }

            return (features, null);
        }
        catch (FormatException ex)
        {
            return (features, ex);
        }

        // The streams complete synchronously
        static bool WaitMoveNext(ValueTask<bool> task)
        {
            FuzzAssert.True(task.IsCompleted, "A streaming reader did not complete synchronously on a stream that completes synchronously");
            return task.GetAwaiter().GetResult();
        }

        static void WaitDispose(ValueTask task)
        {
            FuzzAssert.True(task.IsCompleted, "A streaming reader did not complete synchronously on a stream that completes synchronously");
            task.GetAwaiter().GetResult();
        }
    }

    private static string Describe(List<GeoFeature> features) => Describe(new GeoFeatureCollection(features));

    // The documented differences of the streaming reader: it streams the first "features" array of the root object, and
    // checks its type once the features are returned
    private static bool IsAmbiguousGeoJson(byte[] data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
                return false;

            var types = 0;
            var features = 0;
            var names = 0;
            var isFeatureCollection = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("type"))
                {
                    types++;
                    isFeatureCollection = property.Value.ValueKind is JsonValueKind.String && property.Value.ValueEquals("FeatureCollection");
                }
                else if (property.NameEquals("features"))
                {
                    features++;
                }
                else if (property.NameEquals("name"))
                {
                    names++;
                }
            }

            return types > 1 || features > 1 || names > 1 || (features is 1 && !isFeatureCollection);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            // Invalid JSON, or an escaped unpaired surrogate in a member name
            return false;
        }
    }
}
