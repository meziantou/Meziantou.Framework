using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The expansion and serialization of the background shorthands (https://drafts.csswg.org/css-backgrounds/#background),
/// whose values are lists of layers.
/// </summary>
internal static class CssBackgrounds
{
    /// <summary>The longhands of background, in the order of the components of &lt;final-bg-layer&gt;.</summary>
    private static readonly string[] LayerLonghands = ["background-image", "background-position-x", "background-position-y", "background-size", "background-repeat", "background-attachment", "background-origin", "background-clip"];

    public static bool IsLayered(string shorthand) => shorthand is "background" or "background-position";

    /// <summary>Splits the values (whitespace excluded) at the top-level commas.</summary>
    private static List<List<CssComponentValue>> SplitLayers(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add([]);
            }
            else
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    private static string Serialize(List<CssComponentValue> values, string property)
    {
        var withSpaces = new List<CssComponentValue>(values.Count * 2);
        foreach (var value in values)
        {
            if (withSpaces.Count > 0)
            {
                withSpaces.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Whitespace, Value = " " }));
            }

            withSpaces.Add(value);
        }

        return CssSerializer.SerializeValue(withSpaces, CssPropertyRegistry.Get(property));
    }

    private static string? GetKeyword(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : null;

    /// <summary>
    /// Splits a &lt;bg-position&gt; into its horizontal and vertical components
    /// (https://drafts.csswg.org/css-backgrounds/#background-position): "top" is "center top", "top 10px left" is "left top 10px".
    /// </summary>
    public static (string X, string Y)? ParsePosition(List<CssComponentValue> values)
    {
        switch (values.Count)
        {
            case 1:
            {
                var text = Serialize(values, "background-position-x");
                return GetKeyword(values[0]) switch
                {
                    "top" or "bottom" => ("center", text),
                    _ => (text, "center"),
                };
            }

            case 2:
            {
                var first = GetKeyword(values[0]);
                var second = GetKeyword(values[1]);
                var x = Serialize([values[0]], "background-position-x");
                var y = Serialize([values[1]], "background-position-y");
                if (first is "top" or "bottom" || second is "left" or "right")
                    return (y, x);

                return (x, y);
            }

            case 3 or 4:
            {
                // Keywords, each followed by an optional offset
                var groups = new List<(string Keyword, List<CssComponentValue> Values)>();
                foreach (var value in values)
                {
                    if (GetKeyword(value) is { } keyword)
                    {
                        groups.Add((keyword, [value]));
                    }
                    else if (groups.Count > 0 && groups[^1].Values.Count == 1 && groups[^1].Keyword != "center")
                    {
                        groups[^1].Values.Add(value);
                    }
                    else
                    {
                        return null;
                    }
                }

                if (groups.Count != 2)
                    return null;

                string? x = null;
                string? y = null;
                foreach (var (keyword, groupValues) in groups)
                {
                    var isX = keyword is "left" or "right" || (keyword == "center" && x is null && groups.Exists(g => g.Keyword is "top" or "bottom"));
                    if (keyword == "center" && !isX && y is not null)
                    {
                        isX = true;
                    }

                    if (isX)
                    {
                        x = Serialize(groupValues, "background-position-x");
                    }
                    else
                    {
                        y = Serialize(groupValues, "background-position-y");
                    }
                }

                return x is null || y is null ? null : (x, y);
            }

            default:
                return null;
        }
    }

    /// <summary>The canonical &lt;repeat-style&gt; (https://drafts.csswg.org/css-backgrounds/#background-repeat): "repeat no-repeat" is "repeat-x", "space space" is "space".</summary>
    public static string CanonicalizeRepeat(List<CssComponentValue> values)
    {
        if (values.Count == 2 && GetKeyword(values[0]) is { } first && GetKeyword(values[1]) is { } second)
        {
            if (first == second)
                return first;

            if (first == "repeat" && second == "no-repeat")
                return "repeat-x";

            if (first == "no-repeat" && second == "repeat")
                return "repeat-y";

            return first + " " + second;
        }

        return Serialize(values, "background-repeat");
    }

    /// <summary>The canonical &lt;bg-clip&gt; with border-area or text (https://drafts.csswg.org/css-backgrounds-4/#background-clip): text border-area is border-area text.</summary>
    private static string CanonicalizeClip(List<CssComponentValue> values) => values.Count == 2 ? "border-area text" : Serialize(values, "background-clip");

    /// <summary>Canonicalizes the value of a longhand of the backgrounds (e.g. background-repeat: repeat repeat is repeat).</summary>
    public static string? CanonicalizeLonghand(string property, List<CssComponentValue> significant)
    {
        if (property is not ("background-repeat" or "mask-repeat"))
            return null;

        return string.Join(", ", SplitLayers(significant).Select(CanonicalizeRepeat));
    }

    /// <summary>Expands background-position or background into their longhands; returns null when it cannot be expanded.</summary>
    public static List<CssLonghandValue>? Expand(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var layers = SplitLayers(significant);
        if (shorthand.Name == "background-position")
        {
            var xs = new List<string>();
            var ys = new List<string>();
            foreach (var layer in layers)
            {
                if (ParsePosition(layer) is not { } position)
                    return null;

                xs.Add(position.X);
                ys.Add(position.Y);
            }

            return [new CssLonghandValue("background-position-x", string.Join(", ", xs)), new CssLonghandValue("background-position-y", string.Join(", ", ys))];
        }

        var values = LayerLonghands.ToDictionary(l => l, _ => new List<string>(), StringComparer.Ordinal);
        var color = CssShorthands.GetInitialValue("background-color") ?? "transparent";
        for (var i = 0; i < layers.Count; i++)
        {
            var isFinal = i == layers.Count - 1;
            var grammar = CssPropertyRegistry.GetTypeGrammar(isFinal ? "<final-bg-layer>" : "<bg-layer>");
            var captures = new List<GrammarCapture>();
            if (grammar is null || !ValueMatcher.MatchAll(grammar, layers[i], captures))
                return null;

            var layer = new Dictionary<string, string>(StringComparer.Ordinal);
            var boxes = new List<string>();
            string? clip = null;
            foreach (var capture in captures)
            {
                if (capture.Property is not null || capture.ChildIndex < 0)
                    continue;

                var captured = layers[i][capture.Start..capture.End];
                switch (capture.ChildIndex)
                {
                    case 0:
                        layer["background-image"] = Serialize(captured, "background-image");
                        break;
                    case 1:
                    {
                        // <bg-position> [ / <bg-size> ]?
                        var slash = captured.FindIndex(v => v is CssPreservedToken token && token.Token.IsDelim('/'));
                        var positionValues = slash < 0 ? captured : captured[..slash];
                        if (ParsePosition(positionValues) is not { } position)
                            return null;

                        layer["background-position-x"] = position.X;
                        layer["background-position-y"] = position.Y;
                        if (slash >= 0)
                        {
                            layer["background-size"] = CssPropertyRules.CanonicalizeBgSize("background-size", captured[(slash + 1)..]);
                        }

                        break;
                    }

                    case 2:
                        layer["background-repeat"] = CanonicalizeRepeat(captured);
                        break;
                    case 3:
                        layer["background-attachment"] = Serialize(captured, "background-attachment");
                        break;
                    case 4 or 5:
                        if (captured.Exists(v => GetKeyword(v) is "border-area" or "text"))
                        {
                            clip = CanonicalizeClip(captured);
                        }
                        else
                        {
                            boxes.Add(Serialize(captured, "background-origin"));
                        }

                        break;
                    case 6:
                        color = Serialize(captured, "background-color");
                        break;
                }
            }

            // One <visual-box> sets both background-origin and background-clip, but a value only valid in background-clip
            // (border-area, text) sets background-origin to border-box
            if (clip is not null)
            {
                layer["background-origin"] = boxes.Count > 0 ? boxes[0] : "border-box";
                layer["background-clip"] = clip;
            }
            else if (boxes.Count > 0)
            {
                layer["background-origin"] = boxes[0];
                layer["background-clip"] = boxes.Count > 1 ? boxes[1] : boxes[0];
            }

            foreach (var longhand in LayerLonghands)
            {
                values[longhand].Add(layer.TryGetValue(longhand, out var value) ? value : GetInitial(longhand));
            }
        }

        var result = new List<CssLonghandValue>();
        foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(shorthand))
        {
            if (longhand == "background-color")
            {
                result.Add(new CssLonghandValue(longhand, color));
            }
            else if (values.TryGetValue(longhand, out var layerValues))
            {
                result.Add(new CssLonghandValue(longhand, string.Join(", ", layerValues)));
            }
            else
            {
                return null;
            }
        }

        return result;
    }

    private static string GetInitial(string longhand) => longhand switch
    {
        "background-position-x" or "background-position-y" => "0%",
        _ => CssShorthands.GetInitialValue(longhand) ?? "initial",
    };

    /// <summary>Serializes background-position or background from the values of their longhands, or returns null.</summary>
    public static string? Serialize(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (shorthand.Name == "background-position")
        {
            var xs = SplitList(lookup["background-position-x"]);
            var ys = SplitList(lookup["background-position-y"]);
            if (xs.Count != ys.Count)
                return null;

            return string.Join(", ", xs.Zip(ys, (x, y) => x + " " + y));
        }

        var lists = LayerLonghands.ToDictionary(l => l, l => SplitList(lookup[l]), StringComparer.Ordinal);
        var count = lists["background-image"].Count;
        if (lists.Values.Any(l => l.Count != count))
            return null;

        var layers = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var parts = new List<string>();
            string Get(string longhand) => lists[longhand][i];
            bool IsInitial(string longhand) => Get(longhand) == GetInitial(longhand);

            if (!IsInitial("background-image"))
            {
                parts.Add(Get("background-image"));
            }

            if (!IsInitial("background-position-x") || !IsInitial("background-position-y") || !IsInitial("background-size"))
            {
                var position = Get("background-position-x") + " " + Get("background-position-y");
                parts.Add(IsInitial("background-size") ? position : position + " / " + Get("background-size"));
            }

            if (!IsInitial("background-repeat"))
            {
                parts.Add(Get("background-repeat"));
            }

            if (!IsInitial("background-attachment"))
            {
                parts.Add(Get("background-attachment"));
            }

            var origin = Get("background-origin");
            var clip = Get("background-clip");
            if (!IsInitial("background-origin") || !IsInitial("background-clip"))
            {
                // A value only valid in background-clip (border-area, text) implies background-origin: border-box
                if (origin == clip || (origin == "border-box" && clip.Split(' ').Any(keyword => keyword is "border-area" or "text")))
                {
                    parts.Add(clip);
                }
                else
                {
                    parts.Add(origin + " " + clip);
                }
            }

            if (i == count - 1 && lookup["background-color"] != GetInitial("background-color"))
            {
                parts.Add(lookup["background-color"]);
            }

            layers.Add(parts.Count == 0 ? "none" : string.Join(' ', parts));
        }

        return string.Join(", ", layers);
    }

    /// <summary>Splits a serialized list at the top-level commas.</summary>
    internal static List<string> SplitList(string value)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(value[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        result.Add(value[start..].Trim());
        return result;
    }
}
