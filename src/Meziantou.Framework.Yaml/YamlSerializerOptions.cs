using System.Collections.ObjectModel;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// Configures the behavior of <see cref="YamlSerializer"/> operations.
/// </summary>
public sealed record YamlSerializerOptions
{
    private static readonly YamlConverter[] EmptyConverters = [];
    private static readonly ReadOnlyCollection<YamlConverter> EmptyConvertersReadOnly = Array.AsReadOnly(EmptyConverters);

    /// <summary>Gets a default options instance.</summary>
    public static YamlSerializerOptions Default { get; } = new();

    private readonly ReadOnlyCollection<YamlConverter> _convertersReadOnly = EmptyConvertersReadOnly;

    /// <summary>Gets the custom converters.</summary>
    /// <remarks>Converters are evaluated in order and take precedence over built-in converters.</remarks>
    /// <exception cref="ArgumentNullException">Value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A converter entry is <see langword="null"/>.</exception>
    public IReadOnlyList<YamlConverter> Converters
    {
        get => _convertersReadOnly;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count == 0)
            {
                _convertersReadOnly = EmptyConvertersReadOnly;
                return;
            }

            var copy = new YamlConverter[value.Count];
            for (var i = 0; i < value.Count; i++)
            {
                var converter = value[i];
                if (converter is null)
                {
                    throw new ArgumentException("Converters cannot contain null entries.", nameof(value));
                }

                copy[i] = converter;
            }

