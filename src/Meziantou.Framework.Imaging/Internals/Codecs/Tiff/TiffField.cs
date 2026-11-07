using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One entry of a TIFF image file directory: the tag, the field type, the value count and the raw value field.</summary>
/// <param name="Tag">The tag.</param>
/// <param name="Type">The field type; an unknown type is kept as is and never interpreted.</param>
/// <param name="Count">The number of values.</param>
/// <param name="RawValue">
/// The 4 (classic) or 8 (BigTIFF) bytes of the value field, in file byte order, packed little endian: writing this value
/// back with <see cref="BinaryPrimitives.WriteUInt64LittleEndian"/> restores the bytes as they are stored in the file.
/// </param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TiffField(ushort Tag, TiffFieldType Type, ulong Count, ulong RawValue);
