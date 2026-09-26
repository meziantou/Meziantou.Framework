using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Serialization.Internal;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Configures the behavior of <see cref="TomlSerializer"/> operations.
/// </summary>
public sealed record TomlSerializerOptions
{
    private static readonly TomlConverter[] EmptyConverters = [];
    private static readonly ReadOnlyCollection<TomlConverter> EmptyConvertersReadOnly = Array.AsReadOnly(EmptyConverters);

    /// <summary>Gets a default options instance.</summary>
    public static TomlSerializerOptions Default { get; } = new();

    private ReadOnlyCollection<TomlConverter> _convertersReadOnly = EmptyConvertersReadOnly;

    /// <summary>Gets the custom converters.</summary>
    /// <remarks>Converters are evaluated in order and take precedence over built-in converters.</remarks>
    /// <exception cref="ArgumentNullException">Value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A converter entry is <see langword="null"/>.</exception>
    public IReadOnlyList<TomlConverter> Converters
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

    /// <summary>Gets or sets a metadata resolver used to retrieve <see cref="TomlTypeInfo"/> instances.</summary>
    public ITomlTypeInfoResolver? TypeInfoResolver { get; init; }

    /// <summary>Gets or sets an optional name for the TOML source.</summary>
    /// <remarks>
    /// This value is used to annotate <see cref="TomlException"/> messages with a source name
    /// (for example, a file path) when reporting errors.
    /// </remarks>
    public string? SourceName { get; init; }

    /// <summary>Gets or sets the policy used to convert CLR property names.</summary>
    public TomlNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>Gets or sets the policy used to convert dictionary keys during serialization.</summary>
    public TomlNamingPolicy? DictionaryKeyPolicy { get; init; }

    /// <summary>Gets or sets the preferred object creation handling for properties and fields during deserialization.</summary>
    /// <remarks>
    /// A member-level or type-level object creation handling attribute overrides this setting.
    /// The default behavior is <see cref="TomlObjectCreationHandling.Replace"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined object creation handling.</exception>
    public TomlObjectCreationHandling PreferredObjectCreationHandling
    {
        get;
        init
        {
            if (value is not TomlObjectCreationHandling.Replace and not TomlObjectCreationHandling.Populate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Preferred object creation handling must be Replace or Populate.");
            }

            field = value;
        }
    } = TomlObjectCreationHandling.Replace;

    /// <summary>Gets or sets a value indicating whether property name matching is case-insensitive.</summary>
    public bool PropertyNameCaseInsensitive { get; init; }

    /// <summary>Gets or sets the maximum allowed nesting depth for TOML tables and arrays during serialization and deserialization.</summary>
    /// <remarks>
    /// A value of <c>0</c> uses the default limit of 64.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Value is less than 0.</exception>
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

    /// <summary>Gets or sets the default ignore condition for null/default values.</summary>
    /// <remarks>
    /// The default is <see cref="TomlIgnoreCondition.WhenWritingNull"/>, because TOML has no representation for <see langword="null"/>.
    /// </remarks>
    public TomlIgnoreCondition DefaultIgnoreCondition { get; init; } = TomlIgnoreCondition.WhenWritingNull;

    /// <summary>Gets or sets behavior when duplicate keys are encountered while reading.</summary>
    public TomlDuplicateKeyHandling DuplicateKeyHandling { get; init; } = TomlDuplicateKeyHandling.Error;

    /// <summary>Gets or sets member ordering behavior for emitted tables.</summary>
    public TomlMappingOrderPolicy MappingOrder { get; init; } = TomlMappingOrderPolicy.Declaration;

    /// <summary>Gets or sets behavior for keys containing '.'.</summary>
    public TomlDottedKeyHandling DottedKeyHandling { get; init; } = TomlDottedKeyHandling.Literal;

