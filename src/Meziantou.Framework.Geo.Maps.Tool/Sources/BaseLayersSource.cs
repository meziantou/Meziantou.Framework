#pragma warning disable MA0004 // Use Task.ConfigureAwait

namespace Meziantou.Framework.Geo.Maps.Tool.Sources;

/// <summary>
/// The land, ocean and coastline of Natural Earth at 1:10m (public domain). The land and the ocean are a few polygons
/// of hundreds of thousands of positions: they are repaired and cut into tiles once, when they are loaded, so that a
/// region only cuts the tiles it overlaps. The tiles of a polygon are merged back once they are cut: maps drawn with a
/// curved projection would show the seams between them.
/// </summary>
internal sealed class BaseLayersSource
{
    // A tile is cut in four while it has more positions
    private const int MaxTilePositions = 20_000;

    private readonly List<TiledPolygon> _land;
    private readonly List<TiledPolygon> _ocean;
    private readonly List<(GeoGeometry Geometry, GeoBounds Bounds)> _coastline;

    private BaseLayersSource(List<TiledPolygon> land, List<TiledPolygon> ocean, List<(GeoGeometry Geometry, GeoBounds Bounds)> coastline)
    {
        _land = land;
        _ocean = ocean;
        _coastline = coastline;
    }

    public static async Task<BaseLayersSource> LoadAsync(Downloader downloader, string commit)
    {
        var land = await NaturalEarthSource.ReadAsync(downloader, commit, "10m_physical", "ne_10m_land");
        var ocean = await NaturalEarthSource.ReadAsync(downloader, commit, "10m_physical", "ne_10m_ocean");
        var coastline = await NaturalEarthSource.ReadAsync(downloader, commit, "10m_physical", "ne_10m_coastline");
        return new BaseLayersSource(Tile(land), Tile(ocean), [.. coastline.Select(feature => feature.Geometry).OfType<GeoGeometry>().Select(geometry => (geometry, geometry.GetBounds()!.Value))]);
    }

    /// <summary>Gets the land polygons in a box, cut to it.</summary>
    public IReadOnlyList<GeoFeature> GetLand(GeoBounds bounds) => Clip(_land, bounds);

    /// <summary>Gets the ocean polygons in a box, cut to it.</summary>
    public IReadOnlyList<GeoFeature> GetOcean(GeoBounds bounds) => Clip(_ocean, bounds);

    /// <summary>Gets the coastlines in a box, cut to it.</summary>
    public IReadOnlyList<GeoFeature> GetCoastline(GeoBounds bounds) => Clip(_coastline, bounds);

    private static List<GeoFeature> Clip(List<TiledPolygon> source, GeoBounds bounds) =>
    [
        .. source
            .Where(polygon => GeoBoxes.Intersects(bounds, polygon.Bounds))
            .AsParallel()
            .AsOrdered()
            .Select(polygon => polygon.Clip(bounds))
            .OfType<GeoGeometry>()
            .Select(geometry => new GeoFeature(geometry)),
    ];

    private static List<GeoFeature> Clip(List<(GeoGeometry Geometry, GeoBounds Bounds)> source, GeoBounds bounds) =>
    [
        .. source
            .Where(item => GeoBoxes.Intersects(bounds, item.Bounds))
            .AsParallel()
            .AsOrdered()
            .Select(item => item.Geometry.Clip(bounds))
            .OfType<GeoGeometry>()
            .Select(geometry => new GeoFeature(geometry)),
    ];

    private static List<TiledPolygon> Tile(GeoFeatureCollection features) =>
    [
        .. features
            .Select(feature => feature.Geometry)
            .OfType<GeoGeometry>()
            .SelectMany(geometry => geometry.GetSimpleGeometries())
            .AsParallel()
            .AsOrdered()
            .Select(polygon => polygon.MakeValid())
            .Where(polygon => !polygon.IsEmpty)
            .Select(polygon =>
            {
                var bounds = polygon.GetBounds()!.Value;
                return new TiledPolygon(polygon, bounds, [.. Split(polygon, bounds).Select(tile => (tile, tile.GetBounds()!.Value))]);
            }),
    ];

    /// <summary>Cuts a polygon in the four quarters of its box until each part is small: each level reads every position once.</summary>
    private static List<GeoGeometry> Split(GeoGeometry geometry, GeoBounds bounds)
    {
        if (geometry.GetPositions().Take(MaxTilePositions + 1).Count() <= MaxTilePositions)
            return [geometry];

        var middleX = (bounds.West + bounds.East) / 2;
        var middleY = (bounds.South + bounds.North) / 2;
        GeoBounds[] quarters =
        [
            new(bounds.West, bounds.South, middleX, middleY),
            new(middleX, bounds.South, bounds.East, middleY),
            new(bounds.West, middleY, middleX, bounds.North),
            new(middleX, middleY, bounds.East, bounds.North),
        ];

        return quarters
            .Select(quarter => (Quarter: quarter, Part: geometry.Clip(quarter)))
            .Where(item => item.Part is not null)
            .SelectMany(item => Split(item.Part!, item.Quarter))
            .ToList();
    }

    /// <summary>A polygon of the source, and its tiles.</summary>
    private sealed record TiledPolygon(GeoGeometry Geometry, GeoBounds Bounds, List<(GeoGeometry Geometry, GeoBounds Bounds)> Tiles)
    {
        /// <summary>Cuts the polygon to a box: the tiles it overlaps, cut and merged back.</summary>
        public GeoGeometry? Clip(GeoBounds bounds)
        {
            if (bounds.West <= Bounds.West && bounds.East >= Bounds.East && bounds.South <= Bounds.South && bounds.North >= Bounds.North)
                return Geometry;

            var parts = Tiles
                .Where(tile => GeoBoxes.Intersects(bounds, tile.Bounds))
                .Select(tile => tile.Geometry.Clip(bounds))
                .OfType<GeoGeometry>()
                .SelectMany(part => part.GetSimpleGeometries().OfType<GeoPolygon>())
                .ToList();
            return parts.Count switch
            {
                0 => null,
                1 => parts[0],
                _ => new GeoMultiPolygon(parts).MakeValid(),
            };
        }
    }
}
