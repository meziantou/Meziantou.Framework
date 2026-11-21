namespace Meziantou.Framework.Mail;

/// <summary>A property the records of an offline address book can have: its tag and its flags.</summary>
/// <param name="Tag">The tag of the property, such as <c>0x3001001F</c> for the display name.</param>
/// <param name="Flags">How the server uses the property online.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct OabPropertyDefinition(PstPropertyTag Tag, OabPropertyFlags Flags = OabPropertyFlags.None);
