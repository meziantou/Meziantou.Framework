using Meziantou.Framework.WebPlatform.Css.Syntax;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Masking (https://drafts.fxtf.org/css-masking/#the-mask)
internal static partial class CssPropertyRules
{
    /// <summary>The longhands of mask, in the order of the components of &lt;mask-layer&gt;.</summary>
    private static readonly string[] MaskLayerLonghands = ["mask-image", "mask-position", "mask-size", "mask-repeat", "mask-origin", "mask-clip", "mask-composite", "mask-mode"];

    /// <summary>
    /// A &lt;bg-size&gt; of background-size or mask-size (https://drafts.csswg.org/css-backgrounds/#background-size): a single
    /// size is completed with auto (https://github.com/w3c/csswg-drafts/issues/7802), and auto auto is auto. The negative
    /// results of math functions are clamped to 0 in the computed value.
    /// </summary>
    private static string CanonicalizeBgSizeLayer(string value, bool computed)
    {
        var parts = Cascade.CssValueComputer.SplitComponents(value);
        if (computed)
        {
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith('-', StringComparison.Ordinal) && (parts[i].EndsWith("px", StringComparison.Ordinal) || parts[i].EndsWith('%', StringComparison.Ordinal)))
                {
                    parts[i] = parts[i].EndsWith('%', StringComparison.Ordinal) ? "0%" : "0px";
                }
            }
        }

        return parts switch
        {
            ["auto", "auto"] => "auto",
            [var size] when size is not ("auto" or "cover" or "contain") => size + " auto",
            _ => string.Join(' ', parts),
        };
    }

    /// <summary>The specified value of background-size or mask-size (see <see cref="CanonicalizeBgSizeLayer"/>).</summary>
    public static string CanonicalizeBgSize(string propertyName, List<CssComponentValue> significant)
    {
        var property = CssPropertyRegistry.Get(propertyName);
        return string.Join(", ", Cascade.CssValueComputer.SplitArguments(significant).Select(layer => CanonicalizeBgSizeLayer(string.Join(' ', layer.Select(value => SerializeLengthPercentage(value, property))), computed: false)));
    }

    /// <summary>Computes background-size or mask-size from its generic computed value (see <see cref="CanonicalizeBgSizeLayer"/>).</summary>
    public static string ComputeBgSize(string value) => string.Join(", ", value.Split(", ").Select(layer => CanonicalizeBgSizeLayer(layer, computed: true)));

    /// <summary>
    /// https://drafts.fxtf.org/css-masking/#the-mask: the omitted components of a layer are set to their initial value, a
    /// single &lt;coord-box&gt; sets mask-origin and mask-clip, and mask resets mask-border.
    /// </summary>
    private static Dictionary<string, string>? ExpandMask(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (CssPropertyRegistry.GetTypeGrammar("<mask-layer>") is not { } grammar)
            return null;

        var values = MaskLayerLonghands.ToDictionary(longhand => longhand, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var layer in Cascade.CssValueComputer.SplitArguments(significant))
        {
            var captures = new List<GrammarCapture>();
            if (!ValueMatcher.MatchAll(grammar, layer, captures))
                return null;

            var layerValues = new Dictionary<string, string>(StringComparer.Ordinal);
            var boxes = new List<string>(2);
            var noClip = false;
            foreach (var capture in captures)
            {
                if (capture.Property is not null || capture.ChildIndex < 0)
                    continue;

                var captured = layer[capture.Start..capture.End];
                switch (capture.ChildIndex)
                {
                    case 0:
                        layerValues["mask-image"] = CssSerializer.SerializeValue(CssShorthands.InsertSpaces(captured), CssPropertyRegistry.Get("mask-image"));
                        break;
                    case 1:
                    {
                        // <position> [ / <bg-size> ]?
                        var slash = captured.FindIndex(value => value is CssPreservedToken token && token.Token.IsDelim('/'));
                        layerValues["mask-position"] = SerializePosition(slash < 0 ? captured : captured[..slash], computed: false);
                        if (slash >= 0)
                        {
                            layerValues["mask-size"] = CanonicalizeBgSize("mask-size", captured[(slash + 1)..]);
                        }

                        break;
                    }

                    case 2:
                        layerValues["mask-repeat"] = CssBackgrounds.CanonicalizeRepeat(captured);
                        break;
                    case 3 or 4:
                        if (IsIdent(captured[0], "no-clip"))
                        {
                            noClip = true;
                        }
                        else
                        {
                            boxes.Add(SerializeShapeComponent(captured[0], property: null));
                        }

                        break;
                    case 5:
                        layerValues["mask-composite"] = SerializeShapeComponent(captured[0], property: null);
                        break;
                    case 6:
                        layerValues["mask-mode"] = SerializeShapeComponent(captured[0], property: null);
                        break;
                }
            }

            // One box sets mask-origin and mask-clip, two boxes set them in order, and no-clip sets mask-clip
            if (boxes.Count > 0)
            {
                layerValues["mask-origin"] = boxes[0];
                layerValues["mask-clip"] = boxes[^1];
            }

            if (noClip)
            {
                layerValues["mask-clip"] = "no-clip";
            }

            foreach (var longhand in MaskLayerLonghands)
            {
                values[longhand].Add(layerValues.TryGetValue(longhand, out var value) ? value : CssShorthands.GetInitialValue(longhand) ?? "initial");
            }
        }

        var result = values.ToDictionary(entry => entry.Key, entry => string.Join(", ", entry.Value), StringComparer.Ordinal);
        foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(shorthand))
        {
            if (!result.ContainsKey(longhand) && CssShorthands.GetInitialValue(longhand) is { } initial)
            {
                result[longhand] = initial;
            }
        }

        return result;
    }

    /// <summary>
    /// Serializes mask from its longhands: the components of a layer that have their initial value are omitted (none when
    /// all of them are omitted), and a single box when mask-origin and mask-clip are equal. mask-border must be initial.
    /// </summary>
    private static string? SerializeMask(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(shorthand))
        {
            if (Array.IndexOf(MaskLayerLonghands, longhand) < 0 && (!lookup.TryGetValue(longhand, out var value) || value != CssShorthands.GetInitialValue(longhand)))
                return null;
        }

        var lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var longhand in MaskLayerLonghands)
        {
            if (!lookup.TryGetValue(longhand, out var value))
                return null;

            lists[longhand] = CssBackgrounds.SplitList(value);
        }

        var count = lists["mask-image"].Count;
        if (lists.Values.Any(list => list.Count != count))
            return null;

        var layers = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var parts = new List<string>();
            string Get(string longhand) => lists[longhand][i];
            bool IsInitial(string longhand) => Get(longhand) == CssShorthands.GetInitialValue(longhand);

            if (!IsInitial("mask-image"))
            {
                parts.Add(Get("mask-image"));
            }

            if (!IsInitial("mask-position") || !IsInitial("mask-size"))
            {
                parts.Add(IsInitial("mask-size") ? Get("mask-position") : Get("mask-position") + " / " + Get("mask-size"));
            }

            if (!IsInitial("mask-repeat"))
            {
                parts.Add(Get("mask-repeat"));
            }

            var origin = Get("mask-origin");
            var clip = Get("mask-clip");
            if (origin == clip)
            {
                if (!IsInitial("mask-origin"))
                {
                    parts.Add(origin);
                }
            }
            else if (clip == "no-clip" && IsInitial("mask-origin"))
            {
                parts.Add(clip);
            }
            else
            {
                parts.Add(origin + " " + clip);
            }

            if (!IsInitial("mask-composite"))
            {
                parts.Add(Get("mask-composite"));
            }

            if (!IsInitial("mask-mode"))
            {
                parts.Add(Get("mask-mode"));
            }

            layers.Add(parts.Count == 0 ? "none" : string.Join(' ', parts));
        }

        return string.Join(", ", layers);
    }

    /// <summary>
    /// https://drafts.fxtf.org/css-masking/#clip-property: the legacy rect() whose arguments are separated by whitespace is
    /// serialized with commas.
    /// </summary>
    private static string? CanonicalizeClip(List<CssComponentValue> significant)
    {
        if (significant is not [CssFunction function] || function.Value.Exists(value => value is CssPreservedToken { Type: CssTokenType.Comma }))
            return null;

        return "rect(" + Serialize(ValueMatcher.RemoveWhitespace(function.Value), separator: ", ") + ")";
    }
}
