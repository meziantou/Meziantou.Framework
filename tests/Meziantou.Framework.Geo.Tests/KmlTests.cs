using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.Geo.Tests;

public sealed class KmlTests
{
    private static string GetTestDataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "kml", fileName);

    // TestData/kml/gdal.kml: ogr2ogr -f KML gdal.kml input.geojson (GDAL 3.10.3, ghcr.io/osgeo/gdal:alpine-small-3.10.3),
    // from a FeatureCollection "places" with a feature of each geometry type and the properties name, description, population and capital
    [Fact]
    public void Read_Gdal()
    {
        using var stream = File.OpenRead(GetTestDataPath("gdal.kml"));
        var features = Kml.Read(stream);
        Assert.Null(features.Name);
        Assert.HasCount(8, features);
        Assert.Equal(["places.1", "places.2", "places.3", "places.4", "places.5", "places.6", "places.7", "places.8"], features.Select(feature => feature.Id));
        Assert.Equal(["Paris", "Seine", "Park", "Stops", "Roads", "Islands", "Mixed", "Nowhere"], features.Select(feature => feature.Properties["name"]));
        Assert.All(features, feature => Assert.Equal("places", feature.Properties["folder"]));

        var paris = features[0];
        Assert.Equal("<b>Capital</b> & largest city", paris.Properties["description"]);
        Assert.Equal(2102650.0, paris.Properties["population"]);
        Assert.Equal(1.0, paris.Properties["capital"]);
        Assert.Equal(new GeoPosition(2.3522, 48.8566, 35), Assert.IsType<GeoPoint>(paris.Geometry).Position);

        Assert.False(features[1].Properties.ContainsKey("description"));
        Assert.False(features[1].Properties.ContainsKey("population"));
        Assert.Equal(0.0, features[1].Properties["capital"]);
        Assert.Equal([new GeoPosition(2.2, 48.8), new(2.35, 48.85), new(2.5, 48.83)], Assert.IsType<GeoLineString>(features[1].Geometry).Positions);

        var park = Assert.IsType<GeoPolygon>(features[2].Geometry);
        Assert.Equal([new GeoPosition(2, 48), new(3, 48), new(3, 49), new(2, 49), new(2, 48)], park.Rings[0]);
        Assert.Equal([new GeoPosition(2.4, 48.4), new(2.6, 48.4), new(2.6, 48.6), new(2.4, 48.4)], park.Rings[1]);
        Assert.Equal([new GeoPosition(1, 2), new(3, 4)], Assert.IsType<GeoMultiPoint>(features[3].Geometry).Positions);
        Assert.HasCount(2, Assert.IsType<GeoMultiLineString>(features[4].Geometry).LineStrings);
        Assert.HasCount(2, Assert.IsType<GeoMultiPolygon>(features[5].Geometry).Polygons);
        var mixed = Assert.IsType<GeoGeometryCollection>(features[6].Geometry);
        Assert.Equal([GeoGeometryType.Point, GeoGeometryType.LineString], mixed.Geometries.Select(geometry => geometry.Type));
        Assert.Null(features[7].Geometry);
    }

    [Fact]
    public void Read_GoogleEarth()
    {
        var features = Kml.Parse(File.ReadAllText(GetTestDataPath("google-earth.kml")));
        Assert.Equal("Trip to Paris.kml", features.Name);
        Assert.Equal(["Eiffel Tower", "Louvre", "Walk", "Run", "Two runs", "Ring road", "Campus", "Statue"], features.Select(feature => feature.Properties["name"]));
        Assert.Equal(["Monuments", "Monuments/Museums", "Routes", "Routes", "Routes", "Routes", null, null], features.Select(feature => feature.Properties.GetValueOrDefault("folder")));

        var eiffel = features[0];
        Assert.Equal("eiffel", eiffel.Id);
        Assert.Equal("""<p>Built for the <b>1889</b> World's Fair.</p><img src="https://example.com/eiffel.jpg"/>""", eiffel.Properties["description"]);
        Assert.Equal("Gustave Eiffel", eiffel.Properties["architect"]);
        Assert.Equal(330.0, eiffel.Properties["height"]);
        Assert.Equal(5.8e6, eiffel.Properties["visitors"]);
        Assert.Equal(false, eiffel.Properties["unesco"]);
        Assert.Equal(new GeoPosition(2.2945, 48.8584, 0), Assert.IsType<GeoPoint>(eiffel.Geometry).Position);

        var louvre = features[1];
        Assert.Null(louvre.Id);
        Assert.Equal("The world's most-visited museum & a historic monument", louvre.Properties["description"]);
        Assert.Equal("1793", louvre.Properties["opening"]);
        Assert.Equal("https://www.louvre.fr", louvre.Properties["website"]);
        Assert.Null(louvre.Properties["closed"]);
        var polygon = Assert.IsType<GeoPolygon>(louvre.Geometry);
        Assert.HasCount(2, polygon.Rings);
        Assert.HasCount(5, polygon.Rings[0]);
        Assert.Equal(new GeoPosition(2.3320, 48.8600, 20), polygon.Rings[0][0]);

        Assert.HasCount(3, Assert.IsType<GeoLineString>(features[2].Geometry).Positions);
        Assert.Equal([new GeoPosition(2.2945, 48.8584, 35), new(2.2960, 48.8590, 36.5), new(2.2975, 48.8597, 38)], Assert.IsType<GeoLineString>(features[3].Geometry).Positions);
        var tracks = Assert.IsType<GeoMultiLineString>(features[4].Geometry);
        Assert.Equal([new GeoPosition(7, 8, 9), new(10, 11, 12)], tracks.LineStrings[1].Positions);
        Assert.HasCount(5, Assert.IsType<GeoLineString>(features[5].Geometry).Positions);

        var campus = Assert.IsType<GeoGeometryCollection>(features[6].Geometry);
        Assert.Equal([GeoGeometryType.Point, GeoGeometryType.Polygon, GeoGeometryType.MultiLineString], campus.Geometries.Select(geometry => geometry.Type));
        Assert.Null(features[7].Geometry);
    }

    [Fact]
    public void Read_Kmz_MyMaps()
    {
        using var stream = File.OpenRead(GetTestDataPath("my-maps.kmz"));
        var features = Kmz.Read(stream);
        Assert.Equal("Coffee shops", features.Name);
        Assert.HasCount(2, features);
        Assert.Equal("Café de Flore", features[0].Properties["name"]);
        Assert.Equal("Open every day<br>Address: 172 Bd Saint-Germain", features[0].Properties["description"]);
        Assert.Equal("4.5", features[0].Properties["Rating"]);
        Assert.Equal("https://example.com/flore.jpg", features[0].Properties["gx_media_links"]);
        Assert.Equal("Favorites", features[0].Properties["folder"]);
        Assert.Equal("To try", features[1].Properties["folder"]);
        Assert.Equal(new GeoPosition(2.3333, 48.854, 0), Assert.IsType<GeoPoint>(features[1].Geometry).Position);
    }

    [Theory]
    [InlineData("http://www.opengis.net/kml/2.2")]
    [InlineData("http://earth.google.com/kml/2.2")]
    [InlineData("http://earth.google.com/kml/2.1")]
    [InlineData("http://earth.google.com/kml/2.0")]
    [InlineData("")]
    public void Parse_Namespaces(string ns)
    {
        var features = Kml.Parse($"""<kml xmlns="{ns}"><Folder><name>f</name><Placemark id="p"><name>a</name><Point><coordinates>1,2</coordinates></Point></Placemark></Folder></kml>""");
        Assert.Equal("f", features.Name);
        var feature = Assert.Single(features);
        Assert.Equal("p", feature.Id);
        Assert.Equal("a", feature.Properties["name"]);
        Assert.False(feature.Properties.ContainsKey("folder"));
        Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(feature.Geometry).Position);
    }

    [Fact]
    public void Parse_RootElements()
    {
        Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(Assert.Single(Kml.Parse("<Placemark><Point><coordinates>1,2</coordinates></Point></Placemark>")).Geometry).Position);
        Assert.Equal("d", Kml.Parse("<Document><name>d</name></Document>").Name);
        Assert.Empty(Kml.Parse("""<kml xmlns="http://www.opengis.net/kml/2.2"/>"""));
        Assert.Single(Kml.Parse("""<kml xmlns="http://www.opengis.net/kml/2.2"><Placemark/></kml>"""));
    }

    [Fact]
    public void Parse_Geometries()
    {
        var features = Kml.Parse("""
            <kml xmlns="http://www.opengis.net/kml/2.2"><Document>
              <Placemark><MultiGeometry/></Placemark>
              <Placemark><MultiGeometry><LineString><coordinates>0,0 1,1</coordinates></LineString><LinearRing><coordinates>0,0 1,0 1,1 0,0</coordinates></LinearRing></MultiGeometry></Placemark>
              <Placemark><MultiGeometry><Point><coordinates>0,0</coordinates></Point><Model/></MultiGeometry></Placemark>
              <Placemark><Polygon/></Placemark>
              <Placemark><LineString/></Placemark>
              <Placemark><Polygon><outerBoundaryIs><LinearRing><coordinates>0,0 1,0 1,1 0,0</coordinates></LinearRing></outerBoundaryIs><innerBoundaryIs><LinearRing><coordinates>0,0 1,0 1,1 0,0</coordinates></LinearRing><LinearRing><coordinates>0,0 1,0 1,1 0,0</coordinates></LinearRing></innerBoundaryIs></Polygon></Placemark>
              <Placemark><Model/><Point><coordinates>5,6,7 8,9</coordinates></Point></Placemark>
            </Document></kml>
            """);
        Assert.Empty(Assert.IsType<GeoGeometryCollection>(features[0].Geometry).Geometries);
        Assert.HasCount(2, Assert.IsType<GeoMultiLineString>(features[1].Geometry).LineStrings);
        Assert.Equal([new GeoPosition(0, 0)], Assert.IsType<GeoMultiPoint>(features[2].Geometry).Positions);
        Assert.Empty(Assert.IsType<GeoPolygon>(features[3].Geometry).Rings);
        Assert.Empty(Assert.IsType<GeoLineString>(features[4].Geometry).Positions);
        Assert.HasCount(3, Assert.IsType<GeoPolygon>(features[5].Geometry).Rings);
        Assert.Equal(new GeoPosition(5, 6, 7), Assert.IsType<GeoPoint>(features[6].Geometry).Position);
    }

    [Fact]
    public void Parse_ExtendedData()
    {
        var feature = Assert.Single(Kml.Parse("""
            <kml xmlns="http://www.opengis.net/kml/2.2"><Document>
              <Schema name="legacy"><SimpleField name="n" type="float"/><SimpleField name="b" type="bool"/><SimpleField name="x" type="int"/></Schema>
              <Placemark>
                <name>element</name>
                <ExtendedData>
                  <Data name="name"><value>data</value></Data>
                  <Data name="dup"><value>first</value></Data>
                  <Data name="dup"><value>second</value></Data>
                  <Data><value>no name</value></Data>
                  <SchemaData schemaUrl="other.kml#legacy">
                    <SimpleData name="n">-1.5E2</SimpleData>
                    <SimpleData name="b">true</SimpleData>
                    <SimpleData name="x">not a number</SimpleData>
                  </SchemaData>
                  <SchemaData schemaUrl="#unknown"><SimpleData name="u">1</SimpleData></SchemaData>
                </ExtendedData>
              </Placemark>
            </Document></kml>
            """));
        Assert.Equal("element", feature.Properties["name"]);
        Assert.Equal("first", feature.Properties["dup"]);
        Assert.Equal(-150.0, feature.Properties["n"]);
        Assert.Equal(true, feature.Properties["b"]);
        Assert.Equal("not a number", feature.Properties["x"]);
        Assert.Equal("1", feature.Properties["u"]);
        Assert.HasCount(6, feature.Properties);
    }

    [Theory]
    [InlineData("1,2", "1,2")]
    [InlineData("  1,2,3  ", "1,2,3")]
    [InlineData("1,2\n\t3,4", "1,2|3,4")]
    [InlineData("1, 2, 3 4 ,5", "1,2,3|4,5")]
    [InlineData("1,2,", "1,2")]
    [InlineData("1,2,,", "1,2")]
    [InlineData("-1.5e1,+2.25,1E-3", "-15,2.25,0.001")]
    [InlineData("", "")]
    public void ParseCoordinates(string text, string expected)
    {
        var positions = Kml.ParseCoordinates(text);
        Assert.Equal(expected, string.Join('|', positions.Select(position => FormattableString.Invariant($"{position.Longitude},{position.Latitude}") + (position.Altitude is { } altitude ? FormattableString.Invariant($",{altitude}") : ""))));
    }

    [Theory]
    [InlineData("<kml")]
    [InlineData("<html/>")]
    [InlineData("""<!DOCTYPE kml [<!ENTITY x "y">]><kml/>""")]
    [InlineData("""<!DOCTYPE kml SYSTEM "http://example.com/kml.dtd"><kml/>""")]
    [InlineData("<Placemark><Point><coordinates>1</coordinates></Point></Placemark>")]
    [InlineData("<Placemark><Point><coordinates>1,a</coordinates></Point></Placemark>")]
    [InlineData("<Placemark><Point><coordinates>1,2,3,4</coordinates></Point></Placemark>")]
    [InlineData("<Placemark><Point><coordinates>,1,2</coordinates></Point></Placemark>")]
    [InlineData("<Placemark><Point><coordinates>1,,2</coordinates></Point></Placemark>")]
    [InlineData("<Placemark><Point><coordinates>NaN,2</coordinates></Point></Placemark>")]
    [InlineData("<Placemark><Point><coordinates>1e999,2</coordinates></Point></Placemark>")]
    [InlineData("<Placemark><Point/></Placemark>")]
    [InlineData("<Placemark><Polygon><innerBoundaryIs><LinearRing><coordinates>0,0 1,1 0,0</coordinates></LinearRing></innerBoundaryIs></Polygon></Placemark>")]
    [InlineData("<Placemark><Track><coord>1</coord></Track></Placemark>")]
    [InlineData("<Placemark><Track><coord>1 2 3 4</coord></Track></Placemark>")]
    public void Parse_Invalid(string kml)
    {
        Assert.Throws<FormatException>(() => Kml.Parse(kml));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(kml));
        Assert.Throws<FormatException>(() => Kml.Read(stream));
    }

    [Fact]
    public void Read_DoesNotResolveExternalEntities()
    {
        var path = Path.GetFullPath("kml-secret.txt");
        File.WriteAllText(path, "secret");
        var kml = $"""<?xml version="1.0"?><!DOCTYPE kml [<!ENTITY secret SYSTEM "{new Uri(path)}">]><kml><Placemark><name>&secret;</name></Placemark></kml>""";
        var exception = Assert.Throws<FormatException>(() => Kml.Parse(kml));
        Assert.IsType<System.Xml.XmlException>(exception.InnerException);
    }

    [Fact]
    public void Read_Encodings()
    {
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes("""<?xml version="1.0" encoding="ISO-8859-1"?><kml><Placemark><name>Café</name></Placemark></kml>"""));
        Assert.Equal("Café", Assert.Single(Kml.Read(stream)).Properties["name"]);
    }

    [Fact]
    public void Format_Document()
    {
        GeoFeatureCollection features = new([
            new GeoFeature(
                new GeoPoint(new(2.3522, 48.8566)),
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "Paris", ["description"] = "<b>Capital</b>", ["population"] = 2102650, ["capital"] = true, ["none"] = null },
                id: "paris"),
            new GeoFeature(geometry: null, id: "42"),
        ])
        { Name = "Cities & towns" };

        Assert.Equal(
            """<?xml version="1.0" encoding="utf-8"?><kml xmlns="http://www.opengis.net/kml/2.2"><Document><name>Cities &amp; towns</name><Placemark id="paris"><name>Paris</name><description><![CDATA[<b>Capital</b>]]></description><ExtendedData><Data name="population"><value>2102650</value></Data><Data name="capital"><value>true</value></Data></ExtendedData><Point><coordinates>2.3522,48.8566</coordinates></Point></Placemark><Placemark /></Document></kml>""",
            Kml.Format(features));
    }

    [Fact]
    public void Format_Geometries()
    {
        GeoFeatureCollection features =
        [
            new GeoFeature(new GeoLineString([new(0, 1, 2), new(3, 4)])),
            new GeoFeature(new GeoPolygon([[new(0, 0), new(1, 0), new(1, 1)], [new(0.2, 0.2), new(0.4, 0.2), new(0.4, 0.4), new(0.2, 0.2)]])),
            new GeoFeature(new GeoMultiPoint([new(1, 2), new(3, 4)])),
            new GeoFeature(new GeoGeometryCollection([new GeoMultiLineString([new GeoLineString([new(0, 0), new(1, 1)])]), new GeoMultiPolygon([])])),
            new GeoFeature(new GeoPoint(new(0.1 + 0.2, -1e-7, 1234.5))),
        ];

        var kml = Kml.Format(features);
        Assert.Contains("<LineString><altitudeMode>absolute</altitudeMode><coordinates>0,1,2 3,4</coordinates></LineString>", kml);
        Assert.Contains("<Polygon><outerBoundaryIs><LinearRing><coordinates>0,0 1,0 1,1 0,0</coordinates></LinearRing></outerBoundaryIs><innerBoundaryIs><LinearRing><coordinates>0.2,0.2 0.4,0.2 0.4,0.4 0.2,0.2</coordinates></LinearRing></innerBoundaryIs></Polygon>", kml);
        Assert.Contains("<MultiGeometry><Point><coordinates>1,2</coordinates></Point><Point><coordinates>3,4</coordinates></Point></MultiGeometry>", kml);
        Assert.Contains("<MultiGeometry><MultiGeometry><LineString><coordinates>0,0 1,1</coordinates></LineString></MultiGeometry><MultiGeometry /></MultiGeometry>", kml);
        Assert.Contains("<Point><altitudeMode>absolute</altitudeMode><coordinates>0.30000000000000004,-1E-07,1234.5</coordinates></Point>", kml);

        var copy = Kml.Parse(kml);
        Assert.Equal(new GeoPosition(0.1 + 0.2, -1e-7, 1234.5), Assert.IsType<GeoPoint>(copy[4].Geometry).Position);
        Assert.Equal([GeoGeometryType.MultiLineString, GeoGeometryType.GeometryCollection], Assert.IsType<GeoGeometryCollection>(copy[3].Geometry).Geometries.Select(geometry => geometry.Type));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var features = Kml.Parse(File.ReadAllText(GetTestDataPath("google-earth.kml")));
        var kml = Kml.Format(features, new KmlWriterOptions { Indented = true });
        var copy = Kml.Parse(kml);
        Assert.Equal(kml, Kml.Format(copy, new KmlWriterOptions { Indented = true }));
        Assert.Equal(features.Name, copy.Name);
        Assert.Equal(features.Select(feature => feature.Id), copy.Select(feature => feature.Id));
        Assert.Equal(features.Select(feature => feature.Geometry?.Type), copy.Select(feature => feature.Geometry?.Type));
        Assert.Equal(features.SelectMany(feature => feature.Geometry?.GetPositions() ?? []), copy.SelectMany(feature => feature.Geometry?.GetPositions() ?? []));
        Assert.Equal(features[0].Properties["description"], copy[0].Properties["description"]);
        Assert.Equal("330", copy[0].Properties["height"]);
        Assert.Equal("Monuments/Museums", copy[1].Properties["folder"]);
    }

    [Fact]
    public void Format_Options()
    {
        GeoFeatureCollection features = [new GeoFeature(new GeoPoint(new(1, 2)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = 5, ["title"] = "T", ["text"] = "a ]]> <b>" }, id: "not an id")];
        Assert.Equal(
            """<?xml version="1.0" encoding="utf-8"?><kml xmlns="http://www.opengis.net/kml/2.2"><Document><Placemark><name>T</name><description>a ]]&gt; &lt;b&gt;</description><ExtendedData><Data name="name"><value>5</value></Data></ExtendedData><Point><coordinates>1,2</coordinates></Point></Placemark></Document></kml>""",
            Kml.Format(features, new KmlWriterOptions { NameProperty = "title", DescriptionProperty = "text" }));
        Assert.Contains("""<Data name="title"><value>T</value></Data>""", Kml.Format(features, new KmlWriterOptions { NameProperty = null }));
        Assert.Contains("<Document>\n    <Placemark>", Kml.Format(features, new KmlWriterOptions { Indented = true }));

        using var stream = new MemoryStream();
        Kml.Write(features, stream);
        Assert.Equal((byte)'<', stream.ToArray()[0]);
    }

    [Fact]
    public void Format_Invalid()
    {
        Assert.Throws<ArgumentException>(() => Kml.Format([new GeoFeature(new GeoPoint(new(double.NaN, 2)))]));
        Assert.Throws<ArgumentException>(() => Kml.Format([new GeoFeature(geometry: null, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "\0" })]));
        Assert.Throws<ArgumentException>(() => Kmz.Write([new GeoFeature(new GeoPoint(new(1, double.PositiveInfinity)))], new MemoryStream()));
    }

    [Fact]
    public void Kmz_RoundTrips()
    {
        GeoFeatureCollection features = new([new GeoFeature(new GeoPoint(new(1, 2)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "A" })]) { Name = "N" };
        using var stream = new MemoryStream();
        Kmz.Write(features, stream);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal("doc.kml", Assert.Single(archive.Entries).FullName);
        }

        stream.Position = 0;
        var copy = Kmz.Read(stream);
        Assert.Equal("N", copy.Name);
        Assert.Equal("A", Assert.Single(copy).Properties["name"]);
    }

    [Theory]
    [InlineData("doc.kml", "files/a.kml|doc.kml|b.kml")]
    [InlineData("DOC.KML", "files/a.kml|DOC.KML")]
    [InlineData("b.kml", "files/a.kml|b.kml|c.kml")]
    [InlineData("files/a.kml", "image.png|files/a.kml|files/b.kml")]
    public void Kmz_DocumentEntry(string expected, string entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entries.Split('|'))
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write($"<kml><Document><name>{name}</name></Document></kml>");
            }
        }

        stream.Position = 0;
        Assert.Equal(expected, Kmz.Read(stream).Name);
    }

    [Fact]
    public void Kmz_Invalid()
    {
        Assert.Throws<FormatException>(() => Kmz.Read(new MemoryStream(Encoding.UTF8.GetBytes("<kml/>"))));

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("image.png");
        }

        stream.Position = 0;
        Assert.Throws<FormatException>(() => Kmz.Read(stream));
    }

    [Fact]
    public void ReadAndWrite_Example()
    {
        File.WriteAllText("trip.kml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <kml xmlns="http://www.opengis.net/kml/2.2">
              <Document>
                <name>Trip</name>
                <Folder>
                  <name>Monuments</name>
                  <Placemark>
                    <name>Eiffel Tower</name>
                    <description><![CDATA[<b>330</b> m]]></description>
                    <ExtendedData><Data name="year"><value>1889</value></Data></ExtendedData>
                    <Point><coordinates>2.2945,48.8584,0</coordinates></Point>
                  </Placemark>
                </Folder>
                <Folder>
                  <name>Restaurants</name>
                  <Placemark>
                    <name>Le Procope</name>
                    <Point><coordinates>2.3389,48.8530,0</coordinates></Point>
                  </Placemark>
                </Folder>
              </Document>
            </kml>
            """);

        #region Geo_KmlReadWrite
        GeoFeatureCollection placemarks;
        using (var stream = File.OpenRead("trip.kml"))
        {
            placemarks = Kml.Read(stream);
        }

        // The placemarks of every folder, in order; the "folder" property gives the folder of each one
        Assert.Equal("Trip", placemarks.Name);
        var monuments = placemarks.Where(placemark => (string?)placemark.Properties.GetValueOrDefault("folder") == "Monuments").ToList();
        var eiffelTower = Assert.Single(monuments);
        Assert.Equal("Eiffel Tower", eiffelTower.Properties["name"]);
        Assert.Equal("<b>330</b> m", eiffelTower.Properties["description"]);
        Assert.Equal("1889", eiffelTower.Properties["year"]); // KML has no types: ExtendedData values are strings

        using (var stream = File.Create("monuments.kml"))
        {
            Kml.Write(new GeoFeatureCollection(monuments) { Name = "Monuments" }, stream, new KmlWriterOptions { Indented = true });
        }
        #endregion

        Assert.Single(Kml.Parse(File.ReadAllText("monuments.kml")));

        #region Geo_KmzReadWrite
        using (var stream = File.Create("trip.kmz"))
        {
            Kmz.Write(placemarks, stream);
        }

        using (var stream = File.OpenRead("trip.kmz"))
        {
            var copy = Kmz.Read(stream);
            Assert.HasCount(2, copy);
        }
        #endregion
    }

    [Fact]
    public void Kmz_EncryptedEntry()
    {
        var zip = EncryptEntries(CreateZip("doc.kml", "<kml/>"));
#if NET11_0_OR_GREATER
        // .NET 11 reads encrypted entries, and does not support the encryption of this one
        Assert.Throws<FormatException>(() => Kmz.Read(new MemoryStream(zip)));
#else
        // .NET 10 ignores the flag, and the entry is not encrypted
        Assert.Empty(Kmz.Read(new MemoryStream(zip)));
#endif
    }

    // Sets the "encrypted" flag (bit 0 of the general purpose flags) of every entry, in the local and the central headers
    internal static byte[] EncryptEntries(byte[] zip)
    {
        for (var i = 0; i + 4 <= zip.Length; i++)
        {
            if (zip[i] is 0x50 && zip[i + 1] is 0x4B && zip[i + 2] is 0x03 && zip[i + 3] is 0x04)
            {
                zip[i + 6] |= 1;
            }
            else if (zip[i] is 0x50 && zip[i + 1] is 0x4B && zip[i + 2] is 0x01 && zip[i + 3] is 0x02)
            {
                zip[i + 8] |= 1;
            }
        }

        return zip;
    }

    internal static byte[] CreateZip(string name, string content)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = new StreamWriter(archive.CreateEntry(name).Open());
            entry.Write(content);
        }

        return memory.ToArray();
    }

    [Fact]
    public void Read_InvalidCharacterInDetectedEncoding()
    {
        // "<" in UTF-32, then a surrogate (U+DB00), which UTF-32 cannot hold: creating the XML reader fails
        Assert.Throws<FormatException>(() => Kml.Read(new MemoryStream([0x3C, 0, 0, 0, 0, 0xDB, 0, 0])));
    }

    [Fact]
    public void Read_NonAsciiBytesInDeclaration()
    {
        // A byte above 0x7F is not valid in an XML declaration; when switching to the declared encoding, .NET 10 counts each
        // one as several bytes, past the bytes read so far
        var document = "<?xml version=\"1.0"u8.ToArray().Concat(Enumerable.Repeat((byte)0xFF, 141)).Concat("\" encoding=\"UTF-8\"?>\n<a>"u8.ToArray()).Concat(Enumerable.Repeat((byte)'a', 80)).ToArray();
        Assert.Throws<FormatException>(() => Kml.Read(new MemoryStream(document)));
    }
}
