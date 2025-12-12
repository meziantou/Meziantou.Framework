namespace Meziantou.Framework;

/// <summary>Represents the match result for a single URL component.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#urlpatterncomponentresult">WHATWG URL Pattern Spec - URLPatternComponentResult</see>
/// </remarks>
public sealed class UrlPatternComponentResult
{
    /// <summary>Gets the matched input value.</summary>
    public string Input { get; }

    /// <summary>Gets a dictionary of named groups and their matched values.</summary>
    public IReadOnlyDictionary<string, string?> Groups { get; }

    internal UrlPatternComponentResult(string input, IReadOnlyDictionary<string, string?> groups)
    {
        Input = input;
        Groups = groups;
    }
}
