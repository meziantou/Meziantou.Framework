using System;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Specifies default source generation options for a <see cref="TomlSerializerContext"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TomlSourceGenerationOptionsAttribute : TomlAttribute
{
    /// <summary>Gets or sets a value indicating whether output should be indented.</summary>
    public bool WriteIndented { get; set; }

    /// <summary>Gets or sets the number of spaces to use when indentation is enabled.</summary>
    public int IndentSize { get; set; }

    /// <summary>Gets or sets the newline kind used by the writer.</summary>
    public TomlNewLineKind NewLine { get; set; }

    /// <summary>Gets or sets the policy used to convert CLR property names.</summary>
    public TomlKnownNamingPolicy PropertyNamingPolicy { get; set; }

    /// <summary>Gets or sets the policy used to convert dictionary keys during serialization.</summary>
    public TomlKnownNamingPolicy DictionaryKeyPolicy { get; set; }

    /// <summary>Gets or sets the preferred object creation handling when deserializing object and collection members.</summary>
    public TomlObjectCreationHandling PreferredObjectCreationHandling { get; set; }

    /// <summary>Gets or sets a value indicating whether property name matching is case-insensitive.</summary>
    public bool PropertyNameCaseInsensitive { get; set; }

    /// <summary>Gets or sets a value indicating whether public fields are included during serialization and deserialization.</summary>
    public bool IncludeFields { get; set; }

    /// <summary>Gets or sets a value indicating whether read-only fields are ignored during serialization.</summary>
    public bool IgnoreReadOnlyFields { get; set; }

    /// <summary>Gets or sets a value indicating whether read-only properties are ignored during serialization.</summary>
    public bool IgnoreReadOnlyProperties { get; set; }

    /// <summary>Gets or sets the default ignore condition for null/default values.</summary>
    public TomlIgnoreCondition DefaultIgnoreCondition { get; set; }

    /// <summary>Gets or sets behavior when duplicate keys are encountered while reading.</summary>
    public TomlDuplicateKeyHandling DuplicateKeyHandling { get; set; }

    /// <summary>Gets or sets the maximum depth allowed when reading or writing nested TOML containers.</summary>
    public int MaxDepth { get; set; }

    /// <summary>Gets or sets member ordering behavior for emitted mappings.</summary>
    public TomlMappingOrderPolicy MappingOrder { get; set; }

    /// <summary>Gets or sets behavior for dictionary keys containing '.'.</summary>
    public TomlDottedKeyHandling DottedKeyHandling { get; set; }

    /// <summary>Gets or sets behavior for scalar/array roots that do not naturally map to a TOML document.</summary>
    public TomlRootValueHandling RootValueHandling { get; set; }

    /// <summary>Gets or sets the root key name used when wrapping a scalar/array root.</summary>
    public string? RootValueKeyName { get; set; }

    /// <summary>Gets or sets inline table emission policy.</summary>
    public TomlInlineTablePolicy InlineTablePolicy { get; set; }

    /// <summary>Gets or sets array-of-table emission style.</summary>
    public TomlTableArrayStyle TableArrayStyle { get; set; }

    /// <summary>
    /// Gets or sets converter types to register on the generated context.
    /// </summary>
    /// <remarks>
    /// Converter types must have a public parameterless constructor.
    /// </remarks>
    public Type[]? Converters { get; set; }
}
