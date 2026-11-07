using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Maps.GeoPackage.Internals;

/// <summary>
/// Writes a GeoPackage 1.4 file: the required tables, a feature table per layer with an R-tree spatial index, and the
/// metadata of the map in the metadata extension, as JSON.
/// </summary>
[SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The table and column names are quoted by Quote; the values are parameters")]
internal static class GeoPackageWriter
{
    private const int Wgs84 = 4326;

    public static void Write(string path, MapMetadata metadata, string metadataJson, string date, IReadOnlyDictionary<MapLayer, GeoFeatureCollection> layers)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.Delete(path);
        using var connection = GeoPackageMapFormat.OpenConnection(path, SqliteOpenMode.ReadWriteCreate);
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction, $"PRAGMA application_id = {GeoPackageMapFormat.ApplicationId}; PRAGMA user_version = {GeoPackageMapFormat.UserVersion}");
            Execute(connection, transaction, """
                CREATE TABLE gpkg_spatial_ref_sys (srs_name TEXT NOT NULL, srs_id INTEGER NOT NULL PRIMARY KEY, organization TEXT NOT NULL, organization_coordsys_id INTEGER NOT NULL, definition TEXT NOT NULL, description TEXT);
                INSERT INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description) VALUES ('WGS 84 geodetic', 4326, 'EPSG', 4326, 'GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563,AUTHORITY["EPSG","7030"]],AUTHORITY["EPSG","6326"]],PRIMEM["Greenwich",0,AUTHORITY["EPSG","8901"]],UNIT["degree",0.0174532925199433,AUTHORITY["EPSG","9122"]],AXIS["Latitude",NORTH],AXIS["Longitude",EAST],AUTHORITY["EPSG","4326"]]', 'longitude/latitude coordinates in decimal degrees on the WGS 84 spheroid');
                INSERT INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description) VALUES ('Undefined Cartesian SRS', -1, 'NONE', -1, 'undefined', 'undefined Cartesian coordinate reference system');
                INSERT INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description) VALUES ('Undefined geographic SRS', 0, 'NONE', 0, 'undefined', 'undefined geographic coordinate reference system');
                CREATE TABLE gpkg_contents (table_name TEXT NOT NULL PRIMARY KEY, data_type TEXT NOT NULL, identifier TEXT UNIQUE, description TEXT DEFAULT '', last_change DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')), min_x DOUBLE, min_y DOUBLE, max_x DOUBLE, max_y DOUBLE, srs_id INTEGER, CONSTRAINT fk_gc_r_srs_id FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id));
                CREATE TABLE gpkg_geometry_columns (table_name TEXT NOT NULL, column_name TEXT NOT NULL, geometry_type_name TEXT NOT NULL, srs_id INTEGER NOT NULL, z TINYINT NOT NULL, m TINYINT NOT NULL, CONSTRAINT pk_geom_cols PRIMARY KEY (table_name, column_name), CONSTRAINT uk_gc_table_name UNIQUE (table_name), CONSTRAINT fk_gc_tn FOREIGN KEY (table_name) REFERENCES gpkg_contents(table_name), CONSTRAINT fk_gc_srs FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys (srs_id));
                CREATE TABLE gpkg_extensions (table_name TEXT, column_name TEXT, extension_name TEXT NOT NULL, definition TEXT NOT NULL, scope TEXT NOT NULL, CONSTRAINT ge_tce UNIQUE (table_name, column_name, extension_name));
                CREATE TABLE gpkg_metadata (id INTEGER CONSTRAINT m_pk PRIMARY KEY ASC NOT NULL, md_scope TEXT NOT NULL DEFAULT 'dataset', md_standard_uri TEXT NOT NULL, mime_type TEXT NOT NULL DEFAULT 'text/xml', metadata TEXT NOT NULL DEFAULT '');
                CREATE TABLE gpkg_metadata_reference (reference_scope TEXT NOT NULL, table_name TEXT, column_name TEXT, row_id_value INTEGER, timestamp DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')), md_file_id INTEGER NOT NULL, md_parent_id INTEGER, CONSTRAINT crmr_mfi_fk FOREIGN KEY (md_file_id) REFERENCES gpkg_metadata(id), CONSTRAINT crmr_mpi_fk FOREIGN KEY (md_parent_id) REFERENCES gpkg_metadata(id));
                INSERT INTO gpkg_extensions (table_name, column_name, extension_name, definition, scope) VALUES ('gpkg_metadata', NULL, 'gpkg_metadata', 'http://www.geopackage.org/spec120/#extension_metadata', 'read-write');
                INSERT INTO gpkg_extensions (table_name, column_name, extension_name, definition, scope) VALUES ('gpkg_metadata_reference', NULL, 'gpkg_metadata', 'http://www.geopackage.org/spec120/#extension_metadata', 'read-write');
                """);

            Execute(connection, transaction, "INSERT INTO gpkg_metadata (id, md_scope, md_standard_uri, mime_type, metadata) VALUES (1, 'dataset', $uri, 'application/json', $metadata)",
                ("$uri", GeoPackageMapFormat.MetadataStandardUri), ("$metadata", metadataJson));
            Execute(connection, transaction, "INSERT INTO gpkg_metadata_reference (reference_scope, table_name, column_name, row_id_value, timestamp, md_file_id, md_parent_id) VALUES ('geopackage', NULL, NULL, NULL, $date, 1, NULL)",
                ("$date", date));

            foreach (var (layer, features) in layers.OrderBy(pair => pair.Key))
            {
                WriteLayer(connection, transaction, GeoPackageMapFormat.GetTableName(layer), (metadata.Name ?? metadata.Region) + " - " + layer, metadata.Attribution ?? "", date, features);
            }

            transaction.Commit();
        }
    }

    private static void WriteLayer(SqliteConnection connection, SqliteTransaction transaction, string table, string identifier, string description, string date, GeoFeatureCollection features)
    {
        var columns = GetColumns(features);
        var geometries = features.Select(feature => feature.Geometry is { } geometry && geometry.GetBounds() is { } bounds ? (Geometry: geometry, Bounds: bounds) : ((GeoGeometry Geometry, GeoBounds Bounds)?)null).ToList();
        var types = geometries.OfType<(GeoGeometry Geometry, GeoBounds Bounds)>().Select(item => item.Geometry.Type).Distinct().ToList();
        var geometryType = types.Count == 1 ? GetTypeName(types[0]) : "GEOMETRY";
        var hasZ = features.Any(feature => feature.Geometry?.GetPositions().Any(position => position.Altitude is not null) is true);
        var layerBounds = features.GetBounds();

        var definitions = string.Concat(columns.Select(column => ", " + Quote(column.Name) + " " + GetSqlType(column.Type)));
        Execute(connection, transaction, $"CREATE TABLE {Quote(table)} (fid INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, geom {geometryType}{definitions})");
        Execute(connection, transaction, "INSERT INTO gpkg_contents (table_name, data_type, identifier, description, last_change, min_x, min_y, max_x, max_y, srs_id) VALUES ($table, 'features', $identifier, $description, $date, $minx, $miny, $maxx, $maxy, $srs)",
            ("$table", table), ("$identifier", identifier), ("$description", description), ("$date", date),
            ("$minx", layerBounds?.West), ("$miny", layerBounds?.South), ("$maxx", layerBounds?.East), ("$maxy", layerBounds?.North), ("$srs", Wgs84));
        Execute(connection, transaction, "INSERT INTO gpkg_geometry_columns (table_name, column_name, geometry_type_name, srs_id, z, m) VALUES ($table, 'geom', $type, $srs, $z, 0)",
            ("$table", table), ("$type", geometryType), ("$srs", Wgs84), ("$z", hasZ ? 2 : 0));

        var rtree = "rtree_" + table + "_geom";
        Execute(connection, transaction, $"CREATE VIRTUAL TABLE {Quote(rtree)} USING rtree(id, minx, maxx, miny, maxy)");
        Execute(connection, transaction, "INSERT INTO gpkg_extensions (table_name, column_name, extension_name, definition, scope) VALUES ($table, 'geom', 'gpkg_rtree_index', 'http://www.geopackage.org/spec120/#extension_rtree', 'write-only')",
            ("$table", table));

        using (var insert = connection.CreateCommand())
        using (var index = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {Quote(table)} (fid, geom{string.Concat(columns.Select(column => ", " + Quote(column.Name)))}) VALUES ($fid, $geom{string.Concat(columns.Select((_, i) => ", $p" + i.ToString(CultureInfo.InvariantCulture)))})";
            var fid = insert.Parameters.Add("$fid", SqliteType.Integer);
            var geom = insert.Parameters.Add("$geom", SqliteType.Blob);
            var values = columns.Select((_, i) => insert.Parameters.Add("$p" + i.ToString(CultureInfo.InvariantCulture), SqliteType.Text)).ToArray();

            index.Transaction = transaction;
            index.CommandText = $"INSERT INTO {Quote(rtree)} (id, minx, maxx, miny, maxy) VALUES ($id, $minx, $maxx, $miny, $maxy)";
            var id = index.Parameters.Add("$id", SqliteType.Integer);
            var minX = index.Parameters.Add("$minx", SqliteType.Real);
            var maxX = index.Parameters.Add("$maxx", SqliteType.Real);
            var minY = index.Parameters.Add("$miny", SqliteType.Real);
            var maxY = index.Parameters.Add("$maxy", SqliteType.Real);

            for (var i = 0; i < features.Count; i++)
            {
                var feature = features[i];
                fid.Value = i + 1L;
                geom.Value = geometries[i] is { } item ? GeoPackageGeometry.Write(item.Geometry, item.Bounds, Wgs84) : DBNull.Value;
                for (var c = 0; c < columns.Count; c++)
                {
                    var value = feature.Properties.GetValueOrDefault(columns[c].Name);
                    values[c].SqliteType = GetParameterType(columns[c].Type);
                    values[c].Value = value is null ? DBNull.Value : ToSqlValue(value, columns[c].Type);
                }

                insert.ExecuteNonQuery();
                if (geometries[i] is { } indexed)
                {
                    id.Value = i + 1L;
                    minX.Value = indexed.Bounds.West;
                    maxX.Value = indexed.Bounds.East;
                    minY.Value = indexed.Bounds.South;
                    maxY.Value = indexed.Bounds.North;
                    index.ExecuteNonQuery();
                }
            }
        }

        // The triggers of the extension keep the index up to date when another tool, such as QGIS, edits the table
        var t = Quote(table);
        var r = Quote(rtree);
        Execute(connection, transaction, $"""
            CREATE TRIGGER {Quote(rtree + "_insert")} AFTER INSERT ON {t} WHEN (new."geom" NOT NULL AND NOT ST_IsEmpty(NEW."geom")) BEGIN INSERT OR REPLACE INTO {r} VALUES (NEW."fid", ST_MinX(NEW."geom"), ST_MaxX(NEW."geom"), ST_MinY(NEW."geom"), ST_MaxY(NEW."geom")); END;
            CREATE TRIGGER {Quote(rtree + "_update6")} AFTER UPDATE OF "geom" ON {t} WHEN OLD."fid" = NEW."fid" AND (NEW."geom" NOTNULL AND NOT ST_IsEmpty(NEW."geom")) AND (OLD."geom" NOTNULL AND NOT ST_IsEmpty(OLD."geom")) BEGIN UPDATE {r} SET minx = ST_MinX(NEW."geom"), maxx = ST_MaxX(NEW."geom"), miny = ST_MinY(NEW."geom"), maxy = ST_MaxY(NEW."geom") WHERE id = NEW."fid"; END;
            CREATE TRIGGER {Quote(rtree + "_update7")} AFTER UPDATE OF "geom" ON {t} WHEN OLD."fid" = NEW."fid" AND (NEW."geom" NOTNULL AND NOT ST_IsEmpty(NEW."geom")) AND (OLD."geom" ISNULL OR ST_IsEmpty(OLD."geom")) BEGIN INSERT INTO {r} VALUES (NEW."fid", ST_MinX(NEW."geom"), ST_MaxX(NEW."geom"), ST_MinY(NEW."geom"), ST_MaxY(NEW."geom")); END;
            CREATE TRIGGER {Quote(rtree + "_update2")} AFTER UPDATE OF "geom" ON {t} WHEN OLD."fid" = NEW."fid" AND (NEW."geom" ISNULL OR ST_IsEmpty(NEW."geom")) BEGIN DELETE FROM {r} WHERE id = OLD."fid"; END;
            CREATE TRIGGER {Quote(rtree + "_update5")} AFTER UPDATE ON {t} WHEN OLD."fid" != NEW."fid" AND (NEW."geom" NOTNULL AND NOT ST_IsEmpty(NEW."geom")) BEGIN DELETE FROM {r} WHERE id = OLD."fid"; INSERT OR REPLACE INTO {r} VALUES (NEW."fid", ST_MinX(NEW."geom"), ST_MaxX(NEW."geom"), ST_MinY(NEW."geom"), ST_MaxY(NEW."geom")); END;
            CREATE TRIGGER {Quote(rtree + "_update4")} AFTER UPDATE ON {t} WHEN OLD."fid" != NEW."fid" AND (NEW."geom" ISNULL OR ST_IsEmpty(NEW."geom")) BEGIN DELETE FROM {r} WHERE id IN (OLD."fid", NEW."fid"); END;
            CREATE TRIGGER {Quote(rtree + "_delete")} AFTER DELETE ON {t} WHEN old."geom" NOT NULL BEGIN DELETE FROM {r} WHERE id = OLD."fid"; END;
            """);
    }

    private static List<(string Name, ColumnType Type)> GetColumns(GeoFeatureCollection features)
    {
        var columns = new List<(string Name, ColumnType Type)>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            foreach (var (name, value) in feature.Properties)
            {
                if (value is null)
                    continue;

                var type = GetColumnType(value);
                if (indexes.TryGetValue(name, out var index))
                {
                    columns[index] = (name, Merge(columns[index].Type, type));
                    continue;
                }

                // SQLite compares the names of the columns without case
                if (name.Equals("fid", StringComparison.OrdinalIgnoreCase) || name.Equals("geom", StringComparison.OrdinalIgnoreCase) || columns.Exists(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"The property '{name}' cannot be written to a GeoPackage table: the names of the columns are compared without case, and 'fid' and 'geom' are the identifier and the geometry.", nameof(features));

                indexes[name] = columns.Count;
                columns.Add((name, type));
            }
        }

        return columns;
    }

    private static ColumnType GetColumnType(object value) => value switch
    {
        bool => ColumnType.Boolean,
        sbyte or byte or short or ushort or int => ColumnType.Int32,
        uint or long => ColumnType.Int64,
        ulong number => number <= long.MaxValue ? ColumnType.Int64 : ColumnType.Text,
        float or double or decimal => ColumnType.Double,
        DateTime or DateTimeOffset => ColumnType.DateTime,
        byte[] => ColumnType.Blob,
        _ => ColumnType.Text,
    };

    private static ColumnType Merge(ColumnType a, ColumnType b) => (a, b) switch
    {
        _ when a == b => a,
        (ColumnType.Int32, ColumnType.Int64) or (ColumnType.Int64, ColumnType.Int32) => ColumnType.Int64,
        (ColumnType.Int32 or ColumnType.Int64 or ColumnType.Double, ColumnType.Int32 or ColumnType.Int64 or ColumnType.Double) => ColumnType.Double,
        _ => ColumnType.Text,
    };

    private static string GetSqlType(ColumnType type) => type switch
    {
        ColumnType.Boolean => "BOOLEAN",
        ColumnType.Int32 => "MEDIUMINT",
        ColumnType.Int64 => "INTEGER",
        ColumnType.Double => "DOUBLE",
        ColumnType.DateTime => "DATETIME",
        ColumnType.Blob => "BLOB",
        _ => "TEXT",
    };

    private static SqliteType GetParameterType(ColumnType type) => type switch
    {
        ColumnType.Boolean or ColumnType.Int32 or ColumnType.Int64 => SqliteType.Integer,
        ColumnType.Double => SqliteType.Real,
        ColumnType.Blob => SqliteType.Blob,
        _ => SqliteType.Text,
    };

    private static object ToSqlValue(object value, ColumnType type) => type switch
    {
        ColumnType.Boolean => (bool)value ? 1L : 0L,
        ColumnType.Int32 or ColumnType.Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        ColumnType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        ColumnType.DateTime => value switch
        {
            DateTimeOffset date => GeoPackageMapFormat.FormatDateTime(date),
            DateTime { Kind: DateTimeKind.Local } date => GeoPackageMapFormat.FormatDateTime(new DateTimeOffset(date)),
            DateTime date => GeoPackageMapFormat.FormatDateTime(new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))),
            _ => throw new InvalidOperationException("Unexpected date type."),
        },
        ColumnType.Blob => value,
        _ => value switch
        {
            string text => text,
            bool boolean => boolean ? "true" : "false",
            JsonElement element => element.ValueKind is JsonValueKind.String ? element.GetString()! : element.GetRawText(),
            DateTimeOffset date => GeoPackageMapFormat.FormatDateTime(date),
            DateTime date => GeoPackageMapFormat.FormatDateTime(new DateTimeOffset(date.Kind is DateTimeKind.Local ? date : DateTime.SpecifyKind(date, DateTimeKind.Utc))),
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        },
    };

    private static string GetTypeName(GeoGeometryType type) => type switch
    {
        GeoGeometryType.Point => "POINT",
        GeoGeometryType.LineString => "LINESTRING",
        GeoGeometryType.Polygon => "POLYGON",
        GeoGeometryType.MultiPoint => "MULTIPOINT",
        GeoGeometryType.MultiLineString => "MULTILINESTRING",
        GeoGeometryType.MultiPolygon => "MULTIPOLYGON",
        _ => "GEOMETRYCOLLECTION",
    };

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private enum ColumnType
    {
        Boolean,
        Int32,
        Int64,
        Double,
        Text,
        DateTime,
        Blob,
    }
}
