using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>How the glyphs of a sequence of a contextual lookup are matched.</summary>
internal enum GlyphMatchKind : byte
{
    /// <summary>Any glyph (the matching only checks the mask and the syllable).</summary>
    None,

    /// <summary>The value is the glyph.</summary>
    Glyph,

    /// <summary>The value is the class of the glyph in the class definition at <see cref="SkippyIterator.MatchData"/>.</summary>
    Class,

    /// <summary>The value is the offset of a coverage table from <see cref="SkippyIterator.MatchData"/>.</summary>
    Coverage,
}

/// <summary>
/// The iterator that finds the next or the previous glyph a lookup matches, skipping the glyphs that the lookup ignores
/// (HarfBuzz's <c>skipping_iterator_t</c> and <c>matcher_t</c>): the glyph classes of the lookup flags, the marks of the
/// other mark attachment classes or of the other mark filtering sets, and the default ignorable characters.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct SkippyIterator
{
    public int Index;
    public int End;
    public uint LookupProperties;
    public uint Mask;
    public bool IgnoreZwnj;
    public bool IgnoreZwj;
    public bool IgnoreHidden;
    public bool PerSyllable;
    public byte Syllable;
    public GlyphMatchKind MatchKind;

    /// <summary>The class definition or the base of the coverages of the matching (offsets in the table).</summary>
    public int MatchData;

    /// <summary>The offset in the table of the next value (a 16-bit integer) to match, or -1 when there is none.</summary>
    public int GlyphData;
}

/// <summary>The result of the matching of a glyph by a <see cref="SkippyIterator"/>.</summary>
internal enum SkippyMatch : byte
{
    Match,
    NotMatch,
    Skip,
}

/// <summary>
/// The application of the lookups of a GSUB or GPOS table to a <see cref="ShapingBuffer"/> (HarfBuzz's
/// <c>hb_ot_apply_context_t</c>, https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-layout-gsubgpos.hh, MIT license):
/// the current lookup, its mask and flags, the glyph properties of the GDEF table, the matching of the input, backtrack and
/// lookahead sequences of the contextual lookups, the ligation of the glyphs and the recursion into the nested lookups.
/// </summary>
internal sealed partial class OpenTypeApplyContext
{
    /// <summary>The maximum nesting of the contextual lookups.</summary>
    public const int MaxNestingLevel = 64;

    /// <summary>The maximum length of the sequences of a contextual lookup or a ligature.</summary>
    public const int MaxContextLength = 64;

    private int[] _matchPositions = new int[8];

    public OpenTypeApplyContext(OpenTypeLayoutTable table, OpenTypeGdef? gdef, ShapingBuffer buffer, FontVariationInstance? instance)
    {
        Buffer = buffer;
        Reset(table, gdef, instance);
    }

    /// <summary>Starts the application of the lookups of a table to the buffer (the contexts are reused by the buffer).</summary>
    [MemberNotNull(nameof(Table))]
    public void Reset(OpenTypeLayoutTable table, OpenTypeGdef? gdef, FontVariationInstance? instance)
    {
        Table = table;
        IsPositioning = !table.IsSubstitution;
        Gdef = gdef;
        HasGlyphClasses = gdef is { HasGlyphClasses: true };
        Instance = instance;
        Direction = Buffer.Direction;
        LookupMask = 1;
        LookupIndex = -1;
        LookupProperties = 0;
        NestingLevelLeft = MaxNestingLevel;
        AutoZwnj = true;
        AutoZwj = true;
        PerSyllable = false;
        Random = false;
        NewSyllables = -1;
        LastBase = -1;
        LastBaseUntil = 0;
        InitIterators();
    }

    public OpenTypeLayoutTable Table { get; private set; }

    /// <summary>Whether the table is GPOS.</summary>
    public bool IsPositioning { get; private set; }

    public OpenTypeGdef? Gdef { get; private set; }

    public bool HasGlyphClasses { get; private set; }

    public ShapingBuffer Buffer { get; }

    /// <summary>The instance of the variable font (for the device tables), or null.</summary>
    public FontVariationInstance? Instance { get; private set; }

