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
        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        {
            var words = new List<string>();
            var start = 0;

            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    if (i > start)
                    {
                        words.Add(value[start..i].ToString());
                    }

                    // Add whitespace as its own chunk
                    var wsStart = i;
                    while (i + 1 < value.Length && char.IsWhiteSpace(value[i + 1]))
                    {
                        i++;
                    }

                    words.Add(value[wsStart..(i + 1)].ToString());
                    start = i + 1;
                }
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
