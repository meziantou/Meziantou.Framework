namespace Meziantou.Framework.Mail.Internals;

/// <summary>A named property and the type the library reads and writes it with.</summary>
internal sealed record NamedPropertyDefinition(PstNamedProperty Name, PstPropertyType Type);
