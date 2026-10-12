namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Applies the lookups of a GSUB or GPOS table to a buffer of glyphs: OpenType specification, "GSUB - Glyph
/// Substitution Table", "GPOS - Glyph Positioning Table" and "OpenType Layout Common Table Formats".
/// </summary>
/// <remarks>
/// <para>
/// <b>What a lookup sees.</b> A lookup walks the glyphs it does not skip: its flag names classes of glyphs to ignore
/// (bases, ligatures, marks, or the marks outside an attachment class or a set of the GDEF table). The glyphs of
/// characters that have no visible form of their own (joiners, variation selectors, format characters) are seen
/// through when they are not what a sequence asks for, so that they do not come between the glyphs of a sequence;
/// the zero width non-joiner still keeps a ligature from forming, which is what it is for.
/// </para>
/// <para>
/// <b>Ligature components.</b> When glyphs become a ligature, the marks that followed each of them must later be
/// attached to the right part of the ligature: each ligature gets an identifier and a number of components, and each
/// mark the identifier of its ligature and the component it follows.
/// </para>
/// <para>
/// <b>Bounds.</b> A font is untrusted input. Every read is checked; the nesting of contextual lookups is limited; each
/// application is charged to the budget of the buffer, and the buffer refuses to grow beyond its limit.
/// </para>
/// </remarks>
internal sealed partial class LayoutApplier
{
    /// <summary>The deepest nesting of lookups applied by contextual lookups.</summary>
    private const int MaxNesting = 16;

    /// <summary>The longest input sequence of a ligature or of a contextual rule.</summary>
    private const int MaxSequenceLength = 64;

    private const int SubstitutionExtension = 7;
    private const int SubstitutionReverseChain = 8;
    private const int PositioningExtension = 9;

    private readonly GlyphBuffer _buffer;
    private readonly FontFace _face;
    private readonly GdefTable? _gdef;
    private readonly ReadOnlyMemory<byte> _gdefData;
    private readonly bool _hasVariations;
    private LayoutTable? _table;
    private ReadOnlyMemory<byte> _data;
    private bool _isPositioning;
    private bool _isRightToLeft;

    // The lookup being applied: its flag, its mark filtering set, and the value of the feature that enables it.
    private int _flag;
    private int _markSet;
    private uint _mask;
    private ShaperFeatureFlags _featureFlags;
    private int _syllable;

    // A question about a sequence of glyphs, not the shaping of a text: the flags of the lookups are not applied and,
    // unless a rule may have no context, the context of the rules is not looked at.
    private bool _isQuery;
    private bool _ignoresContext;
    private int _nesting;

    public LayoutApplier(GlyphBuffer buffer, FontFace face)
    {
        _buffer = buffer;
        _face = face;
        _gdef = face.Gdef;
        _gdefData = face.GdefData;
        _hasVariations = face.Instance is not null && face.Gdef is { VariationStore: not 0 };
    }

    /// <summary>Starts applying the lookups of a table.</summary>
    public void Begin(LayoutTable table, ReadOnlyMemory<byte> data, bool isPositioning, bool isRightToLeft)
    {
        _table = table;
        _data = data;
        _isPositioning = isPositioning;
        _isRightToLeft = isRightToLeft;
        _nesting = 0;
    }

    /// <summary>
    /// Gives each glyph its class. The GDEF table has it; a font without glyph classes is given the obvious ones: the
    /// glyphs of nonspacing marks are marks, the others are bases.
    /// </summary>
    public void SetGlyphClasses()
    {
        var gdef = _gdefData.Span;
        foreach (ref var info in _buffer.Info)
        {
            if (_gdef is { HasGlyphClasses: true })
            {
                info.Props = (byte)_gdef.GetGlyphClass(gdef, info.Glyph);
                info.MarkAttachmentClass = (byte)_gdef.GetMarkAttachmentClass(gdef, info.Glyph);
            }
            else
            {
                info.Props = (byte)(info.Category == UnicodeCategory.NonSpacingMark && (info.Flags & GlyphInfo.DefaultIgnorable) == 0 ? GlyphInfo.MarkGlyph : GlyphInfo.BaseGlyph);
            }
        }
    }

