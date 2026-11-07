namespace Meziantou.Framework;

/// <summary>Specifies how strict the line breaking rules are.</summary>
/// <seealso href="https://www.unicode.org/reports/tr14/" />
public enum UnicodeLineBreakStrictness
{
    /// <summary>The default rules of the Unicode Line Breaking Algorithm, without tailoring.</summary>
    Strict = 0,

    /// <summary>The default rules, except that a line can start with a small kana or a prolonged sound mark (the class Conditional_Japanese_Starter behaves as an ideograph).</summary>
    Normal = 1,

    /// <summary>A line can break between any two grapheme clusters. The mandatory breaks are unchanged.</summary>
    Anywhere = 2,
}
