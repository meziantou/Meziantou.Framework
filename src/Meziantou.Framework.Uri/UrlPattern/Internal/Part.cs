namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#parts

/// <summary>Represents one piece of a parsed pattern string.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#parts">WHATWG URL Pattern Spec - Parts</see>
/// </remarks>
internal sealed class Part
{
    public Part(PartType type, string value, PartModifier modifier, string name = "", string prefix = "", string suffix = "")
    {
        Type = type;
        Value = value;
        Modifier = modifier;
        Name = name;
        Prefix = prefix;
        Suffix = suffix;
    }

    /// <summary>Gets the type of the part.</summary>
    public PartType Type { get; }

    /// <summary>Gets the value of the part (the regexp pattern for Regexp parts, empty for others).</summary>
    public string Value { get; }

    /// <summary>Gets the modifier for the part.</summary>
    public PartModifier Modifier { get; }

    /// <summary>Gets the name of the part (for named groups).</summary>
    public string Name { get; }

    /// <summary>Gets the prefix for the part.</summary>
    public string Prefix { get; }

    /// <summary>Gets the suffix for the part.</summary>
    public string Suffix { get; }
}
