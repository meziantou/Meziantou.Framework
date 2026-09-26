namespace Tomlyn.Parsing;

/// <summary>
/// Specifies how <see cref="TomlParser"/> handles syntax errors.
/// </summary>
public enum TomlParserMode
{
    /// <summary>
    /// Stops at the first error and throws a <see cref="TomlException"/>.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Records diagnostics and terminates the event stream without throwing.
    /// </summary>
    Tolerant = 1,
}

