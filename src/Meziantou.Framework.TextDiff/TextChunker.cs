using System.Buffers;

namespace Meziantou.Framework;

public class TextChunker
{
    public static TextChunker Lines { get; } = new LineChunker();
    public static TextChunker Words { get; } = new WordChunker();
    public static TextChunker Characters { get; } = new CharacterChunker();

    public virtual IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        => Lines.Chunk(value);

    private sealed class LineChunker : TextChunker
    {
#if NET8_0_OR_GREATER
        private static SearchValues<char> NewLineCharacters { get; } = SearchValues.Create("\r\n\u0085\u2028\u2029");
#else
        private static SearchValues<string> NewLineCharacters { get; } = SearchValues.Create(["\r", "\n", "\u0085", "\u2028", "\u2029"]);
#endif

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
#if NET8_0_OR_GREATER
        private static SearchValues<char> WhiteSpaceCharacters { get; } = SearchValues.Create("\t\n\v\f\r\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000");
#else
        private static SearchValues<string> WhiteSpaceCharacters { get; } = SearchValues.Create(["\t", "\n", "\v", "\f", "\r", "\u0020", "\u0085", "\u00A0", "\u1680", "\u2000", "\u2001", "\u2002", "\u2003", "\u2004", "\u2005", "\u2006", "\u2007", "\u2008", "\u2009", "\u200A", "\u2028", "\u2029", "\u202F", "\u205F", "\u3000"]);
#endif

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
                while (whiteSpaceEnd < value.Length && value[whiteSpaceEnd..].IndexOfAny(WhiteSpaceCharacters) == 0)
                {
                    whiteSpaceEnd++;
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
        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        {
            var chars = new List<string>(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                chars.Add(value[i].ToString());
            }

            return chars;
        }
    }
}
