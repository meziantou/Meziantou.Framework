namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>The synthetic fonts of Apple Advanced Typography: the <c>morx</c>, <c>kerx</c>, <c>feat</c>, <c>trak</c> and <c>ankr</c> tables.</summary>
internal static partial class FontSamples
{
    // The feature types of the Font Feature Registry the fonts declare in their feat table.
    private const int LigaturesType = 1;
    private const int StylisticAlternativesType = 35;
    private const int ContextualAlternatesType = 36;
    private const int LowerCaseType = 37;

    /// <summary>
    /// A font whose glyphs are substituted by a <c>morx</c> table (version 3) of two chains, with the features of a
    /// <c>feat</c> table.
    /// </summary>
    /// <remarks>
    /// <para>Every glyph is a rectangle whose advance tells it from the others. The subtables of the first chain:</para>
    /// <list type="bullet">
    /// <item><description>Non-contextual substitutions, always on, one per format of lookup table: D (format 0), E (4), G (6), H (8), I (10) and K (2) are replaced by their alternates.</description></item>
    /// <item><description>Ligatures (common ligatures, on by default): fi, fl and ffi; the components after the first one are deleted.</description></item>
    /// <item><description>Small capitals (lower case type, exclusive): a, b and c. Stylistic set 1: S.</description></item>
    /// <item><description>Contextual alternates (on by default): T after A is replaced; X is replaced (as the marked glyph) when Y follows it.</description></item>
    /// <item><description>Rearrangement, always on: M starts a range and N ends it with the verb AxD => DxA.</description></item>
    /// <item><description>Insertion, always on: a glyph before each Q (current glyph); two glyphs after R (marked glyph) when Z comes.</description></item>
    /// <item><description>A contextual substitution in descending order: 1 is replaced when 2 follows it in the text (the subtable sees 2 first).</description></item>
    /// <item><description>A non-contextual substitution of B for vertical lines only.</description></item>
    /// <item><description>Hebrew: alef before bet in the order of the characters (a subtable in logical order); gimel before alef in the order the glyphs are drawn (a subtable in layout order).</description></item>
    /// </list>
    /// <para>The second chain replaces the alternate of X made by the first one: the chains apply in order. The font has no positioning table: its mark U+0301 is placed from the shapes of the glyphs.</para>
    /// </remarks>
    public static FontBuilder AatMorphing()
    {
        var font = CreateAatFont("Meziantou AAT Morphing", "MeziantouAATMorphing-Regular");
        var g = new Dictionary<string, int>(StringComparer.Ordinal);
        var advance = 448;
        foreach (var character in "ABDEFGHIKMNPQRSTXYZabcfil12")
        {
            g[character.ToString()] = font.AddGlyph(FontGlyph.Rectangle(advance, 32, 0, advance - 32, 768), character);
            advance += 16;
        }

        g["alef"] = font.AddGlyph(FontGlyph.Rectangle(640, 32, 0, 608, 512), 0x05D0);
        g["bet"] = font.AddGlyph(FontGlyph.Rectangle(672, 32, 0, 640, 512), 0x05D1);
        g["gimel"] = font.AddGlyph(FontGlyph.Rectangle(704, 32, 0, 672, 512), 0x05D2);
        g["acute"] = font.AddGlyph(FontGlyph.Rectangle(0, -320, 832, -128, 960), 0x0301);
        foreach (var name in new[] { "D", "E", "G", "H", "I", "K", "S", "T", "X", "B", "1", "alef", "gimel" })
        {
            g[name + ".alt"] = font.AddGlyph(FontGlyph.Rectangle(advance, 0, 0, advance, 384));
            advance += 16;
        }

        foreach (var name in new[] { "fi", "fl", "ffi", "a.sc", "b.sc", "c.sc", "Q.before", "R.tail1", "R.tail2", "X.alt2" })
        {
            g[name] = font.AddGlyph(FontGlyph.Rectangle(advance, 0, 128, advance, 640));
            advance += 16;
        }

        // A table of format 0 has a value for every glyph: the glyphs it does not replace are given as themselves.
        var glyphCount = font.Glyphs.Count;
        static Dictionary<int, int> Identity(Dictionary<int, int> map, int glyphCount) => Enumerable.Range(0, glyphCount).ToDictionary(glyph => glyph, glyph => map.TryGetValue(glyph, out var value) ? value : glyph);
        Dictionary<int, int> Map(params (string From, string To)[] pairs) => pairs.ToDictionary(pair => g[pair.From], pair => g[pair.To]);
        static FontMorxSubtable Noncontextual(uint flags, Dictionary<int, int> map, int format, int glyphCount, uint orientation = 0x20000000)
            => new(orientation | 4, flags, FontAatWriter.WriteLookup(map, format, glyphCount));

        const uint Always = 0x01;
        const uint Ligatures = 0x02;
        const uint SmallCapitals = 0x04;
        const uint StylisticSet = 0x08;
        const uint ContextualAlternates = 0x10;
        var subtables = new List<FontMorxSubtable>
        {
            Noncontextual(Always, Identity(Map(("D", "D.alt")), glyphCount), 0, glyphCount),
            Noncontextual(Always, Map(("E", "E.alt")), 4, glyphCount),
            Noncontextual(Always, Map(("G", "G.alt")), 6, glyphCount),
            Noncontextual(Always, Map(("H", "H.alt")), 8, glyphCount),
            Noncontextual(Always, Map(("I", "I.alt")), 10, glyphCount),
            Noncontextual(Always, Map(("K", "K.alt")), 2, glyphCount),
            new(0x20000002, Ligatures, WriteLigatures(g)),
            Noncontextual(SmallCapitals, Map(("a", "a.sc"), ("b", "b.sc"), ("c", "c.sc")), 6, glyphCount),
            Noncontextual(StylisticSet, Map(("S", "S.alt")), 6, glyphCount),
            new(0x20000001, ContextualAlternates, WriteContextualAlternates(g)),
            new(0x20000000, Always, WriteRearrangement(g)),
            new(0x20000005, Always, WriteInsertions(g)),
            new(0x60000001, Always, WriteDescendingSubstitution(g)),
            Noncontextual(Always, Map(("B", "B.alt")), 6, glyphCount, orientation: 0x80000000),
            new(0x30000001, Always, WriteMarkedSubstitution(g, "alef", "bet", "alef.alt")),
            new(0x20000001, Always, WriteMarkedSubstitution(g, "gimel", "alef", "gimel.alt")),
        };

        // Each setting turns its flag on, and the "off" setting turns it off: the disable flags are the complement of
        // the flag. The chain ends with the setting that turns every flag off.
        (int, int, uint, uint)[] features =
        [
            (LigaturesType, 2, Ligatures, ~0u),
            (LigaturesType, 3, 0, ~Ligatures),
            (LowerCaseType, 0, 0, ~SmallCapitals),
            (LowerCaseType, 1, SmallCapitals, ~0u),
            (StylisticAlternativesType, 2, StylisticSet, ~0u),
            (StylisticAlternativesType, 3, 0, ~StylisticSet),
            (ContextualAlternatesType, 0, ContextualAlternates, ~0u),
            (ContextualAlternatesType, 1, 0, ~ContextualAlternates),
            (0, 1, 0, 0),
        ];

        var second = new FontMorxChain(Always, [], [Noncontextual(Always, Map(("X.alt", "X.alt2")), 6, glyphCount)]);
        font.RawTables["morx"] = FontAatWriter.WriteMorx([new FontMorxChain(Always | Ligatures | ContextualAlternates, features, subtables), second], version: 3);
        font.RawTables["feat"] = FontAatWriter.WriteFeat(
        [
            (0, false, 0, [0]),
            (LigaturesType, false, 0, [2]),
            (StylisticAlternativesType, false, 0, [2]),
            (ContextualAlternatesType, false, 0, [0]),
            (LowerCaseType, true, 0, [0, 1]),
        ]);
        return font;
    }

