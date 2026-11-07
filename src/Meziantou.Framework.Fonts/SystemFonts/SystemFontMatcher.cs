namespace Meziantou.Framework.Fonts;

/// <summary>
/// The font matching of CSS Fonts Module Level 4, §5.2 "Matching font styles", for the faces of a family: the faces
/// are narrowed by width, then by style, then by weight, and the first face that remains is chosen.
/// </summary>
internal static class SystemFontMatcher
{
    private const double NormalWidth = 100;

    public static SystemFontDescriptor? Match(IReadOnlyList<SystemFontDescriptor> faces, int weight, bool isItalic)
    {
        if (faces.Count == 0)
            return null;

        var candidates = new List<SystemFontDescriptor>(faces);
        Narrow(candidates, face => WidthDistance(GetWidthRange(face)));
        Narrow(candidates, face => StyleDistance(face, isItalic));
        Narrow(candidates, face => WeightDistance(GetWeightRange(face), weight));
        return candidates[0];
    }

    /// <summary>Keeps the faces with the smallest distance, in their order.</summary>
    private static void Narrow(List<SystemFontDescriptor> candidates, Func<SystemFontDescriptor, (int Rank, double Distance)> distance)
    {
        var best = (Rank: int.MaxValue, Distance: double.MaxValue);
        foreach (var candidate in candidates)
        {
            var value = distance(candidate);
            if (value.Rank < best.Rank || (value.Rank == best.Rank && value.Distance < best.Distance))
            {
                best = value;
            }
        }

        candidates.RemoveAll(candidate => distance(candidate) != best);
    }

    /// <summary>The width is normal: a face of that width, else the narrower widths from the closest, else the wider ones.</summary>
    private static (int Rank, double Distance) WidthDistance((double Min, double Max) range)
    {
        if (range.Min <= NormalWidth && NormalWidth <= range.Max)
            return (0, 0);

        return range.Max < NormalWidth ? (1, NormalWidth - range.Max) : (2, range.Min - NormalWidth);
    }

    /// <summary>Italic: italic, then oblique, then normal. Normal: normal, then oblique, then italic.</summary>
    private static (int Rank, double Distance) StyleDistance(SystemFontDescriptor face, bool isItalic)
    {
        var hasItalicAxis = face.TryGetAxis(FontTags.ItalicAxis, out var italicAxis);
        var supportsItalic = hasItalicAxis ? italicAxis.Maximum >= 65536 : face.IsItalic && !face.IsOblique;
        var supportsNormal = hasItalicAxis ? italicAxis.Minimum <= 0 : !face.IsItalic && !face.IsOblique;
        var rank = isItalic
            ? supportsItalic ? 0 : face.IsOblique ? 1 : 2
            : supportsNormal ? 0 : face.IsOblique ? 1 : 2;
        return (rank, 0);
    }

    /// <summary>
    /// A desired weight from 400 to 500: the heavier weights up to 500, then the lighter ones from the closest, then the
    /// heavier ones above 500. Below 400: the lighter weights, then the heavier ones. Above 500: the heavier weights,
    /// then the lighter ones.
    /// </summary>
    private static (int Rank, double Distance) WeightDistance((double Min, double Max) range, int weight)
    {
        if (range.Min <= weight && weight <= range.Max)
            return (0, 0);

        if (weight is >= 400 and <= 500)
        {
            if (range.Min > weight && range.Min <= 500)
                return (1, range.Min - weight);

            return range.Max < weight ? (2, weight - range.Max) : (3, range.Min - weight);
        }

        if (weight < 400)
            return range.Max < weight ? (1, weight - range.Max) : (2, range.Min - weight);

        return range.Min > weight ? (1, range.Min - weight) : (2, weight - range.Max);
    }

    private static (double Min, double Max) GetWeightRange(SystemFontDescriptor face)
    {
        if (face.TryGetAxis(FontTags.WeightAxis, out var axis))
            return (axis.Minimum / 65536.0, axis.Maximum / 65536.0);

        var weight = face.Weight is >= 1 and <= 1000 ? face.Weight : 400;
        return (weight, weight);
    }

    private static (double Min, double Max) GetWidthRange(SystemFontDescriptor face)
    {
        if (face.TryGetAxis(FontTags.WidthAxis, out var axis))
            return (axis.Minimum / 65536.0, axis.Maximum / 65536.0);

        // CSS Fonts Module Level 4, §2.2 "Font width": the percentages of the width classes.
        var width = face.WidthClass switch
        {
            1 => 50,
            2 => 62.5,
            3 => 75,
            4 => 87.5,
            6 => 112.5,
            7 => 125,
            8 => 150,
            9 => 200,
            _ => NormalWidth,
        };
        return (width, width);
    }
}
