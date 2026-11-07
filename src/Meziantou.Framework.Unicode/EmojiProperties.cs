namespace Meziantou.Framework;

/// <summary>Represents the emoji properties serialized for a Unicode character.</summary>
/// <remarks>
/// These values are persisted as a raw byte in the Unicode data resource, so the flag values
/// are part of that format. This file is compiled into the generator as well, so the writer
/// and the reader cannot disagree. Do not reorder or renumber the members.
/// </remarks>
[Flags]
internal enum EmojiProperties : byte
{
    None = 0,
    Emoji = 1 << 0,
    EmojiPresentation = 1 << 1,
    EmojiModifier = 1 << 2,
    EmojiModifierBase = 1 << 3,
    EmojiComponent = 1 << 4,
    ExtendedPictographic = 1 << 5,
}
