using System.Buffers;

namespace Meziantou.Framework.Geo.Tests;

public sealed class WellKnownBinaryTests
{
    public static TheoryData<string> GdalGeometries()
    {
        var data = new TheoryData<string>();
        foreach (var line in ReadGdalGeometries())
        {
            data.Add(line[0]);
        }

        return data;
    }

    private static IEnumerable<string[]> ReadGdalGeometries() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", "wkt", "gdal-geometries.tsv"))
        .Where(line => line is not ['#', ..])
        .Select(line => line.Split('\t'));

    // Each line was written by GDAL: the WKT and WKB readers must agree with it, and the writers must write the same WKB
    [Theory]
    [MemberData(nameof(GdalGeometries))]
    public void Gdal(string wkt)
    {
        var columns = ReadGdalGeometries().Single(line => line[0] == wkt);
        var geometry = WellKnownText.Parse(wkt);
        var text = WellKnownText.Format(geometry);
        Assert.Equal(text, WellKnownText.Format(WellKnownBinary.ParseHex(columns[1])));
        Assert.Equal(text, WellKnownText.Format(WellKnownBinary.ParseHex(columns[2])));
        if (columns[3] is not "-")
        {
            Assert.Equal(text, WellKnownText.Format(WellKnownBinary.ParseHex(columns[3])));
        }

        if (columns[4] is not "-")
        {
            // The writers keep the M values, as GDAL's ISO WKB does; the last two columns are what GDAL writes without them
            Assert.Equal(columns[1], WellKnownBinary.FormatHex(geometry));
            var withoutMeasures = geometry.Transform(position => position with { Measure = null });
            Assert.Equal(WellKnownText.Format(withoutMeasures), WellKnownText.Format(WellKnownText.Parse(columns[4])));
            Assert.Equal(columns[5], WellKnownBinary.FormatHex(withoutMeasures));
        }
    }

    [Fact]
    public void Read_ExtendedWkbWithSrid()
    {
        // Written by the PGDump driver of GDAL, as PostGIS writes them: SRID 4326 and the Z flag on the root, the Z flag on the parts
        var point = Assert.IsType<GeoPoint>(WellKnownBinary.ParseHex("01010000A0E6100000A835CD3B4ED1024076E09C11A56D48400000000000000000"));
        Assert.Equal(new GeoPosition(2.3522, 48.8566, 0), point.Position);
        var lineString = Assert.IsType<GeoLineString>(WellKnownBinary.ParseHex("01020000A0E610000002000000000000000000000000000000000000000000000000002440000000000000F03F000000000000F03F0000000000803440"));
        Assert.Equal([new GeoPosition(0, 0, 10), new(1, 1, 20.5)], lineString.Positions);
        var multiPolygon = Assert.IsType<GeoMultiPolygon>(WellKnownBinary.ParseHex("01060000A0E61000000100000001030000800100000004000000000000000000000000000000000000000000000000000000000000000000F03F00000000000000000000000000000000000000000000F03F000000000000F03F0000000000000000000000000000000000000000000000000000000000000000"));
        Assert.Equal([new GeoPosition(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 0, 0)], Assert.Single(Assert.Single(multiPolygon.Polygons).Rings));
        var collection = Assert.IsType<GeoGeometryCollection>(WellKnownBinary.ParseHex("01070000A0E6100000020000000101000080000000000000F03F00000000000000400000000000000000010200008002000000000000000000000000000000000000000000000000000000000000000000F03F000000000000F03F0000000000000000"));
        Assert.Equal([new GeoPosition(1, 2, 0), new(0, 0, 0), new(1, 1, 0)], collection.GetPositions());

        WellKnownBinary.ParseHex("01070000A0E6100000020000000101000080000000000000F03F00000000000000400000000000000000010200008002000000000000000000000000000000000000000000000000000000000000000000F03F000000000000F03F0000000000000000", out var srid);
        Assert.Equal(4326, srid);

        // SRID 2154, big-endian
        WellKnownBinary.Read(Convert.FromHexString("00200000010000086A3FF00000000000004000000000000000"), out srid);
        Assert.Equal(2154, srid);

        // Without the SRID flag, and with an SRID on a part only (the SRID of the root counts)
        WellKnownBinary.ParseHex("0101000000000000000000F03F0000000000000040", out srid);
        Assert.Null(srid);
        WellKnownBinary.ParseHex("010400000001000000" + "0101000020E6100000000000000000F03F0000000000000040", out srid);
        Assert.Null(srid);
    }

