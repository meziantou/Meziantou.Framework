using BenchmarkDotNet.Attributes;

#pragma warning disable CA5394 // The data of the benchmarks is not security sensitive

namespace Meziantou.Framework.Geo.Benchmarks;

[MemoryDiagnoser]
public class OperationBenchmarks
{
    private GeoGeometry[] _geometries = [];
    private GeoPosition[] _points = [];
    private GeoGeometry _france = null!;
    private GeoGeometry _spain = null!;

    [GlobalSetup]
    public void Setup()
    {
        _geometries = [.. BenchmarkData.Countries.Select(feature => feature.Geometry!)];
        var random = new Random(42);
        _points = [.. Enumerable.Range(0, 1000).Select(_ => new GeoPosition((random.NextDouble() * 360) - 180, (random.NextDouble() * 140) - 60))];
        _france = BenchmarkData.Countries.First(feature => feature.Id == "FRA").Geometry!;
        _spain = BenchmarkData.Countries.First(feature => feature.Id == "ESP").Geometry!;
    }

    [Benchmark]
    public int Contains()
    {
        var count = 0;
        foreach (var point in _points)
        {
            foreach (var geometry in _geometries)
            {
                if (geometry switch { GeoPolygon polygon => polygon.Contains(point), GeoMultiPolygon multiPolygon => multiPolygon.Contains(point), _ => false })
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Benchmark]
    public int Contains_Geodesic()
    {
        var count = 0;
        foreach (var point in _points.AsSpan(0, 50))
        {
            foreach (var geometry in _geometries)
            {
                if (geometry switch { GeoPolygon polygon => polygon.Contains(point, GeoEdgeType.Geodesic), GeoMultiPolygon multiPolygon => multiPolygon.Contains(point, GeoEdgeType.Geodesic), _ => false })
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Benchmark]
    public double GetBounds()
    {
        var sum = 0.0;
        foreach (var geometry in _geometries)
        {
            sum += geometry.GetBounds()!.Value.West;
        }

        return sum;
    }

    [Benchmark]
    public double GetBounds_Geodesic()
    {
        var sum = 0.0;
        foreach (var geometry in _geometries)
        {
            sum += geometry.GetBounds(GeoEdgeType.Geodesic)!.Value.West;
        }

        return sum;
    }

    [Benchmark]
    public double LabelPosition()
    {
        var sum = 0.0;
        foreach (var geometry in _geometries)
        {
            // A new geometry each time: the label position is cached
            sum += geometry.Transform(position => position).LabelPosition!.Value.Longitude;
        }

        return sum;
    }

    [Benchmark]
    public object MakeValid()
    {
        GeoGeometry? last = null;
        foreach (var geometry in _geometries)
        {
            last = geometry.MakeValid();
        }

        return last!;
    }

    [Benchmark]
    public object? Simplify()
    {
        GeoGeometry? last = null;
        foreach (var geometry in _geometries)
        {
            last = geometry.Simplify(0.1);
        }

        return last;
    }

    [Benchmark]
    public object? Clip()
    {
        GeoGeometry? last = null;
        var bounds = new GeoBounds(-10, 35, 30, 60);
        foreach (var geometry in _geometries)
        {
            last = geometry.Clip(bounds) ?? last;
        }

        return last;
    }

    [Benchmark]
    public object? Union() => _france.Union(_spain);

    [Benchmark]
    public object? Buffer() => _spain.Buffer(10_000);

    [Benchmark]
    public object? ConvexHull()
    {
        GeoGeometry? last = null;
        foreach (var geometry in _geometries)
        {
            last = geometry.ConvexHull();
        }

        return last;
    }

    [Benchmark]
    public double? Distance() => _france.Distance(_geometries[0]);

    [Benchmark]
    public double Length()
    {
        var sum = 0.0;
        foreach (var geometry in _geometries)
        {
            foreach (var line in geometry.GetBoundaryLines())
            {
                sum += line.GetLength();
            }
        }

        return sum;
    }

    [Benchmark]
    public object SplitAtAntimeridian()
    {
        GeoGeometry? last = null;
        foreach (var geometry in _geometries)
        {
            last = geometry.SplitAtAntimeridian();
        }

        return last!;
    }
}
