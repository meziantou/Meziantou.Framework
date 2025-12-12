namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#tokenizing

/// <summary>A tokenize policy is a string that must be either "strict" or "lenient".</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#tokenizing">WHATWG URL Pattern Spec - Tokenizing</see>
/// </remarks>
internal enum TokenizePolicy
{
    /// <summary>Invalid characters cause an error.</summary>
    Strict,

    /// <summary>Invalid characters are treated as literal characters.</summary>
    Lenient,
}
