using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Schemas;

/// <summary>Implements the YAML failsafe schema.</summary>
/// <remarks>The failsafe schema is guaranteed to work with any YAML document.
/// It is therefore the recommended schema for generic YAML tools.
/// A YAML processor should therefore support this schema, at least as an option.</remarks>
public class FailsafeSchema : SchemaBase
{
    /// <summary>
    /// The map short tag: !!map.
    /// </summary>
    public const string MapShortTag = "!!map";

    /// <summary>
    /// The map long tag: tag:yaml.org,2002:map
    /// </summary>
    public const string MapLongTag = "tag:yaml.org,2002:map";

    /// <summary>
    /// The seq short tag: !!seq
    /// </summary>
    public const string SeqShortTag = "!!seq";

    /// <summary>
    /// The seq long tag: tag:yaml.org,2002:seq
    /// </summary>
    public const string SeqLongTag = "tag:yaml.org,2002:seq";

    /// <summary>
    /// Initializes a new instance of the <see cref="FailsafeSchema"/> class.
    /// </summary>
    public FailsafeSchema()
    {
        RegisterTag(MapShortTag, MapLongTag);
        RegisterTag(SeqShortTag, SeqLongTag);
        RegisterTag(StrShortTag, StrLongTag);
        AllowFailsafeString = true;
    }

    /// <summary>
    /// Gets or sets a value indicating whether this schema should always fallback to a
    /// failsafe string in case of not matching any scalar rules. Default is true for <see cref="FailsafeSchema"/>
    /// </summary>
    /// <value><c>true</c> if [allow failsafe string]; otherwise, <c>false</c>.</value>
    protected bool AllowFailsafeString { get; set; }

    /// <summary>Gets the default tag for mapping nodes.</summary>
    protected override string GetDefaultTag(MappingStart nodeEvent)
    {
        return MapShortTag;
    }

    /// <summary>Gets the default tag for sequence nodes.</summary>
    protected override string GetDefaultTag(SequenceStart nodeEvent)
    {
        return SeqShortTag;
    }

    /// <summary>Tries to parse.</summary>
    public override bool TryParse(Scalar scalar, bool decodeValue, [NotNullWhen(true)] out string? defaultTag, out object? value)
    {
        if (base.TryParse(scalar, decodeValue, out defaultTag, out value))
        {
            return true;
        }

        if (AllowFailsafeString)
        {
            value = decodeValue ? scalar.Value : null;
            defaultTag = StrShortTag;
            return true;
        }

        return false;
    }
}