    public ShapingDirection Direction { get; private set; }

    public uint LookupMask { get; private set; } = 1;

    public int LookupIndex { get; set; } = -1;

    public uint LookupProperties { get; private set; }

    public int NestingLevelLeft { get; set; } = MaxNestingLevel;

    public bool AutoZwnj { get; private set; } = true;

    public bool AutoZwj { get; private set; } = true;

    public bool PerSyllable { get; private set; }

    public bool Random { get; set; }

    /// <summary>The syllable given to the substituted glyphs, or -1 to keep theirs (used by the syllabic shapers).</summary>
    public int NewSyllables { get; set; } = -1;

    /// <summary>The last base glyph found by the mark attachments (a cache of the backward search), or -1.</summary>
    public int LastBase { get; set; } = -1;

    /// <summary>The position up to which <see cref="LastBase"/> was searched.</summary>
    public int LastBaseUntil { get; set; }

    /// <summary>The iterator of the input sequence.</summary>
    public SkippyIterator IterInput;

    /// <summary>The iterator of the backtrack and lookahead sequences.</summary>
    public SkippyIterator IterContext;

    /// <summary>The positions of the matched glyphs of the input sequence.</summary>
    public Span<int> MatchPositions => _matchPositions;

    public void SetLookupMask(uint mask, bool init = true)
    {
        LookupMask = mask;
        LastBase = -1;
        LastBaseUntil = 0;
        if (init)
        {
            InitIterators();
        }
    }

    public void SetAutoZwj(bool value, bool init = true)
    {
        AutoZwj = value;
        if (init)
        {
            InitIterators();
        }
    }

    public void SetAutoZwnj(bool value, bool init = true)
    {
        AutoZwnj = value;
        if (init)
        {
            InitIterators();
        }
    }

    public void SetPerSyllable(bool value, bool init = true)
    {
        PerSyllable = value;
        if (init)
        {
            InitIterators();
        }
    }

    /// <summary>Sets the properties of the current lookup (the mark filtering sets that the GDEF table does not have are ignored).</summary>
    public void SetLookupProperties(uint properties)
    {
        if ((properties & (uint)LookupFlags.UseMarkFilteringSet) != 0 && (Gdef is null || (properties >> 16) >= Gdef.MarkGlyphSetCount))
        {
            properties &= ~(uint)LookupFlags.UseMarkFilteringSet;
        }

        LookupProperties = properties;
        InitIterators();
    }

    private void InitIterators()
    {
        InitIterator(ref IterInput, contextMatch: false);
        InitIterator(ref IterContext, contextMatch: true);
    }

