using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// The bidi control codes that represent the CSS <c>unicode-bidi</c> and <c>direction</c> properties in the text of a bidi paragraph
/// (https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection, clause HL3 of UAX #9), resolved by <see cref="BidiResolver"/>.
/// </summary>
/// <remarks>
/// The inline layout inserts the start codes of an inline box before its content and the end codes after it. When an inline box is
/// split by a bidi paragraph boundary (a forced paragraph break, Bidi_Class B), the end codes of the open boxes are inserted before the
/// break and their start codes after it (https://drafts.csswg.org/css-writing-modes-4/#bidi-paragraph-breaks), since UAX #9
/// terminates every embedding at the end of a paragraph (X8).
/// </remarks>
internal static class BidiControls
{
    public const char LeftToRightEmbedding = (char)0x202A;
    public const char RightToLeftEmbedding = (char)0x202B;
    public const char PopDirectionalFormatting = (char)0x202C;
    public const char LeftToRightOverride = (char)0x202D;
    public const char RightToLeftOverride = (char)0x202E;
    public const char LeftToRightIsolate = (char)0x2066;
    public const char RightToLeftIsolate = (char)0x2067;
    public const char FirstStrongIsolate = (char)0x2068;
    public const char PopDirectionalIsolate = (char)0x2069;
    public const char LeftToRightMark = (char)0x200E;
    public const char RightToLeftMark = (char)0x200F;

    /// <summary>U+FFFC, which represents an atomic inline in the text of an inline formatting context (Bidi_Class ON).</summary>
    public const char ObjectReplacementCharacter = (char)0xFFFC;

    private static readonly string Lre = new(LeftToRightEmbedding, 1);
    private static readonly string Rle = new(RightToLeftEmbedding, 1);
    private static readonly string Pdf = new(PopDirectionalFormatting, 1);
    private static readonly string Lro = new(LeftToRightOverride, 1);
    private static readonly string Rlo = new(RightToLeftOverride, 1);
    private static readonly string Lri = new(LeftToRightIsolate, 1);
    private static readonly string Rli = new(RightToLeftIsolate, 1);
    private static readonly string Fsi = new(FirstStrongIsolate, 1);
    private static readonly string Pdi = new(PopDirectionalIsolate, 1);
    private static readonly string FsiLro = new([FirstStrongIsolate, LeftToRightOverride]);
    private static readonly string FsiRlo = new([FirstStrongIsolate, RightToLeftOverride]);
    private static readonly string PdfPdi = new([PopDirectionalFormatting, PopDirectionalIsolate]);

    /// <summary>The control codes inserted at the start of an inline box (https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection-table).</summary>
    public static string GetInlineStart(UnicodeBidi unicodeBidi, bool isRightToLeft) => unicodeBidi switch
    {
        UnicodeBidi.Embed => isRightToLeft ? Rle : Lre,
        UnicodeBidi.Isolate => isRightToLeft ? Rli : Lri,
        UnicodeBidi.BidiOverride => isRightToLeft ? Rlo : Lro,
        UnicodeBidi.IsolateOverride => isRightToLeft ? FsiRlo : FsiLro,
        UnicodeBidi.Plaintext => Fsi,
        _ => "",
    };

    /// <summary>The control codes inserted at the end of an inline box.</summary>
    public static string GetInlineEnd(UnicodeBidi unicodeBidi) => unicodeBidi switch
    {
        UnicodeBidi.Embed or UnicodeBidi.BidiOverride => Pdf,
        UnicodeBidi.Isolate or UnicodeBidi.Plaintext => Pdi,
        UnicodeBidi.IsolateOverride => PdfPdi,
        _ => "",
    };

    /// <summary>
    /// The control codes inserted at the start of the root inline box of a block container: the override of <c>bidi-override</c> and
    /// <c>isolate-override</c> (the other values have no effect on the content of a block container, the paragraph level comes from
    /// <see cref="GetParagraphDirection"/>).
    /// </summary>
    public static string GetBlockContainerStart(UnicodeBidi unicodeBidi, bool isRightToLeft) => unicodeBidi is UnicodeBidi.BidiOverride or UnicodeBidi.IsolateOverride
        ? (isRightToLeft ? Rlo : Lro)
        : "";

    /// <summary>The control codes inserted at the end of the root inline box of a block container.</summary>
    public static string GetBlockContainerEnd(UnicodeBidi unicodeBidi) => unicodeBidi is UnicodeBidi.BidiOverride or UnicodeBidi.IsolateOverride ? Pdf : "";

    /// <summary>
    /// The paragraph embedding level of the bidi paragraphs of a block container (https://drafts.csswg.org/css-writing-modes-4/#bidi-embedding-level):
    /// its <c>direction</c> (clause HL1), or rules P2 and P3 with <c>unicode-bidi: plaintext</c> ("rather than by using the
    /// direction property of the box": a paragraph without strong character is left-to-right, rule P3).
    /// </summary>
    public static BidiParagraphDirection GetParagraphDirection(UnicodeBidi unicodeBidi, bool isRightToLeft)
    {
        if (unicodeBidi == UnicodeBidi.Plaintext)
            return BidiParagraphDirection.AutoLeftToRight;

        return isRightToLeft ? BidiParagraphDirection.RightToLeft : BidiParagraphDirection.LeftToRight;
    }

    /// <summary>
    /// The Bidi_Class of an atomic inline (https://drafts.csswg.org/css-writing-modes-4/#bidi-atomic-inline): a neutral character,
    /// except a replaced element with <c>unicode-bidi: embed | bidi-override</c>, which is a strong character of its direction.
    /// </summary>
    public static BidiClass GetAtomicInlineClass(bool isReplaced, UnicodeBidi unicodeBidi, bool isRightToLeft)
    {
        if (isReplaced && unicodeBidi is UnicodeBidi.Embed or UnicodeBidi.BidiOverride)
            return isRightToLeft ? BidiClass.R : BidiClass.L;

        return BidiClass.ON;
    }
}
