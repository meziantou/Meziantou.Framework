using System.Buffers;

namespace Meziantou.Framework;

/// <summary>Splits a text into the chunks that a diff compares against each other. Derive from this type and
/// override <see cref="Chunk"/> to supply a custom chunking strategy.</summary>
public class TextChunker
{
    /// <summary>
    /// Gets a chunker that splits on line terminators, keeping the terminator at the end of each chunk so the
    /// text can be rebuilt exactly.
    /// </summary>
    /// <remarks>
    /// A text that ends with a line terminator produces a final empty chunk, and an empty text produces a single
    /// empty chunk. This is what makes the chunks concatenate back to the original text; <see cref="Words"/> and
    /// <see cref="Characters"/> produce no chunks at all for an empty text.
    /// </remarks>
    public static TextChunker Lines { get; } = new LineChunker();

    /// <summary>
    /// Gets a chunker that alternates between runs of non-whitespace and runs of whitespace, so that whitespace
    /// is preserved in its own chunks.
    /// </summary>
    public static TextChunker Words { get; } = new WordChunker();

    /// <summary>Gets a chunker that produces one chunk per <see cref="char"/>.</summary>
    /// <remarks>
    /// Chunks are UTF-16 code units, not grapheme clusters: a surrogate pair is split into two chunks.
    /// </remarks>
    public static TextChunker Characters { get; } = new CharacterChunker();

    /// <summary>Splits <paramref name="value"/> into chunks.</summary>
    /// <param name="value">The text to split.</param>
    /// <returns>
    /// The chunks, in order. Concatenating them should reproduce <paramref name="value"/> exactly, otherwise the
    /// diff cannot be used to rebuild either text.
    /// </returns>
    /// <remarks>
    /// The base implementation delegates to <see cref="Lines"/>. A derived chunker that does not override this
    /// method therefore produces line chunks rather than failing.
    /// </remarks>
    public virtual IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        => Lines.Chunk(value);

    private sealed class LineChunker : TextChunker
    {
        private static SearchValues<char> NewLineCharacters { get; } = SearchValues.Create("\r\n\u0085\u2028\u2029");

        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        {
            var lines = new List<string>();
            var start = 0;

            while (start < value.Length)
            {
                var lineEndIndex = value[start..].IndexOfAny(NewLineCharacters);
                if (lineEndIndex < 0)
                {
                    break;
                }

                var separatorStart = start + lineEndIndex;
                var end = separatorStart + 1;
                if (value[separatorStart] == '\r' && end < value.Length && value[end] == '\n')
                {
                    end++;
                }

                lines.Add(value[start..end].ToString());
                start = end;
            }

            lines.Add(value[start..].ToString());

            return lines;
        }
    }

    private sealed class WordChunker : TextChunker
    {
        private static SearchValues<char> WhiteSpaceCharacters { get; } = SearchValues.Create("\t\n\v\f\r\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000");

        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        {
            var words = new List<string>();
            var start = 0;

            while (start < value.Length)
            {
                var whiteSpaceOffset = value[start..].IndexOfAny(WhiteSpaceCharacters);
                if (whiteSpaceOffset < 0)
                {
                    break;
                }

                var whiteSpaceStart = start + whiteSpaceOffset;
                if (whiteSpaceStart > start)
                {
                    words.Add(value[start..whiteSpaceStart].ToString());
                }

                var whiteSpaceEnd = whiteSpaceStart + 1;
                var nonWhiteSpaceOffset = value[whiteSpaceEnd..].IndexOfAnyExcept(WhiteSpaceCharacters);
                if (nonWhiteSpaceOffset < 0)
                {
                    whiteSpaceEnd = value.Length;
                }
                else
                {
                    whiteSpaceEnd += nonWhiteSpaceOffset;
                }

                words.Add(value[whiteSpaceStart..whiteSpaceEnd].ToString());
                start = whiteSpaceEnd;
            }

            if (start < value.Length)
            {
                words.Add(value[start..].ToString());
            }

            return words;
        }
    }

    private sealed class CharacterChunker : TextChunker
    {
        // Cache single-character strings for the ASCII range to avoid allocating a new string per character.
        private static readonly string[] AsciiCache = CreateAsciiCache();

        private static string[] CreateAsciiCache()
        {
            var cache = new string[128];
            for (var i = 0; i < cache.Length; i++)
            {
                cache[i] = ((char)i).ToString();
            }

            return cache;
        }

        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        {
            var chars = new List<string>(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                chars.Add(c < AsciiCache.Length ? AsciiCache[c] : c.ToString());
            }

            return chars;
        }
    }
}
