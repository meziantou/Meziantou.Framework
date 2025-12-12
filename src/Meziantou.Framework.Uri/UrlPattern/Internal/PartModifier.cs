namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#parts

/// <summary>Represents the modifier type for a part.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#parts">WHATWG URL Pattern Spec - Parts</see>
/// </remarks>
internal enum PartModifier
{
    /// <summary>No modifier.</summary>
    None,

    /// <summary>The part is optional (? modifier).</summary>
    Optional,

    /// <summary>The part can repeat zero or more times (* modifier).</summary>
    ZeroOrMore,

    /// <summary>The part can repeat one or more times (+ modifier).</summary>
    OneOrMore,
}
