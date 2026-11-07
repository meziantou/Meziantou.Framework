namespace Meziantou.Framework.Fonts;

/// <summary>
/// The kerning of the pairs of glyphs of a plan: the positioning lookups that only adjust the first glyph of pairs (the
/// usual kerning of a font) are applied to the pairs of glyphs from a cache, filled by applying them to each pair alone;
/// the other lookups of the plan are applied as usual, in the order of the plan.
/// </summary>
/// <remarks>
/// <para>
/// A pair positioning lookup tries each glyph with the glyph that follows it, and moves on to that glyph when it adjusts
/// the first glyph only (value format 2 is empty): every pair of consecutive glyphs is tried once, whatever the other
/// pairs gave. In a run without marks and without characters that have no visible form, no glyph is skipped either
/// (the lookups that skip bases or ligatures are not cached), so the adjustment of a pair depends on its two glyphs
/// alone. Such lookups add their values to the positions and read none, so consecutive ones add up, and applying them
/// from the cache between the other lookups gives the same positions.
/// </para>
/// <para>A cache belongs to a plan, which is used by one shaping at a time.</para>
/// </remarks>
internal sealed class PairKerningCache
{
    private const int PositioningPair = 2;
    private const int PositioningExtension = 9;

    /// <summary>The pairs kept by a group of lookups; a text with more distinct pairs computes the others each time.</summary>
    private const int MaxPairs = 16384;

    private readonly Step[] _steps;
    private readonly GlyphBuffer _pair = new(FontResourceLimits.Default);
    private LayoutApplier? _applier;

    private PairKerningCache(Step[] steps)
    {
        _steps = steps;
    }

    /// <summary>
    /// Creates the cache of the positioning lookups of a plan, or returns <see langword="null"/> when none of them is a
    /// pair adjustment of the first glyph.
    /// </summary>
    public static PairKerningCache? TryCreate(FontFace face, List<TextShaper.PlannedLookup> lookups, bool isVertical)
    {
        if (isVertical || lookups.Count == 0 || face.Gpos is not { } table)
            return null;

        var data = face.GposData.Span;
        var steps = new List<Step>();
        var hasPairs = false;
        try
        {
            foreach (var planned in lookups)
            {
                if (!IsPairKerning(data, table, planned.Index))
                {
                    steps.Add(new Step(planned, Pairs: null));
                }
                else if (steps.Count > 0 && steps[^1].Pairs is { } group)
                {
                    group.Lookups.Add(planned);
                }
                else
                {
                    steps.Add(new Step(planned, new PairGroup()));
                    steps[^1].Pairs!.Lookups.Add(planned);
                    hasPairs = true;
                }
            }
        }
        catch (InvalidFontException)
        {
            // The lookups are applied one by one, which reports the malformed table.
            return null;
        }

        return hasPairs ? new PairKerningCache([.. steps]) : null;
    }

    /// <summary>
    /// Applies the positioning lookups of the plan to a run without marks nor characters without visible form, the
    /// pair adjustments from the cache.
    /// </summary>
    /// <param name="buffer">The glyphs of the run.</param>
    /// <param name="face">The font.</param>
    /// <param name="applier">The applier of the run, which <see cref="LayoutApplier.Begin"/> started on the positioning table.</param>
    /// <param name="digests">The digests of the positioning table.</param>
    /// <param name="isRightToLeft">Whether the run is laid out from right to left.</param>
    /// <returns><see langword="false"/> when the run has such glyphs, or a glyph a cached lookup does not apply at: nothing was applied.</returns>
    public bool TryApply(GlyphBuffer buffer, FontFace face, LayoutApplier applier, LookupDigests? digests, bool isRightToLeft)
    {
        var info = buffer.Info;
        var masks = uint.MaxValue;
        foreach (ref readonly var glyph in info)
        {
            if (glyph.IsMark || glyph.IsIgnorable)
                return false;

            masks &= glyph.Mask;
        }

        foreach (var step in _steps)
        {
            if (step.Pairs is { } group)
            {
                foreach (var lookup in group.Lookups)
                {
                    if ((lookup.Mask & masks) == 0)
                        return false;
                }
            }
        }

        foreach (var step in _steps)
        {
            if (step.Pairs is { } group)
            {
                ApplyPairs(group, buffer, face, digests, isRightToLeft);
            }
            else
            {
                applier.ApplyLookup(step.Lookup.Index, step.Lookup.Mask, step.Lookup.Flags & ~ShaperFeatureFlags.PerSyllable);
            }
        }

        return true;
    }

