using System.Runtime.CompilerServices;
using System.Text.Json;
using Parquet;
using Parquet.Schema;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>
/// Reads the features of a GeoParquet file one row group at a time: the geometry column (WKB) and the top-level columns
/// of the row group are read, the features are returned, then the next row group is read.
/// </summary>
internal static class GeoParquetReader
{
    public static async IAsyncEnumerable<GeoFeature> ReadAsync(Stream stream, GeoParquetReaderOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ParquetReader reader;
        try
        {
            reader = await ParquetReader.CreateAsync(stream, parquetOptions: null, leaveStreamOpen: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || IsParquetError(ex))
        {
            // Parquet.Net reports data that does not have the signature of the format as an IOException
            throw new FormatException("The data is not a valid Parquet file: " + ex.Message, ex);
        }

        await using (reader.ConfigureAwait(false))
        {
            var metadata = GeoParquetMetadata.Read(reader.CustomMetadata, options?.GeometryColumn);
            var fields = reader.Schema.GetDataFields();
            var geometryField = Array.Find(fields, field => field.Path.Length == 1 && field.Name == metadata.GeometryColumn)
                ?? throw new FormatException($"The GeoParquet file has no column '{metadata.GeometryColumn}'.");
            if (!ParquetColumns.IsBinary(geometryField))
                throw new FormatException($"The geometry column '{metadata.GeometryColumn}' of the GeoParquet file is not a binary column.");

            // The properties are the top-level columns; the columns of a struct, a list or a map, such as the bbox covering, are not read
            var coveringColumn = metadata.Covering?.Column;
            var propertyFields = fields
                .Where(field => field.Path.Length == 1 && !ReferenceEquals(field, geometryField) && field.Name != coveringColumn && ParquetColumns.IsSupported(field))
                .ToArray();
            var covering = metadata.Covering is { } c ? GetCoveringFields(fields, c) : null;
            var bounds = options?.Bounds;

            for (var i = 0; i < reader.RowGroupCount; i++)
            {
                using var rowGroup = reader.OpenRowGroupReader(i);
                if (bounds is { } box && covering is not null && !ParquetColumns.MayIntersect(rowGroup, covering.Value, box))
                    continue;

                var rowCount = checked((int)rowGroup.RowCount);
                byte[]?[] geometries;
                var values = new object?[propertyFields.Length][];
                try
                {
                    geometries = await ParquetColumns.ReadBinaryAsync(rowGroup, geometryField, rowCount, cancellationToken).ConfigureAwait(false);
                    for (var j = 0; j < propertyFields.Length; j++)
                    {
                        values[j] = await ParquetColumns.ReadAsync(rowGroup, propertyFields[j], rowCount, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (IsParquetError(ex))
                {
                    throw new FormatException("The Parquet file cannot be read: " + ex.Message, ex);
                }

                for (var row = 0; row < rowCount; row++)
                {
                    var geometry = geometries[row] is { } wkb ? ReadGeometry(wkb) : null;
                    if (bounds is { } filter && (geometry?.GetBounds() is not { } geometryBounds || !Intersects(filter, geometryBounds)))
                        continue;

                    var properties = new Dictionary<string, object?>(propertyFields.Length, StringComparer.Ordinal);
                    for (var j = 0; j < propertyFields.Length; j++)
                    {
                        if (values[j][row] is { } value)
                        {
                            properties[propertyFields[j].Name] = value;
                        }
                    }

                    yield return new GeoFeature(geometry, properties);
                }
            }
        }
    }

    // An empty geometry, such as POINT EMPTY (NaN coordinates) or POLYGON EMPTY, gives a feature without geometry
    private static GeoGeometry? ReadGeometry(byte[] wkb)
    {
        var geometry = WellKnownBinary.Read(wkb);
        return geometry.GetPositions().Any(position => double.IsFinite(position.Longitude) && double.IsFinite(position.Latitude)) ? geometry : null;
    }

    private static bool Intersects(GeoBounds a, GeoBounds b) => b.West <= a.East && b.East >= a.West && b.South <= a.North && b.North >= a.South;

    private static (DataField XMin, DataField YMin, DataField XMax, DataField YMax)? GetCoveringFields(DataField[] fields, GeoParquetCovering covering)
    {
        var xmin = Find(covering.XMin);
        var ymin = Find(covering.YMin);
        var xmax = Find(covering.XMax);
        var ymax = Find(covering.YMax);
        return xmin is null || ymin is null || xmax is null || ymax is null ? null : (xmin, ymin, xmax, ymax);

        DataField? Find(string[] path) => Array.Find(fields, field => field.Path.ToList().SequenceEqual(path, StringComparer.Ordinal));
    }

    // Parquet.Net reports invalid data with several exception types
    private static bool IsParquetError(Exception exception) => exception is EndOfStreamException or ParquetException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or IndexOutOfRangeException or OverflowException or JsonException;
}
