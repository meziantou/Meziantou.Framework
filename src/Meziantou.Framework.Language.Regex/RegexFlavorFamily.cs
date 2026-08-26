namespace Meziantou.Framework.Language.Regex;

/// <summary>Identifies the grammar family a <see cref="RegexFlavor"/> belongs to, which selects the parser.</summary>
public enum RegexFlavorFamily
{
    /// <summary>The .NET grammar, as implemented by <c>System.Text.RegularExpressions</c>.</summary>
    Net,

    /// <summary>The ECMAScript grammar.</summary>
    JavaScript,

    /// <summary>The PCRE and Perl grammar.</summary>
    Pcre,

    /// <summary>The POSIX grammar, both basic and extended.</summary>
    Posix,
}
