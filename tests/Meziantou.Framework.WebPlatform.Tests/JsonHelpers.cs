using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Tests;

internal static class JsonHelpers
{
    /// <summary>Reads a JSON string, preserving lone surrogates (System.Text.Json rejects them).</summary>
    public static string? GetStringPreservingSurrogates(this JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        var raw = element.GetRawText();
        var builder = new StringBuilder(raw.Length);
        for (var i = 1; i < raw.Length - 1; i++)
        {
            var c = raw[i];
            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            c = raw[++i];
            switch (c)
            {
                case 'u':
                    builder.Append((char)int.Parse(raw.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }
}
