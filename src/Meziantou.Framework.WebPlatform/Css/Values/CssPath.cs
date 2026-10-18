using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The path() function (https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-path), used by the d property
/// (https://svgwg.org/svg2-draft/paths.html#TheDProperty), clip-path, shape-outside and offset-path: path( &lt;'fill-rule'&gt;? , &lt;string&gt; ).
/// </summary>
internal static class CssPath
{
    /// <summary>
    /// Parses the arguments of a path() function: the fill rule (null when omitted) and the path data. The path data must be
    /// valid and not empty (the error handling of the d attribute, which keeps the segments before the first error, does not apply).
    /// </summary>
    public static bool TryParse(CssFunction function, out string? fillRule, out List<SvgPathCommand> commands)
    {
        fillRule = null;
        commands = [];
        if (!InfraAscii.EqualsIgnoreAsciiCase(function.Name, "path"))
            return false;

        CssToken pathData;
        switch (ValueMatcher.RemoveWhitespace(function.Value))
        {
            case [CssPreservedToken { Type: CssTokenType.String } data]:
                pathData = data.Token;
                break;

            case [CssPreservedToken { Type: CssTokenType.Ident } rule, CssPreservedToken { Type: CssTokenType.Comma }, CssPreservedToken { Type: CssTokenType.String } data]
                when InfraAscii.ToAsciiLowercase(rule.Token.Value) is "nonzero" or "evenodd":
                fillRule = InfraAscii.ToAsciiLowercase(rule.Token.Value);
                pathData = data.Token;
                break;

            default:
                return false;
        }

        commands = SvgPathData.Parse(pathData.Value, out var complete);
        return complete && commands.Count > 0;
    }

    /// <summary>Returns whether the value is a path() function whose path data is empty or only contains whitespace (path("")).</summary>
    public static bool IsEmpty(List<CssComponentValue> significant) =>
        significant is [CssFunction function]
        && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "path")
        && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.String } data]
        && SvgPathData.Parse(data.Token.Value, out var complete).Count == 0
        && complete;

    /// <summary>Returns whether the path() function is valid.</summary>
    public static bool IsValid(CssFunction function) => TryParse(function, out _, out _);

    /// <summary>
    /// Serializes a path() function: the path data is re-serialized (single spaces, shortest numbers, "Z" for closepath) and the
    /// default fill rule (nonzero) is omitted. When <paramref name="absolute"/> is true (the computed value), the commands are
    /// converted to absolute commands of the same type. Returns null when the function is not a valid path() function.
    /// </summary>
    public static string? Serialize(CssFunction function, bool absolute)
    {
        if (!TryParse(function, out var fillRule, out var commands))
            return null;

        return Serialize(fillRule, absolute ? SvgPathData.ToAbsolute(commands) : commands);
    }

    public static string Serialize(string? fillRule, List<SvgPathCommand> commands)
    {
        var data = CssSerializer.SerializeString(SvgPathData.Serialize(commands));
        return fillRule is "evenodd" ? "path(evenodd, " + data + ")" : "path(" + data + ")";
    }
}
