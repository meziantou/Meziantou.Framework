namespace Meziantou.Framework.Http.Caching.Fuzz;

/// <summary>Generates a valid RFC 8941 dictionary from the bytes of an input, with the values the parser materializes.</summary>
internal sealed class StructuredFieldGenerator
{
    private const string LowerCaseAlpha = "abcdefghijklmnopqrstuvwxyz";
    private const string KeyCharacters = LowerCaseAlpha + "0123456789_-.*";
    private const string TokenFirstCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz*";
    private const string TokenCharacters = TokenFirstCharacters + "0123456789!#$%&'+-.^_`|~:/";

    private static readonly string[] CommonKeys = ["a", "b", "params", "except", "key-order"];

    private readonly byte[] _data;
    private int _position;

    public StructuredFieldGenerator(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    public (string Text, Dictionary<string, StructuredFieldValue> Expected) GenerateDictionary()
    {
        var builder = new StringBuilder();
        var expected = new Dictionary<string, StructuredFieldValue>(StringComparer.Ordinal);
        builder.Append(' ', Next(3));

        var count = Next(6);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                AppendOptionalWhitespace(builder);
                builder.Append(',');
                AppendOptionalWhitespace(builder);
            }

            var key = GenerateKey();
            builder.Append(key);

            // RFC 8941 Section 4.2.2: the last value of a duplicated key wins
            switch (Next(4))
            {
                case 0:
                    // A member without a value is the boolean true
                    expected[key] = StructuredFieldValue.FromItem(StructuredFieldItem.True);
                    AppendParameters(builder);
                    break;

                case 1:
                    builder.Append('=');
                    expected[key] = AppendInnerList(builder);
                    break;

                default:
                    builder.Append('=');
                    expected[key] = StructuredFieldValue.FromItem(AppendBareItem(builder));
                    AppendParameters(builder);
                    break;
            }
        }

        return (builder.ToString(), expected);
    }

    private int Next(int count) => (_position < _data.Length ? _data[_position++] : 0) % count;

    private char Pick(string characters) => characters[Next(characters.Length)];

    private string GenerateKey()
    {
        if (Next(3) is 0)
            return CommonKeys[Next(CommonKeys.Length)];

        var builder = new StringBuilder();
        builder.Append(Pick(LowerCaseAlpha + "*"));
        var length = Next(6);
        for (var i = 0; i < length; i++)
        {
            builder.Append(Pick(KeyCharacters));
        }

        return builder.ToString();
    }

    private StructuredFieldValue AppendInnerList(StringBuilder builder)
    {
        builder.Append('(');
        builder.Append(' ', Next(3));
        var items = new List<StructuredFieldItem>();
        var count = Next(5);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ', 1 + Next(3));
            }

            items.Add(AppendBareItem(builder));
            AppendParameters(builder);
        }

        builder.Append(' ', Next(3));
        builder.Append(')');
        AppendParameters(builder);
        return StructuredFieldValue.FromInnerList(items);
    }

    private void AppendParameters(StringBuilder builder)
    {
        var count = Next(3);
        for (var i = 0; i < count; i++)
        {
            builder.Append(';');
            builder.Append(' ', Next(2));
            builder.Append(GenerateKey());
            if (Next(2) is 0)
            {
                builder.Append('=');
                _ = AppendBareItem(builder);
            }
        }
    }

    private StructuredFieldItem AppendBareItem(StringBuilder builder)
    {
        switch (Next(6))
        {
            case 0:
                // Integer: up to 15 digits
                AppendSign(builder);
                AppendDigits(builder, 1 + Next(15));
                return default;

            case 1:
                // Decimal: up to 12 integer digits and 3 fractional digits
                AppendSign(builder);
                AppendDigits(builder, 1 + Next(12));
                builder.Append('.');
                AppendDigits(builder, 1 + Next(3));
                return default;

            case 2:
            {
                var value = new StringBuilder();
                var length = Next(9);
                for (var i = 0; i < length; i++)
                {
                    value.Append((char)(' ' + Next('~' - ' ' + 1)));
                }

                var text = value.ToString();
                builder.Append('"').Append(text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                return StructuredFieldItem.FromString(text);
            }

            case 3:
            {
                builder.Append(Pick(TokenFirstCharacters));
                var length = Next(7);
                for (var i = 0; i < length; i++)
                {
                    builder.Append(Pick(TokenCharacters));
                }

                return default;
            }

            case 4:
            {
                var bytes = new byte[Next(7)];
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)Next(256);
                }

                builder.Append(':').Append(Convert.ToBase64String(bytes)).Append(':');
                return default;
            }

            default:
                var isTrue = Next(2) is 1;
                builder.Append(isTrue ? "?1" : "?0");
                return isTrue ? StructuredFieldItem.True : StructuredFieldItem.False;
        }
    }

    private void AppendSign(StringBuilder builder)
    {
        if (Next(2) is 1)
        {
            builder.Append('-');
        }
    }

    private void AppendDigits(StringBuilder builder, int count)
    {
        for (var i = 0; i < count; i++)
        {
            builder.Append((char)('0' + Next(10)));
        }
    }

    private void AppendOptionalWhitespace(StringBuilder builder)
    {
        var count = Next(3);
        for (var i = 0; i < count; i++)
        {
            builder.Append(Next(2) is 0 ? ' ' : '\t');
        }
    }
}
