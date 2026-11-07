using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Geo.Fuzz;

public static partial class GeoFuzzTargets
{
    /// <summary>
    /// <c>options, west, south, east, north, data</c>: the four signed bytes are a box in degrees, used for a bounded read
    /// when bit 0 of <c>options</c> is set. Bit 1 writes without a spatial index, and <c>options &gt;&gt; 2</c> sets the
    /// size of the nodes of the index (2 + value). The features read are written back, and the writer is stable: reading
    /// what it wrote and writing it again gives the same bytes. A bounded read of the output gives the features whose bounds
    /// intersect the box, as a read of the whole output does once filtered.
    /// </summary>
    private static void FlatGeobufTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var box = ReadBox(ref data);
        var bytes = data.ToArray();
        var features = Read(() => FlatGeobuf.Read(bytes));
        var bounded = (options & 1) is 0 ? null : Read(() => FlatGeobuf.Read(bytes, new FlatGeobufReaderOptions { Bounds = box }));
        if (features is null)
            return;

        FuzzAssert.Equal(Describe(features), Describe(Read(() => FlatGeobuf.Read(new MemoryStream(bytes, writable: false)))), "Reading the data from a stream gives other features than reading it from memory");
        Exercise(features);
        if (bounded is not null)
        {
            Exercise(bounded);
        }

        if (!IsFinite(features))
            return;

        var writerOptions = new FlatGeobufWriterOptions { IncludeSpatialIndex = (options & 2) is 0, IndexNodeSize = (ushort)(2 + (options >> 2)) };
        var first = FlatGeobuf.Write(features, writerOptions);
        var reread = FlatGeobuf.Read(first);
        FuzzAssert.Equal(features.Count, reread.Count, "The FlatGeobuf written does not have a feature per feature read");
        Exercise(reread);
        var second = FlatGeobuf.Write(reread, writerOptions);
        FuzzAssert.Equal(Convert.ToHexString(first), Convert.ToHexString(second), "Writing the FlatGeobuf read from the output of the writer gives another FlatGeobuf");

        // The index and the scan of the features select the same features, in any order
        var expected = reread.Where(feature => feature.Geometry?.GetBounds() is { } bounds && Intersects(box, bounds)).Select(Describe).Order(StringComparer.Ordinal);
        var actual = FlatGeobuf.Read(first, new FlatGeobufReaderOptions { Bounds = box }).Select(Describe).Order(StringComparer.Ordinal);
        FuzzAssert.Equal(string.Join('\n', expected), string.Join('\n', actual), "A bounded read does not give the features that intersect the box");
    }

    private static GeoBounds ReadBox(ref ReadOnlySpan<byte> data)
    {
        var a = (sbyte)FuzzInput.ReadByte(ref data);
        var b = (sbyte)FuzzInput.ReadByte(ref data);
        var c = (sbyte)FuzzInput.ReadByte(ref data);
        var d = (sbyte)FuzzInput.ReadByte(ref data);
        return new GeoBounds(Math.Min(a, c), Math.Min(b, d), Math.Max(a, c), Math.Max(b, d));
    }

    private static bool Intersects(GeoBounds a, GeoBounds b) => b.West <= a.East && b.East >= a.West && b.South <= a.North && b.North >= a.South;

    private static string Describe(GeoFeature feature)
    {
        var collection = new GeoFeatureCollection { feature };
        return Describe(collection);
    }

    private static IEnumerable<FuzzSeed> GetFlatGeobufSeeds()
    {
        foreach (var (name, data) in GetTestFiles(".fgb"))
        {
            yield return new FuzzSeed(name, (byte[])[0, 0x80, 0x80, 0x7F, 0x7F, .. data]);
            yield return new FuzzSeed(name + "-bounded", (byte[])[1, 0xF6, 0xF6, 10, 10, .. data]);
            yield return new FuzzSeed(name + "-noindex", (byte[])[3 | (6 << 2), 0, 0, 40, 40, .. data]);
        }
    }
}
