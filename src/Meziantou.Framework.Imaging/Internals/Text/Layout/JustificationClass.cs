namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What a cluster is for justification between characters: whether room can be added next to it.</summary>
internal enum JustificationClass : byte
{
    /// <summary>Not classified: the line is only justified at its spaces.</summary>
    None,

    /// <summary>A tabulation, a placeholder, a text set in the room of one upright character, or hanging white space: no room is added next to it.</summary>
    Barrier,

    /// <summary>A character without glyph (a control, a zero width space, a joiner): it is skipped, and its neighbors are next to each other.</summary>
    Ignored,

    /// <summary>A mark, or another character that extends a grapheme cluster: no room is added between it and its base.</summary>
    Mark,

    /// <summary>A space that is widened: U+0020, U+00A0, U+3000.</summary>
    Space,

    /// <summary>Other white space: it is not widened, and is not a letter of a script.</summary>
    WhiteSpace,

    /// <summary>A character of a block script: Han, kana, Bopomofo, Hangul, Yi, or a wide or fullwidth character.</summary>
    Block,

    /// <summary>A character of a clustered script: Thai, Lao, Khmer, Myanmar, Tai Le, New Tai Lue, Tai Tham, Tai Viet.</summary>
    Clustered,

    /// <summary>A character of a cursive script, whose letters join: Arabic, Syriac, Mongolian, N'Ko, Phags-pa, Mandaic, Hanifi Rohingya.</summary>
    Cursive,

    /// <summary>Any other character.</summary>
    Other,
}
