namespace Meziantou.Framework.Language.Ini;

/// <summary>Where a comment can start other than at the beginning of a line.</summary>
/// <remarks>
/// A line whose first character other than whitespace is <c>;</c> or <c>#</c> is always a comment. What differs between
/// INI dialects is whether those characters also start a comment after a key, a value, or a section name.
/// </remarks>
public enum IniInlineCommentMode
{
    /// <summary>Only whole lines are comments: <c>url=http://host/#top ; note</c> has the value <c>http://host/#top ; note</c>.</summary>
    None,

    /// <summary>
    /// <c>;</c> or <c>#</c> starts a comment when whitespace precedes it: <c>url=http://host/#top ; note</c> has the value
    /// <c>http://host/#top</c>. This is what Python's <c>configparser</c> does with inline comment prefixes.
    /// </summary>
    AfterWhitespace,

    /// <summary><c>;</c> or <c>#</c> starts a comment wherever it is: <c>password=abc#123</c> has the value <c>abc</c>.</summary>
    Anywhere,
}
