namespace Meziantou.Framework;

/// <summary>Options for creating a URLPattern.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#dictdef-urlpatternoptions">WHATWG URL Pattern Spec - URLPatternOptions</see>
/// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API#case_sensitivity">MDN - Case sensitivity</see>
/// </remarks>
public sealed class UrlPatternOptions
{
    /// <summary>Gets or sets whether the pathname, search and hash should be matched case-insensitively.</summary>
    /// <remarks>
    /// <para>
    /// If <see langword="true"/>, the pathname, search and hash are matched case-insensitively.
    /// If <see langword="false"/> (default), they are matched case-sensitively.
    /// </para>
    /// <para>
    /// The option does not reach the protocol, username, password, hostname and port components, which the
    /// spec always compiles with the default options. In practice the protocol and hostname are still matched
    /// case-insensitively, because both the pattern and the URL are lower-cased before they are compared; the
    /// username, password and port are always case-sensitive.
    /// </para>
    /// <see href="https://urlpattern.spec.whatwg.org/#create">WHATWG URL Pattern Spec - Create</see>
    /// </remarks>
    public bool IgnoreCase { get; set; }
}
