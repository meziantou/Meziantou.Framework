using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>The shortcodes, such as <c>:smile:</c>, and the smileys, such as <c>:)</c>, that <see cref="MarkdownExtensions.Emoji"/> reads, and their emoji.</summary>
public sealed class MarkdownEmojiMapping
{
    private readonly Dictionary<string, string> _shortcodes = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _smileys = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _emojis = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _emojisBySpan;

    // The lengths of the shortcodes and the smileys by their first character, the longest first
    private readonly Dictionary<char, int[]> _lengthsByFirstCharacter;

    /// <summary>Gets the shortcodes of GitHub and the arrows, and the common smileys, such as <c>:)</c>, <c>;)</c> and <c>&lt;3</c>.</summary>
    public static MarkdownEmojiMapping Default { get; } = new(EmojiData.GetDefaultEmojiShortcodeToUnicode(), EmojiData.GetDefaultSmileyToEmojiShortcode());

    /// <summary>Gets the shortcodes of GitHub and the arrows, without smileys.</summary>
    public static MarkdownEmojiMapping DefaultWithoutSmileys { get; } = new(EmojiData.GetDefaultEmojiShortcodeToUnicode(), new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Creates a mapping from shortcodes to emoji, and from smileys to shortcodes.</summary>
    /// <param name="shortcodes">The emoji of the shortcodes, such as <c>:smile:</c>.</param>
    /// <param name="smileys">The shortcode of the smileys, such as <c>:)</c>, which must be a shortcode of <paramref name="shortcodes"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shortcodes"/> or <paramref name="smileys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key or a value is empty, a smiley is not for a shortcode, or a smiley is a shortcode.</exception>
    public MarkdownEmojiMapping(IReadOnlyDictionary<string, string> shortcodes, IReadOnlyDictionary<string, string> smileys)
    {
        ArgumentNullException.ThrowIfNull(shortcodes);
        ArgumentNullException.ThrowIfNull(smileys);

        foreach (var (shortcode, emoji) in shortcodes)
        {
            if (string.IsNullOrEmpty(shortcode) || string.IsNullOrEmpty(emoji))
            {
                throw new ArgumentException("The shortcodes and their emoji cannot be empty.", nameof(shortcodes));
            }

            _shortcodes[shortcode] = emoji;
            _emojis[shortcode] = emoji;
        }

        foreach (var (smiley, shortcode) in smileys)
        {
            if (string.IsNullOrEmpty(smiley) || string.IsNullOrEmpty(shortcode))
            {
                throw new ArgumentException("The smileys and their shortcodes cannot be empty.", nameof(smileys));
            }

            if (!shortcodes.TryGetValue(shortcode, out var emoji))
            {
                throw new ArgumentException($"The smiley '{smiley}' is for '{shortcode}', which is not a shortcode.", nameof(smileys));
            }

            if (!_emojis.TryAdd(smiley, emoji))
            {
                throw new ArgumentException($"The smiley '{smiley}' is already a shortcode.", nameof(smileys));
            }

            _smileys[smiley] = GetShortcodeKey(shortcode, emoji);
        }

        Shortcodes = new ReadOnlyDictionary<string, string>(_shortcodes);
        Smileys = new ReadOnlyDictionary<string, string>(_smileys);

        _emojisBySpan = _emojis.GetAlternateLookup<ReadOnlySpan<char>>();
        _lengthsByFirstCharacter = _emojis.Keys.GroupBy(key => key[0]).ToDictionary(group => group.Key, group => group.Select(key => key.Length).Distinct().OrderDescending().ToArray());
    }

    // The key of Shortcodes for the shortcode of a smiley: the dictionary of the shortcodes can have a comparer that is not
    // ordinal, so that :ok: finds :OK:. The key with the same emoji that differs only by case is preferred, then any key with
    // the same emoji, which renders the same.
    private string GetShortcodeKey(string shortcode, string emoji)
    {
        if (_shortcodes.ContainsKey(shortcode))
        {
            return shortcode;
        }

        string? result = null;
        foreach (var (key, value) in _shortcodes)
        {
            if (string.Equals(value, emoji, StringComparison.Ordinal))
            {
                if (string.Equals(key, shortcode, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }

                result ??= key;
            }
        }

        return result ?? shortcode;
    }

    /// <summary>Gets the emoji of the shortcodes, such as <c>:smile:</c>.</summary>
    /// <remarks>Use it with <see cref="Smileys"/> to make a mapping from another one, such as <see cref="Default"/> with fewer smileys.</remarks>
    public IReadOnlyDictionary<string, string> Shortcodes { get; }

    /// <summary>Gets the shortcode of the smileys, such as <c>:)</c>: each one is a key of <see cref="Shortcodes"/>.</summary>
    public IReadOnlyDictionary<string, string> Smileys { get; }

    /// <summary>Gets the emoji of a shortcode or of a smiley.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public bool TryGetEmoji(string text, [NotNullWhen(true)] out string? emoji)
    {
        ArgumentNullException.ThrowIfNull(text);

        return _emojis.TryGetValue(text, out emoji);
    }

    internal bool IsFirstCharacter(char c) => _lengthsByFirstCharacter.ContainsKey(c);

    internal IEnumerable<char> FirstCharacters => _lengthsByFirstCharacter.Keys;

    // The longest shortcode or smiley that the text starts with
    internal string? MatchLongest(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || !_lengthsByFirstCharacter.TryGetValue(text[0], out var lengths))
        {
            return null;
        }

        foreach (var length in lengths)
        {
            // A smiley that ends with a letter or a digit, such as :p, does not end in the middle of a word, such as :param
            if (length <= text.Length && _emojisBySpan.TryGetValue(text[..length], out var key, out _) && !(length < text.Length && char.IsLetterOrDigit(text[length - 1]) && char.IsLetterOrDigit(text[length])))
            {
                return key;
            }
        }

        return null;
    }
}
