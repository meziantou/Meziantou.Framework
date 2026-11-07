namespace Meziantou.Framework.Geo.Maps.Internals;

internal sealed class FolderMap : Map
{
    private readonly FolderMapFormat _format;
    private readonly string _directory;

    public FolderMap(FolderMapFormat format, string directory, MapMetadata metadata)
        : base(metadata)
    {
        _format = format;
        _directory = directory;
    }

    public override bool HasLayer(MapLayer layer) => File.Exists(_format.GetLayerPath(_directory, layer));

    protected override IEnumerable<GeoFeature> LoadLayer(MapLayer layer, GeoBounds? bounds) => _format.Read(_format.GetLayerPath(_directory, layer), bounds);
}
