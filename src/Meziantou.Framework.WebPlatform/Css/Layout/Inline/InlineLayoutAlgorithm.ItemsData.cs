using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The data of the items of a formatting context that does not depend on the available inline size (as the NGInlineNodeData of
// Blink): the text after the white space processing, the items and their shaped texts, and the line break opportunities. A box
// whose intrinsic sizes are computed is usually laid out in the same pass (a table cell, a float, a flex item, a shrink-to-fit
// box): its layout reuses the data of the measure instead of collecting the items again. The data is kept for the layout pass
// (see LayoutContext.InlineItemsData): the box tree and the styles do not change during a pass.
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>Whether a margin or a padding of an inline box is a percentage: the inline sizes of the tags depend on the containing block.</summary>
    private bool _hasPercentageEdges;

    /// <summary>The data of the items of the formatting context being laid out, when it is kept for the pass.</summary>
    private InlineItemsData? _itemsData;

    /// <summary>The data of the items of a formatting context that does not depend on the available inline size.</summary>
    internal sealed class InlineItemsData
    {
        public required char[] Text { get; init; }

        public required int[] Offsets { get; init; }

        /// <summary>The items, without the fragments and the sizes of the atomic inlines and of the floats.</summary>
        public required InlineItem[] Items { get; init; }

        public required int[] WrappingCollapsedSpaces { get; init; }

        public bool HasNoWrap { get; init; }

        public bool HasClonedEdges { get; init; }

        public bool HasMandatoryBreakCharacters { get; init; }

        public bool HasAtomicsOrFloats { get; init; }

        /// <summary>The line break opportunities of the text, once they are computed (see <see cref="ComputeBreakOpportunities"/>).</summary>
        public LineBreakOpportunity[]? Opportunities { get; set; }

        /// <summary>The runs of the options of the line breaking of the text, with <see cref="Opportunities"/>.</summary>
        public LineBreakOptionsRun[] BreakRuns { get; set; } = [];

        public int LastHyphenatedWordStart { get; set; }
    }

    /// <summary>
    /// Restores the items of the formatting context collected earlier in the pass (see <see cref="StoreItemsData"/>): the atomic
    /// inlines and the floats are laid out (or measured) again.
    /// </summary>
    private bool TryRestoreItemsData()
    {
        _itemsData = null;
        if (_context.InlineItemsData is not { } cache || !cache.TryGetValue(_block, out var data))
            return false;

        var length = data.Text.Length;
        if (length > _text.Length)
        {
            _text = new char[length];
            _offsets = new int[length];
        }

        data.Text.CopyTo(_text, 0);
        data.Offsets.CopyTo(_offsets, 0);
        _length = length;
        _items.AddRange(data.Items);
        _wrappingCollapsedSpaces.AddRange(data.WrappingCollapsedSpaces);
        _hasNoWrap = data.HasNoWrap;
        _hasClonedEdges = data.HasClonedEdges;
        _hasMandatoryBreakCharacters = data.HasMandatoryBreakCharacters;
        if (data.HasAtomicsOrFloats)
        {
            foreach (ref var item in Items)
            {
                if (item.Kind != InlineItemKind.Atomic && !item.IsFloat)
                    continue;

                // (the letter spacing of the runs of atomic inlines, see ApplyAtomicLetterSpacing)
                var (fragment, minContentInlineSize, inlineSize) = LayoutAtomicOrFloat((ContainerBox)item.Box);
                var spacing = item.HasFlag(InlineItemFlags.LetterSpaced) ? item.Container.Style.Layout.Text.LetterSpacing : 0;
                item.Fragment = fragment;
                item.InlineSize = inlineSize + spacing;
                item.MinContentInlineSize = minContentInlineSize + spacing;
            }
        }

        if (data.Opportunities is { } opportunities && !AppliesTableCellImageQuirk())
        {
            if (_opportunities.Length < length)
            {
                _opportunities = new LineBreakOpportunity[Math.Max(length, _opportunities.Length * 2)];
            }

            opportunities.CopyTo(_opportunities, 0);
            _breakRuns.Clear();
            _breakRuns.AddRange(data.BreakRuns);
            _opportunitiesComputed = true;
            _lastHyphenatedWordStart = data.LastHyphenatedWordStart;
        }

        _itemsData = data;
        return true;
    }

    /// <summary>
    /// Keeps the items of the formatting context for the next layouts of the pass, when its intrinsic sizes are computed: they
    /// do not depend on the available inline size, unless an inline box has percentage edges. The bidi text, the ruby and the
    /// initial letters are not kept.
    /// </summary>
    private void StoreItemsData()
    {
        _itemsData = null;
        if (!_measuring || _needsBidi || HasRuby || _hasRubyAnnotations || _hasPercentageEdges)
            return;

        var items = new InlineItem[_items.Count];
        var hasAtomicsOrFloats = false;
        for (var i = 0; i < items.Length; i++)
        {
            var item = _items[i];
            if (item.Kind == InlineItemKind.Atomic || item.IsFloat)
            {
                if (((ContainerBox)item.Box).InitialLetter is not null)
                    return;

                item.Fragment = null;
                hasAtomicsOrFloats = true;
            }

            items[i] = item;
        }

        var data = new InlineItemsData
        {
            Text = _text.AsSpan(0, _length).ToArray(),
            Offsets = _offsets.AsSpan(0, _length).ToArray(),
            Items = items,
            WrappingCollapsedSpaces = [.. _wrappingCollapsedSpaces],
            HasNoWrap = _hasNoWrap,
            HasClonedEdges = _hasClonedEdges,
            HasMandatoryBreakCharacters = _hasMandatoryBreakCharacters,
            HasAtomicsOrFloats = hasAtomicsOrFloats,
        };

        (_context.InlineItemsData ??= new(ReferenceEqualityComparer.Instance))[_block] = data;
        _itemsData = data;
    }

    /// <summary>Keeps the line break opportunities computed for the items (see <see cref="StoreItemsData"/>).</summary>
    private void StoreOpportunities()
    {
        if (_itemsData is { Opportunities: null } data && !AppliesTableCellImageQuirk())
        {
            data.Opportunities = _opportunities.AsSpan(0, _length).ToArray();
            data.BreakRuns = [.. _breakRuns];
            data.LastHyphenatedWordStart = _lastHyphenatedWordStart;
        }
    }
}