    /// <summary>Applies a lookup to every glyph it concerns.</summary>
    /// <param name="index">The index of the lookup in the lookup list.</param>
    /// <param name="mask">
    /// The bits of the mask of a glyph that hold the value of the feature of the lookup: the lookup applies at the glyphs
    /// where they are not zero.
    /// </param>
    /// <param name="flags">How the feature of the lookup treats the zero width joiners.</param>
    public void ApplyLookup(int index, uint mask, ShaperFeatureFlags flags = ShaperFeatureFlags.None)
    {
        _featureFlags = flags;
        var data = _data.Span;
        var lookup = _table!.GetLookup(data, index);
        _flag = lookup.Flag;
        _markSet = lookup.MarkFilteringSet;
        _mask = mask;
        _syllable = -1;
        if (lookup.SubtableCount == 0)
            return;

        var isPerSyllable = flags.HasFlag(ShaperFeatureFlags.PerSyllable);

        if (!_isPositioning && GetEffectiveType(data, lookup) == SubstitutionReverseChain)
        {
            // The one lookup type that goes from the end of the text to its start, one glyph at a time.
            for (var i = _buffer.Length - 1; i >= 0; i--)
            {
                ref var info = ref _buffer.Info[i];
                if ((info.Mask & mask) != 0 && !IsSkipped(info))
                {
                    var position = i;
                    _syllable = isPerSyllable ? info.Syllable : -1;
                    ApplySubtables(lookup, ref position);
                }
            }

            return;
        }

        var current = 0;
        while (current < _buffer.Length)
        {
            ref var info = ref _buffer.Info[current];
            _syllable = isPerSyllable ? info.Syllable : -1;
            if ((info.Mask & mask) == 0 || IsSkipped(info) || !ApplySubtables(lookup, ref current))
            {
                current++;
            }
        }
    }

    /// <summary>
    /// Asks whether a substitution lookup would replace a sequence of glyphs as a whole: whether it has a rule whose
    /// input is exactly the sequence. The buffer of the applier is a scratch one: it receives the sequence.
    /// </summary>
    /// <param name="index">The index of the lookup in the lookup list.</param>
    /// <param name="glyphs">The glyphs.</param>
    /// <param name="withoutContext">
    /// <see langword="true"/> when only the rules that have no context count; <see langword="false"/> when a rule
    /// counts whatever its context is.
    /// </param>
    public bool WouldSubstitute(int index, ReadOnlySpan<int> glyphs, bool withoutContext)
    {
        var data = _data.Span;
        var lookup = _table!.GetLookup(data, index);
        if (lookup.SubtableCount == 0 || glyphs.IsEmpty)
            return false;

        _buffer.Clear();
        _buffer.StartShaping();
        foreach (var glyph in glyphs)
        {
            _buffer.Add(0, 0);
            ref var info = ref _buffer.Info[^1];
            info.Glyph = glyph;
            info.Mask = uint.MaxValue;
        }

        _flag = 0;
        _markSet = 0;
        _mask = uint.MaxValue;
        _syllable = -1;
        _featureFlags = ShaperFeatureFlags.None;
        _nesting = 0;
        _isQuery = true;
        _ignoresContext = !withoutContext;
        try
        {
            // The rule that applies at the first glyph must take every glyph of the sequence.
            var position = 0;
            return ApplySubtables(lookup, ref position) && position >= _buffer.Length;
        }
        finally
        {
            _isQuery = false;
            _ignoresContext = false;
        }
    }

