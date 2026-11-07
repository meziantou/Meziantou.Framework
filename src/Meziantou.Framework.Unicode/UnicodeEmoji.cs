using System.Collections.ObjectModel;

namespace Meziantou.Framework;

/// <summary>Provides emoji-related query methods for Unicode characters.</summary>
/// <remarks>
/// This class provides methods to check various emoji properties defined in Unicode Technical Standard #51.
/// For more information, see: https://www.unicode.org/reports/tr51/
/// </remarks>
public static class UnicodeEmoji
{
    /// <summary>Determines whether the specified rune has the Emoji property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmoji(Rune rune) => HasProperty(rune.Value, EmojiProperties.Emoji);

    /// <summary>Determines whether the specified code point has the Emoji property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmoji(int codePoint) => HasProperty(codePoint, EmojiProperties.Emoji);

    /// <summary>Determines whether the specified rune has the Emoji_Presentation property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji_Presentation property; otherwise, <see langword="false"/>.</returns>
    public static bool HasEmojiPresentation(Rune rune) => HasProperty(rune.Value, EmojiProperties.EmojiPresentation);

    /// <summary>Determines whether the specified code point has the Emoji_Presentation property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji_Presentation property; otherwise, <see langword="false"/>.</returns>
    public static bool HasEmojiPresentation(int codePoint) => HasProperty(codePoint, EmojiProperties.EmojiPresentation);

    /// <summary>Determines whether the specified rune has the Emoji_Modifier property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji_Modifier property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiModifier(Rune rune) => HasProperty(rune.Value, EmojiProperties.EmojiModifier);

    /// <summary>Determines whether the specified code point has the Emoji_Modifier property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji_Modifier property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiModifier(int codePoint) => HasProperty(codePoint, EmojiProperties.EmojiModifier);

    /// <summary>Determines whether the specified rune has the Emoji_Modifier_Base property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji_Modifier_Base property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiModifierBase(Rune rune) => HasProperty(rune.Value, EmojiProperties.EmojiModifierBase);

    /// <summary>Determines whether the specified code point has the Emoji_Modifier_Base property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji_Modifier_Base property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiModifierBase(int codePoint) => HasProperty(codePoint, EmojiProperties.EmojiModifierBase);

    /// <summary>Determines whether the specified rune has the Emoji_Component property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji_Component property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiComponent(Rune rune) => HasProperty(rune.Value, EmojiProperties.EmojiComponent);

    /// <summary>Determines whether the specified code point has the Emoji_Component property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji_Component property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiComponent(int codePoint) => HasProperty(codePoint, EmojiProperties.EmojiComponent);

    /// <summary>Determines whether the specified rune has the Extended_Pictographic property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Extended_Pictographic property; otherwise, <see langword="false"/>.</returns>
    public static bool IsExtendedPictographic(Rune rune) => HasProperty(rune.Value, EmojiProperties.ExtendedPictographic);

    /// <summary>Determines whether the specified code point has the Extended_Pictographic property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Extended_Pictographic property; otherwise, <see langword="false"/>.</returns>
    public static bool IsExtendedPictographic(int codePoint) => HasProperty(codePoint, EmojiProperties.ExtendedPictographic);

    private static bool HasProperty(int codePoint, EmojiProperties property) => (UnicodeProperties.GetEmojiProperties(codePoint) & property) != EmojiProperties.None;

    /// <summary>Gets the emoji sequences of an emoji property of strings, such as <c>RGI_Emoji_Flag_Sequence</c>.</summary>
    /// <param name="property">The property.</param>
    /// <returns>
    /// The sequences, in the order of the data files of UTS #51. The list is read-only, created once, and shared by all
    /// callers.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="property"/> is not a value of <see cref="UnicodeEmojiSequenceProperty"/>.</exception>
    /// <seealso href="https://www.unicode.org/reports/tr51/#Emoji_Sets" />
    public static IReadOnlyList<string> GetSequences(UnicodeEmojiSequenceProperty property)
    {
        if ((uint)property >= (uint)SequenceCache.Length)
            throw new ArgumentOutOfRangeException(nameof(property), property, "The value is not an emoji property of strings");

        ref var slot = ref SequenceCache[(int)property];
        var sequences = Volatile.Read(ref slot);
        if (sequences is not null)
            return sequences;

        // Concurrent first callers may each decode the list, but only the first one is published and returned to all.
        sequences = new ReadOnlyCollection<string>(CreateSequences(property));
        return Interlocked.CompareExchange(ref slot, sequences, comparand: null) ?? sequences;
    }

    // The arrays are wrapped so that callers cannot cast the shared list back to an array and change it.
    private static readonly ReadOnlyCollection<string>?[] SequenceCache = new ReadOnlyCollection<string>?[6];

    private static string[] CreateSequences(UnicodeEmojiSequenceProperty property)
    {
        var data = UnicodeEmojiSequenceTables.GetSequences((int)property);
        var result = new List<string>();
        var builder = new StringBuilder();
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset];
            offset++;
            builder.Clear();
            for (var i = 0; i < length; i++)
            {
                builder.Append(char.ConvertFromUtf32(UnicodeTableReader.ReadUInt24(data, offset)));
                offset += 3;
            }

            result.Add(builder.ToString());
        }

        return result.ToArray();
    }
}
