namespace Meziantou.Framework.Language.Ini;

/// <summary>The family of INI syntax a document is read with.</summary>
public enum IniDialect
{
    /// <summary>The syntax the common INI dialects agree on, which the other options of <see cref="IniParseOptions"/> adjust.</summary>
    Generic,

    /// <summary>
    /// The syntax of git configuration files, such as <c>.gitconfig</c> or <c>.git/config</c>: subsections such as
    /// <c>[remote "origin"]</c>, double quotes anywhere in a value, the escapes <c>\"</c>, <c>\\</c>, <c>\n</c>, <c>\t</c>,
    /// and <c>\b</c>, and lines that end with <c>\</c> continuing on the next one.
    /// </summary>
    /// <remarks>
    /// The dialect decides how comments, quotes, escapes, and keys without a value are read, so
    /// <see cref="IniParseOptions.InlineComments"/>, <see cref="IniParseOptions.AllowQuotedValues"/>,
    /// <see cref="IniParseOptions.AllowEscapes"/>, <see cref="IniParseOptions.AllowMultilineValues"/>, and
    /// <see cref="IniParseOptions.AllowKeysWithoutValue"/> do not apply to it.
    /// </remarks>
    Git,
}
