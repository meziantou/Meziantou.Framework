# Writes the Parquet files of this folder with pyarrow, the reference implementation the reader is compared with, and
# the values pyarrow reads from them in expected.json. Run it from this folder:
#   docker run --rm -v "$PWD:/data" -w /data python:3.12-slim sh -c "pip install -q pyarrow==21.0.0 shapely==2.1.1 && python generate.py"
import datetime
import decimal
import json

import pyarrow as pa
import pyarrow.parquet as pq
import shapely
from shapely import wkt

GEO = {
    "version": "1.1.0",
    "primary_column": "geometry",
    "columns": {
        "geometry": {
            "encoding": "WKB",
            "geometry_types": [],
            "covering": {"bbox": {"xmin": ["bbox", "xmin"], "ymin": ["bbox", "ymin"], "xmax": ["bbox", "xmax"], "ymax": ["bbox", "ymax"]}},
        }
    },
}

ROWS = 300


def geometry(i):
    match i % 7:
        case 0:
            return None
        case 1:
            return shapely.Point(i * 0.5, -i * 0.25)
        case 2:
            return shapely.Point(i, i / 4, i * 10)
        case 3:
            return shapely.LineString([(0, 0), (i, 1), (2, i)])
        case 4:
            return shapely.Polygon([(0, 0), (i, 0), (i, i), (0, 0)], [[(1, 1), (2, 1), (2, 1.5), (1, 1)]])
        case 5:
            return shapely.MultiPoint([(i, 1), (2, i)])
        case _:
            return shapely.MultiPolygon([shapely.Polygon([(i, i), (i + 1, i), (i + 1, i + 1), (i, i)]), shapely.Polygon([(-i, 0), (-i - 1, 0), (-i, 1), (-i, 0)])])


def nullable(i, value):
    return None if i % 5 == 3 else value


