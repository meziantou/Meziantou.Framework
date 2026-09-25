namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>What the parser expects next, which decides what a run of characters is read as.</summary>
internal enum LexerMode
{
    /// <summary>The start of a line: a section header, a key, or a separator with no key in front of it.</summary>
    LineStart,

    /// <summary>After <c>[</c>: a section name, or <c>]</c>.</summary>
    SectionName,

    /// <summary>After a separator: the value, up to the end of the line or a comment.</summary>
    Value,

    /// <summary>After an entry that should have ended its line: everything up to the end of the line, as one bad token.</summary>
    RestOfLine,
}
