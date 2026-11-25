using System.Text;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Geo.Benchmarks;

[MemoryDiagnoser]
public class FormatBenchmarks
{
    private byte[] _geoJsonUtf8 = [];
    private GeoFeatureCollection _features = [];
    private GeoGeometry[] _geometries = [];
    private string[] _wkt = [];
    private byte[][] _wkb = [];
    private byte[] _flatGeobuf = [];
    private byte[] _shp = [];
    private byte[] _dbf = [];
    private string _topoJson = "";
    private string _kml = "";
    private string _polyline = "";
    private GeoPosition[] _polylinePositions = [];

    [GlobalSetup]
    public void Setup()
    {
        _geoJsonUtf8 = Encoding.UTF8.GetBytes(BenchmarkData.CountriesJson);
        _features = BenchmarkData.Countries;
        _geometries = [.. _features.Select(feature => feature.Geometry!)];
        _wkt = [.. _geometries.Select(WellKnownText.Format)];
        _wkb = [.. _geometries.Select(geometry => WellKnownBinary.Write(geometry))];
        _flatGeobuf = FlatGeobuf.Write(_features);
        using (var shp = new MemoryStream())
        using (var shx = new MemoryStream())
        using (var dbf = new MemoryStream())
        {
            Shapefile.Write(_features, shp, shx, dbf);
            _shp = shp.ToArray();
            _dbf = dbf.ToArray();
        }

        _topoJson = TopoJson.Format(_features);
        _kml = Kml.Format(_features);
        _polylinePositions = [.. _geometries.SelectMany(geometry => geometry.GetPositions())];
        _polyline = EncodedPolyline.Encode(_polylinePositions);
    }

    [Benchmark]
    public GeoFeatureCollection GeoJson_Read() => GeoJson.Read(new MemoryStream(_geoJsonUtf8));

    [Benchmark]
    public string GeoJson_Format() => GeoJson.Format(_features);

    [Benchmark]
    public object WellKnownText_Parse()
    {
        GeoGeometry? last = null;
        foreach (var wkt in _wkt)
        {
            last = WellKnownText.Parse(wkt);
        }

        return last!;
    }

    [Benchmark]
    public int WellKnownText_Format()
    {
        var length = 0;
        foreach (var geometry in _geometries)
        {
            length += WellKnownText.Format(geometry).Length;
        }

        return length;
    }

    [Benchmark]
    public object WellKnownBinary_Read()
    {
        GeoGeometry? last = null;
        foreach (var wkb in _wkb)
        {
            last = WellKnownBinary.Read(wkb);
        }

        return last!;
    }

    [Benchmark]
    public int WellKnownBinary_Write()
    {
        var length = 0;
        foreach (var geometry in _geometries)
        {
            length += WellKnownBinary.Write(geometry).Length;
        }

        return length;
    }

    [Benchmark]
    public GeoFeatureCollection FlatGeobuf_Read() => FlatGeobuf.Read(_flatGeobuf);

    [Benchmark]
    public byte[] FlatGeobuf_Write() => FlatGeobuf.Write(_features);

    [Benchmark]
    public GeoFeatureCollection Shapefile_Read() => Shapefile.Read(new MemoryStream(_shp), new MemoryStream(_dbf));

    [Benchmark]
    public long Shapefile_Write()
    {
        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(_features, shp, shx, dbf);
        return shp.Length;
    }

    [Benchmark]
    public GeoFeatureCollection TopoJson_Parse() => TopoJson.Parse(_topoJson);

    [Benchmark]
    public string TopoJson_Format() => TopoJson.Format(_features);

    [Benchmark]
    public GeoFeatureCollection Kml_Parse() => Kml.Parse(_kml);

    [Benchmark]
    public string Kml_Format() => Kml.Format(_features);

    [Benchmark]
    public int EncodedPolyline_Decode() => EncodedPolyline.Decode(_polyline).Count;

    [Benchmark]
    public string EncodedPolyline_Encode() => EncodedPolyline.Encode(_polylinePositions);
}
