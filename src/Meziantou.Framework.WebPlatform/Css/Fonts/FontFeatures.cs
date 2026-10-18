using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The features of <c>font-variant-caps</c> used for a run (<see cref="FontFeatureList.WithSynthesis"/>).</summary>
internal enum FontCapsFeatures
{
    /// <summary>The features of the computed value.</summary>
    Requested,

    /// <summary>No feature: the small capitals are synthesized.</summary>
    None,

    /// <summary><c>smcp</c>: the fallback of the petite capitals and of <c>unicase</c> when the font has no glyph for them.</summary>
    SmallCaps,

    /// <summary><c>c2sc</c> and <c>smcp</c>: the fallback of <c>all-petite-caps</c>.</summary>
    AllSmallCaps,
}

/// <summary>An OpenType feature and its value (0 disables it, 1 enables it, more selects an alternate).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontFeature(uint Tag, uint Value);

/// <summary>
/// The OpenType features requested for a run, in order of precedence (a later value of a feature wins): an immutable list
/// with a structural equality (it is part of the cache keys of the shaping).
/// </summary>
internal sealed class FontFeatureList : IEquatable<FontFeatureList>
{
    private static readonly uint SmallCapsTag = ShapePlanBuilder.Tag("smcp");
    private static readonly uint CapitalsToSmallCapsTag = ShapePlanBuilder.Tag("c2sc");

    private readonly int _hashCode;
    private readonly (int Start, int Count) _caps;
    private readonly (int Start, int Count) _position;
    private FontFeatureList?[]? _synthesisVariants;

    public FontFeatureList(FontFeature[] items)
        : this(items, caps: default, position: default)
    {
    }

    /// <param name="items">The features, in order of precedence.</param>
    /// <param name="caps">The range of the features of <c>font-variant-caps</c> in the items (replaced by <see cref="WithSynthesis"/>).</param>
    /// <param name="position">The range of the features of <c>font-variant-position</c> in the items (removed by <see cref="WithSynthesis"/>).</param>
    public FontFeatureList(FontFeature[] items, (int Start, int Count) caps, (int Start, int Count) position)
    {
        Items = items;
        _caps = caps;
        _position = position;
        var hash = default(HashCode);
        foreach (var item in items)
        {
            hash.Add(item);
        }

        _hashCode = hash.ToHashCode();
    }

    /// <summary>The features, in order of precedence.</summary>
    public FontFeature[] Items { get; }

    /// <summary>
    /// Returns the features of a run whose small capitals or subscripts and superscripts are synthesized or use a fallback
    /// feature (https://drafts.csswg.org/css-fonts-4/#font-variant-caps-prop): the features of <c>font-variant-caps</c> are
    /// replaced by <paramref name="caps"/>, and the features of <c>font-variant-position</c> are removed when
    /// <paramref name="withoutPosition"/> is true. The lists are cached.
    /// </summary>
    public FontFeatureList WithSynthesis(FontCapsFeatures caps, bool withoutPosition)
    {
        if (caps == FontCapsFeatures.Requested && !withoutPosition)
            return this;

        var variants = _synthesisVariants ??= new FontFeatureList?[8];
        var index = ((int)caps * 2) + (withoutPosition ? 1 : 0);
        if (variants[index] is { } cached)
            return cached;

        var items = new List<FontFeature>(Items.Length + 2);
        for (var i = 0; i <= Items.Length; i++)
        {
            // The replacement features take the place of the features of font-variant-caps in the order of precedence
            if (caps != FontCapsFeatures.Requested && i == _caps.Start)
            {
                if (caps == FontCapsFeatures.AllSmallCaps)
                {
                    items.Add(new FontFeature(CapitalsToSmallCapsTag, 1));
                }

                if (caps is FontCapsFeatures.SmallCaps or FontCapsFeatures.AllSmallCaps)
                {
                    items.Add(new FontFeature(SmallCapsTag, 1));
                }
            }

            if (i == Items.Length)
                break;

            if ((caps != FontCapsFeatures.Requested && i >= _caps.Start && i < _caps.Start + _caps.Count)
                || (withoutPosition && i >= _position.Start && i < _position.Start + _position.Count))
            {
                continue;
            }

            items.Add(Items[i]);
        }

        // The lists are immutable: a concurrent computation creates an equivalent list
        var result = new FontFeatureList([.. items]);
        variants[index] = result;
        return result;
    }

    /// <summary>Returns a list (null for none) with a feature added after its features: it takes precedence over them.</summary>
    public static FontFeatureList With(FontFeatureList? list, FontFeature feature)
        => list is null ? new FontFeatureList([feature]) : new FontFeatureList([.. list.Items, feature], list._caps, list._position);

    /// <summary>Returns the last value of a feature in the list, or null.</summary>
    public uint? GetValue(uint tag)
    {
        uint? result = null;
        foreach (var item in Items)
        {
            if (item.Tag == tag)
            {
                result = item.Value;
            }
        }

        return result;
    }

    public bool Equals([NotNullWhen(true)] FontFeatureList? other) => other is not null && (ReferenceEquals(this, other) || (_hashCode == other._hashCode && Items.AsSpan().SequenceEqual(other.Items)));

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as FontFeatureList);

    public override int GetHashCode() => _hashCode;

    public override string ToString() => string.Join(", ", Items.Select(item => $"\"{OpenTypeTag.ToString(item.Tag)}\" {item.Value}"));

    /// <summary>
    /// Parses a value of <c>font-feature-settings</c> (https://drafts.csswg.org/css-fonts-4/#font-feature-settings-prop):
    /// <c>"tag"</c> and <c>on</c> are 1, <c>off</c> is 0; an invalid tag is ignored. Adds the features to a list.
    /// </summary>
    public static void ParseFeatureSettings(string value, List<FontFeature> features)
    {
        if (value is "normal" or "")
            return;

        foreach (var entry in Cascade.CssValueComputer.SplitArguments(CssParser.ParseListOfComponentValues(value)))
        {
            var significant = ValueMatcher.RemoveWhitespace(entry);
            if (significant is not [CssPreservedToken { Type: CssTokenType.String } tag, ..] || tag.Token.Value is not { Length: 4 } name)
                continue;

            var valid = true;
            foreach (var c in name)
            {
                valid &= c is >= (char)0x20 and <= (char)0x7E;
            }

            if (!valid)
                continue;

            uint? featureValue = significant switch
            {
                [_] => 1,
                [_, CssPreservedToken { Type: CssTokenType.Number } number] when number.Token.NumericValue >= 0 => (uint)Math.Min(number.Token.NumericValue, uint.MaxValue),
                [_, CssPreservedToken { Type: CssTokenType.Ident } keyword] when keyword.Token.Value.Equals("off", StringComparison.OrdinalIgnoreCase) => 0,
                [_, CssPreservedToken { Type: CssTokenType.Ident } keyword] when keyword.Token.Value.Equals("on", StringComparison.OrdinalIgnoreCase) => 1,
                _ => null,
            };

            if (featureValue is { } result)
            {
                features.Add(new FontFeature(((uint)name[0] << 24) | ((uint)name[1] << 16) | ((uint)name[2] << 8) | name[3], result));
            }
        }
    }
}
