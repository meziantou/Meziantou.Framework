using System.Runtime.CompilerServices;
using Meziantou.Framework.Geo.Internals.Parquet;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>
/// Reads the features of a GeoParquet file one row group at a time: the geometry column (WKB) and the top-level columns
/// of the row group are read, the features are returned, then the next row group is read.
/// </summary>
internal static class GeoParquetReader
{
    public static async IAsyncEnumerable<GeoFeature> ReadAsync(Stream stream, GeoParquetReaderOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ParquetFile file;
        try
        {
            file = await ParquetFile.OpenAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (OverflowException ex)
        {
            throw new FormatException("The metadata of the Parquet file is not valid: " + ex.Message, ex);
        }

        var metadata = GeoParquetMetadata.Read(file.KeyValueMetadata, options?.GeometryColumn);
        var columns = file.Columns;
        var geometryColumn = Array.FindIndex(columns, column => column.Path.Length == 1 && column.Name == metadata.GeometryColumn);
        if (geometryColumn < 0)
            throw new FormatException($"The GeoParquet file has no column '{metadata.GeometryColumn}'.");

        if (columns[geometryColumn].PhysicalType is not (ParquetColumn.ByteArray or ParquetColumn.FixedLenByteArray))
            throw new FormatException($"The geometry column '{metadata.GeometryColumn}' of the GeoParquet file is not a binary column.");

        // The properties are the top-level columns that are not repeated; the columns of a structure, a list or a map,
        // such as the bbox covering, are not read
        var coveringColumn = metadata.Covering?.Column;
        var propertyColumns = Enumerable.Range(0, columns.Length)
            .Where(i => i != geometryColumn && columns[i].Path.Length == 1 && columns[i].MaxRepetitionLevel == 0 && columns[i].Name != coveringColumn)
            .ToArray();
        var covering = metadata.Covering is { } c ? GetCoveringColumns(columns, c) : null;
        var bounds = options?.Bounds;

        for (var rowGroup = 0; rowGroup < file.RowGroupCount; rowGroup++)
        {
            if (bounds is { } box && covering is { } coveringColumns && !MayIntersect(file, rowGroup, coveringColumns, box))
                continue;

            var rowCount = file.GetRowCount(rowGroup);
            object?[] geometries;
            var values = new object?[propertyColumns.Length][];
            try
            {
                geometries = await file.ReadColumnAsync(rowGroup, geometryColumn, cancellationToken).ConfigureAwait(false);
                for (var j = 0; j < propertyColumns.Length; j++)
                {
                    values[j] = await file.ReadColumnAsync(rowGroup, propertyColumns[j], cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OverflowException ex)
            {
                throw new FormatException("A value of the Parquet file is out of range: " + ex.Message, ex);
            }

            for (var row = 0; row < rowCount; row++)
            {
                var geometry = geometries[row] switch
                {
                    byte[] wkb => ReadGeometry(wkb),
                    null => null,
                    _ => throw new FormatException($"The geometry column '{metadata.GeometryColumn}' of the GeoParquet file is not a binary column."),
                };

                if (bounds is { } filter && (geometry?.GetBounds() is not { } geometryBounds || !Intersects(filter, geometryBounds)))
                    continue;

                var properties = new Dictionary<string, object?>(propertyColumns.Length, StringComparer.Ordinal);
                for (var j = 0; j < propertyColumns.Length; j++)
                {
                    if (values[j][row] is { } value)
                    {
                        properties[columns[propertyColumns[j]].Name] = value;
                    }
                }

                yield return new GeoFeature(geometry, properties);
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

    /// <summary>
    /// Gets a value that indicates whether a row group may have geometries in a box: <see langword="false"/> only when
    /// the statistics of the bbox covering show that every geometry is outside of it.
    /// </summary>
    private static bool MayIntersect(ParquetFile file, int rowGroup, (int XMin, int YMin, int XMax, int YMax) covering, GeoBounds box)
    {
        return !(file.GetDoubleStatistics(rowGroup, covering.XMin).Min > box.East
            || file.GetDoubleStatistics(rowGroup, covering.XMax).Max < box.West
            || file.GetDoubleStatistics(rowGroup, covering.YMin).Min > box.North
            || file.GetDoubleStatistics(rowGroup, covering.YMax).Max < box.South);
    }

    private static (int XMin, int YMin, int XMax, int YMax)? GetCoveringColumns(ParquetColumn[] columns, GeoParquetCovering covering)
    {
        var xmin = Find(covering.XMin);
        var ymin = Find(covering.YMin);
        var xmax = Find(covering.XMax);
        var ymax = Find(covering.YMax);
        return xmin < 0 || ymin < 0 || xmax < 0 || ymax < 0 ? null : (xmin, ymin, xmax, ymax);

        int Find(string[] path) => Array.FindIndex(columns, column => column.Path.AsSpan().SequenceEqual(path));
    }
}
