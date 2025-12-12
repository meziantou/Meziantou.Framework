namespace Meziantou.Framework;

/// <summary>Options for creating a URLPattern.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#dictdef-urlpatternoptions">WHATWG URL Pattern Spec - URLPatternOptions</see>
/// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API#case_sensitivity">MDN - Case sensitivity</see>
/// </remarks>
public sealed class UrlPatternOptions
{
    /// <summary>Gets or sets whether the pattern matching should be case-insensitive.</summary>
    /// <remarks>
    /// If <c>true</c>, all matching operations will be case-insensitive.
    /// If <c>false</c> (default), matching is case-sensitive.
    /// </remarks>
    public bool IgnoreCase { get; set; }
}
