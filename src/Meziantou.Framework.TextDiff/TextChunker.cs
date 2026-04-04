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
        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        {
            var lines = new List<string>();
            var start = 0;

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\r')
                {
                    var end = i + 1 < value.Length && value[i + 1] == '\n' ? i + 2 : i + 1;
                    lines.Add(value[start..end].ToString());
                    start = end;
                    if (end > i + 1)
                    {
                        i++; // skip \n after \r
                    }
                }
                else if (c == '\n' || c == '\u0085' || c == '\u2028' || c == '\u2029')
                {
                    lines.Add(value[start..(i + 1)].ToString());
                    start = i + 1;
                }
            }

            if (start <= value.Length)
            {
                lines.Add(value[start..].ToString());
            }

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
