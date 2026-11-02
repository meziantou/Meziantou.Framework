using System.Buffers;
using System.Collections.Concurrent;
using System.Text;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// The character encoding of the codes of a predefined CMap (ISO 32000-2, 9.7.5.2, Table 116): Unicode for the
/// <c>Uni*</c> CMaps, a legacy code page (Shift-JIS, EUC, GBK, GB 18030, Big Five, UHC, Johab) for the others.
/// </summary>
internal sealed class CMapCharset
{
    private static readonly ConcurrentDictionary<int, Encoding?> Encodings = new();

    private readonly CMapCharsetKind _kind;
    private readonly int _codePage;

    private CMapCharset(CMapCharsetKind kind, int codePage = 0)
    {
        _kind = kind;
        _codePage = codePage;
    }

    public static CMapCharset Utf16 { get; } = new(CMapCharsetKind.Utf16);

    public static CMapCharset Utf32 { get; } = new(CMapCharsetKind.Utf32);

    public static CMapCharset Utf8 { get; } = new(CMapCharsetKind.Utf8);

    /// <summary>Creates the encoding of a code page; <paramref name="rowCell"/> for the CMaps whose two-byte codes are the row and cell of the character set (<c>H</c>, <c>GB-H</c>, <c>KSC-H</c>), its EUC code minus 0x8080.</summary>
    public static CMapCharset CodePage(int codePage, bool rowCell = false) => new(rowCell ? CMapCharsetKind.RowCell : CMapCharsetKind.CodePage, codePage);

    /// <summary>Gets the text of a code, or <see langword="null"/> when the code has no character in the encoding.</summary>
    public string? Decode(uint code, int length)
    {
        Span<byte> bytes = stackalloc byte[4];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(code >> (8 * (length - 1 - i)));
        }

        bytes = bytes[..length];
        switch (_kind)
        {
            case CMapCharsetKind.Utf16:
                {
                    if (length == 2)
                    {
                        var c = (char)code;
                        return char.IsSurrogate(c) ? null : c.ToString();
                    }

                    if (length == 4 && char.IsSurrogatePair((char)(code >> 16), (char)code))
                        return string.Create(2, code, static (span, value) => (span[0], span[1]) = ((char)(value >> 16), (char)value));

                    return null;
                }

            case CMapCharsetKind.Utf32:
                return Rune.IsValid(code) ? new Rune(code).ToString() : null;
            case CMapCharsetKind.Utf8:
                return Rune.DecodeFromUtf8(bytes, out var rune, out var consumed) == OperationStatus.Done && consumed == length ? rune.ToString() : null;
            default:
                {
                    if (GetEncoding(_codePage) is not { } encoding)
                        return null;

                    if (_kind == CMapCharsetKind.RowCell)
                    {
                        if (length != 2)
                            return null;

                        bytes[0] |= 0x80;
                        bytes[1] |= 0x80;
                    }

                    var text = encoding.GetString(bytes);
                    return text.Length == 0 || text.Contains('�', StringComparison.Ordinal) ? null : text;
                }
        }
    }

    /// <summary>Gets the encoding of a code page of the base class library: the tables are managed, they do not depend on ICU.</summary>
    private static Encoding? GetEncoding(int codePage)
    {
        return Encodings.GetOrAdd(codePage, static codePage => CodePagesEncodingProvider.Instance.GetEncoding(codePage, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("�")));
    }

    private enum CMapCharsetKind
    {
        Utf16,
        Utf32,
        Utf8,
        CodePage,
        RowCell,
    }
}
