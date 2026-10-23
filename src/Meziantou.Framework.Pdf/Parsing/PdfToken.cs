using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>A token of the PDF syntax: its kind, where it is, and the value of a number.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PdfToken(PdfTokenKind Kind, int Start, int End, long IntegerValue = 0, double RealValue = 0)
{
    public int Length => End - Start;
}
