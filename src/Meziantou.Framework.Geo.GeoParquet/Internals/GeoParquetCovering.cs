namespace Meziantou.Framework.Geo.Internals;

/// <summary>The columns of the bounding box of each geometry (GeoParquet 1.1), whose statistics give the extent of each row group.</summary>
internal sealed record GeoParquetCovering(string Column, string[] XMin, string[] YMin, string[] XMax, string[] YMax);
