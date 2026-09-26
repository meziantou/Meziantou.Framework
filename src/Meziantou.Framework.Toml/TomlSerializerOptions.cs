using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;
using Tomlyn.Serialization;

namespace Tomlyn;

/// <summary>
/// Configures the behavior of <see cref="TomlSerializer"/> operations.
/// </summary>
public sealed record TomlSerializerOptions
{
    private static readonly TomlConverter[] EmptyConverters = [];
    private static readonly ReadOnlyCollection<TomlConverter> EmptyConvertersReadOnly = Array.AsReadOnly(EmptyConverters);

    /// <summary>
    /// Gets a default options instance.
    /// </summary>
    public static TomlSerializerOptions Default { get; } = new();

    private ReadOnlyCollection<TomlConverter> _convertersReadOnly = EmptyConvertersReadOnly;

    /// <summary>
    /// Gets the custom converters.
    /// </summary>
    /// <remarks>
    /// Converters are evaluated in order and take precedence over built-in converters.
    /// </remarks>
    public IReadOnlyList<TomlConverter> Converters
    {
        get => _convertersReadOnly;
        init
        {
            ArgumentGuard.ThrowIfNull(value, nameof(value));
            if (value.Count == 0)
            {
                _convertersReadOnly = EmptyConvertersReadOnly;
                return;
            }

            var copy = new TomlConverter[value.Count];
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

    /// <summary>
    /// Gets or sets a metadata resolver used to retrieve <see cref="TomlTypeInfo"/> instances.
    /// </summary>
    public ITomlTypeInfoResolver? TypeInfoResolver { get; init; }

    /// <summary>
    /// Gets or sets an optional name for the TOML source.
    /// </summary>
    public string? SourceName { get; init; }

    /// <summary>
    /// Gets or sets the policy used to convert CLR property names.
    /// </summary>
    public JsonNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>
    /// Gets or sets the policy used to convert dictionary keys during serialization.
    /// </summary>
    public JsonNamingPolicy? DictionaryKeyPolicy { get; init; }

    /// <summary>
    /// Gets or sets the preferred object creation handling when deserializing object and collection members.
    /// </summary>
    public JsonObjectCreationHandling PreferredObjectCreationHandling
    {
        get;
        init
        {
            if (value is not JsonObjectCreationHandling.Replace and not JsonObjectCreationHandling.Populate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Preferred object creation handling must be Replace or Populate.");
            }

            field = value;
        }
    } = JsonObjectCreationHandling.Replace;

    /// <summary>
    /// Gets or sets a value indicating whether property name matching is case-insensitive.
    /// </summary>
    public bool PropertyNameCaseInsensitive { get; init; }

    /// <summary>
    /// Gets or sets the maximum depth allowed when reading or writing nested TOML containers.
    /// </summary>
    /// <remarks>
    /// A value of <c>0</c> uses the default maximum depth of 64, mirroring <c>System.Text.Json</c>.
    /// </remarks>
    public int MaxDepth
    {
        get;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Max depth must be greater than or equal to 0.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the default ignore condition for null/default values.
    /// </summary>
    public TomlIgnoreCondition DefaultIgnoreCondition { get; init; } = TomlIgnoreCondition.WhenWritingNull;

    /// <summary>
    /// Gets or sets behavior when duplicate keys are encountered while reading.
    /// </summary>
    public TomlDuplicateKeyHandling DuplicateKeyHandling { get; init; } = TomlDuplicateKeyHandling.Error;

    /// <summary>
    /// Gets or sets member ordering behavior for emitted mappings.
    /// </summary>
    public TomlMappingOrderPolicy MappingOrder { get; init; } = TomlMappingOrderPolicy.Declaration;

    /// <summary>
    /// Gets or sets behavior for dictionary keys containing '.'.
    /// </summary>
    public TomlDottedKeyHandling DottedKeyHandling { get; init; } = TomlDottedKeyHandling.Literal;

    /// <summary>
    /// Gets or sets polymorphism options.
    /// </summary>
    public TomlPolymorphismOptions PolymorphismOptions { get; init; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether output should be indented.
    /// </summary>
    public bool WriteIndented { get; init; } = true;

    /// <summary>
    /// Gets or sets the number of spaces to use when <see cref="WriteIndented"/> is enabled.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the newline kind used by the writer.
    /// </summary>
    public TomlNewLineKind NewLine { get; init; } = TomlNewLineKind.Lf;

    /// <summary>
    /// Gets or sets behavior for scalar/array roots that do not naturally map to a TOML document.
    /// </summary>
    public TomlRootValueHandling RootValueHandling { get; init; } = TomlRootValueHandling.Error;

    /// <summary>
    /// Gets the root key name used when <see cref="RootValueHandling"/> is <see cref="TomlRootValueHandling.WrapInRootKey"/>.
    /// </summary>
    public string RootValueKeyName
    {
        get;
        init
        {
            ArgumentGuard.ThrowIfNull(value, nameof(value));
            if (!TomlKeyValidation.IsValidKeyName(value))
            {
                throw new ArgumentException("Root value key name must be non-empty and contain valid Unicode characters.", nameof(value));
            }

            field = value;
        }
    } = "value";

    /// <summary>
    /// Gets scalar string style preferences for serialization.
    /// </summary>
    public TomlStringStylePreferences StringStylePreferences { get; init; } = new();

    /// <summary>
    /// Gets or sets inline table emission policy.
    /// </summary>
    public TomlInlineTablePolicy InlineTablePolicy { get; init; } = TomlInlineTablePolicy.Never;

    /// <summary>
    /// Gets or sets array-of-table emission style.
    /// </summary>
    public TomlTableArrayStyle TableArrayStyle { get; init; } = TomlTableArrayStyle.Headers;

    /// <summary>
    /// Gets or sets an optional metadata store used to capture or apply TOML trivia/comment metadata.
    /// </summary>
    public ITomlMetadataStore? MetadataStore { get; init; }
}
