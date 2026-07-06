namespace Meziantou.Framework.Yaml;

/// <summary>Determines how numbers are handled during serialization and deserialization.</summary>
/// <remarks>
/// This mirrors <see cref="System.Text.Json.Serialization.JsonNumberHandling"/> from <c>System.Text.Json</c>.
/// Because YAML scalars are untyped, the built-in numeric converters already parse numbers from quoted
/// scalars; <see cref="AllowReadingFromString"/> therefore primarily governs reading named floating-point
/// literals from quoted strings. <see cref="WriteAsString"/> forces numbers to be emitted as quoted string
/// scalars.
/// </remarks>
[Flags]
public enum YamlNumberHandling
{
    /// <summary>Numbers are read and written using the default YAML scalar representation.</summary>
    None = 0,

    /// <summary>
    /// Numbers may be read from quoted string scalars, including named floating-point literals
    /// (<c>"NaN"</c>, <c>"Infinity"</c>, <c>"-Infinity"</c>).
    /// </summary>
    AllowReadingFromString = 1,

    /// <summary>Numbers are written as quoted string scalars.</summary>
    WriteAsString = 2,

    /// <summary>
    /// Allows reading and writing the named floating-point literals <c>"NaN"</c>, <c>"Infinity"</c>,
    /// and <c>"-Infinity"</c> as quoted strings.
    /// </summary>
    AllowNamedFloatingPointLiterals = 4,
}
