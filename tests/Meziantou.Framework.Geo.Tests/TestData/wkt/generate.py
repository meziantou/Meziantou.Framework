from osgeo import ogr, gdal

gdal.UseExceptions()

inputs = [
    "POINT (1 2)",
    "POINT (2.3522 48.8566)",
    "POINT (-122.4194 37.7749)",
    "POINT Z (1 2 3)",
    "POINT M (1 2 4)",
    "POINT ZM (1 2 3 4)",
    "POINT EMPTY",
    "LINESTRING (0 0,1 1,2 0.5)",
    "LINESTRING Z (0 0 10,1 1 20.5,2 0.5 -3)",
    "LINESTRING M (0 0 1,1 1 2)",
    "LINESTRING EMPTY",
    "POLYGON ((0 0,4 0,4 4,0 4,0 0),(1 1,2 1,2 2,1 1))",
    "POLYGON Z ((0 0 1,4 0 1,4 4 1,0 0 1))",
    "POLYGON EMPTY",
    "MULTIPOINT ((1 2),(3 4))",
    "MULTIPOINT Z ((1 2 3),(4 5 6))",
    "MULTIPOINT EMPTY",
    "MULTILINESTRING ((0 0,1 1),(2 2,3 3,4 2))",
    "MULTILINESTRING M ((0 0 5,1 1 6))",
    "MULTILINESTRING EMPTY",
    "MULTIPOLYGON (((0 0,1 0,1 1,0 0)),((5 5,6 5,6 6,5 5),(5.2 5.1,5.8 5.1,5.8 5.7,5.2 5.1)))",
    "MULTIPOLYGON ZM (((0 0 1 2,1 0 1 2,1 1 1 2,0 0 1 2)))",
    "MULTIPOLYGON EMPTY",
    "GEOMETRYCOLLECTION (POINT (1 2),LINESTRING (0 0,1 1),POLYGON ((0 0,1 0,1 1,0 0)))",
    "GEOMETRYCOLLECTION Z (POINT Z (1 2 3),GEOMETRYCOLLECTION Z (LINESTRING Z (0 0 1,1 1 2)))",
    "GEOMETRYCOLLECTION (MULTIPOINT ((1 2)),MULTILINESTRING ((0 0,1 1)),MULTIPOLYGON (((0 0,1 0,1 1,0 0))))",
    "GEOMETRYCOLLECTION EMPTY",
]

print("# Written by GDAL " + gdal.__version__ + " (osgeo.ogr) with generate.py (one line per geometry, tab-separated): input WKT, ISO WKB little-endian, ISO WKB big-endian, OGR legacy WKB little-endian (Z flag 0x80000000), ISO WKT without M, ISO WKB little-endian without M")
for wkt in inputs:
    g = ogr.CreateGeometryFromWkt(wkt)
    ndr = g.ExportToIsoWkb(ogr.wkbNDR).hex().upper()
    xdr = g.ExportToIsoWkb(ogr.wkbXDR).hex().upper()
    old = g.ExportToWkb(ogr.wkbNDR).hex().upper() if not g.IsMeasured() and wkt != "POINT EMPTY" else "-"
    e = g.Clone()
    e.SetMeasured(False)
    if wkt == "POINT EMPTY":
        expected_wkt = "-"
        expected_wkb = "-"
    else:
        expected_wkt = e.ExportToIsoWkt()
        expected_wkb = e.ExportToIsoWkb(ogr.wkbNDR).hex().upper()
    print("\t".join([wkt, ndr, xdr, old, expected_wkt, expected_wkb]))
