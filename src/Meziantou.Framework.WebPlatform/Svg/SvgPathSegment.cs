namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://svgwg.org/specs/paths/#InterfaceSVGPathSegment: a command of path data (e.g. <c>{ type: "L", values: [10, 20] }</c>).
/// The browsers implement it as a dictionary: the segments returned by <see cref="SvgPathElement.GetPathData"/> are not bound
/// to the element.
/// </summary>
/// <seealso href="https://svgwg.org/specs/paths/#InterfaceSVGPathSegment"/>
[IdlInterface("SVGPathSegment", "https://svgwg.org/specs/paths/#InterfaceSVGPathSegment")]
public sealed class SvgPathSegment
{
    /// <summary>Creates a segment with an empty type and no values.</summary>
    [NotInIdl("Dictionary constructor")]
    public SvgPathSegment()
    {
    }

    /// <summary>Creates a segment with the given command letter and values.</summary>
    [NotInIdl("Dictionary constructor")]
    public SvgPathSegment(string type, IReadOnlyList<float> values)
    {
        Type = type;
        Values = values;
    }

    /// <summary>The command letter of the segment (e.g. "M", "l", "Z").</summary>
    /// <seealso href="https://svgwg.org/specs/paths/#InterfaceSVGPathSegment"/>
    [IdlMember("type", "https://svgwg.org/specs/paths/#InterfaceSVGPathSegment")]
    public string Type { get; set; } = "";

    /// <summary>The values of the command. The flags of an arc are 0 or 1.</summary>
    /// <seealso href="https://svgwg.org/specs/paths/#InterfaceSVGPathSegment"/>
    [IdlMember("values", "https://svgwg.org/specs/paths/#InterfaceSVGPathSegment")]
    public IReadOnlyList<float> Values { get; set; } = [];

    /// <inheritdoc/>
    public override string ToString() => Values.Count == 0 ? Type : Type + " " + string.Join(' ', Values.Select(Svg.SvgParsing.FormatNumber));
}

/// <summary>https://svgwg.org/specs/paths/#InterfaceSVGPathData</summary>
/// <seealso href="https://svgwg.org/specs/paths/#InterfaceSVGPathData"/>
[IdlInterface("SVGPathDataSettings", "https://svgwg.org/specs/paths/#InterfaceSVGPathData")]
public sealed class SvgPathDataSettings
{
    /// <summary>Whether the segments are converted to the absolute M, L, C and Z commands.</summary>
    /// <seealso href="https://svgwg.org/specs/paths/#InterfaceSVGPathData"/>
    [IdlMember("normalize", "https://svgwg.org/specs/paths/#InterfaceSVGPathData")]
    public bool Normalize { get; init; }
}