            _convertersReadOnly = Array.AsReadOnly(copy);
        }
    }

    /// <summary>Gets or sets the policy used to convert CLR property names.</summary>
    public YamlNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>Gets or sets an optional name for the YAML source.</summary>
    /// <remarks>
    /// This value is used to annotate <see cref="YamlException"/> messages with a source name
    /// (for example, a file path) when reporting parse errors.
    /// </remarks>
    public string? SourceName { get; init; }

    /// <summary>Gets or sets the policy used to convert dictionary keys during serialization.</summary>
    public YamlNamingPolicy? DictionaryKeyPolicy { get; init; }

    /// <summary>Gets or sets a value indicating whether property name matching is case-insensitive.</summary>
    public bool PropertyNameCaseInsensitive { get; init; }

    /// <summary>Gets or sets a value indicating whether public fields are included during serialization and deserialization.</summary>
    public bool IncludeFields { get; init; }

    /// <summary>Gets or sets a value indicating whether read-only fields are ignored during serialization.</summary>
    public bool IgnoreReadOnlyFields { get; init; }

    /// <summary>Gets or sets a value indicating whether read-only properties are ignored during serialization.</summary>
    public bool IgnoreReadOnlyProperties { get; init; }

    /// <summary>Gets or sets a value indicating whether deserialization rejects properties that do not match a .NET member.</summary>
    public bool RejectUnmatchedProperties { get; init; }

    /// <summary>Gets or sets a value indicating whether required constructor parameters must be present during deserialization.</summary>
    public bool RespectRequiredConstructorParameters { get; init; } = true;

    /// <summary>Gets or sets a value indicating whether nullable annotations are enforced during serialization and deserialization.</summary>
    public bool RespectNullableAnnotations { get; init; } = true;

    /// <summary>Gets or sets how unmapped YAML members are handled during object deserialization.</summary>
    /// <remarks>Extension data members still capture unmatched properties when present.</remarks>
    public YamlUnmappedMemberHandling UnmappedMemberHandling { get; init; } = YamlUnmappedMemberHandling.Skip;

    /// <summary>Gets or sets the preferred object creation handling for properties and fields during deserialization.</summary>
    /// <remarks>
    /// A member-level or type-level <see cref="YamlObjectCreationHandlingAttribute"/> overrides this setting.
    /// The default behavior is <see cref="YamlObjectCreationHandling.Replace"/>.
    /// </remarks>
    public YamlObjectCreationHandling PreferredObjectCreationHandling { get; init; } = YamlObjectCreationHandling.Replace;

    /// <summary>Gets or sets the default ignore condition for null/default values.</summary>
    public YamlIgnoreCondition DefaultIgnoreCondition { get; init; }

    /// <summary>Gets or sets a value indicating whether output should be indented.</summary>
    public bool WriteIndented { get; init; } = true;

    /// <summary>
    /// Gets or sets the number of spaces to use when <see cref="WriteIndented"/> is enabled.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is less than 1.</exception>
    public int IndentSize
    {
        get;
        init
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Indent size must be at least 1.");
            }

            field = value;
        }
    } = 2;

    /// <summary>Gets or sets member ordering behavior for emitted mappings.</summary>
    public YamlMappingOrderPolicy MappingOrder { get; init; } = YamlMappingOrderPolicy.Declaration;

    /// <summary>Gets or sets how mappings are emitted when they appear as items in block sequences.</summary>
    /// <remarks>
    /// The default is <see cref="YamlSequenceItemStyle.Compact"/>, which emits the first mapping key on the same
    /// line as the sequence dash when possible.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="YamlSequenceItemStyle"/>.</exception>
    public YamlSequenceItemStyle BlockSequenceMappingStyle
    {
        get;
        init
        {
            ValidateSequenceItemStyle(value, nameof(value));
            field = value;
        }
    } = YamlSequenceItemStyle.Compact;

    /// <summary>Gets or sets how nested sequences are emitted when they appear as items in block sequences.</summary>
    /// <remarks>
    /// The default is <see cref="YamlSequenceItemStyle.Expanded"/>, which keeps nested sequence items on lines
    /// following the parent sequence dash.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="YamlSequenceItemStyle"/>.</exception>
    public YamlSequenceItemStyle BlockSequenceSequenceStyle
    {
        get;
        init
        {
            ValidateSequenceItemStyle(value, nameof(value));
            field = value;
        }
    } = YamlSequenceItemStyle.Expanded;

    /// <summary>Gets or sets the schema used for scalar resolution.</summary>
    public YamlSchemaKind Schema { get; init; } = YamlSchemaKind.Core;

    /// <summary>
    /// Gets or sets a value indicating whether scalar deserialization should resolve through <see cref="Schema"/>.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, built-in converters use a fast span-based path for common YAML 1.2 scalars while still honoring quoted scalars as strings.
    /// When <see langword="true"/>, scalar resolution goes through the selected <see cref="Schema"/>.
    /// </remarks>
    public bool UseSchema { get; init; }

    /// <summary>Gets or sets behavior when duplicate mapping keys are encountered while reading.</summary>
    public YamlDuplicateKeyHandling DuplicateKeyHandling { get; init; } = YamlDuplicateKeyHandling.Error;

    /// <summary>Gets or sets a value indicating whether unregistered runtime type names from YAML tags are allowed during deserialization.</summary>
    /// <remarks>Enabling this option allows tag-based type name activation and should only be used with trusted YAML input.</remarks>
    public bool UnsafeAllowDeserializeFromTagTypeName { get; init; }

    /// <summary>Gets scalar style preferences for serialization.</summary>
    /// <exception cref="ArgumentNullException">Value is <see langword="null"/>.</exception>
    public YamlScalarStylePreferences ScalarStylePreferences
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    /// <summary>Gets polymorphism options.</summary>
    /// <exception cref="ArgumentNullException">Value is <see langword="null"/>.</exception>
    public YamlPolymorphismOptions PolymorphismOptions
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    /// <summary>Gets or sets object reference handling behavior.</summary>
    public YamlReferenceHandling ReferenceHandling { get; init; }

    /// <summary>Gets or sets the maximum allowed nesting depth for YAML mappings and sequences during serialization and deserialization.</summary>
    /// <remarks>
    /// A value of <c>0</c> uses the default limit of 64.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Value is less than 0.</exception>
    public int MaxDepth
    {
        get;
        init
        {
            YamlDepthHelper.ValidateMaxDepth(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether YAML anchors (<c>&amp;name</c>) are allowed during deserialization.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, encountering a node that declares an anchor throws a <see cref="YamlException"/>.
    /// </remarks>
    public bool AllowAnchors { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether YAML aliases (<c>*name</c>) are allowed during deserialization.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, encountering an alias throws a <see cref="YamlException"/>.
    /// </remarks>
    public bool AllowAliases { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether YAML merge keys (<c>&lt;&lt;</c>) are allowed during deserialization.
    /// </summary>
    /// <remarks>
    /// Merge keys are only recognized when <see cref="Schema"/> is <see cref="YamlSchemaKind.Core"/> or
    /// <see cref="YamlSchemaKind.Extended"/>. When this option is <see langword="false"/> and a merge key would
    /// otherwise be applied, a <see cref="YamlException"/> is thrown.
    /// </remarks>
    public bool AllowMergeKeys { get; init; } = true;

    /// <summary>
    /// Gets or sets a metadata resolver used to retrieve <see cref="YamlTypeInfo"/> instances.
    /// </summary>
    public IYamlTypeInfoResolver? TypeInfoResolver { get; init; }

    internal int EffectiveMaxDepth => YamlDepthHelper.GetEffectiveMaxDepth(MaxDepth);

    internal static void ValidateSequenceItemStyle(YamlSequenceItemStyle value, string paramName)
    {
        if (value is not (YamlSequenceItemStyle.Default or YamlSequenceItemStyle.Expanded or YamlSequenceItemStyle.Compact))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The YAML sequence item style is not valid.");
        }
    }
}
