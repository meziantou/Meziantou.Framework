namespace Meziantou.Framework.Language.Regex;

/// <summary>The options that change how a pattern is read.</summary>
/// <remarks>
/// These are the options an engine is given alongside the pattern, and the ones an inline construct such as
/// <c>(?i)</c> or a JavaScript flag letter can set. Options that only affect matching, such as
/// <c>RegexOptions.Compiled</c>, are not represented because they cannot change the syntax tree.
/// </remarks>
[Flags]
public enum RegexPatternOptions
{
    None = 0,

    /// <summary>Case-insensitive matching. Set by <c>(?i)</c> and by the JavaScript <c>i</c> flag.</summary>
    IgnoreCase = 1 << 0,

    /// <summary><c>^</c> and <c>$</c> match at line boundaries. Set by <c>(?m)</c> and by the JavaScript <c>m</c> flag.</summary>
    Multiline = 1 << 1,

    /// <summary><c>.</c> matches a line feed. Set by <c>(?s)</c>; the JavaScript spelling is <see cref="DotAll"/>.</summary>
    Singleline = 1 << 2,

    /// <summary>Only named groups capture. Set by <c>(?n)</c>.</summary>
    ExplicitCapture = 1 << 3,

    /// <summary>Whitespace and <c>#</c> comments are insignificant. Set by <c>(?x)</c>.</summary>
    IgnorePatternWhitespace = 1 << 4,

    /// <summary>ECMAScript-compatible behaviour, as <c>RegexOptions.ECMAScript</c> defines it.</summary>
    EcmaScript = 1 << 5,

    /// <summary>The JavaScript <c>u</c> flag: the pattern is read as a sequence of code points.</summary>
    Unicode = 1 << 6,

    /// <summary>The JavaScript <c>v</c> flag: <see cref="Unicode"/> plus class set operations.</summary>
    UnicodeSets = 1 << 7,

    /// <summary>The JavaScript <c>s</c> flag. The .NET spelling is <see cref="Singleline"/>.</summary>
    DotAll = 1 << 8,

    /// <summary>The JavaScript <c>g</c> flag. It does not change the syntax tree; it is kept so a literal round-trips.</summary>
    Global = 1 << 9,

    /// <summary>The JavaScript <c>y</c> flag. It does not change the syntax tree; it is kept so a literal round-trips.</summary>
    Sticky = 1 << 10,

    /// <summary>The JavaScript <c>d</c> flag. It does not change the syntax tree; it is kept so a literal round-trips.</summary>
    HasIndices = 1 << 11,
}
