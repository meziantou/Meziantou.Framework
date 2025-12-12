namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#parts

/// <summary>Represents the type of a part in a pattern string.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#parts">WHATWG URL Pattern Spec - Parts</see>
/// </remarks>
internal enum PartType
{
    /// <summary>The part represents a simple fixed text string.</summary>
    FixedText,

    /// <summary>The part represents a matching group with a custom regular expression.</summary>
    Regexp,

    /// <summary>The part represents a matching group that matches code points up to the next separator code point.</summary>
    SegmentWildcard,

    /// <summary>The part represents a matching group that greedily matches all code points.</summary>
    FullWildcard,
}
