using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Schemas;

/// <summary>Implements the Core schema.</summary>
/// <remarks>
/// The Core schema is an extension of the JSON schema, allowing for more human-readable presentation of the same types.
/// This is the recommended default schema that YAML processor should use unless instructed otherwise.
/// It is also strongly recommended that other schemas should be based on it.
/// </remarks>
public class CoreSchema : JsonSchema
{
    /// <summary>Gets instance.</summary>
    public static readonly CoreSchema Instance = new CoreSchema();

    /// <summary>Registers scalar resolution rules for the YAML core schema.</summary>
    protected override void PrepareScalarRules()
    {
        // 10.2.1.1. Null
        AddScalarRule<object>("!!null", @"null|Null|NULL|\~|", m => null, null);

        AddScalarRule("!!bool", @"true|True|TRUE", m => true, null);
        AddScalarRule("!!bool", @"false|False|FALSE", m => false, null);

        AddScalarRule(new Type[] { typeof(ulong), typeof(long), typeof(int) }, "!!int", @"[-+]?(0|[1-9][0-9_]*)", m => DecodeCoreInteger(m.Value), null);
        AddScalarRule(new Type[] { typeof(ulong), typeof(long), typeof(int) }, "!!int", @"[-+]?0x([0-9a-fA-F_]+)", m => DecodeCoreInteger(m.Value), null);
        AddScalarRule(new Type[] { typeof(ulong), typeof(long), typeof(int) }, "!!int", @"[-+]?0o([0-7_]+)", m => DecodeCoreInteger(m.Value), null);
        AddScalarRule(new Type[] { typeof(ulong), typeof(long), typeof(int) }, "!!int", @"[-+]?0b([01_]+)", m => DecodeCoreInteger(m.Value), null);

        // Make float before tagged integers to keep the common decimal path fast.
        AddScalarRule("!!float", @"[-+]?(\.[0-9_]+|[0-9][0-9_]*(\.[0-9_]*)?)([eE][-+]?[0-9]+)?", m => Convert.ToDouble(m.Value.Replace("_", "", StringComparison.Ordinal), CultureInfo.InvariantCulture), null);

        AddScalarRule("!!float", @"\+?(\.inf|\.Inf|\.INF)", m => double.PositiveInfinity, null);
        AddScalarRule("!!float", @"-(\.inf|\.Inf|\.INF)", m => double.NegativeInfinity, null);
        AddScalarRule("!!float", @"\.nan|\.NaN|\.NAN", m => double.NaN, null);

        AllowFailsafeString = true;

        // We are not calling the base as we want to completely override scalar rules
        // and in order to have a more concise set of regex
    }

    private static object DecodeCoreInteger(string text)
    {
        if (YamlScalar.TryParseInt64(text.AsSpan(), out var signed))
        {
            return signed is >= int.MinValue and <= int.MaxValue ? (object)(int)signed : signed;
        }

        if (YamlScalar.TryParseUInt64(text.AsSpan(), out var unsigned))
        {
            if (unsigned <= int.MaxValue)
            {
                return (int)unsigned;
            }

            if (unsigned <= (ulong)long.MaxValue)
            {
                return (long)unsigned;
            }

            return unsigned;
        }

        throw new FormatException($"Invalid YAML integer scalar '{text}'.");
    }
}
