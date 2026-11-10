using System.Runtime.InteropServices;

namespace Meziantou.Framework.MediaTags.Formats.Asf;

/// <summary>An attribute of an ASF file, from any of the objects that store them.</summary>
/// <param name="Name">The name.</param>
/// <param name="Type">The data type.</param>
/// <param name="Value">The value. A boolean is a 32-bit value, whichever object it comes from.</param>
/// <param name="Stream">The stream the attribute describes, or 0 for the whole file.</param>
/// <param name="Language">The index of the language in the language list, or 0.</param>
/// <param name="Record">The record as stored, to copy it unchanged.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AsfAttribute(string Name, AsfDataType Type, ReadOnlyMemory<byte> Value, int Stream, int Language, ReadOnlyMemory<byte> Record)
{
    /// <summary>Gets whether the attribute applies to the whole file, rather than to one stream or one language.</summary>
    public bool IsFileLevel => Stream == 0 && Language == 0;
}
