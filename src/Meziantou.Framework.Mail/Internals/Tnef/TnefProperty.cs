namespace Meziantou.Framework.Mail.Internals.Tnef;

/// <summary>A property read from a TNEF stream, in the format of the values of an Outlook data file. A named property has its name, and an identifier of 0x8000 or more that the reader assigns.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct TnefProperty(PstPropertyTag Tag, PstNamedProperty? Name, byte[] Value);