    /// <summary>
    /// A font kerned by a <c>kerx</c> table (version 3) with a subtable of each format, and an <c>ankr</c> table.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Format 0: A then V is tightened by 96, V then A by 64; bet then alef (in the order they are drawn) by 32. Cross-stream: J then K is raised by 48, with the glyphs after K.</description></item>
    /// <item><description>Format 2: T or Y (two rows) then o or e (two columns).</description></item>
    /// <item><description>Format 6: L or P then O or Q.</description></item>
    /// <item><description>Format 1: C pushed, then D pushed with the values 48 (to D) and 16 (to C); the same for E and F from the last glyph; G pushed, then H with a cross-stream value of 40.</description></item>
    /// <item><description>Format 4: the acute accent is placed by its anchor on the anchor of A or B (ankr); the grave accent by coordinates on A or B.</description></item>
    /// <item><description>Format 0 for vertical lines: A then V, by 128.</description></item>
    /// </list>
    /// </remarks>
    public static FontBuilder AatKerning()
    {
        var font = CreateAatFont("Meziantou AAT Kerning", "MeziantouAATKerning-Regular");
        var g = new Dictionary<string, int>(StringComparer.Ordinal);
        var advance = 448;
        foreach (var character in "ABCDEFGHJKLOPQTVYeo")
        {
            g[character.ToString()] = font.AddGlyph(FontGlyph.Rectangle(advance, 32, 0, advance - 32, 768), character);
            advance += 16;
        }

        g["alef"] = font.AddGlyph(FontGlyph.Rectangle(640, 32, 0, 608, 512), 0x05D0);
        g["bet"] = font.AddGlyph(FontGlyph.Rectangle(672, 32, 0, 640, 512), 0x05D1);
        g["acute"] = font.AddGlyph(FontGlyph.Rectangle(0, -320, 832, -128, 960), 0x0301);
        g["grave"] = font.AddGlyph(FontGlyph.Rectangle(0, -320, 832, -128, 960), 0x0300);

        var pairs = FontAatWriter.WriteKerxPairs(new Dictionary<(int, int), int> { [(g["A"], g["V"])] = -96, [(g["V"], g["A"])] = -64, [(g["bet"], g["alef"])] = -32 });
        var crossPairs = FontAatWriter.WriteKerxPairs(new Dictionary<(int, int), int> { [(g["J"], g["K"])] = 48 });
        var verticalPairs = FontAatWriter.WriteKerxPairs(new Dictionary<(int, int), int> { [(g["A"], g["V"])] = -128 });
        var classArray = FontAatWriter.WriteKerxClassArray(
            new Dictionary<int, int> { [g["T"]] = 1, [g["Y"]] = 2 },
            new Dictionary<int, int> { [g["o"]] = 1, [g["e"]] = 2 },
            [[0, 0, 0], [0, -80, -48], [0, -112, -16]]);
        var indexArray = FontAatWriter.WriteKerxIndexArray(
            new Dictionary<int, int> { [g["L"]] = 1, [g["P"]] = 2 },
            new Dictionary<int, int> { [g["O"]] = 1, [g["Q"]] = 2 },
            [[0, 0, 0], [0, -24, -40], [0, -56, -72]]);
        font.RawTables["kerx"] = FontAatWriter.WriteKerx(
        [
            (0x00000000, pairs),
            (0x40000000, crossPairs),
            (0x80000000, verticalPairs),
            (0x00000002, classArray),
            (0x00000006, indexArray),
            (0x00000001, WriteContextualKerning(g["C"], g["D"], [48, 16])),
            (0x10000001, WriteContextualKerning(g["F"], g["E"], [40, 24])),
            (0x40000001, WriteContextualKerning(g["G"], g["H"], [40])),
            (0x00000004, WriteAttachments(g, "acute", actionType: 1, [0, 0])),
            (0x00000004, WriteAttachments(g, "grave", actionType: 2, [384, 768, -224, 0])),
        ], version: 3);
        font.RawTables["ankr"] = FontAatWriter.WriteAnkr(new Dictionary<int, (int X, int Y)[]>
        {
            [g["A"]] = [(224, 800)],
            [g["B"]] = [(160, 832)],
            [g["acute"]] = [(-224, 0)],
        });
        return font;
    }

