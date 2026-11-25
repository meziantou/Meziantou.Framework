using System.Runtime.InteropServices;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Content;

/// <summary>The items of an array operand of a content stream: tokens of the content, or the items of a parsed array.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct ContentArray
{
    private readonly PdfToken[]? _tokens;
    private readonly int _start;
    private readonly PdfArray? _array;

    public ContentArray(PdfToken[] tokens, int start, int count)
    {
        _tokens = tokens;
        _start = start;
        Count = count;
    }

    public ContentArray(PdfArray array)
    {
        _array = array;
        Count = array.Count;
    }

    public int Count { get; }

    /// <summary>Gets an item. The items of a parsed array are not resolved, as a content stream has no indirect reference.</summary>
    public ContentOperand this[int index] => _array is not null ? ContentOperand.FromObject(_array[index]) : ContentOperand.FromToken(_tokens![_start + index]);
}
