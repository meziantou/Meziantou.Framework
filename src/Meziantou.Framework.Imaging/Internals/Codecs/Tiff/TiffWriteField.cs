using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One directory entry <see cref="TiffDocumentWriter"/> is about to write: its values are already serialized in the byte order of the file.</summary>
/// <param name="Tag">The tag.</param>
/// <param name="Type">The field type.</param>
/// <param name="Count">The number of values.</param>
/// <param name="Data">The serialized values; shorter than the value field of an entry means the values are stored inline.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TiffWriteField(ushort Tag, TiffFieldType Type, ulong Count, byte[] Data);