    /// <summary>A font whose glyphs are spaced by a <c>trak</c> table: 64 units at 8 points and below, none at 12, -32 at 24, -96 at 72 and above.</summary>
    /// <remarks>
    /// The font has no other table of Apple Advanced Typography; its mark U+0301 is not spaced. It has a <c>STAT</c>
    /// table without axis, as the font of the tracking tests of HarfBuzz: the reference shaper tracks only a font that
    /// has one, which has nothing to do with tracking (the library tracks every font when a size is given).
    /// </remarks>
    public static FontBuilder AatTracking()
    {
        var font = CreateAatFont("Meziantou AAT Tracking", "MeziantouAATTracking-Regular");
        font.AddGlyph(FontGlyph.Rectangle(512, 32, 0, 480, 768), 'A');
        font.AddGlyph(FontGlyph.Rectangle(640, 32, 0, 608, 768), 'B');
        font.AddGlyph(FontGlyph.Rectangle(0, -320, 832, -128, 960), 0x0301);
        font.RawTables["STAT"] = [0, 1, 0, 1, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2];
        font.RawTables["trak"] = FontAatWriter.WriteTrak([8, 12, 24, 72], [(-1, [32, 0, -64, -128]), (0, [64, 0, -32, -96]), (1, [128, 64, 32, 0])]);
        return font;
    }

