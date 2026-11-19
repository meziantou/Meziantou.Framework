namespace Meziantou.Framework.Markdown;

/// <summary>Configures how <see cref="MarkdownExtensions.AutoIdentifiers"/> makes the identifiers of the headings.</summary>
[Flags]
public enum MarkdownAutoIdentifierOptions
{
    /// <summary>The identifier keeps the letters of any script, and a reference link cannot link to a heading.</summary>
    None = 0,

    /// <summary>A reference link whose label is the text of a heading, such as <c>[Getting started]</c>, links to it, unless a link reference definition has this label.</summary>
    AutoLink = 1,

    /// <summary>The identifier only has ASCII characters: the accents are removed, and the other letters are written in ASCII when they can be (<c>ß</c> becomes <c>ss</c>) or removed.</summary>
    AllowOnlyAscii = 2,

    /// <summary>The identifier is made as GitHub makes it: the letters, the combining marks, the digits, <c>-</c> and <c>_</c> are kept, in lower case, and each space becomes <c>-</c>.</summary>
    GitHub = 4,

    /// <summary>The default options: <see cref="AutoLink"/> and <see cref="AllowOnlyAscii"/>.</summary>
    Default = AutoLink | AllowOnlyAscii,
}