    /// <summary>Tries the subtables of a lookup at a glyph, in order; the first one that applies ends the lookup there.</summary>
    /// <param name="lookup">The lookup.</param>
    /// <param name="index">The glyph; when a subtable applies, the glyph the lookup continues at.</param>
    private bool ApplySubtables(in LayoutLookup lookup, ref int index)
    {
        var data = _data.Span;
        for (var i = 0; i < lookup.SubtableCount; i++)
        {
            var start = index;
            if (Apply(lookup.Type, LayoutTable.GetSubtable(data, lookup, i), ref index))
            {
                // A lookup that applies always moves on, even when it changed nothing after its first glyph.
                index = Math.Max(index, _isPositioning ? start + 1 : start);
                return true;
            }
        }

        return false;
    }

    private bool Apply(int type, int subtable, ref int index)
    {
        _buffer.Charge(1);
        if (_isPositioning)
        {
            return type switch
            {
                1 => ApplySinglePositioning(subtable, ref index),
                2 => ApplyPairPositioning(subtable, ref index),
                3 => ApplyCursiveAttachment(subtable, ref index),
                4 => ApplyMarkToBase(subtable, ref index),
                5 => ApplyMarkToLigature(subtable, ref index),
                6 => ApplyMarkToMark(subtable, ref index),
                7 => ApplyContext(subtable, ref index),
                8 => ApplyChainedContext(subtable, ref index),
                PositioningExtension => ApplyExtension(subtable, PositioningExtension, ref index),
                _ => false,
            };
        }

        return type switch
        {
            1 => ApplySingleSubstitution(subtable, ref index),
            2 => ApplyMultipleSubstitution(subtable, ref index),
            3 => ApplyAlternateSubstitution(subtable, ref index),
            4 => ApplyLigatureSubstitution(subtable, ref index),
            5 => ApplyContext(subtable, ref index),
            6 => ApplyChainedContext(subtable, ref index),
            SubstitutionExtension => ApplyExtension(subtable, SubstitutionExtension, ref index),
            SubstitutionReverseChain => ApplyReverseChainedSubstitution(subtable, ref index),
            _ => false,
        };
    }

    /// <summary>An extension subtable holds a subtable of another type at a 32-bit offset.</summary>
    private bool ApplyExtension(int subtable, int extensionType, ref int index)
    {
        var data = _data.Span;
        var type = FontBinary.UInt16(data, subtable + 2);
        if (FontBinary.UInt16(data, subtable) != 1 || type == extensionType)
            return false;

        return Apply(type, FontBinary.AddOffset(subtable, FontBinary.Offset32(data, subtable + 4)), ref index);
    }

    /// <summary>Gets the type of the subtables of a lookup, through an extension.</summary>
    private int GetEffectiveType(ReadOnlySpan<byte> data, in LayoutLookup lookup)
    {
        var extension = _isPositioning ? PositioningExtension : SubstitutionExtension;
        return lookup.Type == extension ? FontBinary.UInt16(data, LayoutTable.GetSubtable(data, lookup, 0) + 2) : lookup.Type;
    }

    /// <summary>Applies a lookup at one glyph, for a contextual lookup.</summary>
    private void ApplyNested(int lookupIndex, int position)
    {
        if (_nesting == MaxNesting)
            throw FontErrors.Invalid("the lookups of a layout table are nested too deeply.");

        var data = _data.Span;
        var lookup = _table!.GetLookup(data, lookupIndex);

        // A lookup that goes backward cannot be applied in the middle of a forward pass.
        if (lookup.SubtableCount == 0 || (!_isPositioning && GetEffectiveType(data, lookup) == SubstitutionReverseChain))
            return;

        var (flag, markSet) = (_flag, _markSet);
        _flag = lookup.Flag;
        _markSet = lookup.MarkFilteringSet;
        _nesting++;
        try
        {
            ApplySubtables(lookup, ref position);
        }
        finally
        {
            _nesting--;
            _flag = flag;
            _markSet = markSet;
        }
    }

    /// <summary>Determines whether the lookup being applied ignores a glyph.</summary>
    private bool IsSkipped(in GlyphInfo info) => IsSkipped(info, _flag);