    /// <summary>
    /// A font that has both the tables of Apple Advanced Typography and the OpenType Layout tables, which replace A by
    /// another glyph each and kern it with B: the shaper chooses the tables.
    /// </summary>
    /// <param name="withMorx">The font has a <c>morx</c> table (A is replaced by its first alternate) and a <c>GSUB</c> table for Latin (A is replaced by its second alternate).</param>
    /// <param name="withEmptySubstitutions">The font has a <c>GSUB</c> table without script.</param>
    /// <remarks>The <c>kerx</c> table tightens A, and its first alternate, then B by 96; the <c>GPOS</c> table by 192.</remarks>
    public static FontBuilder AatSelection(bool withMorx, bool withEmptySubstitutions = false)
    {
        var font = CreateAatFont("Meziantou AAT Selection", "MeziantouAATSelection-Regular");
        var a = font.AddGlyph(FontGlyph.Rectangle(1024, 32, 0, 992, 768), 'A');
        var b = font.AddGlyph(FontGlyph.Rectangle(768, 32, 0, 736, 768), 'B');
        var morphed = font.AddGlyph(FontGlyph.Rectangle(896, 32, 0, 864, 512));
        var substituted = font.AddGlyph(FontGlyph.Rectangle(640, 32, 0, 608, 512));
        if (withMorx)
        {
            font.RawTables["morx"] = FontAatWriter.WriteMorx([new FontMorxChain(1, [], [new FontMorxSubtable(0x20000004, 1, FontAatWriter.WriteLookup(new Dictionary<int, int> { [a] = morphed }))])]);
            font.RawTables["GSUB"] = FontLayoutWriter.WriteSingleSubstitution("latn", "ccmp", a, substituted);
        }
        else if (withEmptySubstitutions)
        {
            font.RawTables["GSUB"] = FontLayoutWriter.WriteSingleSubstitution(null, "ccmp", a, substituted);
        }

        var first = withMorx ? morphed : a;
        font.RawTables["kerx"] = FontAatWriter.WriteKerx([(0, FontAatWriter.WriteKerxPairs(new Dictionary<(int, int), int> { [(first, b)] = -96 }))]);
        font.RawTables["GPOS"] = FontLayoutWriter.WritePairAdjustment("DFLT", "kern", first, b, -192);
        return font;
    }

    private static FontBuilder CreateAatFont(string familyName, string postScriptName)
    {
        var font = new FontBuilder
        {
            FamilyName = familyName,
            PostScriptName = postScriptName,
            UnitsPerEm = 1024,
            Ascender = 832,
            Descender = -192,
            TypoAscender = 832,
            TypoDescender = -192,
            WinAscent = 832,
            WinDescent = 192,
            CmapFormats = FontCmapFormats.Format4,
        };

        font.AddGlyph(FontGlyph.Frame(512, 64, 0, 448, 768, 64));
        font.AddGlyph(FontGlyph.Empty(320), ' ');
        return font;
    }

