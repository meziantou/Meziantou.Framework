namespace Meziantou.Framework.Geo.Maps;

/// <summary>A source of the data of a map, with its license.</summary>
/// <param name="Name">The name of the source, such as <c>Natural Earth</c>.</param>
/// <param name="Url">The address of the source, or <see langword="null"/>.</param>
/// <param name="License">The license of the data, such as <c>ODbL-1.0</c>, or <see langword="null"/>.</param>
public sealed record MapSource(string Name, Uri? Url = null, string? License = null);
