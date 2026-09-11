using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Schemas;

/// <summary>Decodes the scalar spellings shared by the built-in schemas.</summary>
/// <remarks>
/// Tag resolution classifies a scalar by its spelling, not by whether a CLR type can hold it, so a decoder must
/// never throw: the schemas expose their resolution through <c>TryParse</c> methods.
/// </remarks>
internal static class SchemaScalarDecoder
{
    /// <summary>Decodes an integer scalar into the narrowest CLR type that can represent it.</summary>
    /// <param name="text">The scalar text, including any sign, base prefix, and underscores.</param>
    public static object DecodeInteger(string text)
    {
        if (YamlScalar.TryParseInt64(text.AsSpan(), out var signed))
        {
            return signed is >= int.MinValue and <= int.MaxValue ? (object)(int)signed : signed;
        }

        if (YamlScalar.TryParseUInt64(text.AsSpan(), out var unsigned))
        {
            return unsigned;
        }

        // No fixed-size integer type can hold the value. A double keeps its magnitude and matches what the
        // parser used when the schema is disabled returns for the same text.
        if (YamlScalar.TryParseDouble(text.AsSpan(), out var approximation))
        {
            return approximation;
        }

        return text;
    }

    /// <summary>Decodes an integer scalar written with an explicit base.</summary>
    /// <param name="sign">The sign the scalar was written with, which is empty, <c>+</c>, or <c>-</c>.</param>
    /// <param name="basePrefix">The base prefix to decode the digits with (<c>0b</c>, <c>0o</c>, or <c>0x</c>).</param>
    /// <param name="digits">The digits, which may contain underscores.</param>
    public static object DecodeInteger(string sign, string basePrefix, string digits)
        => DecodeInteger(string.Concat(sign, basePrefix, digits));
}
