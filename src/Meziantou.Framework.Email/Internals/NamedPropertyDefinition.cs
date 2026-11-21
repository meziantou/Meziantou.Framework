namespace Meziantou.Framework.Email.Internals;

/// <summary>A named property and the type the library reads and writes it with.</summary>
internal sealed record NamedPropertyDefinition(MapiNamedProperty Name, MapiPropertyType Type);
