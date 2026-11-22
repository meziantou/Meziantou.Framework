using System.Text;

namespace Meziantou.Framework.Csv.Fuzz;

/// <summary>A character-by-character implementation of the grammar of the reader, used to check the SIMD implementation.</summary>
internal static class ReferenceParser
{
    public static string[][] Parse(string text, char separator, char? quote, char? comment = null)
    {
        var records = new List<string[]>();
        var values = new List<string>();
        var value = new StringBuilder();
        var position = 0;
        var fieldStart = true;
        var hasContent = false;
        while (position < text.Length)
        {
            var c = text[position];
            if (comment is not null && c == comment && fieldStart && !hasContent && values.Count == 0)
            {
                // A comment line: skipped with its line terminator
                while (position < text.Length && text[position] is not ('\r' or '\n'))
                {
                    position++;
                }

                if (position < text.Length)
                {
                    position += text[position] == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 : 1;
                }

                continue;
            }

            if (fieldStart && quote is not null && c == quote)
            {
                // Quoted value: a doubled quote is a quote, a single quote closes the value, and the rest of the value is literal
                hasContent = true;
                fieldStart = false;
                position++;
                while (position < text.Length)
                {
                    if (text[position] == quote)
                    {
                        if (position + 1 < text.Length && text[position + 1] == quote)
                        {
                            value.Append(quote.Value);
                            position += 2;
                            continue;
                        }

                        position++;
                        break;
                    }

                    value.Append(text[position]);
                    position++;
                }

                continue;
            }

            fieldStart = false;
            if (c == separator)
            {
                values.Add(value.ToString());
                value.Clear();
                fieldStart = true;
                hasContent = true;
                position++;
                continue;
            }

            if (c is '\r' or '\n')
            {
                position += c == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 : 1;
                EndRecord();
                continue;
            }

            hasContent = true;
            value.Append(c);
            position++;
        }

        if (hasContent)
        {
            EndRecord();
        }

        return [.. records];

        void EndRecord()
        {
            if (hasContent)
            {
                values.Add(value.ToString());
            }

            records.Add([.. values]);
            values.Clear();
            value.Clear();
            fieldStart = true;
            hasContent = false;
        }
    }
}
