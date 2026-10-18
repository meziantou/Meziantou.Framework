using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The text shaping of the layout: converts a text run (characters of one style and one bidi level) to positioned glyphs with
/// the fonts of a <see cref="FontFallbackList"/>, with the OpenType shaping engine (<see cref="OpenTypeShapingEngine"/>, a port
/// of HarfBuzz). It implements:
/// <list type="bullet">
/// <item>the clusters: a base character with its combining marks, joiners and variation selectors (the typographic character units);</item>
/// <item>the font runs: the per-character font fallback with the <c>unicode-range</c> of the font faces (https://drafts.csswg.org/css-fonts-4/#font-style-matching),
/// the combining marks staying with the font of their base when it supports them (cluster matching);</item>
/// <item>the script runs (UAX #24, with the Script_Extensions and the paired brackets): the text is shaped by runs of one
/// font, one script and one orientation;</item>
/// <item>the OpenType shaping of each run: the normalization against the font, the GSUB substitutions (ligatures,
/// contextual forms, the Arabic joining, the vertical alternates) and the GPOS positioning (kerning, mark attachment,
/// cursive attachment), or the <c>kern</c> table and the fallback mark positioning, with the features of the CSS
/// properties (<see cref="TextShapingOptions.Features"/>) and the language system of the content language;</item>
/// <item>the advances of <c>hmtx</c>/<c>HVAR</c>, or of <c>vmtx</c> for the upright glyphs of vertical text (<c>text-orientation</c>);</item>
/// <item><c>letter-spacing</c> and <c>word-spacing</c> (https://drafts.csswg.org/css-text-4/#spacing);</item>
/// <item>the synthesized small capitals and subscripts and superscripts (<c>font-synthesis-small-caps</c>, <c>font-synthesis-position</c>).</item>
/// </list>
/// </summary>
/// <remarks>
/// The results are cached per font list (<see cref="TextShapeCache"/>): the lists of a document are recreated when its fonts
/// change. The text transformations (<c>text-transform</c>, the white space processing, the tab stops) are applied by the
/// inline layout before and after the shaping; the bidi mirroring of the right-to-left runs is applied by the inline layout
/// with the bidi data too (the characters it passes are already mirrored).
/// </remarks>
internal static partial class TextShaper
{
    /// <summary>Shapes a text run, using the cache of the font list.</summary>
    public static ShapedText Shape(ReadOnlySpan<char> text, FontFallbackList fonts, in TextShapingOptions options)
    {
        var cache = fonts.ShapeCache;
        if (cache.TryGetShapedText(text, options, out var result))
            return result;

        result = ShapeUncached(text, fonts, options);
        cache.AddShapedText(text, options, result);
        return result;
    }

    /// <summary>
    /// Returns the advance of a text run along the inline axis in CSS px (the sum of the advances of its glyphs, with the
    /// kerning and the spacing), using the cache of the font list. It does not allocate the glyphs.
    /// </summary>
    public static double MeasureWidth(ReadOnlySpan<char> text, FontFallbackList fonts, in TextShapingOptions options)
    {
        if (text.IsEmpty)
            return 0;

        var cache = fonts.ShapeCache;
        if (cache.TryGetWidth(text, options, out var width))
            return width;

        var output = new GlyphWriter(text.Length);
        try
        {
            width = ShapeCore(text, fonts, options, ref output, collectRuns: false);
        }
        finally
        {
            output.Dispose();
        }

        cache.AddWidth(text, options, width);
        return width;
    }

    /// <summary>
    /// Returns the composition of a text combined upright in vertical text (<c>text-combine-upright</c>, "tate-chu-yoko",
    /// https://drafts.csswg.org/css-writing-modes-4/#text-combine-layout) from its horizontal shaping: the composition is a
    /// single unit 1em wide along the vertical line (<paramref name="emSize"/>): the glyphs are compressed horizontally when
    /// they are wider ("the UA must compress the composition to 1em", by scaling the advances), else centered. The result has the
    /// horizontal options (its glyphs are laid out horizontally across the vertical line) and <see cref="ShapedText.IsCombinedUpright"/>.
    /// </summary>
    public static ShapedText CombineUpright(ShapedText horizontal, double emSize)
    {
        var glyphs = horizontal.Glyphs.ToArray();
        if (glyphs.Length == 0)
            return horizontal;

        var width = horizontal.Width;
        var scale = width > emSize && width > 0 ? emSize / width : 1;
        var padding = emSize - (width * scale);

        // Half of the remaining space is before the first glyph and half after the last one. The glyphs are in logical order:
        // the glyph on the left of the composition, which is moved after the space on the left, is the last one in a
        // right-to-left composition
        var leftGlyph = horizontal.Options.IsRightToLeft ? glyphs.Length - 1 : 0;
        for (var i = 0; i < glyphs.Length; i++)
        {
            var glyph = glyphs[i];

            // A single cluster: the composition is not split
            glyphs[i] = glyph with
            {
                Cluster = 0,
                Advance = (glyph.Advance * scale) + (i == 0 ? padding / 2 : 0) + (i == glyphs.Length - 1 ? padding / 2 : 0),
                OffsetX = (glyph.OffsetX * scale) + (i == leftGlyph ? padding / 2 : 0),
                Flags = i == 0 ? glyph.Flags | ShapedGlyphFlags.ClusterStart : glyph.Flags & ~ShapedGlyphFlags.ClusterStart,
            };
        }

        return new ShapedText(horizontal.Length, glyphs, horizontal.Runs.ToArray(), emSize, horizontal.Options) { IsCombinedUpright = true, CombinedScale = scale };
    }