    private void InitIterator(ref SkippyIterator iterator, bool contextMatch)
    {
        iterator.End = Buffer.Length;
        iterator.GlyphData = -1;
        iterator.MatchKind = GlyphMatchKind.None;
        iterator.MatchData = 0;
        iterator.LookupProperties = LookupProperties;

        // Ignore ZWNJ when matching GPOS, or the context of GSUB when asked to; ignore ZWJ when matching the context, or when asked to
        iterator.IgnoreZwnj = IsPositioning || (contextMatch && AutoZwnj);
        iterator.IgnoreZwj = contextMatch || AutoZwj;

        // The hidden glyphs (e.g. CGJ) are ignored by GPOS
        iterator.IgnoreHidden = IsPositioning;
        iterator.Mask = contextMatch ? uint.MaxValue : LookupMask;
        iterator.PerSyllable = !IsPositioning && PerSyllable;
        iterator.Syllable = 0;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Glyph properties
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether a glyph is not skipped by the properties of a lookup (the classes of the lookup flags, the mark filtering).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CheckGlyphProperty(in GlyphInfo info, uint matchProperties)
    {
        var glyphProperties = (uint)info.GlyphProperties;

        // e.g. the glyph is a ligature and the lookup ignores the ligatures
        if ((glyphProperties & matchProperties & (uint)LookupFlags.IgnoreFlags) != 0)
            return false;

        if ((glyphProperties & (uint)GlyphProperties.Mark) != 0)
            return MatchMarkProperties(info, glyphProperties, matchProperties);

        return true;
    }

    private bool MatchMarkProperties(in GlyphInfo info, uint glyphProperties, uint matchProperties)
    {
        // The index of the mark filtering set is in the high 16 bits
        if ((matchProperties & (uint)LookupFlags.UseMarkFilteringSet) != 0)
            return Gdef is not null && Gdef.MarkSetCovers((int)(matchProperties >> 16), info.Codepoint);

        // The marks of another attachment type are ignored
        if ((matchProperties & (uint)LookupFlags.MarkAttachmentType) != 0)
            return (matchProperties & (uint)LookupFlags.MarkAttachmentType) == (glyphProperties & (uint)LookupFlags.MarkAttachmentType);

        return true;
    }

    /// <summary>Updates the properties of the current glyph after a substitution (HarfBuzz's <c>_set_glyph_class</c>).</summary>
    private void SetGlyphClass(int glyph, GlyphProperties classGuess = GlyphProperties.None, bool ligature = false, bool component = false)
    {
        Buffer.Digest.Add(glyph);
        ref var current = ref Buffer.Current;
        if (NewSyllables >= 0)
        {
            current.Syllable = (byte)NewSyllables;
        }

        var properties = current.GlyphProperties | GlyphProperties.Substituted;
        if (ligature)
        {
            // Only the last ligation or multiplication matters (as Uniscribe)
            properties |= GlyphProperties.Ligated;
            properties &= ~GlyphProperties.Multiplied;
        }

        if (component)
        {
            properties |= GlyphProperties.Multiplied;
        }

        if (HasGlyphClasses)
        {
            current.GlyphProperties = (properties & GlyphProperties.Preserve) | Gdef!.GetGlyphProperties(glyph);
        }
        else if (classGuess != GlyphProperties.None)
        {
            current.GlyphProperties = (properties & GlyphProperties.Preserve) | classGuess;
        }
        else
        {
            current.GlyphProperties = properties;
        }
    }

    /// <summary>Replaces the current glyph in the output.</summary>
    public void ReplaceGlyph(int glyph)
    {
        SetGlyphClass(glyph);
        Buffer.ReplaceGlyph(glyph);
    }

    /// <summary>Replaces the current glyph in place (the reverse lookups and GPOS do not write to the output).</summary>
    public void ReplaceGlyphInPlace(int glyph)
    {
        SetGlyphClass(glyph);
        Buffer.Current.Codepoint = glyph;
    }

    public void ReplaceGlyphWithLigature(int glyph, GlyphProperties classGuess)
    {
        SetGlyphClass(glyph, classGuess, ligature: true);
        Buffer.ReplaceGlyph(glyph);
    }

    public void OutputGlyphForComponent(int glyph, GlyphProperties classGuess)
    {
        SetGlyphClass(glyph, classGuess, component: true);
        Buffer.OutputGlyph(glyph);
    }

    /// <summary>The next random number of the <c>rand</c> feature.</summary>
    public uint NextRandom() => Buffer.NextRandom();

    // ----------------------------------------------------------------------------------------------------------------
    // Skipping iterators
    // ----------------------------------------------------------------------------------------------------------------

    public void SetMatch(ref SkippyIterator iterator, GlyphMatchKind kind, int matchData, int glyphData)
    {
        iterator.MatchKind = kind;
        iterator.MatchData = matchData;
        iterator.GlyphData = glyphData;
    }

    /// <summary>Starts a forward iteration (GSUB) from a position.</summary>
    public void Reset(ref SkippyIterator iterator, int start)
    {
        iterator.Index = start;
        iterator.End = Buffer.Length;
        iterator.Syllable = Buffer.Current.Syllable;
    }

    /// <summary>Starts a backward iteration from an output position.</summary>
    public void ResetBack(ref SkippyIterator iterator, int start)
    {
        iterator.Index = start;
        iterator.Syllable = Buffer.Current.Syllable;
    }

    /// <summary>Starts an iteration without changing the end and the syllable (GPOS).</summary>
    public static void ResetFast(ref SkippyIterator iterator, int start) => iterator.Index = start;

    /// <summary>Whether a glyph is skipped: SkipYes, SkipMaybe (a default ignorable that a match can still take), SkipNo (HarfBuzz's <c>may_skip</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int MaySkip(in SkippyIterator iterator, in GlyphInfo info)
    {
        if (!CheckGlyphProperty(info, iterator.LookupProperties))
            return SkipYes;

        if (info.IsDefaultIgnorable && (iterator.IgnoreZwnj || !info.IsZwnj) && (iterator.IgnoreZwj || !info.IsZwj) && (iterator.IgnoreHidden || !info.IsHidden))
            return SkipMaybe;

        return SkipNo;
    }

    public const int SkipNo = 0;
    public const int SkipYes = 1;
    public const int SkipMaybe = 2;

    private const int MatchNo = 0;
    private const int MatchYes = 1;
    private const int MatchMaybe = 2;

    private int MayMatch(in SkippyIterator iterator, in GlyphInfo info)
    {
        if ((info.Mask & iterator.Mask) == 0 || (iterator.PerSyllable && iterator.Syllable != 0 && iterator.Syllable != info.Syllable))
            return MatchNo;

        if (iterator.MatchKind == GlyphMatchKind.None)
            return MatchMaybe;

        var table = Table.Data;
        var value = iterator.GlyphData >= 0 ? FontBinary.U16(table, iterator.GlyphData) : 0;
        var matched = iterator.MatchKind switch
        {
            GlyphMatchKind.Glyph => info.Codepoint == value,
            GlyphMatchKind.Class => Table.GetCachedClass(table, iterator.MatchData, info.Codepoint) == value,
            _ => value != 0 && Table.GetCachedCoverageIndex(table, iterator.MatchData + value, info.Codepoint) >= 0,
        };

        return matched ? MatchYes : MatchNo;
    }

    /// <summary>Matches a glyph.</summary>
    public SkippyMatch Match(in SkippyIterator iterator, in GlyphInfo info)
    {
        var skip = MaySkip(iterator, info);
        if (skip == SkipYes)
            return SkippyMatch.Skip;

        var match = MayMatch(iterator, info);
        if (match == MatchYes || (match == MatchMaybe && skip == SkipNo))
            return SkippyMatch.Match;

        if (skip == SkipNo)
            return SkippyMatch.NotMatch;

        return SkippyMatch.Skip;
    }

    /// <summary>Moves to the next glyph that matches; <paramref name="unsafeTo"/> is the end of the glyphs that were checked.</summary>
    public bool Next(ref SkippyIterator iterator, out int unsafeTo)
    {
        var info = Buffer.Info;
        var stop = iterator.End - 1;
        while (iterator.Index < stop)
        {
            iterator.Index++;
            switch (Match(iterator, info[iterator.Index]))
            {
                case SkippyMatch.Match:
                    if (iterator.GlyphData >= 0)
                    {
                        iterator.GlyphData += 2;
                    }

                    unsafeTo = 0;
                    return true;
                case SkippyMatch.NotMatch:
                    unsafeTo = iterator.Index + 1;
                    return false;
            }
        }

        unsafeTo = iterator.End;
        return false;
    }

    /// <summary>Moves to the previous glyph of the output that matches; <paramref name="unsafeFrom"/> is the start of the glyphs that were checked.</summary>
    public bool Prev(ref SkippyIterator iterator, out int unsafeFrom)
    {
        var output = Buffer.OutInfo;
        while (iterator.Index > 0)
        {
            iterator.Index--;
            switch (Match(iterator, output[iterator.Index]))
            {
                case SkippyMatch.Match:
                    if (iterator.GlyphData >= 0)
                    {
                        iterator.GlyphData += 2;
                    }

                    unsafeFrom = 0;
                    return true;
                case SkippyMatch.NotMatch:
                    unsafeFrom = Math.Max(1, iterator.Index) - 1;
                    return false;
            }
        }

        unsafeFrom = 0;
        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Sequences of the contextual lookups and of the ligatures
    // ----------------------------------------------------------------------------------------------------------------

    private void EnsureMatchPositions(int count)
    {
        if (count > _matchPositions.Length)
        {
            Array.Resize(ref _matchPositions, Math.Max(count, _matchPositions.Length * 2));
        }
    }

    /// <summary>
    /// Matches the input sequence from the current glyph (HarfBuzz's <c>match_input</c>): <paramref name="count"/> glyphs, the
    /// first one is already matched, the others are the values at <paramref name="input"/>. Fills <see cref="MatchPositions"/>;
    /// <paramref name="endPosition"/> is the end of the match (or of the glyphs checked when it fails).
    /// </summary>
    public bool MatchInput(int count, int input, GlyphMatchKind kind, int matchData, out int endPosition, out int totalComponentCount)
    {
        var buffer = Buffer;
        totalComponentCount = 0;
        if (count == 1)
        {
            endPosition = buffer.Index + 1;
            _matchPositions[0] = buffer.Index;
            totalComponentCount = buffer.Current.LigatureComponentCount;
            return true;
        }

        if (count > MaxContextLength)
        {
            endPosition = buffer.Index + 1;
            return false;
        }

        ref var iterator = ref IterInput;
        Reset(ref iterator, buffer.Index);
        SetMatch(ref iterator, kind, matchData, input);
        EnsureMatchPositions(count);

        // Ligatures cannot be formed across glyphs attached to different components of previous ligatures, except when they
        // ligate with their own marks, or when the marks belong to different components of a ligature that is skipped
        // (see the comment of match_input in HarfBuzz)
        var firstLigatureId = buffer.Current.LigatureId;
        var firstLigatureComponent = buffer.Current.LigatureComponent;
        const int NotChecked = 0, MayNotSkip = 1, MaySkipBase = 2;
        var ligatureBase = NotChecked;
        var info = buffer.Info;
        for (var i = 1; i < count; i++)
        {
            if (!Next(ref iterator, out var unsafeTo))
            {
                endPosition = unsafeTo;
                return false;
            }

            _matchPositions[i] = iterator.Index;
            var thisLigatureId = info[iterator.Index].LigatureId;
            var thisLigatureComponent = info[iterator.Index].LigatureComponent;
            if (firstLigatureId != 0 && firstLigatureComponent != 0)
            {
                // The first component was attached to a previous ligature component: all the components must be attached to
                // the same one, unless the base ligature is ignored
                if (firstLigatureId != thisLigatureId || firstLigatureComponent != thisLigatureComponent)
                {
                    if (ligatureBase == NotChecked)
                    {
                        var found = false;
                        var output = buffer.OutInfo;
                        var j = buffer.OutLength;
                        while (j > 0 && output[j - 1].LigatureId == firstLigatureId)
                        {
                            if (output[j - 1].LigatureComponent == 0)
                            {
                                j--;
                                found = true;
                                break;
                            }

                            j--;
                        }

                        ligatureBase = found && MaySkip(iterator, output[j]) == SkipYes ? MaySkipBase : MayNotSkip;
                    }

                    if (ligatureBase == MayNotSkip)
                    {
                        endPosition = iterator.Index + 1;
                        return false;
                    }
                }
            }
            else if (thisLigatureId != 0 && thisLigatureComponent != 0 && thisLigatureId != firstLigatureId)
            {
                // The first component was not attached to a ligature: the other ones must not be attached to another one
                endPosition = iterator.Index + 1;
                return false;
            }

            totalComponentCount += info[iterator.Index].LigatureComponentCountInLigation;
        }

        endPosition = iterator.Index + 1;
        totalComponentCount += buffer.Current.LigatureComponentCount;
        _matchPositions[0] = buffer.Index;
        return true;
    }

    /// <summary>
    /// Replaces the matched input sequence by a ligature glyph (HarfBuzz's <c>ligate_input</c>): the clusters are merged, the
    /// marks between the components get the ligature id and their component, a sequence of marks is a mark ligature.
    /// </summary>
    public void LigateInput(int count, int matchEnd, int ligatureGlyph, int totalComponentCount)
    {
        var buffer = Buffer;
        buffer.MergeClusters(buffer.Index, matchEnd);

        var info = buffer.Info;
        var isBaseLigature = info[_matchPositions[0]].IsBaseGlyph;
        var isMarkLigature = info[_matchPositions[0]].IsMark;
        for (var i = 1; i < count; i++)
        {
            if (!info[_matchPositions[i]].IsMark)
            {
                isBaseLigature = false;
                isMarkLigature = false;
                break;
            }
        }

        var isLigature = !isBaseLigature && !isMarkLigature;
        var classGuess = isLigature ? GlyphProperties.Ligature : GlyphProperties.None;
        var ligatureId = isLigature ? buffer.AllocateLigatureId() : 0;
        var lastLigatureId = buffer.Current.LigatureId;
        var lastComponentCount = buffer.Current.LigatureComponentCount;
        var componentsSoFar = lastComponentCount;
        if (isLigature)
        {
            buffer.Current.SetLigaturePropertiesForLigature(ligatureId, totalComponentCount);
            if (buffer.Current.GeneralCategory == UnicodeCategory.NonSpacingMark)
            {
                buffer.Current.SetGeneralCategory(UnicodeCategory.OtherLetter);
            }
        }

        ReplaceGlyphWithLigature(ligatureGlyph, classGuess);
        for (var i = 1; i < count; i++)
        {
            while (buffer.Index < _matchPositions[i] && buffer.Successful)
            {
                if (isLigature)
                {
                    var thisComponent = buffer.Current.LigatureComponent;
                    if (thisComponent == 0)
                    {
                        thisComponent = lastComponentCount;
                    }

                    var newComponent = componentsSoFar - lastComponentCount + Math.Min(thisComponent, lastComponentCount);
                    buffer.Current.SetLigaturePropertiesForMark(ligatureId, newComponent);
                }

                buffer.NextGlyph();
            }

            lastLigatureId = buffer.Current.LigatureId;
            lastComponentCount = buffer.Current.LigatureComponentCountInLigation;
            componentsSoFar += lastComponentCount;

            // Skip the base glyph
            buffer.Index++;
        }

        if (!isMarkLigature && lastLigatureId != 0)
        {
            // Re-adjust the components of the marks that follow (the buffer may have grown: read its array again)
            info = buffer.Info;
            for (var i = buffer.Index; i < buffer.Length; i++)
            {
                if (lastLigatureId != info[i].LigatureId)
                    break;

                var thisComponent = info[i].LigatureComponent;
                if (thisComponent == 0)
                    break;

                var newComponent = componentsSoFar - lastComponentCount + Math.Min(thisComponent, lastComponentCount);
                info[i].SetLigaturePropertiesForMark(ligatureId, newComponent);
            }
        }
    }

    /// <summary>Matches the backtrack sequence before the current glyph, in the output (HarfBuzz's <c>match_backtrack</c>).</summary>
    public bool MatchBacktrack(int count, int backtrack, GlyphMatchKind kind, int matchData, out int matchStart)
    {
        if (count == 0)
        {
            matchStart = Buffer.BacktrackLength;
            return true;
        }

        ref var iterator = ref IterContext;
        ResetBack(ref iterator, Buffer.BacktrackLength);
        SetMatch(ref iterator, kind, matchData, backtrack);
        for (var i = 0; i < count; i++)
        {
            if (!Prev(ref iterator, out var unsafeFrom))
            {
                matchStart = unsafeFrom;
                return false;
            }
        }

        matchStart = iterator.Index;
        return true;
    }

    /// <summary>Matches the lookahead sequence from <paramref name="startIndex"/> (HarfBuzz's <c>match_lookahead</c>).</summary>
    public bool MatchLookahead(int count, int lookahead, GlyphMatchKind kind, int matchData, int startIndex, out int endIndex)
    {
        if (count == 0)
        {
            endIndex = startIndex;
            return true;
        }

        ref var iterator = ref IterContext;
        Reset(ref iterator, startIndex - 1);
        SetMatch(ref iterator, kind, matchData, lookahead);
        for (var i = 0; i < count; i++)
        {
            if (!Next(ref iterator, out var unsafeTo))
            {
                endIndex = unsafeTo;
                return false;
            }
        }

        endIndex = iterator.Index + 1;
        return true;
    }

    /// <summary>
    /// Applies the nested lookups of a contextual lookup to the matched input sequence (HarfBuzz's <c>apply_lookup</c>):
    /// <paramref name="lookupRecords"/> is the offset of the records (sequence index, lookup index) in the table. The match
    /// positions are adjusted when a nested lookup changes the length of the buffer.
    /// </summary>
    public void ApplyLookup(int count, int lookupCount, int lookupRecords, int matchEnd)
    {
        var buffer = Buffer;
        var table = Table.Data;

        // The positions of this match are copied: the nested lookups match their own sequences (the ligatures, the nested
        // contextual lookups), which overwrite the positions of the context (HarfBuzz's match_positions of the caller)
        Span<int> matchPositions = stackalloc int[MaxContextLength + 1];
        _matchPositions.AsSpan(0, count).CopyTo(matchPositions);

        // The positions are distances from the start of the output
        var backtrackLength = buffer.BacktrackLength;
        var end = backtrackLength + matchEnd - buffer.Index;
        var delta = backtrackLength - buffer.Index;
        for (var j = 0; j < count; j++)
        {
            matchPositions[j] += delta;
        }

        for (var i = 0; i < lookupCount && buffer.Successful; i++)
        {
            var index = FontBinary.U16(table, lookupRecords + (i * 4));
            if (index >= count)
                continue;

            var originalLength = buffer.BacktrackLength + buffer.LookaheadLength;

            // This can happen if earlier recursed lookups deleted many entries
            if (matchPositions[index] >= originalLength)
                continue;

            if (!buffer.MoveTo(matchPositions[index]))
                break;

            if (buffer.MaxOps <= 0)
                break;

            if (!Recurse(FontBinary.U16(table, lookupRecords + (i * 4) + 2)))
                continue;

            var newLength = buffer.BacktrackLength + buffer.LookaheadLength;
            delta = newLength - originalLength;
            if (delta == 0)
                continue;

            // The nested lookup changed the length of the buffer: when it grew by n, n glyphs were added after the current
            // position; when it shrank by n, n match positions after the current one were removed
            end += delta;
            if (end < matchPositions[index])
            {
                // Never rewind the end before the current position
                delta += matchPositions[index] - end;
                end = matchPositions[index];
            }

            var next = index + 1;
            if (delta > 0)
            {
                if (delta + count > MaxContextLength)
                    break;
            }
            else
            {
                delta = Math.Max(delta, next - count);
                next -= delta;
            }

            // Shift
            matchPositions.Slice(next, count - next).CopyTo(matchPositions[(next + delta)..]);
            next += delta;
            count += delta;

            // Fill the new entries, and fix up the other ones
            for (var j = index + 1; j < next; j++)
            {
                matchPositions[j] = matchPositions[j - 1] + 1;
            }

            for (; next < count; next++)
            {
                matchPositions[next] += delta;
            }
        }

        buffer.MoveTo(Math.Max(0, end));
    }

    /// <summary>Applies a nested lookup to the current glyph.</summary>
    public bool Recurse(int lookupIndex)
    {
        if (NestingLevelLeft == 0)
        {
            Buffer.Successful = false;
            return false;
        }

        Buffer.MaxOps--;
        if (Buffer.MaxOps < 0)
        {
            Buffer.Successful = false;
            return false;
        }

        var lookup = Table.GetLookup(lookupIndex);
        if (lookup is null)
            return false;

        NestingLevelLeft--;
        var savedProperties = LookupProperties;
        var savedIndex = LookupIndex;
        LookupIndex = lookupIndex;
        SetLookupProperties(lookup.Properties);
        var result = lookup.Digest.MayHave(Buffer.Current.Codepoint) && ApplyLookupToCurrentGlyph(lookup);
        LookupIndex = savedIndex;
        SetLookupProperties(savedProperties);
        NestingLevelLeft++;
        return result;
    }
}
