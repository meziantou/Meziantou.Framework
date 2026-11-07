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

        // 10.2.1.3. Integer. Base 10 accepts leading zeros and a sign; base 8 and base 16 accept neither.
        // Underscore separators and binary literals are YAML 1.1 spellings, which the extended schema adds back.
        AddScalarRule(new Type[] { typeof(ulong), typeof(long), typeof(int) }, "!!int", @"[-+]?[0-9]+", m => SchemaScalarDecoder.DecodeInteger(m.Value), null);
        AddScalarRule(new Type[] { typeof(ulong), typeof(long), typeof(int) }, "!!int", @"0x[0-9a-fA-F]+", m => SchemaScalarDecoder.DecodeInteger(m.Value), null);
        AddScalarRule(new Type[] { typeof(ulong), typeof(long), typeof(int) }, "!!int", @"0o[0-7]+", m => SchemaScalarDecoder.DecodeInteger(m.Value), null);

        // 10.2.1.4. Floating Point. The integer rules come first so a plain decimal number keeps the !!int tag.
        AddScalarRule("!!float", @"[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?", m => Convert.ToDouble(m.Value, CultureInfo.InvariantCulture), null);

        AddScalarRule("!!float", @"\+?(\.inf|\.Inf|\.INF)", m => double.PositiveInfinity, null);
        AddScalarRule("!!float", @"-(\.inf|\.Inf|\.INF)", m => double.NegativeInfinity, null);
        AddScalarRule("!!float", @"\.nan|\.NaN|\.NAN", m => double.NaN, null);

        AllowFailsafeString = true;

        // We are not calling the base as we want to completely override scalar rules
        // and in order to have a more concise set of regex
    }
}
