namespace Meziantou.Framework.UrlPatternInternal;

/// <summary>The percent-encode sets of the URL Standard.</summary>
/// <remarks>
/// Each set is a superset of the one before it, except that <see cref="Fragment"/> and
/// <see cref="Query"/> both extend the C0 control set in different directions.
/// <see href="https://url.spec.whatwg.org/#percent-encoded-bytes">URL Standard - Percent-encoded bytes</see>
/// </remarks>
internal enum PercentEncodeSet
{
    /// <summary>C0 controls and every code point greater than U+007E (~).</summary>
    C0Control,

    /// <summary>The C0 control set, plus U+0020 SPACE, '"', '&lt;', '&gt;' and '`'.</summary>
    Fragment,

    /// <summary>The C0 control set, plus U+0020 SPACE, '"', '#', '&lt;' and '&gt;'.</summary>
    Query,

    /// <summary>The query set, plus '\''. Used for the query of a URL with a special scheme.</summary>
    SpecialQuery,

    /// <summary>The query set, plus '?', '`', '{' and '}'.</summary>
    Path,

    /// <summary>The path set, plus '/', ':', ';', '=', '@', '[', '\\', ']', '^' and '|'.</summary>
    UserInfo,
}
