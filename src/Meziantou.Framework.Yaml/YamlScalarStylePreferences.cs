namespace Meziantou.Framework.Yaml;

/// <summary>Provides high-level preferences for scalar style emission.</summary>
public sealed class YamlScalarStylePreferences
{
    /// <summary>Gets or sets a value indicating whether plain style should be preferred when possible.</summary>
    public bool PreferPlainStyle { get; init; } = true;

    /// <summary>Gets or sets a value indicating whether quoted style should be preferred for ambiguous scalars.</summary>
    public bool PreferQuotedForAmbiguousScalars { get; init; } = true;

    /// <summary>Gets or sets the style used to emit string values.</summary>
    /// <remarks>
    /// <para>
    /// The default is <see cref="ScalarStyle.Any"/>, which lets the writer pick between the plain and the
    /// double-quoted style. Any other value asks for that style, for example <see cref="ScalarStyle.Literal"/> to
    /// emit a block scalar (<c>|</c>):
    /// </para>
    /// <code>
    /// script: |-
    ///   echo one
    ///   echo two
    /// </code>
    /// <para>
    /// The requested style is used only when the value round-trips through it. A literal or folded scalar cannot
    /// represent an empty value, a carriage return, a control character, or a line that ends with a blank, and no
    /// block scalar can be written inside a flow collection. When the style does not fit, the value is written
    /// using the automatic style instead.
    /// </para>
    /// <para>
    /// This applies to string values. Mapping keys and non-string scalars such as numbers, booleans, and dates keep
    /// their own representation.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="ScalarStyle"/>.</exception>
    public ScalarStyle StringStyle
    {
        get;
        init
        {
            YamlSerializerOptions.ValidateScalarStyle(value, nameof(value));
            field = value;
        }
    } = ScalarStyle.Any;
}

