using System.Runtime.InteropServices;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Content;

/// <summary>
/// An operand that <see cref="ContentStreamReader"/> read: a token of the content (a number, a name, a string, an array
/// of such tokens, or a keyword in an array), or an object (a dictionary, another array, <c>true</c>, <c>false</c>,
/// <c>null</c>, or the dictionary and data of an inline image). A token becomes an object only when it is asked for
/// (<see cref="ContentStreamReader.GetObject(in ContentOperand)"/>), so reading the operands of most operators allocates nothing.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct ContentOperand
{
    private ContentOperand(PdfToken token, PdfObject? value, int itemStart, int itemCount)
    {
        Token = token;
        Object = value;
        ItemStart = itemStart;
        ItemCount = itemCount;
    }

    /// <summary>Gets the token of the operand, or a token of kind <see cref="PdfTokenKind.EndOfInput"/> when the operand is an <see cref="Object"/>.</summary>
    public PdfToken Token { get; }

    /// <summary>Gets the object of the operand, or <see langword="null"/> when the operand is a <see cref="Token"/>.</summary>
    public PdfObject? Object { get; }

    /// <summary>Gets the index of the first item of an array token in the items of the reader.</summary>
    internal int ItemStart { get; }

    /// <summary>Gets the number of items of an array token.</summary>
    internal int ItemCount { get; }

    /// <summary>Gets a value indicating whether the operand is an array of tokens, whose items the reader keeps.</summary>
    internal bool IsTokenArray => Object is null && Token.Kind is PdfTokenKind.ArrayStart;

    public static ContentOperand FromToken(PdfToken token) => new(token, value: null, 0, 0);

    public static ContentOperand FromObject(PdfObject value) => new(default, value, 0, 0);

    public static ContentOperand FromTokenArray(int start, int end, int itemStart, int itemCount) => new(new PdfToken(PdfTokenKind.ArrayStart, start, end), value: null, itemStart, itemCount);

    /// <summary>Gets the value of a number, as <see cref="PdfNumber.DoubleValue"/> gives it.</summary>
    /// <returns><see langword="false"/> when the operand is not a number.</returns>
    public bool TryGetNumber(out double value)
    {
        switch (Token.Kind)
        {
            case PdfTokenKind.Integer:
                value = Token.IntegerValue;
                return true;
            case PdfTokenKind.Real:
                value = Token.RealValue;
                return true;
        }

        if (Object is PdfNumber number)
        {
            value = number.DoubleValue;
            return true;
        }

        value = 0;
        return false;
    }
}
