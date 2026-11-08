using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo.Tests;

// The files of TestData/shapefile were written by GDAL 3.10.3 (docker image ghcr.io/osgeo/gdal:alpine-small-3.10.3):
//   ogr2ogr -f "ESRI Shapefile" points.shp points.geojson -lco ENCODING=UTF-8
//   ogr2ogr -f "ESRI Shapefile" lines.shp lines.geojson            (then lines.dbf renamed to lines.DBF)
//   ogr2ogr -f "ESRI Shapefile" polygons.shp polygons.geojson
//   ogr2ogr -f "ESRI Shapefile" <name>.shp zm.csv -where "name='<name>'" -nln <name>   (pointz, pointm, linem, polygonz, multipointzm; the CSV has a WKT column)
//   ogr2ogr -f "ESRI Shapefile" multipatch.shp zm.csv -where "name='polygonz'" -lco SHPT=MULTIPATCH
//   ogr2ogr -f "ESRI Shapefile" latin1.shp latin.geojson -lco ENCODING=ISO-8859-1
//   ogr2ogr -f "ESRI Shapefile" cp1252.shp latin.geojson -lco ENCODING=CP1252
//   ogr2ogr -f "ESRI Shapefile" countries.shp.zip polygons.geojson -nln countries -lco ENCODING=UTF-8   (renamed to countries.zip)
// The geometries and the values the tests expect are the ones "ogrinfo -al" prints for these files.
public sealed class ShapefileTests
{
    private static string GetPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "shapefile", name);

    [Fact]
    public void Read_Points()
    {
        var features = Shapefile.Read(GetPath("points.shp"));
        Assert.Equal("points", features.Name);
        Assert.HasCount(3, features);
        Assert.Equal(new GeoPosition(2.3522, 48.8566), Assert.IsType<GeoPoint>(features[0].Geometry).Position);
        Assert.Equal("Paris", features[0].Properties["name"]);
        Assert.Equal(2102650.0, features[0].Properties["population"]);
        Assert.Equal(20000.5, features[0].Properties["density"]);
        Assert.Equal(true, features[0].Properties["capital"]);
        Assert.Equal(new DateOnly(1952, 1, 31), features[0].Properties["founded"]);
        Assert.Null(features[0].Id);
        Assert.Equal(new GeoPosition(8.5417, 47.3769), Assert.IsType<GeoPoint>(features[1].Geometry).Position);
        Assert.Equal("Z\u00FCrich \u2013 \u65E5\u672C", features[1].Properties["name"]);
        Assert.Equal(4700.25, features[1].Properties["density"]);
        Assert.Equal(false, features[1].Properties["capital"]);
        Assert.Equal(new DateOnly(1218, 5, 6), features[1].Properties["founded"]);
        Assert.Null(features[2].Geometry);
        Assert.Equal(["name", "population", "density", "capital", "founded"], features[2].Properties.Keys);
        Assert.All(features[2].Properties.Values, value => value is null);
    }

    [Fact]
    public void Read_LinesWithUpperCaseDbfExtension()
    {
        var features = Shapefile.Read(GetPath("lines.shp"));
        Assert.HasCount(2, features);
        Assert.Equal(1.0, features[0].Properties["id"]);
        Assert.Equal([new GeoPosition(0, 0), new(1, 1), new(2, 0)], Assert.IsType<GeoLineString>(features[0].Geometry).Positions);
        var multiLineString = Assert.IsType<GeoMultiLineString>(features[1].Geometry);
        Assert.Equal([new GeoPosition(10, 10), new(11, 11)], multiLineString.LineStrings[0].Positions);
        Assert.Equal([new GeoPosition(12, 12), new(13, 13), new(14, 12)], multiLineString.LineStrings[1].Positions);
    }

    [Fact]
    public void Read_PolygonsWithHolesAndIslands()
    {
        var features = Shapefile.Read(GetPath("polygons.shp"));
        var polygon = Assert.IsType<GeoPolygon>(features[0].Geometry);
        Assert.Equal([new GeoPosition(0, 0), new(0, 10), new(10, 10), new(10, 0), new(0, 0)], polygon.Rings[0]);
        Assert.Equal([new GeoPosition(2, 2), new(4, 2), new(4, 4), new(2, 4), new(2, 2)], polygon.Rings[1]);
        Assert.True(polygon.Contains(new GeoPosition(5, 5)));
        Assert.False(polygon.Contains(new GeoPosition(3, 3)));

        // The island in the hole of the first polygon is a polygon
        var multiPolygon = Assert.IsType<GeoMultiPolygon>(features[1].Geometry);
        Assert.Equal([2, 1, 1], multiPolygon.Polygons.Select(item => item.Rings.Count));
        Assert.Equal(new GeoPosition(22, 2), multiPolygon.Polygons[0].Rings[1][0]);
        Assert.Equal(new GeoPosition(24, 4), multiPolygon.Polygons[1].Rings[0][0]);
        Assert.Equal(new GeoPosition(40, 0), multiPolygon.Polygons[2].Rings[0][0]);
        Assert.True(multiPolygon.Contains(new GeoPosition(25, 5)));
        Assert.False(multiPolygon.Contains(new GeoPosition(23, 5)));
    }

    [Fact]
    public void Read_ZAndM()
    {
        Assert.Equal(new GeoPosition(1, 2, 3), Assert.IsType<GeoPoint>(Assert.Single(Shapefile.Read(GetPath("pointz.shp"))).Geometry).Position);
        Assert.Equal(new GeoPosition(1, 2) { Measure = 3 }, Assert.IsType<GeoPoint>(Assert.Single(Shapefile.Read(GetPath("pointm.shp"))).Geometry).Position);

        var lines = Assert.IsType<GeoMultiLineString>(Assert.Single(Shapefile.Read(GetPath("linem.shp"))).Geometry);
        Assert.Equal([new GeoPosition(0, 0) { Measure = 1 }, new(1, 1) { Measure = 2 }], lines.LineStrings[0].Positions);
        Assert.Equal([new GeoPosition(2, 2) { Measure = 3 }, new(3, 3) { Measure = 4 }], lines.LineStrings[1].Positions);

        // The M values of a Z shape are optional: this polygon has none
        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(Shapefile.Read(GetPath("polygonz.shp"))).Geometry);
        Assert.Equal([new GeoPosition(0, 0, 1), new(0, 10, 2), new(10, 10, 3), new(10, 0, 4), new(0, 0, 1)], polygon.Rings[0]);
        Assert.Equal([new GeoPosition(2, 2, 5), new(4, 2, 6), new(4, 4, 7), new(2, 4, 8), new(2, 2, 5)], polygon.Rings[1]);

        var multiPoint = Assert.IsType<GeoMultiPoint>(Assert.Single(Shapefile.Read(GetPath("multipointzm.shp"))).Geometry);
        Assert.Equal([new GeoPosition(1, 2, 3) { Measure = 4 }, new(5, 6, 7) { Measure = 8 }], multiPoint.Positions);
    }

    [Fact]
    public void Read_MultiPatch()
    {
        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(Shapefile.Read(GetPath("multipatch.shp"))).Geometry);
        Assert.Equal([new GeoPosition(0, 0, 1), new(0, 10, 2), new(10, 10, 3), new(10, 0, 4), new(0, 0, 1)], polygon.Rings[0]);
        Assert.Equal([new GeoPosition(2, 2, 5), new(4, 2, 6), new(4, 4, 7), new(2, 4, 8), new(2, 2, 5)], polygon.Rings[1]);
    }

    [Fact]
    public void Read_MultiPatchTriangles()
    {
        // A triangle strip of 4 points and a triangle fan of 4 points
        GeoPosition[] strip = [new(0, 0, 1), new(1, 0, 2), new(0, 1, 3), new(1, 1, 4)];
        GeoPosition[] fan = [new(5, 5, 0), new(6, 5, 0), new(6, 6, 0), new(5, 6, 0)];
        var content = CreatePartsContent(ShapefileShapeType.MultiPatch, [strip, fan], partTypes: [0, 1], hasZ: true);
        using var shp = new MemoryStream(CreateShp(content));
        var multiPolygon = Assert.IsType<GeoMultiPolygon>(Assert.Single(Shapefile.Read(shp, dbf: null)).Geometry);
        Assert.HasCount(4, multiPolygon.Polygons);
        Assert.Equal([strip[0], strip[1], strip[2], strip[0]], multiPolygon.Polygons[0].Rings[0]);
        Assert.Equal([strip[1], strip[2], strip[3], strip[1]], multiPolygon.Polygons[1].Rings[0]);
        Assert.Equal([fan[0], fan[1], fan[2], fan[0]], multiPolygon.Polygons[2].Rings[0]);
        Assert.Equal([fan[0], fan[2], fan[3], fan[0]], multiPolygon.Polygons[3].Rings[0]);
    }

    [Fact]
    public void Read_Encodings()
    {
        var latin1 = Assert.Single(Shapefile.Read(GetPath("latin1.shp")));
        Assert.Equal("Caf\u00E9 cr\u00E8me", latin1.Properties["name"]);
        Assert.Equal("?uro", latin1.Properties["note"]);

        var cp1252 = Assert.Single(Shapefile.Read(GetPath("cp1252.shp")));
        Assert.Equal("Caf\u00E9 cr\u00E8me", cp1252.Properties["name"]);
        Assert.Equal("\u20ACuro", cp1252.Properties["note"]);

        // The options override the .cpg file
        Assert.Equal("\u0080uro", Assert.Single(Shapefile.Read(GetPath("cp1252.shp"), new ShapefileReaderOptions { Encoding = Encoding.Latin1 })).Properties["note"]);

        // Without .cpg file: the language driver ID, else UTF-8 when the value is valid UTF-8, else ISO-8859-1
        var dbf = File.ReadAllBytes(GetPath("cp1252.dbf"));
        dbf[29] = 0x03;
        Assert.Equal("\u20ACuro", ReadFromStreams("cp1252.shp", dbf)[0].Properties["note"]);
        dbf[29] = 0x00;
        Assert.Equal("Caf\u00E9 cr\u00E8me", ReadFromStreams("cp1252.shp", dbf)[0].Properties["name"]);
        Assert.Equal("\u0080uro", ReadFromStreams("cp1252.shp", dbf)[0].Properties["note"]);
        Assert.Equal("Z\u00FCrich \u2013 \u65E5\u672C", ReadFromStreams("points.shp", File.ReadAllBytes(GetPath("points.dbf")))[1].Properties["name"]);
    }

    [Fact]
    public void Read_StreamsWithoutDbf()
    {
        using var shp = File.OpenRead(GetPath("points.shp"));
        var features = Shapefile.Read(shp, dbf: null);
        Assert.Null(features.Name);
        Assert.HasCount(3, features);
        Assert.Empty(features[0].Properties);
        Assert.Null(features[2].Geometry);
    }

    [Fact]
    public void Read_SkipsDeletedRecordsWithTheirShapes()
    {
        var dbf = File.ReadAllBytes(GetPath("points.dbf"));
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(dbf.AsSpan(8));
        dbf[headerLength] = (byte)'*';
        var features = ReadFromStreams("points.shp", dbf);
        Assert.HasCount(2, features);
        Assert.Equal(new GeoPosition(8.5417, 47.3769), Assert.IsType<GeoPoint>(features[0].Geometry).Position);
        Assert.Equal("Z\u00FCrich \u2013 \u65E5\u672C", features[0].Properties["name"]);
    }

    [Fact]
    public void Read_FeaturesAreTheDbfRecords()
    {
        var dbf = File.ReadAllBytes(GetPath("lines.DBF"));
        BinaryPrimitives.WriteUInt32LittleEndian(dbf.AsSpan(4), 1);
        Assert.Single(ReadFromStreams("lines.shp", dbf));

        // The records without shape have no geometry
        var points = File.ReadAllBytes(GetPath("points.dbf"));
        var shp = CreateShp(CreatePointContent(1, 2));
        using var shpStream = new MemoryStream(shp);
        using var dbfStream = new MemoryStream(points);
        var features = Shapefile.Read(shpStream, dbfStream);
        Assert.HasCount(3, features);
        Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(features[0].Geometry).Position);
        Assert.Equal("Paris", features[0].Properties["name"]);
        Assert.Null(features[1].Geometry);
        Assert.Equal(4700.25, features[1].Properties["density"]);
    }

    [Fact]
    public void Read_DbaseValues()
    {
        // Two fields with the same name, blank and invalid values, a memo field
        var dbf = CreateDbf(
            [("a", 'C', 3), ("a", 'N', 5), ("l", 'L', 1), ("d", 'D', 8), ("m", 'M', 10)],
            ["abc  1.5T20240229         7", "   *****?        ", " x   abcy20230229"]);
        using var shp = new MemoryStream(CreateShp(CreatePointContent(0, 0), CreatePointContent(0, 0), CreatePointContent(0, 0)));
        using var dbfStream = new MemoryStream(dbf);
        var features = Shapefile.Read(shp, dbfStream);
        Assert.Equal(["a", "a_1", "l", "d", "m"], features[0].Properties.Keys);
        Assert.Equal("abc", features[0].Properties["a"]);
        Assert.Equal(1.5, features[0].Properties["a_1"]);
        Assert.Equal(true, features[0].Properties["l"]);
        Assert.Equal(new DateOnly(2024, 2, 29), features[0].Properties["d"]);
        Assert.Equal("7", features[0].Properties["m"]);
        Assert.All(features[1].Properties.Values, value => value is null);
        Assert.Equal(" x", features[2].Properties["a"]);
        Assert.Null(features[2].Properties["a_1"]);
        Assert.Equal(true, features[2].Properties["l"]);
        Assert.Null(features[2].Properties["d"]);
    }

    [Fact]
    public void Read_RingsWithoutExpectedOrientation()
    {
        // Counterclockwise rings only: an exterior ring, its hole, an island in the hole, and another exterior ring
        GeoPosition[] outer = [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)];
        GeoPosition[] hole = [new(2, 2), new(8, 2), new(8, 8), new(2, 8), new(2, 2)];
        GeoPosition[] island = [new(4, 4), new(6, 4), new(6, 6), new(4, 6), new(4, 4)];
        GeoPosition[] other = [new(20, 0), new(21, 0), new(21, 1), new(20, 1), new(20, 0)];
        var multiPolygon = Assert.IsType<GeoMultiPolygon>(ShapefileRings.Assemble([island, other, hole, outer]));
        Assert.HasCount(3, multiPolygon.Polygons);
        Assert.Equal([outer, hole], multiPolygon.Polygons[0].Rings);
        Assert.Equal([island], multiPolygon.Polygons[1].Rings);
        Assert.Equal([other], multiPolygon.Polygons[2].Rings);

        // A counterclockwise ring outside the clockwise ones is a polygon
        GeoPosition[] clockwise = [.. outer.Reverse()];
        var orphan = Assert.IsType<GeoMultiPolygon>(ShapefileRings.Assemble([clockwise, hole, other]));
        Assert.Equal([clockwise, hole], orphan.Polygons[0].Rings);
        Assert.Equal([other], orphan.Polygons[1].Rings);
    }

    [Fact]
    public void ReadZip()
    {
        using var zip = File.OpenRead(GetPath("countries.zip"));
        var features = Shapefile.ReadZip(zip);
        Assert.Equal("countries", features.Name);
        Assert.HasCount(2, features);
        Assert.Equal("square with hole", features[0].Properties["name"]);
        Assert.HasCount(3, Assert.IsType<GeoMultiPolygon>(features[1].Geometry).Polygons);
    }

    [Fact]
    public void ReadZip_SeveralShapefiles()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntryFromFile(GetPath("points.shp"), "data/Points.SHP");
            archive.CreateEntryFromFile(GetPath("points.dbf"), "data/Points.dbf");
            archive.CreateEntryFromFile(GetPath("points.cpg"), "data/points.CPG");
            archive.CreateEntryFromFile(GetPath("lines.shp"), "lines.shp");
            archive.CreateEntryFromFile(GetPath("lines.shp"), "__MACOSX/data/._Points.shp");
        }

        stream.Position = 0;
        Assert.Throws<FormatException>(() => Shapefile.ReadZip(stream));
        stream.Position = 0;
        var points = Shapefile.ReadZip(stream, "points");
        Assert.Equal("Points", points.Name);
        Assert.Equal("Z\u00FCrich \u2013 \u65E5\u672C", points[1].Properties["name"]);
        stream.Position = 0;
        var lines = Shapefile.ReadZip(stream, "LINES");
        Assert.HasCount(2, lines);
        Assert.Empty(lines[0].Properties);
        stream.Position = 0;
        Assert.HasCount(3, Shapefile.ReadZip(stream, "data/points"));
        stream.Position = 0;
        Assert.Throws<ArgumentException>(() => Shapefile.ReadZip(stream, "roads"));
    }

    [Fact]
    public void ReadZip_Invalid()
    {
        using var notZip = new MemoryStream([1, 2, 3, 4]);
        Assert.Throws<FormatException>(() => Shapefile.ReadZip(notZip));

        using var empty = new MemoryStream();
        using (var archive = new ZipArchive(empty, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("readme.txt");
        }

        empty.Position = 0;
        Assert.Throws<FormatException>(() => Shapefile.ReadZip(empty));
    }

    public static TheoryData<byte[]> InvalidShpFiles()
    {
        var valid = CreateShp(CreatePointContent(1, 2));
        var wrongCode = valid.ToArray();
        wrongCode[3] = 0;
        var shortLength = valid.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(shortLength.AsSpan(24), 10);
        var longContent = valid.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(longContent.AsSpan(104), 100);
        var truncatedFile = valid.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(truncatedFile.AsSpan(24), 1000);
        BinaryPrimitives.WriteInt32BigEndian(truncatedFile.AsSpan(104), 900);
        var hugeContent = valid.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(hugeContent.AsSpan(24), int.MaxValue);
        BinaryPrimitives.WriteInt32BigEndian(hugeContent.AsSpan(104), int.MaxValue - 100);
        var line = CreatePartsContent(ShapefileShapeType.PolyLine, [[new(0, 0), new(1, 1)]], partTypes: null, hasZ: false);
        var negativeParts = line.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(negativeParts.AsSpan(36), -1);
        var hugePoints = line.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(hugePoints.AsSpan(40), int.MaxValue);
        var invalidPartIndex = line.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(invalidPartIndex.AsSpan(44), 3);
        var multiPatch = CreatePartsContent(ShapefileShapeType.MultiPatch, [[new(0, 0, 0), new(1, 1, 0), new(1, 0, 0)]], partTypes: [9], hasZ: true);
        var unknownType = CreatePointContent(1, 2);
        unknownType[0] = 2;

        var data = new TheoryData<byte[]>();
        foreach (var item in (byte[][])
        [
            [],
            valid[..50],
            wrongCode,
            shortLength,
            longContent,
            truncatedFile,
            hugeContent,
            valid[..^4],
            CreateShp(new byte[2]),
            CreateShp(CreatePointContent(1, 2)[..12]),
            CreateShp(negativeParts),
            CreateShp(hugePoints),
            CreateShp(invalidPartIndex),
            CreateShp(line[..^8]),
            CreateShp(multiPatch),
            CreateShp(unknownType),
        ])
        {
            data.Add(item);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidShpFiles))]
    public void Read_InvalidShp(byte[] shp)
    {
        using var stream = new MemoryStream(shp);
        Assert.Throws<FormatException>(() => Shapefile.Read(stream, dbf: null));
    }

    [Fact]
    public void Read_InvalidDbf()
    {
        var valid = File.ReadAllBytes(GetPath("points.dbf"));
        var shortRecords = valid.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(shortRecords.AsSpan(10), 10);
        var tooManyRecords = valid.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(tooManyRecords.AsSpan(4), int.MaxValue);
        foreach (var dbf in (byte[][])[[], valid[..20], valid[..100], valid[..300], shortRecords, tooManyRecords])
        {
            Assert.Throws<FormatException>(() => ReadFromStreams("points.shp", dbf));
        }
    }

    private const string Lambert93Prj = """PROJCS["RGF_1993_Lambert_93",GEOGCS["GCS_RGF_1993",DATUM["D_RGF_1993",SPHEROID["GRS_1980",6378137.0,298.257222101]],PRIMEM["Greenwich",0.0],UNIT["Degree",0.0174532925199433]],PROJECTION["Lambert_Conformal_Conic"],PARAMETER["False_Easting",700000.0],PARAMETER["False_Northing",6600000.0],PARAMETER["Central_Meridian",3.0],PARAMETER["Standard_Parallel_1",49.0],PARAMETER["Standard_Parallel_2",44.0],PARAMETER["Latitude_Of_Origin",46.5],UNIT["Meter",1.0]]""";

    [Fact]
    public async Task Read_ProjectedCoordinateSystem()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            // A shapefile in Lambert 93, as the open data of France: the .prj file of the writer is replaced
            var path = Path.Combine(directory.FullName, "communes.shp");
            var polygon = new GeoPolygon([[new(652310.72, 6862414.64), new(652310.72, 6862514.64), new(652410.72, 6862414.64), new(652310.72, 6862414.64)]]);
            Shapefile.Write([new GeoFeature(polygon, new Dictionary<string, object?> { ["name"] = "Paris" }), new GeoFeature(geometry: null)], path);
            File.Delete(Path.Combine(directory.FullName, "communes.prj"));
            File.WriteAllText(Path.Combine(directory.FullName, "communes.PRJ"), Lambert93Prj);

            var features = Shapefile.Read(path);
            var crs = Assert.IsType<ProjectedCoordinateSystem>(features.CoordinateReferenceSystem);
            Assert.Equal("RGF_1993_Lambert_93", crs.Name);
            Assert.Equal(polygon.Rings, ((GeoPolygon)features[0].Geometry!).Rings);

            var converted = Shapefile.Read(path, new ShapefileReaderOptions { TransformToWgs84 = true });
            Assert.Same(CoordinateReferenceSystem.Wgs84, converted.CoordinateReferenceSystem);
            var position = ((GeoPolygon)converted[0].Geometry!).Rings[0][0];
            Assert.Equal(2.35, position.Longitude, 1e-6);
            Assert.Equal(48.86, position.Latitude, 1e-6);
            Assert.Equal("Paris", converted[0].Properties["name"]);
            Assert.Null(converted[1].Geometry);

            // The streaming reader converts the positions the same way
            var streamed = await Shapefile.ReadFeaturesAsync(path, new ShapefileReaderOptions { TransformToWgs84 = true }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(position, ((GeoPolygon)streamed[0].Geometry!).Rings[0][0]);
            Assert.Null(streamed[1].Geometry);
            var notConverted = await Shapefile.ReadFeaturesAsync(path, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(polygon.Rings, ((GeoPolygon)notConverted[0].Geometry!).Rings);

            // The same files in a zip archive
            using var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in directory.GetFiles())
                {
                    archive.CreateEntryFromFile(file.FullName, "data/" + file.Name);
                }
            }

            zip.Position = 0;
            var zipped = Shapefile.ReadZip(zip, new ShapefileReaderOptions { TransformToWgs84 = true });
            Assert.Equal(position, ((GeoPolygon)zipped[0].Geometry!).Rings[0][0]);
            zip.Position = 0;
            var zippedStreamed = await Shapefile.ReadZipFeaturesAsync(zip, options: new ShapefileReaderOptions { TransformToWgs84 = true }, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(position, ((GeoPolygon)zippedStreamed[0].Geometry!).Rings[0][0]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_CoordinateSystemOfThePrjFile()
    {
        // The files of GDAL have a .prj file of WGS 84: the positions are not converted
        var features = Shapefile.Read(GetPath("points.shp"), new ShapefileReaderOptions { TransformToWgs84 = true });
        var crs = Assert.IsType<GeographicCoordinateSystem>(features.CoordinateReferenceSystem);
        Assert.Equal(new GeoPosition(2.3522, 48.8566), ((GeoPoint)features[0].Geometry!).Position);
        Assert.Equal(Ellipsoid.Wgs84.SemiMajorAxis, crs.Ellipsoid.SemiMajorAxis);

        // Streams have no .prj file
        using var shp = File.OpenRead(GetPath("points.shp"));
        Assert.Null(Shapefile.Read(shp, dbf: null).CoordinateReferenceSystem);
    }

    [Fact]
    public async Task Read_InvalidPrjFile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "points.shp");
            Shapefile.Write([new GeoFeature(new GeoPoint(new GeoPosition(1, 2)))], path);

            // Ignored unless the positions must be converted
            File.WriteAllText(Path.Combine(directory.FullName, "points.prj"), "PROJCS[");
            Assert.Null(Shapefile.Read(path).CoordinateReferenceSystem);
            Assert.Throws<FormatException>(() => Shapefile.Read(path, new ShapefileReaderOptions { TransformToWgs84 = true }));

            File.WriteAllText(Path.Combine(directory.FullName, "points.prj"), """PROJCS["Albers",GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]],PROJECTION["Albers_Conic_Equal_Area"],UNIT["metre",1]]""");
            Assert.Null(Shapefile.Read(path).CoordinateReferenceSystem);
            Assert.Throws<NotSupportedException>(() => Shapefile.Read(path, new ShapefileReaderOptions { TransformToWgs84 = true }));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Shapefile.ReadFeaturesAsync(path, new ShapefileReaderOptions { TransformToWgs84 = true }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
            Assert.Single(await Shapefile.ReadFeaturesAsync(path, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Documentation_ReadProjected()
    {
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "communes.shp");
        Shapefile.Write([new GeoFeature(new GeoPoint(new GeoPosition(652310.72, 6862414.64)))], path);
        File.WriteAllText(Path.ChangeExtension(path, ".prj"), Lambert93Prj);
        try
        {
            #region Geo_ShapefileReadProjected
            // The .prj file declares Lambert 93: the positions are converted to longitudes and latitudes
            var features = Shapefile.Read(path, new ShapefileReaderOptions { TransformToWgs84 = true });
            var position = ((GeoPoint)features[0].Geometry!).Position;
            Assert.Equal(2.35, position.Longitude, 1e-6);
            Assert.Equal(48.86, position.Latitude, 1e-6);

            // Without the option, the positions are those of the file, and CoordinateReferenceSystem tells what they are
            var projected = Shapefile.Read(path);
            Assert.Equal("RGF_1993_Lambert_93", projected.CoordinateReferenceSystem!.Name);
            Assert.False(projected.CoordinateReferenceSystem.IsGeographic);
            #endregion
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Write_RoundTrip()
    {
        var longText = new string('a', 253) + "\u00E9";
        var features = new GeoFeatureCollection
        {
            new(new GeoPoint(new GeoPosition(2.3522, 48.8566)), new Dictionary<string, object?>
            {
                ["name"] = "Z\u00FCrich",
                ["count"] = 42,
                ["big"] = long.MaxValue,
                ["ratio"] = 0.1,
                ["whole"] = 3.0,
                ["flag"] = true,
                ["date"] = new DateOnly(2024, 2, 29),
                ["time"] = new DateTime(2024, 3, 1, 23, 59, 0, DateTimeKind.Utc),
                ["mixed"] = 1,
                ["long_name_1"] = "a",
                ["long_name_2"] = "b",
                ["text"] = longText,
                ["float"] = 0.1f,
            }),
            new(new GeoPoint(new GeoPosition(-1e-7, 1e7)), new Dictionary<string, object?>
            {
                ["name"] = null,
                ["count"] = -7L,
                ["ratio"] = 1e300,
                ["whole"] = double.NaN,
                ["flag"] = false,
                ["date"] = null,
                ["time"] = new DateTimeOffset(2024, 3, 1, 23, 0, 0, TimeSpan.FromHours(-5)),
                ["mixed"] = "text",
                ["extra"] = 2.5m,
            }),
            new(geometry: null),
        };

        var read = RoundTrip(features);
        Assert.HasCount(3, read);
        Assert.Equal(new GeoPosition(2.3522, 48.8566), Assert.IsType<GeoPoint>(read[0].Geometry).Position);
        Assert.Equal(new GeoPosition(-1e-7, 1e7), Assert.IsType<GeoPoint>(read[1].Geometry).Position);
        Assert.Null(read[2].Geometry);
        Assert.Equal(["name", "count", "big", "ratio", "whole", "flag", "date", "time", "mixed", "long_name_", "long_nam_1", "text", "float", "extra"], read[0].Properties.Keys);
        Assert.Equal("Z\u00FCrich", read[0].Properties["name"]);
        Assert.Equal(42.0, read[0].Properties["count"]);
        Assert.Equal((double)long.MaxValue, read[0].Properties["big"]);
        Assert.Equal(0.1, read[0].Properties["ratio"]);
        Assert.Equal(3.0, read[0].Properties["whole"]);
        Assert.Equal(true, read[0].Properties["flag"]);
        Assert.Equal(new DateOnly(2024, 2, 29), read[0].Properties["date"]);
        Assert.Equal(new DateOnly(2024, 3, 1), read[0].Properties["time"]);
        Assert.Equal("1", read[0].Properties["mixed"]);
        Assert.Equal("a", read[0].Properties["long_name_"]);
        Assert.Equal("b", read[0].Properties["long_nam_1"]);
        Assert.Equal(new string('a', 253), read[0].Properties["text"]);
        Assert.Equal(0.1, read[0].Properties["float"]);
        Assert.Null(read[0].Properties["extra"]);
        Assert.Null(read[1].Properties["name"]);
        Assert.Equal(-7.0, read[1].Properties["count"]);
        Assert.Null(read[1].Properties["big"]);
        Assert.Equal(1e300, read[1].Properties["ratio"]);
        Assert.Null(read[1].Properties["whole"]);
        Assert.Equal(false, read[1].Properties["flag"]);
        Assert.Null(read[1].Properties["date"]);
        Assert.Equal(new DateOnly(2024, 3, 1), read[1].Properties["time"]);
        Assert.Equal("text", read[1].Properties["mixed"]);
        Assert.Equal(2.5, read[1].Properties["extra"]);
        Assert.All(read[2].Properties.Values, value => value is null);
    }

    [Fact]
    public void Write_GdalFieldWidths()
    {
        var features = new GeoFeatureCollection
        {
            new(new GeoPoint(new GeoPosition(0, 0)), new Dictionary<string, object?> { ["name"] = "abc", ["count"] = 12345, ["ratio"] = 0.5, ["flag"] = true, ["date"] = new DateOnly(2024, 1, 2) }),
        };

        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);
        var bytes = dbf.ToArray();
        Assert.Equal(0x03, bytes[0]);
        Assert.Equal(0x1A, bytes[^1]);
        Assert.Equal((ushort)(32 + (5 * 32) + 1), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)));
        Assert.Equal((ushort)(1 + 3 + 5 + 24 + 1 + 8), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)));
        Assert.Equal(" abc12345" + new string(' ', 21) + "0.5T20240102", Encoding.ASCII.GetString(bytes, 32 + (5 * 32) + 1, 42));
    }

    [Fact]
    public void Write_FidFieldWhenNoProperties()
    {
        var read = RoundTrip([new(new GeoPoint(new GeoPosition(1, 2))), new(new GeoPoint(new GeoPosition(3, 4)))]);
        Assert.Equal(0.0, read[0].Properties["FID"]);
        Assert.Equal(1.0, read[1].Properties["FID"]);
    }

    [Fact]
    public void Write_PolygonsOrientation()
    {
        // Exterior rings counterclockwise and holes clockwise, as RFC 7946 recommends, without closing position
        var features = new GeoFeatureCollection
        {
            new(new GeoPolygon([[new(0, 0), new(10, 0), new(10, 10), new(0, 10)], [new(2, 2), new(2, 4), new(4, 4), new(4, 2), new(2, 2)]])),
            new(new GeoMultiPolygon([new GeoPolygon([[new(20, 0), new(21, 0), new(21, 1), new(20, 0)]]), new GeoPolygon([[new(30, 0), new(30, 1), new(31, 1), new(30, 0)]])])),
            new(new GeoPolygon([])),
        };

        var read = RoundTrip(features);
        var polygon = Assert.IsType<GeoPolygon>(read[0].Geometry);
        Assert.Equal([new GeoPosition(0, 0), new(0, 10), new(10, 10), new(10, 0), new(0, 0)], polygon.Rings[0]);
        Assert.Equal([new GeoPosition(2, 2), new(4, 2), new(4, 4), new(2, 4), new(2, 2)], polygon.Rings[1]);
        var multiPolygon = Assert.IsType<GeoMultiPolygon>(read[1].Geometry);
        Assert.Equal([new GeoPosition(20, 0), new(21, 1), new(21, 0), new(20, 0)], multiPolygon.Polygons[0].Rings[0]);
        Assert.Equal([new GeoPosition(30, 0), new(30, 1), new(31, 1), new(30, 0)], multiPolygon.Polygons[1].Rings[0]);
        Assert.Null(read[2].Geometry);
    }

    [Fact]
    public void Write_ShapeTypes()
    {
        Assert.Equal(ShapefileShapeType.Point, WriteAndGetShapeType([new(new GeoPoint(new GeoPosition(1, 2)))]));
        Assert.Equal(ShapefileShapeType.Null, WriteAndGetShapeType([new(geometry: null)]));
        Assert.Equal(ShapefileShapeType.Null, WriteAndGetShapeType([]));

        // A point in a MultiPoint shapefile is a MultiPoint of one point
        var points = new GeoFeatureCollection { new(new GeoPoint(new GeoPosition(1, 2))), new(new GeoMultiPoint([new(3, 4), new(5, 6)])) };
        Assert.Equal(ShapefileShapeType.MultiPoint, WriteAndGetShapeType(points));
        var read = RoundTrip(points);
        Assert.Equal([new GeoPosition(1, 2)], Assert.IsType<GeoMultiPoint>(read[0].Geometry).Positions);
        Assert.Equal([new GeoPosition(3, 4), new(5, 6)], Assert.IsType<GeoMultiPoint>(read[1].Geometry).Positions);

        // With Z when a position has an altitude; the others get 0
        var lines = new GeoFeatureCollection { new(new GeoLineString([new(0, 0, 5), new(1, 1)])), new(new GeoMultiLineString([new GeoLineString([new(2, 2), new(3, 3)]), new GeoLineString([new(4, 4), new(5, 5)])])) };
        Assert.Equal(ShapefileShapeType.PolyLineZ, WriteAndGetShapeType(lines));
        read = RoundTrip(lines);
        Assert.Equal([new GeoPosition(0, 0, 5), new(1, 1, 0)], Assert.IsType<GeoLineString>(read[0].Geometry).Positions);
        Assert.HasCount(2, Assert.IsType<GeoMultiLineString>(read[1].Geometry).LineStrings);

        Assert.Equal(ShapefileShapeType.PointZ, WriteAndGetShapeType([new(new GeoPoint(new GeoPosition(1, 2, 3)))]));
        Assert.Equal(ShapefileShapeType.MultiPointZ, WriteAndGetShapeType([new(new GeoMultiPoint([new(1, 2, 3)]))]));
        Assert.Equal(ShapefileShapeType.PolyLine, WriteAndGetShapeType([new(new GeoLineString([new(1, 2), new(3, 4)]))]));
        Assert.Equal(ShapefileShapeType.PolygonZ, WriteAndGetShapeType([new(new GeoPolygon([[new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 0, 1)]]))]));
        Assert.Equal(new GeoPosition(1, 2, 3), Assert.IsType<GeoPoint>(Assert.Single(RoundTrip([new(new GeoPoint(new GeoPosition(1, 2, 3)))])).Geometry).Position);
    }

    [Fact]
    public void Write_Measures()
    {
        // Without altitudes, the measures need the M shapes; a position without measure gets the no-data value
        var points = new GeoFeatureCollection { new(new GeoPoint(new GeoPosition(1, 2) { Measure = 3 })), new(new GeoPoint(new GeoPosition(4, 5))) };
        Assert.Equal(ShapefileShapeType.PointM, WriteAndGetShapeType(points));
        Assert.Equal([new GeoPosition(1, 2) { Measure = 3 }, new(4, 5)], RoundTrip(points).Select(feature => Assert.IsType<GeoPoint>(feature.Geometry).Position));

        GeoPosition[] multiPoint = [new(1, 2) { Measure = -1.5 }, new(3, 4) { Measure = 0 }];
        Assert.Equal(ShapefileShapeType.MultiPointM, WriteAndGetShapeType([new(new GeoMultiPoint(multiPoint))]));
        Assert.Equal(multiPoint, Assert.IsType<GeoMultiPoint>(Assert.Single(RoundTrip([new(new GeoMultiPoint(multiPoint))])).Geometry).Positions);

        GeoPosition[] line = [new(0, 0) { Measure = 0 }, new(1, 1), new(2, 2) { Measure = 20 }];
        Assert.Equal(ShapefileShapeType.PolyLineM, WriteAndGetShapeType([new(new GeoLineString(line))]));
        Assert.Equal(line, Assert.IsType<GeoLineString>(Assert.Single(RoundTrip([new(new GeoLineString(line))])).Geometry).Positions);

        // The exterior ring is clockwise in a shapefile: the measures follow their positions when it is reversed
        GeoPosition[] ring = [new(0, 0) { Measure = 1 }, new(0, 1) { Measure = 2 }, new(1, 1) { Measure = 3 }, new(0, 0) { Measure = 1 }];
        Assert.Equal(ShapefileShapeType.PolygonM, WriteAndGetShapeType([new(new GeoPolygon([ring]))]));
        Assert.Equal(ring, Assert.Single(Assert.IsType<GeoPolygon>(Assert.Single(RoundTrip([new(new GeoPolygon([ring]))])).Geometry).Rings));

        // With altitudes, the Z shapes hold the measures
        GeoPosition[] lineZ = [new(0, 0, 10) { Measure = 1 }, new(1, 1, 11) { Measure = 2 }];
        Assert.Equal(ShapefileShapeType.PolyLineZ, WriteAndGetShapeType([new(new GeoLineString(lineZ))]));
        Assert.Equal(lineZ, Assert.IsType<GeoLineString>(Assert.Single(RoundTrip([new(new GeoLineString(lineZ))])).Geometry).Positions);
        var pointZ = new GeoPosition(1, 2, 3) { Measure = 4 };
        Assert.Equal(pointZ, Assert.IsType<GeoPoint>(Assert.Single(RoundTrip([new(new GeoPoint(pointZ))])).Geometry).Position);

        // The times are not written
        Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(Assert.Single(RoundTrip([new(new GeoPoint(new GeoPosition(1, 2) { Time = DateTimeOffset.UnixEpoch }))])).Geometry).Position);
    }

    [Fact]
    public void Write_MeasureRanges()
    {
        var features = new GeoFeatureCollection
        {
            new(new GeoLineString([new(0, 0) { Measure = 5 }, new(1, 1) { Measure = -2 }])),
            new(new GeoLineString([new(2, 2), new(3, 3)])),
        };

        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);
        var bytes = shp.ToArray();
        Assert.Equal([-2.0, 5], Enumerable.Range(0, 2).Select(i => BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(84 + (8 * i)))));

        // The second line has no measure: its range is the no-data value. Its record is the last one: header, box, counts, part, points, M range, M values.
        var mRange = bytes.Length - (8 * 2) - 16;
        Assert.All(Enumerable.Range(0, 4).Select(i => BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(mRange + (8 * i)))), value => Assert.True(value < -1e38));
    }

    [Fact]
    public void Write_HeadersAndIndex()
    {
        var features = new GeoFeatureCollection
        {
            new(new GeoLineString([new(-1, -2, 3), new(4, 5, -6)])),
            new(geometry: null),
            new(new GeoMultiLineString([new GeoLineString([new(10, 20, 0), new(11, 21, 0)]), new GeoLineString([new(12, 22, 0), new(13, 23, 0)])])),
        };

        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);
        var shpBytes = shp.ToArray();
        var shxBytes = shx.ToArray();
        Assert.Equal(shpBytes.Length, BinaryPrimitives.ReadInt32BigEndian(shpBytes.AsSpan(24)) * 2);
        Assert.Equal(shxBytes.Length, BinaryPrimitives.ReadInt32BigEndian(shxBytes.AsSpan(24)) * 2);
        Assert.HasCount(100 + (3 * 8), shxBytes);
        Assert.Equal(ShapefileShapeType.PolyLineZ, BinaryPrimitives.ReadInt32LittleEndian(shxBytes.AsSpan(32)));
        Assert.Equal([-1.0, -2, 13, 23, -6, 3], Enumerable.Range(0, 6).Select(i => BinaryPrimitives.ReadDoubleLittleEndian(shpBytes.AsSpan(36 + (8 * i)))));
        for (var i = 0; i < 3; i++)
        {
            var offset = BinaryPrimitives.ReadInt32BigEndian(shxBytes.AsSpan(100 + (8 * i))) * 2;
            Assert.Equal(i + 1, BinaryPrimitives.ReadInt32BigEndian(shpBytes.AsSpan(offset)));
            Assert.Equal(BinaryPrimitives.ReadInt32BigEndian(shxBytes.AsSpan(104 + (8 * i))), BinaryPrimitives.ReadInt32BigEndian(shpBytes.AsSpan(offset + 4)));
        }
    }

    [Fact]
    public void Write_Invalid()
    {
        using var stream = new MemoryStream();
        GeoFeatureCollection mixed = [new(new GeoPoint(new GeoPosition(1, 2))), new(new GeoLineString([new(0, 0), new(1, 1)]))];
        Assert.Throws<ArgumentException>(() => Shapefile.Write(mixed, stream, stream, stream));
        GeoFeatureCollection collection = [new(new GeoGeometryCollection([new GeoPoint(new GeoPosition(1, 2))]))];
        Assert.Throws<ArgumentException>(() => Shapefile.Write(collection, stream, stream, stream));
        GeoFeatureCollection notFinite = [new(new GeoPoint(new GeoPosition(double.NaN, 2)))];
        Assert.Throws<ArgumentException>(() => Shapefile.Write(notFinite, stream, stream, stream));
        Assert.Throws<ArgumentException>(() => Shapefile.WriteZip(notFinite, stream, "a"));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void WriteZip_RoundTrip()
    {
        GeoFeatureCollection features = [new(new GeoPoint(new GeoPosition(1, 2)), new Dictionary<string, object?> { ["name"] = "\u65E5\u672C" })];
        using var stream = new MemoryStream();
        Shapefile.WriteZip(features, stream, "places");
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal(["places.shp", "places.shx", "places.dbf", "places.cpg", "places.prj"], archive.Entries.Select(entry => entry.FullName));
        }

        stream.Position = 0;
        var read = Shapefile.ReadZip(stream);
        Assert.Equal("places", read.Name);
        Assert.Equal("\u65E5\u672C", Assert.Single(read).Properties["name"]);
    }

    [Fact]
    public void Write_Files()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            GeoFeatureCollection features = [new(new GeoPoint(new GeoPosition(1, 2)), new Dictionary<string, object?> { ["name"] = "a" })];
            Shapefile.Write(features, Path.Combine(directory.FullName, "places"));
            Assert.Equal(["places.cpg", "places.dbf", "places.prj", "places.shp", "places.shx"], directory.GetFiles().Select(file => file.Name).Order(StringComparer.Ordinal));
            Assert.Equal("UTF-8", File.ReadAllText(Path.Combine(directory.FullName, "places.cpg")));
            Assert.StartsWith("GEOGCS[\"GCS_WGS_1984\"", File.ReadAllText(Path.Combine(directory.FullName, "places.prj")));
            Assert.Equal("a", Assert.Single(Shapefile.Read(Path.Combine(directory.FullName, "places.shp"))).Properties["name"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Documentation_Read()
    {
        #region Geo_ShapefileRead
        // Reads points.shp with the attributes of points.dbf, decoded with the encoding of points.cpg
        var features = Shapefile.Read("TestData/shapefile/points.shp");
        Assert.Equal("points", features.Name);
        var paris = features[0];
        Assert.Equal(new GeoPosition(2.3522, 48.8566), ((GeoPoint)paris.Geometry!).Position);
        Assert.Equal("Paris", paris.Properties["name"]);
        Assert.Equal(2_102_650.0, paris.Properties["population"]); // Numbers are doubles
        Assert.Equal(true, paris.Properties["capital"]);
        Assert.Equal(new DateOnly(1952, 1, 31), paris.Properties["founded"]);
        #endregion
    }

    [Fact]
    public void Documentation_ReadZip()
    {
        #region Geo_ShapefileReadZip
        // A shapefile downloaded as a .zip archive
        using var zip = File.OpenRead("TestData/shapefile/countries.zip");
        var features = Shapefile.ReadZip(zip);
        Assert.Equal("countries", features.Name);
        var polygon = (GeoPolygon)features[0].Geometry!;
        Assert.HasCount(2, polygon.Rings); // The exterior ring and a hole
        Assert.True(polygon.Contains(new GeoPosition(5, 5)));
        #endregion
    }

    [Fact]
    public void Documentation_Write()
    {
        #region Geo_ShapefileWrite
        var features = new GeoFeatureCollection
        {
            new(new GeoPoint(new GeoPosition(2.3522, 48.8566)), new Dictionary<string, object?> { ["name"] = "Paris", ["population"] = 2_102_650 }),
            new(new GeoPoint(new GeoPosition(-0.5792, 44.8378)), new Dictionary<string, object?> { ["name"] = "Bordeaux", ["population"] = 261_804 }),
        };

        // Writes cities.shp, cities.shx, cities.dbf, cities.cpg and cities.prj
        var directory = Directory.CreateTempSubdirectory();
        Shapefile.Write(features, Path.Combine(directory.FullName, "cities.shp"));

        // Or a .zip archive of these files
        using var zip = new MemoryStream();
        Shapefile.WriteZip(features, zip, "cities");
        #endregion

        Assert.Equal("Bordeaux", Shapefile.Read(Path.Combine(directory.FullName, "cities.shp"))[1].Properties["name"]);
        directory.Delete(recursive: true);
        zip.Position = 0;
        Assert.Equal(261_804.0, Shapefile.ReadZip(zip)[1].Properties["population"]);
    }

    private static GeoFeatureCollection RoundTrip(GeoFeatureCollection features)
    {
        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);
        shp.Position = 0;
        dbf.Position = 0;
        return Shapefile.Read(shp, dbf);
    }

    private static int WriteAndGetShapeType(GeoFeatureCollection features)
    {
        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);
        return BinaryPrimitives.ReadInt32LittleEndian(shp.GetBuffer().AsSpan(32));
    }

    private static GeoFeatureCollection ReadFromStreams(string shpName, byte[] dbf)
    {
        using var shp = File.OpenRead(GetPath(shpName));
        using var dbfStream = new MemoryStream(dbf);
        return Shapefile.Read(shp, dbfStream);
    }

    private static byte[] CreateShp(params byte[][] contents)
    {
        var length = 100 + contents.Sum(content => 8 + content.Length);
        var result = new byte[length];
        BinaryPrimitives.WriteInt32BigEndian(result, ShapefileShapeType.FileCode);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(24), length / 2);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), ShapefileShapeType.Version);
        var offset = 100;
        for (var i = 0; i < contents.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset), i + 1);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset + 4), contents[i].Length / 2);
            contents[i].CopyTo(result, offset + 8);
            offset += 8 + contents[i].Length;
        }

        return result;
    }

    private static byte[] CreatePointContent(double x, double y)
    {
        var content = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(content, ShapefileShapeType.Point);
        BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(4), x);
        BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(12), y);
        return content;
    }

    private static byte[] CreatePartsContent(int shapeType, GeoPosition[][] parts, int[]? partTypes, bool hasZ)
    {
        var pointCount = parts.Sum(part => part.Length);
        var partArrays = (partTypes is null ? 4 : 8) * parts.Length;
        var content = new byte[44 + partArrays + (16 * pointCount) + (hasZ ? 16 + (8 * pointCount) : 0)];
        BinaryPrimitives.WriteInt32LittleEndian(content, shapeType);
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(36), parts.Length);
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(40), pointCount);
        var start = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(44 + (4 * i)), start);
            if (partTypes is not null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(44 + (4 * parts.Length) + (4 * i)), partTypes[i]);
            }

            start += parts[i].Length;
        }

        var offset = 44 + partArrays;
        var positions = parts.SelectMany(part => part).ToArray();
        foreach (var position in positions)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(offset), position.Longitude);
            BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(offset + 8), position.Latitude);
            offset += 16;
        }

        if (hasZ)
        {
            offset += 16;
            foreach (var position in positions)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(offset), position.Altitude ?? 0);
                offset += 8;
            }
        }

        return content;
    }

    private static byte[] CreateDbf((string Name, char Type, int Length)[] fields, string[] records)
    {
        var headerLength = 32 + (32 * fields.Length) + 1;
        var recordLength = 1 + fields.Sum(field => field.Length);
        var result = new byte[headerLength + (recordLength * records.Length) + 1];
        result[0] = 0x03;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)records.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), (ushort)headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), (ushort)recordLength);
        for (var i = 0; i < fields.Length; i++)
        {
            Encoding.ASCII.GetBytes(fields[i].Name).CopyTo(result, 32 + (32 * i));
            result[32 + (32 * i) + 11] = (byte)fields[i].Type;
            result[32 + (32 * i) + 16] = (byte)fields[i].Length;
        }

        result[headerLength - 1] = 0x0D;
        for (var i = 0; i < records.Length; i++)
        {
            Encoding.ASCII.GetBytes(" " + records[i].PadRight(recordLength - 1)).CopyTo(result, headerLength + (recordLength * i));
        }

        result[^1] = 0x1A;
        return result;
    }

    [Fact]
    public void ReadZip_EncryptedEntry()
    {
        // The entry is not a valid .shp file: .NET 10 ignores the flag and reads it, .NET 11 does not support its encryption
        Assert.Throws<FormatException>(() => Shapefile.ReadZip(new MemoryStream(KmlTests.EncryptEntries(KmlTests.CreateZip("layer.shp", "x")))));
    }

    [Fact]
    public void Read_TextPaddedWithNullBytes()
    {
        GeoFeatureCollection features =
        [
            new GeoFeature(new GeoPoint(new(1, 2)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "abcdef" }),
            new GeoFeature(new GeoPoint(new(3, 4)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "x" }),
        ];
        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);

        // Some writers pad with NUL bytes, after spaces or instead of them: the value ends at the first one
        var bytes = dbf.ToArray();
        var index = bytes.AsSpan().IndexOf("x     "u8);
        "x \0\0\0\0"u8.CopyTo(bytes.AsSpan(index));

        var read = Shapefile.Read(new MemoryStream(shp.ToArray()), new MemoryStream(bytes));
        Assert.Equal("x", read[1].Properties["name"]);
    }

    [Fact]
    public void Write_TextTooLongCutBeforeUtf8Sequence()
    {
        // 255 bytes: cut to 254 bytes, the value would end in the middle of "€", so it is cut to 252 bytes, the width of the field
        var value = new string('a', 252) + "€";
        GeoFeatureCollection features = [new GeoFeature(new GeoPoint(new(1, 2)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = value })];
        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);

        var bytes = dbf.ToArray();
        Assert.Equal(252, bytes[32 + 16]);
        Assert.Equal((ushort)(1 + 252), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)));
        var read = Shapefile.Read(new MemoryStream(shp.ToArray()), new MemoryStream(bytes));
        Assert.Equal(new string('a', 252), read[0].Properties["name"]);

        using var shp2 = new MemoryStream();
        using var shx2 = new MemoryStream();
        using var dbf2 = new MemoryStream();
        Shapefile.Write(read, shp2, shx2, dbf2);
        Assert.Equal(bytes, dbf2.ToArray());
    }

    [Fact]
    public void Write_FieldNameReadsBackAsWritten()
    {
        // Trimmed and cut to 10 bytes, the name would end with spaces, which the readers trim
        GeoFeatureCollection features = [new GeoFeature(new GeoPoint(new(1, 2)), new Dictionary<string, object?>(StringComparer.Ordinal) { [" f         o"] = "x" })];
        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);

        Assert.Equal("f", dbf.ToArray().AsSpan(32, 11).TrimEnd((byte)0).ToArray().Select(b => (char)b).Aggregate("", (text, c) => text + c));
        var read = Shapefile.Read(new MemoryStream(shp.ToArray()), new MemoryStream(dbf.ToArray()));
        Assert.Equal("x", read[0].Properties["f"]);
    }

    [Fact]
    public void Orientation_ReversingNegatesExactly()
    {
        // A ring the fuzzer found: the rounding of its signed area depends on the direction, so the writer reversed it at
        // each write
        GeoPosition[] ring =
        [
            new(3.16E-322, 5.877471754111521E-39), new(1.288229753919445E-231, 5.877471754111521E-39), new(4.383618698016868E-193, 2.5243548967072737E-29),
            new(4.383618698016868E-193, 2.5243548967072737E-29), new(1.288230488171005E-231, 5.877471754111521E-39), new(3.16E-322, 5.877471754111521E-39),
        ];
        GeoPosition[] reversed = [.. ring.Reverse()];
        Assert.Equal(-ShapefileRings.GetOrientation(ring), ShapefileRings.GetOrientation(reversed));
        Assert.Equal(-ShapefileRings.GetOrientation([new(0, 0), new(0, 1), new(1, 1), new(0, 0)]), ShapefileRings.GetOrientation([new(0, 0), new(1, 1), new(0, 1), new(0, 0)]));
        Assert.True(ShapefileRings.GetOrientation([new(0, 0), new(0, 1), new(1, 1), new(0, 0)]) < 0);
    }

    [Fact]
    public void Read_RingWhoseOrientationOverflows()
    {
        // The area of the second polygon overflows (infinity minus infinity): its ring is still read, as an exterior ring
        var polygons = new GeoMultiPolygon(
        [
            new GeoPolygon([[new(0, 0), new(0, 1), new(1, 1), new(1, 0), new(0, 0)]]),
            new GeoPolygon([[new(1e200, 2e200), new(2e200, 1e200), new(2e200, 2e200), new(1e200, 2e200)]]),
        ]);
        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write([new GeoFeature(polygons)], shp, shx, dbf);

        var read = Shapefile.Read(new MemoryStream(shp.ToArray()), new MemoryStream(dbf.ToArray()));
        Assert.HasCount(2, Assert.IsType<GeoMultiPolygon>(read[0].Geometry).Polygons);
    }

    [Theory]
    [InlineData("points")]
    [InlineData("lines")]
    [InlineData("polygons")]
    [InlineData("pointz")]
    [InlineData("pointm")]
    [InlineData("linem")]
    [InlineData("polygonz")]
    [InlineData("multipatch")]
    [InlineData("multipointzm")]
    [InlineData("latin1")]
    [InlineData("cp1252")]
    public async Task ReadFeaturesAsync_MatchesRead(string name)
    {
        var expected = Shapefile.Read(GetPath(name + ".shp"));
        expected.Name = null;
        var fromPath = await Shapefile.ReadFeaturesAsync(GetPath(name + ".shp"), cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GeoJson.Format(expected), GeoJson.Format(new GeoFeatureCollection(fromPath)));

        // From streams that return a few bytes per read, without the encoding of the .cpg file
        var dbfPath = Directory.EnumerateFiles(Path.GetDirectoryName(GetPath(name))!, name + ".dbf", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Single();
        using var shp = new CountingStream(File.ReadAllBytes(GetPath(name + ".shp")), canSeek: false, maxReadLength: 3);
        using var dbf = new CountingStream(File.ReadAllBytes(dbfPath), canSeek: false, maxReadLength: 3);
        using var shpSync = File.OpenRead(GetPath(name + ".shp"));
        using var dbfSync = File.OpenRead(dbfPath);
        var expectedFromStreams = Shapefile.Read(shpSync, dbfSync);
        var fromStreams = await Shapefile.ReadFeaturesAsync(shp, dbf, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GeoJson.Format(expectedFromStreams), GeoJson.Format(new GeoFeatureCollection(fromStreams)));
    }

    [Fact]
    public async Task ReadFeaturesAsync_WithoutDbf()
    {
        using var shp = File.OpenRead(GetPath("points.shp"));
        var features = await Shapefile.ReadFeaturesAsync(shp, dbf: null, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.HasCount(3, features);
        Assert.All(features, feature => Assert.Empty(feature.Properties));
    }

    [Fact]
    public async Task ReadZipFeaturesAsync()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntryFromFile(GetPath("points.shp"), "data/Points.SHP");
            archive.CreateEntryFromFile(GetPath("points.dbf"), "data/Points.dbf");
            archive.CreateEntryFromFile(GetPath("points.cpg"), "data/points.CPG");
            archive.CreateEntryFromFile(GetPath("lines.shp"), "lines.shp");
        }

        stream.Position = 0;
        await Assert.ThrowsAsync<FormatException>(async () => await Shapefile.ReadZipFeaturesAsync(stream, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
        stream.Position = 0;
        var points = await Shapefile.ReadZipFeaturesAsync(stream, "points", cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Zürich – 日本", points[1].Properties["name"]);
        stream.Position = 0;
        var lines = await Shapefile.ReadZipFeaturesAsync(stream, "LINES", cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.HasCount(2, lines);
        stream.Position = 0;
        await Assert.ThrowsAsync<ArgumentException>(async () => await Shapefile.ReadZipFeaturesAsync(stream, "roads", cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));

        using var zip = File.OpenRead(GetPath("countries.zip"));
        var expected = Shapefile.ReadZip(zip);
        expected.Name = null;
        zip.Position = 0;
        var countries = await Shapefile.ReadZipFeaturesAsync(zip, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GeoJson.Format(expected), GeoJson.Format(new GeoFeatureCollection(countries)));

        using var notZip = new MemoryStream([1, 2, 3, 4]);
        await Assert.ThrowsAsync<FormatException>(async () => await Shapefile.ReadZipFeaturesAsync(notZip, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadFeaturesAsync_Invalid()
    {
        using var shp = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<FormatException>(async () => await Shapefile.ReadFeaturesAsync(shp, dbf: null, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));

        // A .dbf file that declares more records than it has
        var dbfBytes = File.ReadAllBytes(GetPath("points.dbf"));
        BinaryPrimitives.WriteUInt32LittleEndian(dbfBytes.AsSpan(4), 10);
        using var points = File.OpenRead(GetPath("points.shp"));
        using var dbf = new MemoryStream(dbfBytes);
        var read = 0;
        await Assert.ThrowsAsync<FormatException>(async () =>
        {
            await foreach (var feature in Shapefile.ReadFeaturesAsync(points, dbf, cancellationToken: TestContext.Current.CancellationToken))
            {
                read++;
            }
        });
        Assert.Equal(3, read);
    }

    [Fact]
    public async Task ReadFeaturesAsync_LargeShapefile_ReadsAsTheFeaturesAreEnumerated()
    {
        // The files of one point, whose record is repeated 300,000 times: about 8 MB of .shp and 6 MB of .dbf, generated as they are read
        const int Count = 300_000;
        using var shpTemplate = new MemoryStream();
        using var shxTemplate = new MemoryStream();
        using var dbfTemplate = new MemoryStream();
        Shapefile.Write([new GeoFeature(new GeoPoint(new GeoPosition(2.35, 48.86)), new Dictionary<string, object?> { ["name"] = "Paris" })], shpTemplate, shxTemplate, dbfTemplate);
        var shpBytes = shpTemplate.ToArray();
        var shpHeader = shpBytes[..100];
        var shpRecord = shpBytes[100..];
        BinaryPrimitives.WriteInt32BigEndian(shpHeader.AsSpan(24), (int)((100 + ((long)shpRecord.Length * Count)) / 2));
        var dbfBytes = dbfTemplate.ToArray();
        var dbfHeaderLength = BinaryPrimitives.ReadUInt16LittleEndian(dbfBytes.AsSpan(8));
        var dbfHeader = dbfBytes[..dbfHeaderLength];
        var dbfRecord = dbfBytes[dbfHeaderLength..^1];
        BinaryPrimitives.WriteUInt32LittleEndian(dbfHeader.AsSpan(4), Count);

        using var shp = new RepeatedRecordStream(shpHeader, shpRecord, Count, []);
        using var dbf = new RepeatedRecordStream(dbfHeader, dbfRecord, Count, [0x1A]);
        var index = 0;
        await foreach (var feature in Shapefile.ReadFeaturesAsync(shp, dbf, cancellationToken: TestContext.Current.CancellationToken))
        {
            // The record of a feature is read when the feature is returned, not before
            Assert.Equal(100 + ((index + 1L) * shpRecord.Length), shp.Position);
            Assert.Equal(dbfHeader.Length + ((index + 1L) * dbfRecord.Length), dbf.Position);
            Assert.Equal("Paris", feature.Properties["name"]);
            index++;
        }

        Assert.Equal(Count, index);
    }

    [Fact]
    public async Task Documentation_ReadFeaturesAsync()
    {
        #region Geo_ShapefileReadFeaturesAsync
        // The records of the .shp and .dbf files are read one at a time, as the features are enumerated
        var names = new List<string?>();
        await foreach (var feature in Shapefile.ReadFeaturesAsync("TestData/shapefile/points.shp"))
        {
            names.Add((string?)feature.Properties["name"]);
        }

        Assert.Equal("Paris", names[0]);
        #endregion
    }
}