    [Fact]
    public void Read_ExtendedWkbWithMeasures()
    {
        // POINT M (1 2 4) and POINT ZM (1 2 3 4) with the EWKB flags, big-endian
        Assert.Equal(new GeoPosition(1, 2) { Measure = 4 }, Assert.IsType<GeoPoint>(WellKnownBinary.ParseHex("00400000013FF000000000000040000000000000004010000000000000")).Position);
        Assert.Equal(new GeoPosition(1, 2, 3) { Measure = 4 }, Assert.IsType<GeoPoint>(WellKnownBinary.ParseHex("00C00000013FF0000000000000400000000000000040080000000000004010000000000000")).Position);
    }

    [Fact]
    public void Write_Measures()
    {
        // POINT M (1 2 3): type 2001; POINT ZM (1 2 3 4): type 3001
        Assert.Equal("01D1070000000000000000F03F00000000000000400000000000000840", WellKnownBinary.FormatHex(new GeoPoint(new GeoPosition(1, 2) { Measure = 3 })));
        Assert.Equal("01B90B0000000000000000F03F000000000000004000000000000008400000000000001040", WellKnownBinary.FormatHex(new GeoPoint(new GeoPosition(1, 2, 3) { Measure = 4 })));

        // A missing measure is written as NaN, which reads back as no measure; the times are not written
        GeoGeometry[] geometries =
        [
            new GeoLineString([new GeoPosition(0, 0) { Measure = 1 }, new GeoPosition(1, 1), new GeoPosition(2, 2, 3) { Measure = -4, Time = DateTimeOffset.UnixEpoch }]),
            new GeoMultiPoint([new GeoPosition(1, 2) { Measure = 3 }, new GeoPosition(4, 5) { Measure = 6 }]),
            new GeoMultiPolygon([new GeoPolygon([[new GeoPosition(0, 0) { Measure = 1 }, new GeoPosition(1, 0) { Measure = 2 }, new GeoPosition(1, 1) { Measure = 3 }, new GeoPosition(0, 0) { Measure = 1 }]])]),
            new GeoGeometryCollection([new GeoPoint(new GeoPosition(1, 2) { Measure = 3 }), new GeoMultiLineString([new GeoLineString([new GeoPosition(0, 0) { Measure = 0 }, new GeoPosition(1, 1) { Measure = 1 }])])]),
        ];

        foreach (var geometry in geometries)
        {
            var copy = WellKnownBinary.Read(WellKnownBinary.Write(geometry));
            Assert.Equal(geometry.Type, copy.Type);
            Assert.Equal(geometry.GetPositions().Select(position => position with { Altitude = geometry.GetPositions().Any(p => p.Altitude is not null) ? position.Altitude ?? 0 : null, Time = null }), copy.GetPositions());
        }
    }

    [Fact]
    public void Read_MixedByteOrders()
    {
        // A big-endian multipoint of a little-endian point and a big-endian point
        var multiPoint = Assert.IsType<GeoMultiPoint>(WellKnownBinary.ParseHex("000000000400000002" + "0101000000000000000000F03F0000000000000040" + "000000000140080000000000004010000000000000"));
        Assert.Equal([new GeoPosition(1, 2), new(3, 4)], multiPoint.Positions);
    }

