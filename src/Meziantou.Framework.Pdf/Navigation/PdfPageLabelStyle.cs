namespace Meziantou.Framework.Pdf;

/// <summary>The numbering style of a page label range (ISO 32000-2, 12.4.2, table 161).</summary>
public enum PdfPageLabelStyle
{
    /// <summary>The labels have no number: they are the prefix alone.</summary>
    None,

    /// <summary>Decimal Arabic numerals: 1, 2, 3 (<c>/D</c>).</summary>
    Decimal,

    /// <summary>Uppercase Roman numerals: I, II, III (<c>/R</c>).</summary>
    UppercaseRoman,

    /// <summary>Lowercase Roman numerals: i, ii, iii (<c>/r</c>).</summary>
    LowercaseRoman,

    /// <summary>Uppercase letters: A to Z, then AA to ZZ... (<c>/A</c>).</summary>
    UppercaseLetters,

    /// <summary>Lowercase letters: a to z, then aa to zz... (<c>/a</c>).</summary>
    LowercaseLetters,
}