    /// <summary>The ligatures fi, fl and ffi: the components are pushed, and a ligature action pops them.</summary>
    private static byte[] WriteLigatures(Dictionary<string, int> g)
    {
        const int SetComponent = 0x8000;
        const int PerformAction = 0x2000;

        // Classes: f 4, i 5, l 6. States: 0 and 1 (start), 2 (after f), 3 (after ff).
        var classes = new Dictionary<int, int> { [g["f"]] = 4, [g["i"]] = 5, [g["l"]] = 6 };
        int[] start = [0, 0, 0, 0, 1, 0, 0];
        int[] afterF = [0, 0, 0, 0, 2, 3, 4];
        int[] afterFf = [0, 0, 0, 0, 2, 5, 4];
        int[][] entries =
        [
            [0, 0, 0],
            [2, SetComponent, 0],
            [3, SetComponent, 0],
            [0, SetComponent | PerformAction, 0],
            [0, SetComponent | PerformAction, 2],
            [0, SetComponent | PerformAction, 4],
        ];

        // The component table: i 0 (index 0), f 0 (index 1), l 1 (index 2), the second f of ffi 2 (index 3). The sum of
        // the entries of the components is the index of the ligature: fi 0, fl 1, ffi 2.
        static uint Action(int componentIndex, int glyph, bool isLast) => (isLast ? 0x80000000u : 0) | ((uint)(componentIndex - glyph) & 0x3FFFFFFF);
        uint[] actions =
        [
            Action(0, g["i"], isLast: false), Action(1, g["f"], isLast: true),
            Action(2, g["l"], isLast: false), Action(1, g["f"], isLast: true),
            Action(0, g["i"], isLast: false), Action(3, g["f"], isLast: false), Action(1, g["f"], isLast: true),
        ];

        return FontAatWriter.WriteStateTable(classes, 7, [start, start, afterF, afterFf], entries,
        [
            FontAatWriter.WriteUInt32Array(actions),
            FontAatWriter.WriteUInt16Array([0, 0, 1, 2]),
            FontAatWriter.WriteUInt16Array([g["fi"], g["fl"], g["ffi"]]),
        ]);
    }

    /// <summary>T after A is replaced (current glyph); X is marked, and replaced when Y follows it (marked glyph).</summary>
    private static byte[] WriteContextualAlternates(Dictionary<string, int> g)
    {
        const int SetMark = 0x8000;

        // Classes: A 4, T 5, X 6, Y 7. States: 0 and 1 (start), 2 (after A), 3 (after X).
        var classes = new Dictionary<int, int> { [g["A"]] = 4, [g["T"]] = 5, [g["X"]] = 6, [g["Y"]] = 7 };
        int[] start = [0, 0, 0, 0, 1, 0, 2, 0];
        int[] afterA = [0, 0, 0, 0, 1, 3, 2, 0];
        int[] afterX = [0, 0, 0, 0, 1, 0, 2, 4];
        int[][] entries =
        [
            [0, 0, 0xFFFF, 0xFFFF],
            [2, 0, 0xFFFF, 0xFFFF],
            [3, SetMark, 0xFFFF, 0xFFFF],
            [0, 0, 0xFFFF, 0],
            [0, 0, 1, 0xFFFF],
        ];

        return FontAatWriter.WriteStateTable(classes, 8, [start, start, afterA, afterX], entries,
        [
            FontAatWriter.WriteSubstitutionTables([new Dictionary<int, int> { [g["T"]] = g["T.alt"] }, new Dictionary<int, int> { [g["X"]] = g["X.alt"] }]),
        ]);
    }

    /// <summary>M marks the first glyph of a range, N the last one, with the verb AxD => DxA.</summary>
    private static byte[] WriteRearrangement(Dictionary<string, int> g)
    {
        const int MarkFirst = 0x8000;
        const int MarkLast = 0x2000;

        var classes = new Dictionary<int, int> { [g["M"]] = 4, [g["N"]] = 5 };
        int[] state = [0, 0, 0, 0, 1, 2];
        int[][] entries = [[0, 0], [0, MarkFirst], [0, MarkLast | 3]];
        return FontAatWriter.WriteStateTable(classes, 6, [state, state], entries, []);
    }

