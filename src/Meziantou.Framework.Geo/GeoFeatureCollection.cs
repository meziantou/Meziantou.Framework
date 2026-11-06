using System.Collections.ObjectModel;

namespace Meziantou.Framework.Geo;

/// <summary>A list of features, such as the countries of a map, as the readers return it and the writers take it.</summary>
public sealed class GeoFeatureCollection : Collection<GeoFeature>
{
    /// <summary>Initializes a new instance of the <see cref="GeoFeatureCollection"/> class that is empty.</summary>
    public GeoFeatureCollection()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GeoFeatureCollection"/> class that contains features.</summary>
    /// <param name="features">The features.</param>
    public GeoFeatureCollection(IEnumerable<GeoFeature> features)
        : base([.. features ?? throw new ArgumentNullException(nameof(features))])
    {
    }

    /// <summary>Gets or sets the name of the collection, such as the name of a KML document or of a TopoJSON object, or <see langword="null"/>.</summary>
    public string? Name { get; set; }

    /// <summary>Gets the smallest box that contains the geometries of the features.</summary>
    /// <returns>The box, or <see langword="null"/> when no feature has a position.</returns>
    public GeoBounds? GetBounds() => GeoBounds.FromPositions(this.Where(feature => feature.Geometry is not null).SelectMany(feature => feature.Geometry!.GetPositions()));

    /// <inheritdoc />
    protected override void InsertItem(int index, GeoFeature item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
    }

    /// <inheritdoc />
    protected override void SetItem(int index, GeoFeature item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
    }
}
