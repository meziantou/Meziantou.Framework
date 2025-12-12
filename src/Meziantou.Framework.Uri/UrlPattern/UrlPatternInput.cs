namespace Meziantou.Framework;

/// <summary>Represents input provided to pattern matching.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#dictdef-urlpatterninput">WHATWG URL Pattern Spec - URLPatternInput</see>
/// </remarks>
public sealed class UrlPatternInput
{
    /// <summary>Gets the URL string input, if provided.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
    public string? Url { get; }

    /// <summary>Gets the URL pattern init input, if provided.</summary>
    public UrlPatternInit? Init { get; }

    internal UrlPatternInput(string url)
    {
        Url = url;
    }

    internal UrlPatternInput(UrlPatternInit init)
    {
        Init = init;
    }
}