    [Fact]
    public void Read_EmptyPoints()
    {
        Assert.Empty(Assert.IsType<GeoMultiPoint>(WellKnownBinary.ParseHex("0101000000000000000000F87F000000000000F87F")).Positions);

        // MULTIPOINT (EMPTY, (1 2)): the empty point is skipped
        var multiPoint = Assert.IsType<GeoMultiPoint>(WellKnownBinary.ParseHex("010400000002000000" + "0101000000000000000000F87F000000000000F87F" + "0101000000000000000000F03F0000000000000040"));
        Assert.Equal([new GeoPosition(1, 2)], multiPoint.Positions);

        // A NaN altitude is no altitude
        Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(WellKnownBinary.ParseHex("01E9030000000000000000F03F0000000000000040000000000000F87F")).Position);
    }

    [Theory]
    [InlineData("", "The WKB is truncated at offset 0")]
    [InlineData("01", "The WKB is truncated at offset 1")]
    [InlineData("0101000000000000000000F03F", "The WKB is truncated at offset 13")]
    [InlineData("0201000000000000000000F03F0000000000000040", "The byte order 2 is not valid, expected 0 or 1 at offset 0")]
    [InlineData("0101000000000000000000F03F000000000000004000", "The WKB has 1 bytes after the geometry, at offset 21")]
    [InlineData("0108000000000000000000F03F0000000000000040", "The geometry type 8 is not supported at offset 1")]
    [InlineData("0111000000000000000000F03F0000000000000040", "The geometry type 17 is not supported at offset 1")]
    [InlineData("01A10F0000000000000000F03F0000000000000040", "The geometry type 4001 is not supported at offset 1")]
    [InlineData("0100000000000000000000F03F0000000000000040", "The geometry type 0 is not supported at offset 1")]
    [InlineData("0102000000FFFFFF7F", "The count 2147483647 exceeds the size of the data at offset 5")]
    [InlineData("0102000000FFFFFFFF", "The count 4294967295 exceeds the size of the data at offset 5")]
    [InlineData("0103000000FFFFFF7F00000000", "The count 2147483647 exceeds the size of the data at offset 5")]
    [InlineData("0107000000FFFFFF7F0107000000000000000107000000000000000107000000000000000107000000000000000107000000000000000107000000000000000107000000000000", "The count 2147483647 exceeds the size of the data at offset 5")]
    [InlineData("010400000001000000010200000000000000", "A Point was expected but the geometry is a LineString at offset 10")]
    [InlineData("010600000001000000010200000000000000", "A Polygon was expected but the geometry is a LineString at offset 10")]
    public void Read_Invalid(string hex, string message)
    {
        var exception = Assert.Throws<FormatException>(() => WellKnownBinary.ParseHex(hex));
        Assert.StartsWith(message, exception.Message);
    }

    [Fact]
    public void Read_NestingLimit()
    {
        var deep = string.Concat(Enumerable.Repeat("010700000001000000", 10_000)) + "010700000000000000";
        Assert.Throws<FormatException>(() => WellKnownBinary.ParseHex(deep));
        var shallow = string.Concat(Enumerable.Repeat("010700000001000000", 50)) + "0101000000000000000000F03F0000000000000040";
        Assert.Equal([new GeoPosition(1, 2)], WellKnownBinary.ParseHex(shallow).GetPositions());
    }

    [Fact]
    public void ParseHex_Invalid()
    {
        Assert.Throws<FormatException>(() => WellKnownBinary.ParseHex("010"));
        Assert.Throws<FormatException>(() => WellKnownBinary.ParseHex("0x0101"));
        Assert.Throws<ArgumentNullException>(() => WellKnownBinary.ParseHex(null!));
    }

    [Fact]
    public void Write_Formats()
    {
        Assert.Equal("0101000000000000000000F03F0000000000000040", WellKnownBinary.FormatHex(new GeoPoint(new(1, 2))));
        Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(WellKnownBinary.ParseHex("0101000000000000000000f03f0000000000000040")).Position);

        // A geometry has a single number of dimensions: a missing altitude is written as 0
        var lineString = new GeoLineString([new(0, 1), new(2, 3, 4)]);
        Assert.Equal([new GeoPosition(0, 1, 0), new(2, 3, 4)], WellKnownBinary.Read(WellKnownBinary.Write(lineString)).GetPositions());

        var buffer = new ArrayBufferWriter<byte>();
        WellKnownBinary.Write(new GeoPoint(new(1, 2)), buffer);
        WellKnownBinary.Write(new GeoPoint(new(3, 4)), buffer);
        Assert.Equal(42, buffer.WrittenCount);
        Assert.Equal(new GeoPosition(3, 4), Assert.IsType<GeoPoint>(WellKnownBinary.Read(buffer.WrittenSpan[21..])).Position);
    }

    [Fact]
    public void Write_NotFinite()
    {
        Assert.Throws<ArgumentException>(() => WellKnownBinary.Write(new GeoPoint(new(double.NaN, 2))));
        Assert.Throws<ArgumentException>(() => WellKnownBinary.FormatHex(new GeoMultiPoint([new(1, 2, double.NegativeInfinity)])));
        Assert.Throws<ArgumentNullException>(() => WellKnownBinary.Write(null!));
    }

    [Fact]
    public void ReadWrite_Example()
    {
        #region Geo_WellKnownBinaryReadWrite
        // The WKB of a geometry column, as read from a database or a GeoParquet file
        var polygon = new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 0)]]);
        byte[] wkb = WellKnownBinary.Write(polygon);

        var geometry = WellKnownBinary.Read(wkb);

        Assert.Equal(GeoGeometryType.Polygon, geometry.Type);
        Assert.Equal(polygon.Rings[0], ((GeoPolygon)geometry).Rings[0]);
        #endregion
    }

    [Fact]
    public void Hex_Example()
    {
        #region Geo_WellKnownBinaryHex
        // PostGIS writes a geometry value as the hexadecimal text of its extended WKB (here with SRID 4326)
        var geometry = WellKnownBinary.ParseHex("0101000020E6100000A835CD3B4ED1024076E09C11A56D4840");
        Assert.Equal(new GeoPosition(2.3522, 48.8566), ((GeoPoint)geometry).Position);

        // PostGIS reads this text back with ST_GeomFromWKB(decode(@hex, 'hex'), 4326)
        var hex = WellKnownBinary.FormatHex(geometry);
        Assert.Equal("0101000000A835CD3B4ED1024076E09C11A56D4840", hex);
        #endregion
    }
}
