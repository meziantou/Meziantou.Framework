namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An entry of a <see cref="Typography.GlyphCache"/>: a <see cref="GlyphMask"/>, a <see cref="ColorGlyph"/>, or <see langword="null"/> when there is none.</summary>
internal sealed record GlyphCacheEntry(GlyphCacheKey Key, object? Value, long Bytes);
