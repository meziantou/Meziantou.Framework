using System.Text.RegularExpressions;

namespace Meziantou.Framework.Yaml.Schemas;

/// <summary>Implements a JSON schema.</summary>
/// <remarks>
/// The JSON schema is the lowest common denominator of most modern computer languages, and allows parsing JSON files.
/// A YAML processor should therefore support this schema, at least as an option. It is also strongly recommended that other schemas should be based on it. .
/// </remarks>>
public class JsonSchema : FailsafeSchema
{
    /// <summary>
    /// The null short tag: !!null
    /// </summary>
    public const string NullShortTag = "!!null";

    /// <summary>
    /// The null long tag: tag:yaml.org,2002:null
    /// </summary>
    public const string NullLongTag = "tag:yaml.org,2002:null";

    /// <summary>
    /// The bool short tag: !!bool
    /// </summary>
    public const string BoolShortTag = "!!bool";

    /// <summary>
    /// The bool long tag: tag:yaml.org,2002:bool
    /// </summary>
    public const string BoolLongTag = "tag:yaml.org,2002:bool";

    /// <summary>
    /// The int short tag: !!int
    /// </summary>
    public const string IntShortTag = "!!int";

    /// <summary>
    /// The int long tag: tag:yaml.org,2002:int
    /// </summary>
    public const string IntLongTag = "tag:yaml.org,2002:int";

    /// <summary>
    /// The float short tag: !!float
    /// </summary>
    public const string FloatShortTag = "!!float";

    /// <summary>
    /// The float long tag: tag:yaml.org,2002:float
    /// </summary>
    public const string FloatLongTag = "tag:yaml.org,2002:float";

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSchema"/> class.
    /// </summary>
    public JsonSchema()
    {
        // All bool type
        RegisterDefaultTagMapping<bool>(BoolShortTag, true);

        // All int types
        RegisterDefaultTagMapping<sbyte>(IntShortTag);
        RegisterDefaultTagMapping<byte>(IntShortTag);
        RegisterDefaultTagMapping<short>(IntShortTag);
        RegisterDefaultTagMapping<ushort>(IntShortTag);
        RegisterDefaultTagMapping<int>(IntShortTag, true);
        RegisterDefaultTagMapping<uint>(IntShortTag);
        RegisterDefaultTagMapping<long>(IntShortTag);
        RegisterDefaultTagMapping<ulong>(IntShortTag);

        // All double/float types
        RegisterDefaultTagMapping<float>(FloatShortTag, true);
        RegisterDefaultTagMapping<double>(FloatShortTag);

        // All string types
        RegisterDefaultTagMapping<char>(StrShortTag);
        RegisterDefaultTagMapping<string>(StrShortTag, true);

        RegisterTag(NullShortTag, NullLongTag);
        RegisterTag(BoolShortTag, BoolLongTag);
        RegisterTag(IntShortTag, IntLongTag);
        RegisterTag(FloatShortTag, FloatLongTag);
    }

    /// <summary>Registers scalar resolution rules for the JSON schema.</summary>
    protected override void PrepareScalarRules()
    {
        // 10.2.1.1. Null
        AddScalarRule<object>("!!null", @"null", m => null, null);

        // 10.2.1.2. Boolean
        AddScalarRule("!!bool", @"true", m => true, null);
        AddScalarRule("!!bool", @"false", m => false, null);

        // 10.2.1.3. Integer
        AddScalarRule(new Type[] { typeof(ulong), typeof(long), typeof(int) }, "!!int", @"-?(0|[1-9][0-9]*)", JsonSchema.DecodeInteger, null);

        // 10.2.1.4. Floating Point
        AddScalarRule("!!float", @"-?(0|[1-9][0-9]*)(\.[0-9]*)?([eE][-+]?[0-9]+)?", m => Convert.ToDouble(m.Value, CultureInfo.InvariantCulture), null);

        // Json doesn't allow failsafe string, so we are disabling it here.
        AllowFailsafeString = false;

        // We are not calling the base as we want to completely override scalar rules
        // and in order to have a more concise set of regex
    }

    /// <summary>Decodes a matched integer scalar into an <see cref="int"/>, <see cref="long"/>, or <see cref="ulong"/>.</summary>
    /// <remarks>
    /// A scalar too large for any of those types is decoded as a <see cref="double"/>: tag resolution classifies a
    /// scalar by its spelling, so an unrepresentable value must not fail the surrounding <c>TryParse</c> call.
    /// </remarks>
    protected static object DecodeInteger(Match m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return SchemaScalarDecoder.DecodeInteger(m.Value);
    }
}
