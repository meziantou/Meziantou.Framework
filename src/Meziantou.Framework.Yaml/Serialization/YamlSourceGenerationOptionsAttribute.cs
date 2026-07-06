namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Specifies the default <see cref="YamlSerializerOptions"/> used by a source-generated <see cref="YamlSerializerContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is inspired by <c>System.Text.Json</c>'s <c>JsonSourceGenerationOptionsAttribute</c>, but targets
/// <see cref="YamlSerializerOptions"/> instead.
/// </para>
/// <para>
/// The Meziantou.Framework.Yaml source generator reads this attribute at compile time and emits a context that constructs
/// an immutable <see cref="YamlSerializerOptions"/> instance. The generated context expects the same options instance
/// to be used consistently at runtime.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class YamlSourceGenerationOptionsAttribute : YamlAttribute
{
    /// <summary>Gets or sets a value indicating whether emitted YAML should be indented.</summary>
    public bool WriteIndented { get; set; }

    /// <summary>
    /// Gets or sets the number of spaces to use when <see cref="WriteIndented"/> is enabled.
    /// </summary>
    public int IndentSize { get; set; }

    /// <summary>Gets or sets the policy used to convert CLR property names.</summary>
    public YamlKnownNamingPolicy PropertyNamingPolicy { get; set; }

    /// <summary>Gets or sets the policy used to convert dictionary keys during serialization.</summary>
    public YamlKnownNamingPolicy DictionaryKeyPolicy { get; set; }

    /// <summary>Gets or sets a value indicating whether property name matching is case-insensitive.</summary>
    public bool PropertyNameCaseInsensitive { get; set; }

    /// <summary>Gets or sets a value indicating whether public fields are included during serialization and deserialization.</summary>
    public bool IncludeFields { get; set; }

    /// <summary>Gets or sets a value indicating whether read-only fields are ignored during serialization.</summary>
    public bool IgnoreReadOnlyFields { get; set; }

    /// <summary>Gets or sets a value indicating whether read-only properties are ignored during serialization.</summary>
    public bool IgnoreReadOnlyProperties { get; set; }

    /// <summary>Gets or sets a value indicating whether deserialization rejects properties that do not match a .NET member.</summary>
    public bool RejectUnmatchedProperties { get; set; }

    /// <summary>Gets or sets a value indicating whether required constructor parameters must be present during deserialization.</summary>
    public bool RespectRequiredConstructorParameters { get; set; }

    /// <summary>Gets or sets a value indicating whether nullable annotations are enforced during serialization and deserialization.</summary>
    public bool RespectNullableAnnotations { get; set; }

    /// <summary>Gets or sets how unmapped YAML members are handled during object deserialization.</summary>
    public YamlUnmappedMemberHandling UnmappedMemberHandling { get; set; }

    /// <summary>Gets or sets the preferred object creation handling for members during deserialization.</summary>
    public YamlObjectCreationHandling PreferredObjectCreationHandling { get; set; }

    /// <summary>Gets or sets the default ignore condition for null/default values.</summary>
    public YamlIgnoreCondition DefaultIgnoreCondition { get; set; }

    /// <summary>Gets or sets member ordering behavior for emitted mappings.</summary>
    public YamlMappingOrderPolicy MappingOrder { get; set; }

    /// <summary>Gets or sets how mappings are emitted when they appear as items in block sequences.</summary>
    public YamlSequenceItemStyle BlockSequenceMappingStyle { get; set; }

    /// <summary>Gets or sets how nested sequences are emitted when they appear as items in block sequences.</summary>
    public YamlSequenceItemStyle BlockSequenceSequenceStyle { get; set; }

    /// <summary>Gets or sets the schema used for scalar resolution.</summary>
    public YamlSchemaKind Schema { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether scalar deserialization should resolve through <see cref="Schema"/>.
    /// </summary>
    public bool UseSchema { get; set; }

    /// <summary>Gets or sets behavior when duplicate mapping keys are encountered while reading.</summary>
    public YamlDuplicateKeyHandling DuplicateKeyHandling { get; set; }

    /// <summary>Gets or sets a value indicating whether unregistered runtime type names from YAML tags are allowed during deserialization.</summary>
    public bool UnsafeAllowDeserializeFromTagTypeName { get; set; }

    /// <summary>Gets or sets object reference handling behavior.</summary>
    public YamlReferenceHandling ReferenceHandling { get; set; }

    /// <summary>Gets or sets an optional name for the YAML source.</summary>
    /// <remarks>
    /// This value is used to annotate <see cref="YamlException"/> messages with a source name
    /// (for example, a file path) when reporting parse errors.
    /// </remarks>
    public string? SourceName { get; set; }

    /// <summary>Gets or sets a value indicating whether plain scalar style should be preferred when possible.</summary>
    public bool PreferPlainStyle { get; set; }

    /// <summary>Gets or sets a value indicating whether quoted scalar style should be preferred for ambiguous scalars.</summary>
    public bool PreferQuotedForAmbiguousScalars { get; set; }

    /// <summary>Gets or sets how type discriminators are represented during polymorphic serialization.</summary>
    public YamlTypeDiscriminatorStyle DiscriminatorStyle { get; set; }

    /// <summary>Gets or sets the property name used for discriminator-based polymorphism.</summary>
    public string? TypeDiscriminatorPropertyName { get; set; }

    /// <summary>Gets or sets behavior when an unknown derived type discriminator is encountered.</summary>
    public YamlUnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; set; }

    /// <summary>Gets or sets converter types to be instantiated and registered on the generated options instance.</summary>
    /// <remarks>
    /// Each converter type must derive from <see cref="YamlConverter"/> and expose a public parameterless constructor.
    /// </remarks>
    public Type[]? Converters { get; set; }
}