    private bool IsSkipped(in GlyphInfo info, int flag)
    {
        if (_isQuery)
            return false;

        // The bits of the three classes are the bits of the flags that ignore them.
        if ((info.Props & flag & (LayoutLookup.IgnoreBaseGlyphs | LayoutLookup.IgnoreLigatures | LayoutLookup.IgnoreMarks)) != 0)
            return true;

        if (info.IsMark)
        {
            if ((flag & LayoutLookup.UseMarkFilteringSet) != 0)
                return _gdef is null || !_gdef.IsInMarkGlyphSet(_gdefData.Span, _markSet, info.Glyph);

            if ((flag & LayoutLookup.MarkAttachmentTypeMask) != 0)
                return (flag >> 8) != info.MarkAttachmentClass;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a glyph that is not the one a sequence expects is passed over: the glyph of a character
    /// without visible form.
    /// </summary>
    /// <param name="info">The glyph.</param>
    /// <param name="isContext">
    /// <see langword="true"/> for the sequences of contextual lookups, which see through the zero width non-joiner;
    /// <see langword="false"/> for the components of a ligature, which it separates.
    /// </param>
    /// <remarks>
    /// The zero width joiner is passed over everywhere, unless the feature of the lookup is one in which it also
    /// separates glyphs (<see cref="ShaperFeatureFlags.ManualZeroWidthJoiner"/>): the ligatures of the scripts whose
    /// letters join, and the placement of marks. Positioning always sees through the zero width non-joiner.
    /// </remarks>
    private bool IsTransparent(in GlyphInfo info, bool isContext)
    {
        if (!info.IsIgnorable)
            return false;

        if ((info.Flags & GlyphInfo.ZeroWidthNonJoiner) != 0)
            return _isPositioning || (isContext && !_featureFlags.HasFlag(ShaperFeatureFlags.ManualZeroWidthNonJoiner));

        if ((info.Flags & GlyphInfo.ZeroWidthJoiner) != 0)
            return isContext || !_featureFlags.HasFlag(ShaperFeatureFlags.ManualZeroWidthJoiner);

        return _isPositioning || (info.Flags & GlyphInfo.Hidden) == 0;
    }

    /// <summary>Finds the next glyph the lookup does not skip, or the length of the buffer.</summary>
    private int NextGlyph(int index)
    {
        var info = _buffer.Info;
        do
        {
            index++;
        }
        while (index < info.Length && (IsSkipped(info[index]) || IsTransparent(info[index], isContext: false)));

        return Math.Min(index, info.Length);
    }

    /// <summary>Finds the previous glyph a flag does not skip, or -1.</summary>
    private int PreviousGlyph(int index, int flag)
    {
        var info = _buffer.Info;
        do
        {
            index--;
        }
        while (index >= 0 && (IsSkipped(info[index], flag) || IsTransparent(info[index], isContext: false)));

        return index;
    }

    /// <summary>
    /// Sets the glyph of an entry after a substitution, and its class: the one of the GDEF table, or the one the
    /// substitution implies for a font without glyph classes.
    /// </summary>
    private void SetGlyph(ref GlyphInfo info, int glyph, int history, int impliedClass)
    {
        info.Glyph = glyph;
        info.Flags |= GlyphInfo.Substituted;
        var kept = (info.Props & (GlyphInfo.Ligated | GlyphInfo.Multiplied | GlyphInfo.LigatureWithComponents)) | history;

        // A glyph that was one of several and is now a ligature is a ligature: what it was before is forgotten.
        if ((history & GlyphInfo.Ligated) != 0)
        {
            kept &= ~GlyphInfo.Multiplied;
        }

        if (_gdef is { HasGlyphClasses: true })
        {
            info.Props = (byte)(kept | _gdef.GetGlyphClass(_gdefData.Span, glyph));
            info.MarkAttachmentClass = (byte)_gdef.GetMarkAttachmentClass(_gdefData.Span, glyph);
        }
        else
        {
            info.Props = (byte)(kept | (impliedClass != 0 ? impliedClass : info.Props & GlyphInfo.ClassMask));
        }
    }
}
