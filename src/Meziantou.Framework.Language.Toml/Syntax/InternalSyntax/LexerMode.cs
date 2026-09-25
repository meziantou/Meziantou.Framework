namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>What the parser expects next, which decides how a run of characters is read.</summary>
/// <remarks>
/// TOML cannot be tokenized without knowing where it is: <c>1.5</c> is a float after <c>=</c> and the dotted key
/// <c>1</c>, <c>5</c> before it, and <c>true</c> is a boolean in one place and a key in the other.
/// </remarks>
internal enum LexerMode
{
    Key,
    Value,
}
