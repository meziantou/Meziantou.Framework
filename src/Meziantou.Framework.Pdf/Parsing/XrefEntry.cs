using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>An entry of a cross-reference section (ISO 32000-2, 7.5.4 and 7.5.8.3).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct XrefEntry(XrefEntryType Type, long Offset, int Generation, int ContainerNumber, int Index)
{
    public static XrefEntry Free { get; } = new(XrefEntryType.Free, 0, 0, 0, 0);

    public static XrefEntry InFile(long offset, int generation) => new(XrefEntryType.InFile, offset, generation, 0, 0);

    public static XrefEntry InObjectStream(int containerNumber, int index) => new(XrefEntryType.InObjectStream, -1, 0, containerNumber, index);
}
