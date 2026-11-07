namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The forms of the Han ideographs a text asks for, which choose the installed fonts its ideographs are looked for in.</summary>
internal enum HanForms : byte
{
    /// <summary>Simplified Chinese: the forms of a text without a language, or in another language.</summary>
    Simplified,

    /// <summary>Traditional Chinese (Taiwan, Macao).</summary>
    Traditional,

    /// <summary>Traditional Chinese of Hong Kong, and Cantonese.</summary>
    HongKong,

    /// <summary>Japanese.</summary>
    Japanese,

    /// <summary>Korean.</summary>
    Korean,
}