    /// <summary>Gets polymorphism options.</summary>
    /// <remarks>
    /// <see cref="TomlPolymorphismOptions"/> is immutable: its <see cref="TomlPolymorphismOptions.DerivedTypeMappings"/> are
    /// copied when assigned, so options that are already in use cannot change.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Value is <see langword="null"/>.</exception>
    public TomlPolymorphismOptions PolymorphismOptions
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

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

    /// <summary>Gets or sets the newline kind used by the writer.</summary>
    public TomlNewLineKind NewLine { get; init; } = TomlNewLineKind.Lf;

    /// <summary>Gets or sets behavior for scalar/array roots that do not naturally map to a TOML document.</summary>
    public TomlRootValueHandling RootValueHandling { get; init; } = TomlRootValueHandling.Error;

    /// <summary>
    /// Gets the root key name used when <see cref="RootValueHandling"/> is <see cref="TomlRootValueHandling.WrapInRootKey"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Value is not a valid key name.</exception>
    public string RootValueKeyName
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!TomlKeyValidation.IsValidKeyName(value))
            {
                throw new ArgumentException("Root value key name must be non-empty and contain valid Unicode characters.", nameof(value));
            }

            field = value;
        }
    } = "value";

    /// <summary>Gets string style preferences for serialization.</summary>
    /// <exception cref="ArgumentNullException">Value is <see langword="null"/>.</exception>
    public TomlStringStylePreferences StringStylePreferences
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    /// <summary>Gets or sets inline table emission policy.</summary>
    public TomlInlineTablePolicy InlineTablePolicy { get; init; } = TomlInlineTablePolicy.Never;

    /// <summary>Gets or sets array-of-table emission style.</summary>
    public TomlTableArrayStyle TableArrayStyle { get; init; } = TomlTableArrayStyle.Headers;

    /// <summary>Gets or sets an optional metadata store used to capture or apply TOML trivia/comment metadata.</summary>
    public ITomlMetadataStore? MetadataStore { get; init; }

    /// <summary>Gets the <see cref="TomlTypeInfo{T}"/> contract metadata resolved by this options instance.</summary>
    /// <typeparam name="T">The type to resolve metadata for.</typeparam>
    /// <returns>The metadata resolved for <typeparamref name="T"/>.</returns>
    /// <exception cref="TomlException">No metadata is available for <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode(TomlTypeInfoResolverPipeline.ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(TomlTypeInfoResolverPipeline.ReflectionBasedSerializationMessage)]
    public TomlTypeInfo<T> GetTypeInfo<T>()
    {
        var typeInfo = TomlTypeInfoResolverPipeline.Resolve(this, typeof(T));
        return AsGenericTypeInfo<T>(typeInfo);
    }

    /// <summary>Attempts to get the <see cref="TomlTypeInfo{T}"/> contract metadata resolved by this options instance.</summary>
    /// <typeparam name="T">The type to resolve metadata for.</typeparam>
    /// <param name="typeInfo">The metadata resolved for <typeparamref name="T"/>, when available.</param>
    /// <returns><see langword="true"/> when metadata is available for <typeparamref name="T"/>; otherwise <see langword="false"/>.</returns>
    [RequiresUnreferencedCode(TomlTypeInfoResolverPipeline.ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(TomlTypeInfoResolverPipeline.ReflectionBasedSerializationMessage)]
    public bool TryGetTypeInfo<T>([NotNullWhen(returnValue: true)] out TomlTypeInfo<T>? typeInfo)
    {
        var resolved = TomlTypeInfoResolverPipeline.TryResolve(this, typeof(T));
        if (resolved is null)
        {
            typeInfo = null;
            return false;
        }

        typeInfo = AsGenericTypeInfo<T>(resolved);
        return true;
    }

    private static TomlTypeInfo<T> AsGenericTypeInfo<T>(TomlTypeInfo typeInfo)
    {
        return typeInfo as TomlTypeInfo<T> ?? new DelegatingTomlTypeInfo<T>(typeInfo);
    }
}