    private static bool IsPairKerning(ReadOnlySpan<byte> data, LayoutTable table, int index)
    {
        var lookup = table.GetLookup(data, index);
        if (lookup.SubtableCount == 0 || (lookup.Flag & (LayoutLookup.IgnoreBaseGlyphs | LayoutLookup.IgnoreLigatures)) != 0)
            return false;

        for (var i = 0; i < lookup.SubtableCount; i++)
        {
            var subtable = LayoutTable.GetSubtable(data, lookup, i);
            var type = lookup.Type;
            if (type == PositioningExtension)
            {
                if (FontBinary.UInt16(data, subtable) != 1)
                    return false;

                type = FontBinary.UInt16(data, subtable + 2);
                subtable = FontBinary.AddOffset(subtable, FontBinary.Offset32(data, subtable + 4));
            }

            // The format, the coverage, then the value formats of the first and of the second glyph.
            if (type != PositioningPair || FontBinary.UInt16(data, subtable) is not (1 or 2) || FontBinary.UInt16(data, subtable + 6) != 0)
                return false;
        }

        return true;
    }

    private void ApplyPairs(PairGroup group, GlyphBuffer buffer, FontFace face, LookupDigests? digests, bool isRightToLeft)
    {
        var info = buffer.Info;
        var positions = buffer.Positions;
        for (var i = 0; i + 1 < info.Length; i++)
        {
            var key = ((uint)info[i].Glyph << 16) | (uint)info[i + 1].Glyph;
            if (!group.Pairs.TryGetValue(key, out var pair))
            {
                pair = Compute(group, face, digests, info[i].Glyph, info[i + 1].Glyph, isRightToLeft);
                if (group.Pairs.Count < MaxPairs)
                {
                    group.Pairs.Add(key, pair);
                }
            }

            if (pair.Moves)
            {
                ref var position = ref positions[i];
                position.XAdvance += pair.Adjustment.XAdvance;
                position.YAdvance += pair.Adjustment.YAdvance;
                position.XOffset += pair.Adjustment.XOffset;
                position.YOffset += pair.Adjustment.YOffset;
                buffer.MarkUnsafeToBreak(i, i + 2);
            }
        }
    }

    /// <summary>Applies the lookups of a group to a pair of glyphs alone: the adjustment of the first one, and whether a lookup moved the pair.</summary>
    private (GlyphPosition Adjustment, bool Moves) Compute(PairGroup group, FontFace face, LookupDigests? digests, int first, int second, bool isRightToLeft)
    {
        var buffer = _pair;
        buffer.Clear();
        buffer.Add(first, 0);
        buffer.Add(second, 1);
        foreach (ref var glyph in buffer.Info)
        {
            glyph.Mask = uint.MaxValue;
        }

        buffer.StartShaping();
        buffer.CreatePositions();
        if (_applier is null)
        {
            _applier = new LayoutApplier(buffer, face);
        }
        else
        {
            _applier.Reset(buffer, face);
        }

        _applier.SetGlyphClasses();
        _applier.Begin(face.Gpos!, face.GposData, isPositioning: true, isRightToLeft, isVertical: false, digests);
        foreach (var lookup in group.Lookups)
        {
            _applier.ApplyLookup(lookup.Index, uint.MaxValue, lookup.Flags & ~ShaperFeatureFlags.PerSyllable);
        }

        return (buffer.Positions[0], buffer.Info[0].IsUnsafeToBreak || buffer.Info[1].IsUnsafeToBreak);
    }

    /// <summary>A lookup of the plan applied as usual, or a group of consecutive pair kerning lookups applied from the cache.</summary>
    private sealed record Step(TextShaper.PlannedLookup Lookup, PairGroup? Pairs);

    private sealed class PairGroup
    {
        public List<TextShaper.PlannedLookup> Lookups { get; } = [];

        public Dictionary<uint, (GlyphPosition Adjustment, bool Moves)> Pairs { get; } = [];
    }
}