    /// <summary>A glyph inserted before Q (current glyph, kashida-like); R is marked, and two glyphs are inserted after it when Z comes.</summary>
    private static byte[] WriteInsertions(Dictionary<string, int> g)
    {
        const int SetMark = 0x8000;
        const int CurrentIsKashidaLike = 0x2000;
        const int CurrentInsertBefore = 0x0800;

        var classes = new Dictionary<int, int> { [g["Q"]] = 4, [g["R"]] = 5, [g["Z"]] = 6 };
        int[] state = [0, 0, 0, 0, 1, 2, 3];
        int[][] entries =
        [
            [0, 0, 0xFFFF, 0xFFFF],
            [0, CurrentIsKashidaLike | CurrentInsertBefore | (1 << 5), 0, 0xFFFF],
            [0, SetMark, 0xFFFF, 0xFFFF],
            [0, 2, 0xFFFF, 1],
        ];

        return FontAatWriter.WriteStateTable(classes, 7, [state, state], entries, [FontAatWriter.WriteUInt16Array([g["Q.before"], g["R.tail1"], g["R.tail2"]])]);
    }

    /// <summary>A subtable that reads the glyphs from the last one: 1 is replaced when the glyph before it, in its order, is 2.</summary>
    private static byte[] WriteDescendingSubstitution(Dictionary<string, int> g)
    {
        var classes = new Dictionary<int, int> { [g["1"]] = 4, [g["2"]] = 5 };
        int[] start = [0, 0, 0, 0, 0, 1];
        int[] after2 = [0, 0, 0, 0, 2, 1];
        int[][] entries = [[0, 0, 0xFFFF, 0xFFFF], [2, 0, 0xFFFF, 0xFFFF], [0, 0, 0xFFFF, 0]];
        return FontAatWriter.WriteStateTable(classes, 6, [start, start, after2], entries, [FontAatWriter.WriteSubstitutionTables([new Dictionary<int, int> { [g["1"]] = g["1.alt"] }])]);
    }

    /// <summary>A glyph is marked, and replaced when another one follows it, in the order the subtable reads them.</summary>
    private static byte[] WriteMarkedSubstitution(Dictionary<string, int> g, string marked, string next, string replacement)
    {
        const int SetMark = 0x8000;

        var classes = new Dictionary<int, int> { [g[marked]] = 4, [g[next]] = 5 };
        int[] start = [0, 0, 0, 0, 1, 0];
        int[] afterMarked = [0, 0, 0, 0, 1, 2];
        int[][] entries = [[0, 0, 0xFFFF, 0xFFFF], [2, SetMark, 0xFFFF, 0xFFFF], [0, 0, 0, 0xFFFF]];
        return FontAatWriter.WriteStateTable(classes, 6, [start, start, afterMarked], entries, [FontAatWriter.WriteSubstitutionTables([new Dictionary<int, int> { [g[marked]] = g[replacement] }])]);
    }

    /// <summary>The first glyph is pushed; the second one is pushed and the values kern the glyphs popped, from the last pushed.</summary>
    /// <remarks>
    /// The list of values pops the whole stack (eight glyphs): the glyphs pushed before are kerned by 0. The reference
    /// shaper reads the end of a list as a last value of -2 when glyphs remain on the stack, which the specification
    /// does not say ("The end of the list is marked by the value 0xFFFF").
    /// </remarks>
    private static byte[] WriteContextualKerning(int first, int second, int[] values)
    {
        const int Push = 0x8000;

        var classes = new Dictionary<int, int> { [first] = 4, [second] = 5 };
        int[] state = [0, 0, 0, 0, 1, 2];
        int[][] entries = [[0, 0, 0xFFFF], [0, Push, 0xFFFF], [0, Push, 0]];
        return FontAatWriter.WriteStateTable(classes, 6, [state, state], entries, [FontAatWriter.WriteUInt16Array([.. values, .. new int[8 - values.Length], 0xFFFF])]);
    }

    /// <summary>A or B is marked; a mark that follows is placed by the points of the action.</summary>
    private static byte[] WriteAttachments(Dictionary<string, int> g, string mark, int actionType, int[] action)
    {
        const int Mark = 0x8000;

        var classes = new Dictionary<int, int> { [g["A"]] = 4, [g["B"]] = 4, [g[mark]] = 5 };
        int[] state = [0, 0, 0, 0, 1, 2];
        int[][] entries = [[0, 0, 0xFFFF], [0, Mark, 0xFFFF], [0, 0, 0]];

        // The flags of the subtable: the type of action in the two high bits, the offset of the actions in the others.
        return FontAatWriter.WriteStateTable(classes, 6, [state, state], entries, [FontAatWriter.WriteUInt16Array(action)], (_, offset) => ((uint)actionType << 30) | offset);
    }
}