    /// <summary>Shapes a text run without the cache.</summary>
    public static ShapedText ShapeUncached(ReadOnlySpan<char> text, FontFallbackList fonts, in TextShapingOptions options)
    {
        var output = new GlyphWriter(text.Length);
        try
        {
            var width = ShapeCore(text, fonts, options, ref output, collectRuns: true);
            return new ShapedText(text.Length, output.Glyphs.ToArray(), output.Runs.ToArray(), width, options, output.Carets?.ToArray());
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <summary>The properties of a code point of the text for the segmentation into runs.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct CodePointInfo
    {
        /// <summary>The UTF-16 index of the code point.</summary>
        public int Index;

        /// <summary>The code point given to the shaping (a tab or a carriage return is a space, a control character its visible substitute).</summary>
        public int CodePoint;

        /// <summary>The UTF-16 index of the typographic character unit of the code point.</summary>
        public int Cluster;

        public MatchedFont Font;

        public bool Upright;

        public UnicodeScript Script;

        /// <summary>The synthesis of the small capitals or of the subscripts and superscripts of the code point.</summary>
        public Synthesis Synthesis;

        /// <summary>The number of code points added by the case mapping of a synthesized small capital (e.g. 1 for ß → SS).</summary>
        public byte Expansion;
    }

    private static double ShapeCore(ReadOnlySpan<char> text, FontFallbackList fonts, in TextShapingOptions options, ref GlyphWriter output, bool collectRuns)
    {
        var codePoints = ArrayPool<CodePointInfo>.Shared.Rent(text.Length);
        try
        {
            var count = Segment(text, fonts, options, codePoints);
            if (NeedsSynthesis(options))
            {
                ResolveSynthesis(codePoints.AsSpan(0, count), options);
            }

            var buffer = fonts.ShapeCache.RentBuffer();
            try
            {
                ShapeRuns(text, codePoints.AsSpan(0, count), options, buffer, ref output, collectRuns);
            }
            finally
            {
                fonts.ShapeCache.ReturnBuffer(buffer);
            }

            return ApplySpacing(text, codePoints.AsSpan(0, count), options, ref output);
        }
        finally
        {
            // The references to the fonts are cleared (there is at most one code point per UTF-16 code unit: the rest of the
            // rented array is not used)
            codePoints.AsSpan(0, text.Length).Clear();
            ArrayPool<CodePointInfo>.Shared.Return(codePoints);
        }
    }

    /// <summary>
    /// Splits the text into code points with their typographic character unit, their font (the per-cluster font fallback),
    /// their orientation and their script.
    /// </summary>
    private static int Segment(ReadOnlySpan<char> text, FontFallbackList fonts, in TextShapingOptions options, CodePointInfo[] codePoints)
    {
        var count = 0;
        var orientation = options.Orientation;
        MatchedFont? previousFont = null;
        var scripts = new ScriptItemizer(stackalloc int[ScriptItemizer.MaxBrackets], stackalloc UnicodeScript[ScriptItemizer.MaxBrackets]);
        var index = 0;
        while (index < text.Length)
        {
            var clusterStart = index;
            var codePoint = ReadCodePoint(text, ref index);

            // The cluster: the base character with the following combining marks, joiners, variation selectors, emoji modifiers and tags
            var clusterEnd = index;
            while (clusterEnd < text.Length)
            {
                var position = clusterEnd;
                var next = ReadCodePoint(text, ref position);
                if (!IsClusterExtender(next))
                    break;

                clusterEnd = position;
            }

            MatchedFont font;
            var shapedCodePoint = codePoint is '\t' or '\r' ? ' ' : codePoint;
            if (codePoint == '\n' || UnicodeProperties.IsDefaultIgnorable(codePoint))
            {
                // The segment breaks and the default ignorable characters are invisible (in the font of the previous character)
                font = previousFont ?? fonts.PrimaryFont;
            }
            else if (codePoint < 0x20 && codePoint is not ('\t' or '\r') || codePoint is >= 0x7F and < 0xA0)
            {
                // https://drafts.csswg.org/css-text-4/#white-space-processing: the other control characters "must be rendered as a
                // visible glyph which the UA must synthesize if the glyphs found in the font are not visible [...] The UA may
                // [...] substitute the glyphs provided for the corresponding symbol in the Control Pictures block [...] or use some
                // other method to provide an appropriate visible glyph": the control picture, else U+FFFD REPLACEMENT CHARACTER,
                // else the missing glyph of the first available font
                var picture = codePoint < 0x20 ? 0x2400 + codePoint : codePoint == 0x7F ? 0x2421 : 0xFFFD;
                if (fonts.FindFontForCharacter(picture) is { } pictureFont)
                {
                    font = pictureFont;
                    shapedCodePoint = picture;
                }
                else if (fonts.FindFontForCharacter(0xFFFD) is { } replacementFont)
                {
                    font = replacementFont;
                    shapedCodePoint = 0xFFFD;
                }
                else
                {
                    font = fonts.PrimaryFont;
                }
            }
            else
            {
                // https://drafts.csswg.org/css-fonts-4/#cluster-matching: the first font that supports the variation sequence of the
                // cluster (or the one implied by font-variant-emoji), else the first font that supports the character; no font
                // supports the character: the first available font renders it (its missing glyph, or the advance of a space)
                var selector = GetVariationSelector(text, index, codePoint, options.Emoji);
                font = (selector != 0 ? fonts.FindFontForVariationSequence(codePoint, selector) : null)
                    ?? fonts.FindFontForCharacter(codePoint is '\t' or '\r' ? ' ' : codePoint) ?? fonts.PrimaryFont;
            }

            // https://drafts.csswg.org/css-writing-modes-4/#text-orientation: in vertical text, the cluster is upright or rotated
            // according to its base character; the characters Tr are upright when the font has a vertical alternate for them
            var upright = orientation switch
            {
                TextShapingOrientation.VerticalUpright => true,
                TextShapingOrientation.VerticalMixed => UnicodeProperties.GetVerticalOrientation(codePoint) switch
                {
                    UnicodeVerticalOrientation.Upright or UnicodeVerticalOrientation.TransformedUpright => true,
                    UnicodeVerticalOrientation.TransformedRotated => font.Font.HasVerticalAlternate(font.GetGlyph(codePoint)),
                    _ => false,
                },
                _ => false,
            };

            var script = scripts.Next(codePoint);
            codePoints[count++] = new CodePointInfo { Index = clusterStart, CodePoint = shapedCodePoint, Cluster = clusterStart, Font = font, Upright = upright, Script = script };

            // The combining marks, joiners and selectors of the cluster
            var composedBase = shapedCodePoint;
            for (var position = index; position < clusterEnd;)
            {
                var markStart = position;
                var mark = ReadCodePoint(text, ref position);

                // https://drafts.csswg.org/css-fonts-4/#cluster-matching: the mark uses the font of its base when the font supports it,
                // or when the font has the precomposed character of the base and the mark (the shaping composes them, as in the
                // browsers: "e" followed by U+0301 is drawn with the glyph of U+00E9)
                MatchedFont markFont;
                if (ShapingNormalizer.ComposeUnicode(composedBase, mark, out var composed) && font.HasGlyph(composed))
                {
                    markFont = font;
                    composedBase = composed;
                }
                else
                {
                    markFont = UnicodeProperties.IsDefaultIgnorable(mark) || font.HasGlyph(mark) ? font : fonts.GetFontForCharacter(mark);
                }

                codePoints[count++] = new CodePointInfo { Index = markStart, CodePoint = mark, Cluster = clusterStart, Font = markFont, Upright = upright, Script = script };
            }

            previousFont = font;
            index = clusterEnd;
        }

        // The Common and Inherited characters before the first script take the script that follows them
        ScriptItemizer.ResolveLeading(codePoints.AsSpan(0, count));
        return count;
    }

    /// <summary>
    /// The script runs of a text (https://www.unicode.org/reports/tr24/, as Chrome's ScriptRunIterator): the characters of the
    /// scripts Common and Inherited take the script of the run, unless their Script_Extensions exclude it; a closing paired
    /// bracket takes the script of its opening bracket. The characters before the first script take the script that follows.
    /// </summary>
    private ref struct ScriptItemizer(Span<int> bracketCodePoints, Span<UnicodeScript> bracketScripts)
    {
        /// <summary>The maximum number of opened brackets that are tracked.</summary>
        public const int MaxBrackets = 32;

        private readonly Span<int> _bracketCodePoints = bracketCodePoints;
        private readonly Span<UnicodeScript> _bracketScripts = bracketScripts;
        private UnicodeScript _current = UnicodeScript.Common;
        private int _bracketCount;

        /// <summary>The script of the next base character.</summary>
        public UnicodeScript Next(int codePoint)
        {
            // The ASCII letters are Latin, the other ASCII characters are Common without Script_Extensions
            if (codePoint < 0x80 && _current == UnicodeScript.Latin && codePoint is not ('(' or ')' or '[' or ']' or '{' or '}'))
                return UnicodeScript.Latin;

            var script = UnicodeScripts.GetScript(codePoint);

            // The Script_Extensions, when they are not the Script property
            Span<UnicodeScript> extensionBuffer = stackalloc UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
            var extensionCount = UnicodeScripts.GetScriptExtensions(codePoint, extensionBuffer);
            ReadOnlySpan<UnicodeScript> extensions = extensionCount > 1 || extensionBuffer[0] != script ? extensionBuffer[..extensionCount] : [];

            // Hiragana and Katakana have the same OpenType script (kana): a text that mixes them is one run (e.g. their kerning)
            if (script is UnicodeScript.Hiragana or UnicodeScript.Katakana)
            {
                script = UnicodeScript.KatakanaOrHiragana;
            }

            if (script is UnicodeScript.Common or UnicodeScript.Inherited or UnicodeScript.Unknown)
            {
                // A character used by some scripts only (e.g. the Arabic tatweel) changes the script when it excludes the current
                // one: the first of its scripts by ISO 15924 code
                if (!extensions.IsEmpty && _current != UnicodeScript.Common && !extensions.Contains(_current))
                {
                    _current = GetFirstByIso15924Code(extensions);
                }

                switch (codePoint < 0x28 ? UnicodeBidiPairedBracketType.None : UnicodeProperties.GetBidiPairedBracketType(codePoint))
                {
                    case UnicodeBidiPairedBracketType.Open:
                        if (_bracketCount < MaxBrackets)
                        {
                            _bracketCodePoints[_bracketCount] = CharacterProperties.GetBidiPairedBracket(codePoint);
                            _bracketScripts[_bracketCount] = _current;
                            _bracketCount++;
                        }

                        break;

                    case UnicodeBidiPairedBracketType.Close:
                        // The closing bracket of an opening bracket (the brackets opened after it are closed too)
                        for (var i = _bracketCount - 1; i >= 0; i--)
                        {
                            if (_bracketCodePoints[i] == codePoint)
                            {
                                _bracketCount = i;
                                var opening = _bracketScripts[i];
                                if (opening != UnicodeScript.Common)
                                {
                                    _current = opening;
                                }

                                break;
                            }
                        }

                        break;
                }

                return _current;
            }

            // A character of several scripts keeps the current one when it is one of them
            if (!extensions.IsEmpty && extensions.Contains(_current))
                return _current;

            // The brackets opened before the first script take it
            if (_current == UnicodeScript.Common)
            {
                for (var i = 0; i < _bracketCount; i++)
                {
                    _bracketScripts[i] = script;
                }
            }

            _current = script;
            return script;
        }

        private static UnicodeScript GetFirstByIso15924Code(ReadOnlySpan<UnicodeScript> scripts)
        {
            var result = scripts[0];
            for (var i = 1; i < scripts.Length; i++)
            {
                if (string.CompareOrdinal(UnicodeScripts.GetIso15924Code(scripts[i]), UnicodeScripts.GetIso15924Code(result)) < 0)
                {
                    result = scripts[i];
                }
            }

            return result;
        }

        /// <summary>Gives the first script of the text to the characters before it.</summary>
        public static void ResolveLeading(Span<CodePointInfo> codePoints)
        {
            var first = 0;
            while (first < codePoints.Length && codePoints[first].Script == UnicodeScript.Common)
            {
                first++;
            }

            if (first == 0 || first == codePoints.Length)
                return;

            var script = codePoints[first].Script;
            for (var i = 0; i < first; i++)
            {
                codePoints[i].Script = script;
            }
        }
    }

    /// <summary>Shapes the runs of code points of the same font, orientation, script and synthesis, and writes their glyphs.</summary>
    private static void ShapeRuns(ReadOnlySpan<char> text, ReadOnlySpan<CodePointInfo> codePoints, in TextShapingOptions options, ShapingBuffer buffer, ref GlyphWriter output, bool collectRuns)
    {
        var start = 0;
        while (start < codePoints.Length)
        {
            var font = codePoints[start].Font;
            var upright = codePoints[start].Upright;
            var script = codePoints[start].Script;
            var synthesis = codePoints[start].Synthesis;
            var end = start + 1;
            while (end < codePoints.Length && ReferenceEquals(codePoints[end].Font, font) && codePoints[end].Upright == upright && codePoints[end].Script == script && codePoints[end].Synthesis == synthesis)
            {
                end++;
            }

            ShapeRun(text, codePoints, start, end, options, buffer, ref output, collectRuns);
            start = end;
        }
    }

    private static void ShapeRun(ReadOnlySpan<char> text, ReadOnlySpan<CodePointInfo> codePoints, int start, int end, in TextShapingOptions options, ShapingBuffer buffer, ref GlyphWriter output, bool collectRuns)
    {
        var first = codePoints[start];
        var font = first.Font;
        var synthesis = first.Synthesis;
        var synthesisScale = synthesis == Synthesis.None ? 1 : GetSynthesisScale(synthesis, options);
        var emSize = font.GetEmSize(options.FontSize * synthesisScale);
        var instance = font.GetVariationInstance(emSize);
        var shapingFont = new ShapingFont(font.Font, instance);
        var direction = first.Upright ? ShapingDirection.TopToBottom : options.IsRightToLeft ? ShapingDirection.RightToLeft : ShapingDirection.LeftToRight;

        buffer.Reset(end - start);
        buffer.Direction = direction;
        buffer.Script = first.Script;
        if ((synthesis & (Synthesis.Uppercase | Synthesis.Lowercase)) == Synthesis.None)
        {
            for (var i = start; i < end; i++)
            {
                buffer.Add(codePoints[i].CodePoint, codePoints[i].Index);
            }
        }
        else
        {
            // The synthesized small capitals: the case mapping of the characters, in their clusters
            var turkic = TextTransformer.UsesTurkicCasing(options.Language);
            for (var i = start; i < end; i++)
            {
                AddMappedCodePoint(buffer, codePoints[i].CodePoint, codePoints[i].Index, (synthesis & Synthesis.Uppercase) != Synthesis.None, turkic);
            }
        }

        // The characters around the run (of the text, or of the context of the text)
        Span<int> before = stackalloc int[1];
        Span<int> after = stackalloc int[1];
        var beforeCodePoint = start > 0 ? codePoints[start - 1].CodePoint : options.Context.Before;
        var afterCodePoint = end < codePoints.Length ? codePoints[end].CodePoint : options.Context.After;
        before[0] = beforeCodePoint;
        after[0] = afterCodePoint;
        buffer.SetContext(beforeCodePoint == 0 ? [] : before, afterCodePoint == 0 ? [] : after);

        // As Chrome, the CJK text is kerned only with font-kerning: normal, with the proportional alternates of the font (palt
        // or vpal: their kerning is designed for them)
        var isCjk = first.Script is UnicodeScript.Han or UnicodeScript.KatakanaOrHiragana or UnicodeScript.Bopomofo;
        var features = options.Features;
        if (features is not null && (synthesis & (Synthesis.CapsFeaturesMask | Synthesis.Position)) != Synthesis.None)
        {
            features = features.WithSynthesis(GetCapsFeatures(synthesis), withoutPosition: (synthesis & Synthesis.Position) != Synthesis.None);
        }

        var key = new ShapePlanKey(
            first.Script,
            direction,
            options.Language,
            options.LanguageOverride,
            font.FaceFeatures,
            features,
            font.Font.LayoutGsub?.FindFeatureVariations(instance) ?? -1,
            font.Font.LayoutGpos?.FindFeatureVariations(instance) ?? -1,
            DisableKerning: !options.Kerning || (isCjk && !options.KerningRequested),
            ProportionalAlternates: isCjk && options.Kerning && options.KerningRequested,
            VerticalKerning: direction == ShapingDirection.TopToBottom && options.Kerning && options.KerningRequested);
        var plan = font.Font.GetShapePlan(key);
        OpenTypeShapingEngine.Shape(plan, buffer, shapingFont);

        // The glyphs, in CSS px
        var scale = emSize / font.Font.UnitsPerEm;
        var info = buffer.Info;
        var positions = buffer.Positions;
        var runStart = output.Count;
        var segmentStart = codePoints[start].Index;
        var clusterCount = 0;

        // The synthesized subscripts and superscripts are shifted towards the line-over or the line-under side (the cross offset
        // of the upright glyphs is the horizontal one: the line-over side of the vertical lines is on the right)
        var baselineShift = (synthesis & Synthesis.Position) != Synthesis.None ? GetPositionShift(options.Position, options.FontSize) : 0;
        if (direction == ShapingDirection.TopToBottom)
        {
            baselineShift = -baselineShift;
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            ref readonly var glyph = ref info[i];
            ref readonly var position = ref positions[i];
            if (i == 0 || glyph.Cluster != info[i - 1].Cluster)
            {
                clusterCount++;
            }

            // A mark in another font than its base stays in the cluster of its base
            var cluster = glyph.Cluster;
            if (cluster == segmentStart && first.Cluster < segmentStart)
            {
                cluster = first.Cluster;
            }

            var flags = first.Upright ? ShapedGlyphFlags.Upright : ShapedGlyphFlags.None;
            if (output.Count == 0 || output.Glyphs[^1].Cluster != cluster)
            {
                flags |= ShapedGlyphFlags.ClusterStart;
            }

            if (glyph.IsDefaultIgnorable)
            {
                flags |= ShapedGlyphFlags.Invisible;
            }

            if (glyph.IsMark || glyph.IsUnicodeMark)
            {
                flags |= ShapedGlyphFlags.Mark;
            }

            if ((glyph.Mask & GlyphFlags.UnsafeToBreak) != 0)
            {
                flags |= ShapedGlyphFlags.UnsafeToBreak;
            }

            double advance, offsetX, offsetY;
            if (direction == ShapingDirection.TopToBottom)
            {
                // The upright glyphs: the vertical advances are downwards, the cross offset is the horizontal one
                advance = -position.YAdvance * scale;
                offsetX = -position.YOffset * scale;
                offsetY = position.XOffset * scale;
            }
            else
            {
                advance = position.XAdvance * scale;
                offsetX = position.XOffset * scale;
                offsetY = -position.YOffset * scale;
            }

            output.Add(new ShapedGlyph((ushort)glyph.Codepoint, cluster, advance, offsetX, offsetY + baselineShift, flags));
        }

        if (collectRuns && output.Count > runStart)
        {
            output.AddRun(new ShapedFontRun(font, emSize, runStart, output.Count - runStart, first.Upright) { SynthesisScale = (float)synthesisScale });

            // The carets inside the clusters of several typographic character units (each unit has its own cluster when the
            // glyph clusters are as many as the units)
            var units = 0;
            for (var i = start; i < end; i++)
            {
                if (codePoints[i].Cluster == codePoints[i].Index)
                {
                    units++;
                }
            }

            if (units != clusterCount)
            {
                AddLigatureCarets(text, codePoints, start, end, font, instance, scale, buffer, runStart, ref output);
            }
        }
    }

    /// <summary>
    /// Records the caret positions inside the clusters of several typographic character units (the ligatures): the caret
    /// values of the ligature glyph in the GDEF table when it has one for each unit, else an equal division of the advance of
    /// the cluster, as Chrome does.
    /// </summary>
    private static void AddLigatureCarets(ReadOnlySpan<char> text, ReadOnlySpan<CodePointInfo> codePoints, int start, int end, MatchedFont font, FontVariationInstance? instance, double scale, ShapingBuffer buffer, int runStart, ref GlyphWriter output)
    {
        var glyphs = output.Glyphs;
        var g = runStart;
        var c = start;
        Span<double> carets = stackalloc double[16];
        while (g < glyphs.Length)
        {
            // The glyphs of the cluster, and its typographic character units
            var cluster = glyphs[g].Cluster;
            var glyphEnd = g + 1;
            while (glyphEnd < glyphs.Length && glyphs[glyphEnd].Cluster == cluster)
            {
                glyphEnd++;
            }

            var clusterEnd = glyphEnd < glyphs.Length ? glyphs[glyphEnd].Cluster : (end < codePoints.Length ? codePoints[end].Index : text.Length);
            while (c < end && codePoints[c].Index < cluster)
            {
                c++;
            }

            var units = 0;
            for (var i = c; i < end && codePoints[i].Index < clusterEnd; i++)
            {
                if (codePoints[i].Cluster == codePoints[i].Index)
                {
                    units++;
                }
            }

            if (units > 1)
            {
                var width = 0d;
                var ligatureGlyph = -1;
                var baseGlyphs = 0;
                for (var i = g; i < glyphEnd; i++)
                {
                    width += glyphs[i].Advance;
                    if ((glyphs[i].Flags & ShapedGlyphFlags.Mark) == ShapedGlyphFlags.None)
                    {
                        ligatureGlyph = i;
                        baseGlyphs++;
                    }
                }

                var hasCarets = baseGlyphs == 1 && units - 1 <= carets.Length && font.Font.LayoutGdef is { } gdef && gdef.TryGetLigatureCarets(glyphs[ligatureGlyph].GlyphId, instance, carets, out var caretCount) && caretCount == units - 1;
                var unit = 0;
                for (var i = c; i < end && codePoints[i].Index < clusterEnd; i++)
                {
                    if (codePoints[i].Cluster != codePoints[i].Index || codePoints[i].Index == cluster)
                        continue;

                    unit++;
                    double offset;
                    if (hasCarets)
                    {
                        // The caret values are from the origin of the glyph, from left to right
                        var advance = glyphs[ligatureGlyph].Advance;
                        offset = buffer.IsBackward ? advance - (carets[units - 1 - unit] * scale) : carets[unit - 1] * scale;
                    }
                    else
                    {
                        offset = width * unit / units;
                    }

                    output.AddCaret(new LigatureCaret(codePoints[i].Index, offset));
                }
            }

            g = glyphEnd;
        }
    }

    /// <summary>
    /// Applies <c>letter-spacing</c> and <c>word-spacing</c> to the glyphs (https://drafts.csswg.org/css-text-4/#spacing), and
    /// returns the width of the text: the word spacing is added after each word-separator character of a cluster, the letter
    /// spacing after each visible typographic character unit (to the last glyph of its cluster). In a right-to-left run, the
    /// letter spacing is on the right of the clusters (as Chrome and Firefox) and the word spacing on the left of the word
    /// separators, and the marks stay on their base.
    /// </summary>
    private static double ApplySpacing(ReadOnlySpan<char> text, ReadOnlySpan<CodePointInfo> codePoints, in TextShapingOptions options, ref GlyphWriter output)
    {
        var glyphs = output.Glyphs;
        var width = 0d;
        var c = 0;
        var g = 0;
        while (g < glyphs.Length)
        {
            var cluster = glyphs[g].Cluster;
            var glyphEnd = g + 1;
            var visible = !glyphs[g].IsInvisible;
            while (glyphEnd < glyphs.Length && glyphs[glyphEnd].Cluster == cluster)
            {
                visible |= !glyphs[glyphEnd].IsInvisible;
                glyphEnd++;
            }

            var clusterEnd = glyphEnd < glyphs.Length ? glyphs[glyphEnd].Cluster : text.Length;
            while (c < codePoints.Length && codePoints[c].Index < cluster)
            {
                c++;
            }

            var separators = 0;
            var units = 0;
            var expansion = 0;
            for (var i = c; i < codePoints.Length && codePoints[i].Index < clusterEnd; i++)
            {
                expansion += codePoints[i].Expansion;
                var codePoint = codePoints[i].CodePoint;
                if (IsWordSeparator(codePoint))
                {
                    separators++;
                }

                // The invisible characters have no letter spacing, nor the letters of the cursive scripts, whose connections would
                // break (https://drafts.csswg.org/css-text-4/#cursive-tracking: as Chrome and Firefox, for the joining scripts)
                if (options.LetterSpacing != 0 && codePoints[i].Cluster == codePoints[i].Index && codePoint != '\n' && !UnicodeProperties.IsDefaultIgnorable(codePoint)
                    && !ArabicJoiningScripts.HasJoining(UnicodeScripts.GetScript(codePoint)))
                {
                    units++;
                }
            }

            var wordSpacing = 0d;
            if (separators > 0)
            {
                wordSpacing = separators * options.WordSpacing;
                glyphs[g] = glyphs[g] with { Flags = glyphs[g].Flags | ShapedGlyphFlags.WordSeparator };
            }

            var letterSpacing = 0d;
            if (options.LetterSpacing != 0 && visible && units > 0)
            {
                if (expansion > 0)
                {
                    // The case mapping of a synthesized small capital added characters (e.g. ß → SS): the letter spacing goes
                    // between their glyphs too (as Chrome and Firefox), to the glyphs that do not get the spacing of the cluster
                    var first = options.IsRightToLeft ? g + 1 : g;
                    var last = options.IsRightToLeft ? glyphEnd : glyphEnd - 1;
                    for (var i = first; i < last && expansion > 0; i++)
                    {
                        if ((glyphs[i].Flags & ShapedGlyphFlags.Mark) == ShapedGlyphFlags.None)
                        {
                            glyphs[i] = glyphs[i] with { Advance = glyphs[i].Advance + options.LetterSpacing };
                            expansion--;
                        }
                    }
                }

                letterSpacing = units * options.LetterSpacing;
                glyphs[glyphEnd - 1] = glyphs[glyphEnd - 1] with { Flags = glyphs[glyphEnd - 1].Flags | ShapedGlyphFlags.LetterSpaced };
            }

            if (!options.IsRightToLeft)
            {
                if (wordSpacing + letterSpacing != 0)
                {
                    glyphs[glyphEnd - 1] = glyphs[glyphEnd - 1] with { Advance = glyphs[glyphEnd - 1].Advance + wordSpacing + letterSpacing };
                }
            }
            else
            {
                // A right-to-left run is drawn from its right edge, and the glyphs of a cluster do not move (e.g. a mark stays on
                // its base): the letter spacing is on the right of the cluster, between it and the previous one (the advance of
                // its first glyph), as Chrome and Firefox (css/CSS2/bidi-005); the word spacing is after the word separator, on
                // its left (the advance of its last glyph, whose offset compensates it), as Firefox
                if (letterSpacing != 0)
                {
                    glyphs[g] = glyphs[g] with { Advance = glyphs[g].Advance + letterSpacing };
                }

                if (wordSpacing != 0)
                {
                    var last = glyphs[glyphEnd - 1];
                    glyphs[glyphEnd - 1] = last with { Advance = last.Advance + wordSpacing, OffsetX = last.OffsetX + wordSpacing };
                }
            }

            for (var i = g; i < glyphEnd; i++)
            {
                width += glyphs[i].Advance;
            }

            g = glyphEnd;
        }

        return width;
    }

    /// <summary>
    /// The variation selector of a cluster for the font matching: the variation selector that follows its base character, else
    /// the one implied by <c>font-variant-emoji</c> for an Emoji Presentation Participating Code Point
    /// (https://drafts.csswg.org/css-fonts-4/#font-variant-emoji-prop: "as if U+FE0E VARIATION SELECTOR-15 was appended"), or 0.
    /// </summary>
    private static int GetVariationSelector(ReadOnlySpan<char> text, int index, int codePoint, FontEmojiVariant emoji)
    {
        const int TextPresentationSelector = 0xFE0E;
        const int EmojiPresentationSelector = 0xFE0F;
        if (index < text.Length && (text[index] is >= (char)0xFE00 and <= (char)0xFE0F || char.IsHighSurrogate(text[index])))
        {
            var position = index;
            var next = ReadCodePoint(text, ref position);
            if (CharacterProperties.IsVariationSelector(next))
                return next;
        }

        if (emoji == FontEmojiVariant.Normal || codePoint < 0x23 || !CharacterProperties.IsEmojiPresentationParticipating(codePoint))
            return 0;

        return emoji switch
        {
            FontEmojiVariant.Text => TextPresentationSelector,
            FontEmojiVariant.Emoji => EmojiPresentationSelector,
            _ => UnicodeEmoji.HasEmojiPresentation(codePoint) ? EmojiPresentationSelector : TextPresentationSelector,
        };
    }

    /// <summary>Reads the code point at an index of a text and moves the index after it; a lone surrogate is U+FFFD.</summary>
    private static int ReadCodePoint(ReadOnlySpan<char> text, ref int index)
    {
        var c = text[index];
        if (!char.IsSurrogate(c))
        {
            index++;
            return c;
        }

        Rune.DecodeFromUtf16(text[index..], out var rune, out var consumed);
        index += consumed;
        return rune.Value;
    }

    /// <summary>
    /// Whether a character extends the cluster of the previous one: the combining marks (General_Category M, which includes
    /// the variation selectors), the zero width joiner and non-joiner, the emoji modifiers and the tag characters (the
    /// Grapheme_Cluster_Break=Extend characters of UAX #29, https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Boundary_Rules).
    /// </summary>
    private static bool IsClusterExtender(int codePoint)
    {
        if (codePoint < 0x300)
            return false;

        return codePoint is 0x200C or 0x200D or (>= 0x1F3FB and <= 0x1F3FF) or (>= 0xE0020 and <= 0xE007F) || CharacterProperties.IsMark(codePoint);
    }

    /// <summary>The word-separator characters (https://drafts.csswg.org/css-text-4/#word-separator).</summary>
    internal static bool IsWordSeparator(int codePoint) => codePoint is 0x20 or 0xA0 or 0x1361 or 0x10100 or 0x10101 or 0x1039F or 0x1091F;

    /// <summary>The glyphs, the font runs and the ligature carets written by the shaping, in arrays rented from the shared pool.</summary>
    private struct GlyphWriter : IDisposable
    {
        private ShapedGlyph[] _glyphs;
        private int _count;
        private ShapedFontRun[] _runs;
        private int _runCount;

        public GlyphWriter(int capacity)
        {
            _glyphs = ArrayPool<ShapedGlyph>.Shared.Rent(Math.Max(capacity, 4));
            _count = 0;
            _runs = [];
            _runCount = 0;
            Carets = null;
        }

        public readonly int Count => _count;

        public readonly Span<ShapedGlyph> Glyphs => _glyphs.AsSpan(0, _count);

        public readonly ReadOnlySpan<ShapedFontRun> Runs => _runs.AsSpan(0, _runCount);

        public List<LigatureCaret>? Carets { get; private set; }

        public void Add(in ShapedGlyph glyph)
        {
            if (_count == _glyphs.Length)
            {
                var glyphs = ArrayPool<ShapedGlyph>.Shared.Rent(_glyphs.Length * 2);
                _glyphs.AsSpan(0, _count).CopyTo(glyphs);
                ArrayPool<ShapedGlyph>.Shared.Return(_glyphs);
                _glyphs = glyphs;
            }

            _glyphs[_count++] = glyph;
        }

        /// <summary>Adds a font run, merged with the previous one when it has the same font (e.g. two scripts of a font).</summary>
        public void AddRun(in ShapedFontRun run)
        {
            if (_runCount > 0 && _runs[_runCount - 1] is var last && ReferenceEquals(last.Font, run.Font) && last.EmSize == run.EmSize && last.IsUpright == run.IsUpright && last.GlyphStart + last.GlyphCount == run.GlyphStart)
            {
                _runs[_runCount - 1] = last with { GlyphCount = last.GlyphCount + run.GlyphCount };
                return;
            }

            if (_runCount == _runs.Length)
            {
                // The runs are pooled (most texts have one)
                var runs = ArrayPool<ShapedFontRun>.Shared.Rent(Math.Max(4, _runs.Length * 2));
                _runs.AsSpan(0, _runCount).CopyTo(runs);
                ReturnRuns();
                _runs = runs;
            }

            _runs[_runCount++] = run;
        }

        public void AddCaret(in LigatureCaret caret) => (Carets ??= []).Add(caret);

        private readonly void ReturnRuns()
        {
            if (_runs.Length > 0)
            {
                ArrayPool<ShapedFontRun>.Shared.Return(_runs, clearArray: true);
            }
        }

        public void Dispose()
        {
            if (_glyphs.Length > 0)
            {
                ArrayPool<ShapedGlyph>.Shared.Return(_glyphs);
                _glyphs = [];
            }

            ReturnRuns();
            _runs = [];
            _runCount = 0;
        }
    }
}
