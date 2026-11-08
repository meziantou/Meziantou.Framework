namespace Meziantou.Framework.Geo.Internals;

/// <summary>The optional values of the positions of FlatGeobuf data: Z (the altitudes) and M (the measures).</summary>
internal readonly record struct FlatGeobufDimensions(bool HasZ, bool HasM);