def table():
    geometries = [geometry(i) for i in range(ROWS)]
    bbox = [None if g is None else dict(zip(["xmin", "ymin", "xmax", "ymax"], g.bounds)) for g in geometries]
    columns = {
        "id": pa.array(range(ROWS), pa.int32()),
        "geometry": pa.array([None if g is None else shapely.to_wkb(g, flavor="iso") for g in geometries], pa.binary()),
        "bbox": pa.array(bbox, pa.struct([(n, pa.float64()) for n in ["xmin", "ymin", "xmax", "ymax"]])),
        "i8": pa.array([nullable(i, (i % 200) - 100) for i in range(ROWS)], pa.int8()),
        "i16": pa.array([nullable(i, i * 101 - 15000) for i in range(ROWS)], pa.int16()),
        "i64": pa.array([nullable(i, i * 123456789012 - 5) for i in range(ROWS)], pa.int64()),
        "u8": pa.array([nullable(i, i % 256) for i in range(ROWS)], pa.uint8()),
        "u16": pa.array([nullable(i, i * 200) for i in range(ROWS)], pa.uint16()),
        "u32": pa.array([nullable(i, 4_000_000_000 - i) for i in range(ROWS)], pa.uint32()),
        "u64": pa.array([nullable(i, 18_000_000_000_000_000_000 - i) for i in range(ROWS)], pa.uint64()),
        "f32": pa.array([nullable(i, i * 0.5 - 7) for i in range(ROWS)], pa.float32()),
        "f64": pa.array([nullable(i, i * 1.25e-3 - 1e10) for i in range(ROWS)], pa.float64()),
        "flag": pa.array([nullable(i, i % 3 == 0) for i in range(ROWS)], pa.bool_()),
        "name": pa.array([nullable(i, f"name {i % 17} é日") for i in range(ROWS)], pa.string()),
        "blob": pa.array([nullable(i, bytes([i % 256, 0, 255, i // 256])) for i in range(ROWS)], pa.binary()),
        "day": pa.array([nullable(i, datetime.date(2000, 1, 1) + datetime.timedelta(days=i * 37)) for i in range(ROWS)], pa.date32()),
        "ts_ms": pa.array([nullable(i, datetime.datetime(2020, 1, 2, 3, 4, 5, 6000) + datetime.timedelta(minutes=i * 13)) for i in range(ROWS)], pa.timestamp("ms")),
        "ts_us_utc": pa.array([nullable(i, datetime.datetime(1969, 12, 31, 23, 59, 58, 123456, tzinfo=datetime.timezone.utc) + datetime.timedelta(hours=i)) for i in range(ROWS)], pa.timestamp("us", tz="UTC")),
        "time_ms": pa.array([nullable(i, datetime.time(i % 24, i % 60, 7, 250000)) for i in range(ROWS)], pa.time32("ms")),
        "dec": pa.array([nullable(i, decimal.Decimal(i * 1234 - 99999) / 100) for i in range(ROWS)], pa.decimal128(9, 2)),
        "dec_wide": pa.array([nullable(i, decimal.Decimal(i * 1234567890123 - 9) / 1000) for i in range(ROWS)], pa.decimal128(25, 3)),
        "fixed": pa.array([nullable(i, bytes([i % 256] * 3)) for i in range(ROWS)], pa.binary(3)),
        "tags": pa.array([[str(i), "x"] for i in range(ROWS)], pa.list_(pa.string())),
    }
    return pa.table(columns).replace_schema_metadata({"geo": json.dumps(GEO)})


def expected(t):
    rows = []
    for row in t.to_pylist():
        values = {}
        for name, value in row.items():
            if name in ("bbox", "tags"):
                continue
            if name == "geometry":
                values[name] = None if value is None else shapely.from_wkb(value).wkt
            elif value is None:
                values[name] = None
            elif isinstance(value, bool):
                values[name] = "true" if value else "false"
            elif isinstance(value, (bytes, bytearray)):
                values[name] = value.hex()
            elif isinstance(value, float):
                values[name] = repr(value)
            elif isinstance(value, datetime.datetime):
                values[name] = value.replace(tzinfo=None).isoformat(timespec="microseconds")
            elif isinstance(value, (datetime.date, datetime.time)):
                values[name] = value.isoformat()
            else:
                values[name] = str(value)
        rows.append(values)
    return rows


t = table()
variants = {
    "uncompressed-v1": dict(compression="NONE", data_page_version="1.0"),
    "snappy-v1": dict(compression="SNAPPY", data_page_version="1.0"),
    "snappy-v2": dict(compression="SNAPPY", data_page_version="2.0"),
    "gzip-v2": dict(compression="GZIP", data_page_version="2.0"),
    "brotli-v1": dict(compression="BROTLI", data_page_version="1.0"),
    "lz4raw-v2": dict(compression="LZ4", data_page_version="2.0"),
    "zstd-v2": dict(compression="ZSTD", data_page_version="2.0"),
    "plain-v1": dict(compression="NONE", use_dictionary=False, data_page_version="1.0"),
    "delta-v2": dict(
        compression="SNAPPY",
        use_dictionary=False,
        data_page_version="2.0",
        column_encoding={"id": "DELTA_BINARY_PACKED", "i64": "DELTA_BINARY_PACKED", "name": "DELTA_BYTE_ARRAY", "blob": "DELTA_LENGTH_BYTE_ARRAY", "geometry": "DELTA_LENGTH_BYTE_ARRAY", "flag": "RLE"},
    ),
    "split-v1": dict(compression="NONE", use_dictionary=False, data_page_version="1.0", column_encoding={"f32": "BYTE_STREAM_SPLIT", "f64": "BYTE_STREAM_SPLIT", "fixed": "BYTE_STREAM_SPLIT", "i64": "BYTE_STREAM_SPLIT"}),
    # Small pages and row groups: a column chunk of several pages, a file of several row groups
    "pages-v1": dict(compression="SNAPPY", data_page_size=256, row_group_size=70, write_page_index=True),
}
for name, options in variants.items():
    pq.write_table(t, f"{name}.parquet", write_statistics=True, **options)

with open("expected.json", "w", encoding="utf-8") as f:
    json.dump(expected(t), f, ensure_ascii=False, indent=0)

# Row groups along the longitudes, to check that a box skips the row groups outside of it
rows, groups = 500, 24
ids = list(range(rows * groups))
points = [shapely.Point(-180 + (i // rows) * 15 + (i % 50) * 0.1, (i % rows) // 50 * 0.5) for i in ids]
grid = pa.table({
    "id": pa.array(ids, pa.int32()),
    "geometry": pa.array([shapely.to_wkb(p, flavor="iso") for p in points], pa.binary()),
    "bbox": pa.array([dict(zip(["xmin", "ymin", "xmax", "ymax"], p.bounds)) for p in points], pa.struct([(n, pa.float64()) for n in ["xmin", "ymin", "xmax", "ymax"]])),
}).replace_schema_metadata({"geo": json.dumps(GEO)})
pq.write_table(grid, "rowgroups.parquet", compression="SNAPPY", row_group_size=rows, write_statistics=True)

# A Parquet file that is not a GeoParquet file: no "geo" metadata
pq.write_table(pa.table({"id": pa.array([1, 2], pa.int32())}), "no-geo.parquet")
