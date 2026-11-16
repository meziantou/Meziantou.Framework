namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>A part of the compressed data of an entry, in a volume.</summary>
internal sealed record RarDataPart(int Volume, long Offset, long Length);
