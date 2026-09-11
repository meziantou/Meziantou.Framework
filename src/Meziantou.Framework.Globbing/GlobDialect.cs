namespace Meziantou.Framework.Globbing;

/// <summary>Specifies the glob pattern dialect to use when parsing a pattern.</summary>
public enum GlobDialect
{
    /// <summary>
    ///     Use the default glob pattern syntax: <c>*</c>, <c>?</c>, <c>**</c>, bracket expressions, <c>{a,b}</c> sets,
    ///     <c>\</c> escapes and a leading <c>!</c> to negate the pattern. Wildcards do not match a leading dot unless
    ///     <see cref="GlobOptions.MatchLeadingDot"/> is set.
    /// </summary>
    Standard = 0,

    /// <summary>
    ///     Use gitignore pattern syntax, as described by gitignore(5) and implemented by git's wildmatch: a pattern
    ///     without a <c>/</c> matches a name at any depth, a leading <c>/</c> anchors it, and a trailing <c>/</c>
    ///     only matches directories. Bracket expressions accept <c>^</c> as a negation, escapes and character
    ///     classes such as <c>[[:alpha:]]</c>, and braces are ordinary characters. An entry that git can never match,
    ///     such as one holding an unterminated bracket expression, is rejected.
    /// </summary>
    Git = 1,

    /// <summary>
    ///     Use MSBuild glob pattern syntax: <c>*</c>, <c>?</c> and <c>**</c>, where both <c>/</c> and <c>\</c> are
    ///     path separators and <c>%XX</c> escapes a character. A file name made of <c>*.*</c> matches every file,
    ///     including the ones without an extension, a leading <c>..</c> is kept, and a leading separator makes the
    ///     pattern absolute. Wildcards match a leading dot.
    /// </summary>
    MSBuild = 2,

    /// <summary>
    ///     Use POSIX fnmatch pattern syntax without path-separator awareness, as <c>fnmatch</c> with no flag: the
    ///     pattern matches a plain string, and <c>*</c>, <c>?</c> and bracket expressions also match a <c>/</c>.
    ///     Bracket expressions support <c>^</c> as a negation, escapes, character classes such as <c>[[:alpha:]]</c>,
    ///     equivalence classes such as <c>[[=a=]]</c> and collating symbols such as <c>[[.-.]]</c>, and a <c>[</c>
    ///     that does not open a complete bracket expression is an ordinary character.
    /// </summary>
    Posix = 3,

    /// <summary>
    ///     Use POSIX fnmatch pattern syntax where ordinary wildcards do not cross path separators, as <c>fnmatch</c>
    ///     with <c>FNM_PATHNAME</c>: a <c>/</c> is only matched by a <c>/</c> of the pattern, and every <c>/</c> is
    ///     significant, including a leading one. The syntax is the one of <see cref="Posix"/>.
    /// </summary>
    PosixPath = 4,
}
