using Meziantou.Framework.Charts;
using Meziantou.Framework.Imaging.Charts;

namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>Renders a preview of a bundle, every layer on one map, as SVG and PNG, to check what a build produced.</summary>
internal static class PreviewRenderer
{
    private const double Width = 1024;

    public static void Render(Region region, Dictionary<MapLayer, GeoFeatureCollection> layers, GeoBounds? extent, string directory)
    {
        if (extent is not { } bounds)
            return;

        // The world and the regions near the poles, such as Antarctica, do not fit Mercator, which stops at 85°
        GeoProjection projection = bounds.North - bounds.South > 100 || bounds.South < -80 || bounds.North > 80 ? new EqualEarthProjection() : new MercatorProjection();
        var (x0, y0) = projection.Project(new GeoPosition(bounds.West, Math.Max(bounds.South, -projection.MaxLatitude)));
        var (x1, y1) = projection.Project(new GeoPosition(bounds.East, Math.Min(bounds.North, projection.MaxLatitude)));
        var height = Math.Clamp(Width * Math.Abs(y1 - y0) / Math.Max(Math.Abs(x1 - x0), 1e-9), 256, 2048);

        // Half a pixel of the preview: smaller details are not visible, and would make the SVG files very large
        var tolerance = (bounds.East - bounds.West) / Width / 2;
        var plot = new Plot
        {
            Title = region.Name,
            Width = Width,
            Height = height,
            Clip = true,
            Coordinates = new GeoCoordinates { Projection = projection, Extent = bounds },
        };

        AddLayer(plot, layers, MapLayer.Ocean, tolerance, fill: "#cfe3f3", stroke: null);
        AddLayer(plot, layers, MapLayer.Land, tolerance, fill: "#f4f1ea", stroke: null);
        AddLayer(plot, layers, MapLayer.StatesProvinces, tolerance, fill: "#ebe6da", stroke: "#c8bfae");
        AddLayer(plot, layers, MapLayer.Borders, tolerance, fill: null, stroke: "#6b6255");
        AddLayer(plot, layers, MapLayer.Lakes, tolerance, fill: "#a9cbe8", stroke: null);
        AddLayer(plot, layers, MapLayer.Rivers, tolerance, fill: null, stroke: "#6fa5d6");
        AddLayer(plot, layers, MapLayer.Coastline, tolerance, fill: null, stroke: "#7d9bb5");

        // The largest cities: the marks of the charts do not move labels that overlap, so only a few are labeled
        if (layers.TryGetValue(MapLayer.Cities, out var cities))
        {
            var largest = cities
                .Where(city => city.Geometry is GeoPoint point && bounds.Contains(point.Position))
                .OrderByDescending(city => (int)city.Properties["rank"]!)
                .ThenByDescending(city => (long)city.Properties["population"]!)
                .Take(12)
                .Select(city => (Name: (string?)city.Properties["name"], Position: ((GeoPoint)city.Geometry!).Position))
                .ToList();
            plot.Add(new DotMark<(string? Name, GeoPosition Position)>(largest, x: city => city.Position.Longitude, y: city => city.Position.Latitude) { FillColor = "#2b2b2b", Radius = 2.5 });
            plot.Add(new TextMark<(string? Name, GeoPosition Position)>(largest, text: city => city.Name, x: city => city.Position.Longitude, y: city => city.Position.Latitude) { OffsetY = -9, FontSize = 11 });
        }

        Directory.CreateDirectory(directory);
        using (var stream = File.Create(Path.Combine(directory, region.Key + ".svg")))
        {
            plot.WriteSvg(stream);
        }

        using var image = plot.ToImage();
        image.Save(Path.Combine(directory, region.Key + ".png"));
    }

    private static void AddLayer(Plot plot, Dictionary<MapLayer, GeoFeatureCollection> layers, MapLayer layer, double tolerance, string? fill, string? stroke)
    {
        if (!layers.TryGetValue(layer, out var features))
            return;

        var geometries = features.Select(feature => feature.Geometry?.Simplify(tolerance)).OfType<GeoGeometry>().ToList();
        // Without a color, the marks get the colors of the theme: the tiles of the land and the ocean would be outlined
        plot.Add(new GeoMark<GeoGeometry>(geometries, geometry => geometry)
        {
            FillColor = fill ?? ChartColor.Transparent,
            StrokeColor = stroke ?? ChartColor.Transparent,
            StrokeWidth = 0.75,
        });
    }
}
