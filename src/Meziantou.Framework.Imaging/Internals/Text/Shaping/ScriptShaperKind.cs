namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The shaping model of a script: what a text needs besides the features every script has.</summary>
internal enum ScriptShaperKind
{
    /// <summary>No analysis of the text: the features of the font do the work.</summary>
    Default,

    /// <summary>Letters that join: Arabic and Syriac.</summary>
    Arabic,

    /// <summary>Hebrew: the order of its points, and the precomposed letters of fonts that cannot position them.</summary>
    Hebrew,

    /// <summary>Hangul: syllable blocks composed from letters, or drawn with them.</summary>
    Hangul,

    /// <summary>Thai and Lao: the vowel written with a ring above the consonant and a letter after it.</summary>
    Thai,

    /// <summary>The Indic scripts: syllables whose characters are reordered around a base consonant.</summary>
    Indic,

    /// <summary>Khmer: subscript letters written with a coeng, and vowels drawn before the syllable.</summary>
    Khmer,

    /// <summary>Myanmar: a kinzi, stacked and medial consonants, and vowels drawn before the syllable.</summary>
    Myanmar,
}
