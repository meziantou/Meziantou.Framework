using System.Buffers.Binary;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Fonts.Fuzz;

/// <summary>
/// The hinting of a font must only throw <see cref="FontException"/>. A hinted outline must have finite coordinates,
/// the contours of the outline the font draws, and, with the TrueType instructions, an advance that is a whole number of
/// pixels. It must not depend on the glyphs hinted before it nor on the storage the hinter rents. A subset must load,
/// and keep the advances and the bounds of the glyphs of its closure.
/// </summary>
public static class FontsFuzzTargets
{
    private const int MaxHintedGlyphs = 24;
    private const int MaxComparedGlyphs = 512;
    private const uint Cvt = ((uint)'c' << 24) | ((uint)'v' << 16) | ((uint)'t' << 8) | ' ';
    private const uint Fpgm = ((uint)'f' << 24) | ((uint)'p' << 16) | ((uint)'g' << 8) | 'm';
    private const uint Prep = ((uint)'p' << 24) | ((uint)'r' << 16) | ((uint)'e' << 8) | 'p';

    // A glyph program that loops forever exhausts the budget quickly
    private static readonly FontResourceLimits Limits = new()
    {
        MaxEncodedBytes = 4 * 1024 * 1024,
        MaxFontBytes = 16 * 1024 * 1024,
        MaxGlyphComplexity = 200_000,
    };

    private static readonly PooledFontEnvironment SharedEnvironment = new(Limits, 64 * 1024 * 1024);

    // The fonts the bytecode target puts its programs in: hinted fonts, whose functions the programs can call, and a font without instructions
    private static readonly string[] BytecodeBaseNames =
    [
        "OpenSans-Regular.subset.ttf",
        "DroidSans.subset.ttf",
        "Roboto-Regular.subset.ttf",
        "57a9d9f83020155cbb1d2be1f43d82388cbecc88.ttf",
        "boxes.ttf",
    ];

    private static readonly Lazy<List<BaseFont>> BytecodeBases = new(LoadBytecodeBases);

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("fonts-hinting", Hinting) { Seeds = GetHintingSeeds },
        new("fonts-hinting-bytecode", HintingBytecode) { Seeds = GetBytecodeSeeds },
        new("fonts-subset", Subset) { Seeds = () => GetFontSeeds().Select((seed, index) => seed with { Data = (byte[])[(byte)index, (byte)(index * 13), .. seed.Data.Span] }) },
    ];

    /// <summary>Hints the glyphs of a font file: option bytes give the hinting mode and the size.</summary>
    private static void Hinting(ReadOnlySpan<byte> data)
    {
        var mode = (GlyphHinting)(FuzzInput.ReadByte(ref data) % 3);
        var size = ReadSize(FuzzInput.ReadByte(ref data));
        FontFace[] faces;
        try
        {
            faces = FontLoader.Load(data, SharedEnvironment);
        }
        catch (FontException)
        {
            return;
        }

        foreach (var face in faces.Take(2))
        {
            CheckHinting(face, size, mode, GetHintedGlyphs(face));
        }
    }

    /// <summary>
    /// Runs TrueType programs from the input in a valid font: option bytes give the font, the size, the programs to
    /// replace and the glyph whose instructions are replaced, then come the font program, the control value program and
    /// the control values (each one a 16-bit big-endian length and its bytes), and the instructions of the glyph.
    /// </summary>
    private static void HintingBytecode(ReadOnlySpan<byte> data)
    {
        var bases = BytecodeBases.Value;
        var baseFont = bases[FuzzInput.ReadByte(ref data) % bases.Count];
        var size = ReadSize(FuzzInput.ReadByte(ref data));
        var flags = FuzzInput.ReadByte(ref data);
        var glyphChoice = FuzzInput.ReadByte(ref data);

        var tables = new SortedDictionary<uint, byte[]>(baseFont.Tables);
        var head = (byte[])tables[FontTags.Head].Clone();
        tables[FontTags.Head] = head;
        if ((flags & 0x10) != 0)
        {
            // Bit 3 of the flags of 'head': the size is rounded to a whole number of pixels
            BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(16), (ushort)(BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(16)) ^ 8));
        }

        if ((flags & 0x01) != 0)
        {
            // The sizes maxp declares for the state of the programs
            var maxp = (byte[])tables[FontTags.Maxp].Clone();
            tables[FontTags.Maxp] = maxp;
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(14), 2);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(16), FuzzInput.ReadByte(ref data));
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(18), FuzzInput.ReadByte(ref data));
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(20), FuzzInput.ReadByte(ref data));
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(22), FuzzInput.ReadByte(ref data));
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(24), (ushort)(FuzzInput.ReadByte(ref data) * 4));
        }

        if ((flags & 0x02) != 0)
        {
            SetTable(tables, Fpgm, ReadSection(ref data));
        }

        if ((flags & 0x04) != 0)
        {
            SetTable(tables, Prep, ReadSection(ref data));
        }

        if ((flags & 0x08) != 0)
        {
            SetTable(tables, Cvt, ReadSection(ref data));
        }

        var glyph = baseFont.SimpleGlyphs[glyphChoice % baseFont.SimpleGlyphs.Length];
        var glyphs = (byte[][])baseFont.Glyphs.Clone();
        glyphs[glyph] = WithInstructions(glyphs[glyph], data.Length > ushort.MaxValue ? data[..ushort.MaxValue] : data);
        (tables[FontTags.Glyf], tables[FontTags.Loca]) = WriteGlyf(glyphs);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);

        var face = LoadSingle(FontSubsetter.WriteSfnt(FontTags.TrueType, tables), "The font with the programs of the input");
        var hinted = new List<int> { glyph };
        hinted.AddRange(Enumerable.Range(0, Math.Min(face.GlyphCount, 8)).Where(other => other != glyph));
        CheckHinting(face, size, GlyphHinting.Full, hinted);
    }

    /// <summary>Subsets a font file: option bytes give whether the instructions are kept and the glyphs to keep.</summary>
    private static void Subset(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var count = FuzzInput.ReadByte(ref data);
        FontFace[] faces;
        try
        {
            faces = FontLoader.Load(data, SharedEnvironment);
        }
        catch (FontException)
        {
            return;
        }

        foreach (var face in faces.Take(2))
        {
            SubsetFace(face, keepHinting: (flags & 1) != 0, stride: ((flags >> 1) % 16) + 1, count: (count % 64) + 1);
        }
    }

    private static void CheckHinting(FontFace face, double size, GlyphHinting mode, List<int> glyphs)
    {
        var allocator = SharedEnvironment.CreateAllocator("fuzz");
        using var scratch = new GlyphScratch(allocator);
        GlyphOutcome[] forward;
        GlyphHinting hinting;
        using (var first = CreateHinter(face, size, mode, scratch, allocator, out _))
        {
            if (first is null)
                return;

            hinting = first.Hinting;
            FuzzAssert.True(mode == GlyphHinting.Full ? hinting is GlyphHinting.Full or GlyphHinting.Light : hinting == mode, $"The hinter hints with {hinting} when {mode} is asked for");
            forward = [.. glyphs.Select(glyph => Decode(first, glyph, scratch))];

            // The hinter restores the state the control value program left before each glyph
            for (var i = glyphs.Count - 1; i >= 0; i--)
            {
                CheckSameOutcome(forward[i], Decode(first, glyphs[i], scratch), $"The hinted outline of the glyph {glyphs[i]} depends on the glyphs hinted before it");
            }
        }

        for (var i = 0; i < glyphs.Count; i++)
        {
            if (forward[i].Error is not null)
                continue;

            var unhinted = DecodeUnhinted(face, glyphs[i], scratch);
            if (unhinted.Error is null)
            {
                FuzzAssert.Equal(unhinted.Contours, forward[i].Contours, $"The hinted outline of the glyph {glyphs[i]} does not have the contours of the outline");
            }
        }

        // A second hinter rents the storage the first one gave back, without clearing it
        using (var second = CreateHinter(face, size, mode, scratch, allocator, out var error))
        {
            if (second is null)
            {
                FuzzAssert.Fail($"A second hinter cannot be created: {error}");
                return;
            }

            FuzzAssert.Equal(hinting, second.Hinting, "A second hinter hints differently");
            for (var i = 0; i < glyphs.Count; i++)
            {
                CheckSameOutcome(forward[i], Decode(second, glyphs[i], scratch), $"A second hinter gives another outline for the glyph {glyphs[i]}");
            }
        }
    }

    private static GlyphHinter? CreateHinter(FontFace face, double size, GlyphHinting mode, GlyphScratch scratch, FontAllocator allocator, out string? error)
    {
        try
        {
            error = null;
            return GlyphHinter.Create(face, size, mode, scratch, allocator);
        }
        catch (FontException ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static GlyphOutcome Decode(GlyphHinter hinter, int glyph, GlyphScratch scratch)
    {
        var sink = new RecordingSink();
        var budget = new GlyphBudget(Limits);
        try
        {
            var advance = hinter.DecodeOutline(glyph, sink, scratch, ref budget);
            FuzzAssert.True(double.IsFinite(advance), $"The advance of the glyph {glyph} is {advance}");
            if (hinter.Hinting == GlyphHinting.Full)
            {
                FuzzAssert.Equal(Math.Round(advance, MidpointRounding.AwayFromZero), advance, $"The advance of the glyph {glyph} is not a whole number of pixels");
            }

            return new GlyphOutcome(Error: null, advance, [.. sink.Values], sink.Contours);
        }
        catch (FontException ex)
        {
            return new GlyphOutcome(ex.GetType().Name, Advance: 0, [], Contours: 0);
        }
    }

    private static GlyphOutcome DecodeUnhinted(FontFace face, int glyph, GlyphScratch scratch)
    {
        var sink = new RecordingSink();
        var budget = new GlyphBudget(Limits);
        try
        {
            face.DecodeOutline(glyph, sink, scratch, ref budget);
            return new GlyphOutcome(Error: null, Advance: 0, [.. sink.Values], sink.Contours);
        }
        catch (FontException ex)
        {
            return new GlyphOutcome(ex.GetType().Name, Advance: 0, [], Contours: 0);
        }
    }

    private static void CheckSameOutcome(GlyphOutcome expected, GlyphOutcome actual, string message)
    {
        FuzzAssert.Equal(expected.Error, actual.Error, message);
        FuzzAssert.Equal(expected.Advance, actual.Advance, message);
        FuzzAssert.Equal(expected.Path.Length, actual.Path.Length, message);
        FuzzAssert.True(expected.Path.AsSpan().SequenceEqual(actual.Path), message);
    }

    private static List<int> GetHintedGlyphs(FontFace face)
    {
        var glyphs = new List<int>();
        for (var glyph = 0; glyph < Math.Min(face.GlyphCount, MaxHintedGlyphs - 6); glyph++)
        {
            glyphs.Add(glyph);
        }

        foreach (var codePoint in "Aag@\u00E9")
        {
            try
            {
                var glyph = face.MapCodePoint(codePoint);
                if (!glyphs.Contains(glyph))
                {
                    glyphs.Add(glyph);
                }
            }
            catch (FontException)
            {
            }
        }

        // The last glyph, and a glyph the font does not have
        if (face.GlyphCount > 0 && !glyphs.Contains(face.GlyphCount - 1))
        {
            glyphs.Add(face.GlyphCount - 1);
        }

        glyphs.Add(face.GlyphCount);
        return glyphs;
    }

    private static void SubsetFace(FontFace face, bool keepHinting, int stride, int count)
    {
        var requested = new List<int> { -1, face.GlyphCount, int.MaxValue };
        foreach (var codePoint in "AZae\u00E9fi\u672C")
        {
            try
            {
                requested.Add(face.MapCodePoint(codePoint));
            }
            catch (FontException)
            {
            }
        }

        for (int i = 0, glyph = 1; i < count && glyph < face.GlyphCount; i++, glyph += stride)
        {
            requested.Add(glyph);
        }

        SortedSet<int> closure;
        try
        {
            closure = FontSubsetter.GetClosure(face, requested);
        }
        catch (FontException)
        {
            return;
        }

        FuzzAssert.True(closure.Contains(0), "The closure does not have the glyph 0");
        foreach (var glyph in closure)
        {
            FuzzAssert.True(glyph == 0 || (glyph > 0 && glyph < face.GlyphCount), $"The closure has the glyph {glyph}, which the font does not have ({face.GlyphCount} glyphs)");
        }

        foreach (var glyph in requested.Where(glyph => glyph >= 0 && glyph < face.GlyphCount))
        {
            FuzzAssert.True(closure.Contains(glyph), $"The closure does not have the requested glyph {glyph}");
        }

        try
        {
            FuzzAssert.True(FontSubsetter.GetClosure(face, closure).SetEquals(closure), "The closure of the closure has other glyphs");
        }
        catch (FontException ex)
        {
            FuzzAssert.Fail($"The closure of the closure throws: {ex.GetType().Name}: {ex.Message}");
        }

        switch (face.Outlines)
        {
            case FontOutlineKind.TrueType:
                SubsetTrueType(face, requested, closure, keepHinting);
                break;

            case FontOutlineKind.Cff:
                SubsetCff(face, requested, closure);
                break;

            default:
                CheckThrows<ArgumentException>(() => FontSubsetter.SubsetTrueType(face, requested), $"SubsetTrueType accepts a face with {face.Outlines} outlines");
                CheckThrows<ArgumentException>(() => FontSubsetter.SubsetCff(face, requested, "ABCDEF+Fuzz"), $"SubsetCff accepts a face with {face.Outlines} outlines");
                break;
        }
    }

    private static void SubsetTrueType(FontFace face, List<int> requested, SortedSet<int> closure, bool keepHinting)
    {
        CheckThrows<ArgumentException>(() => FontSubsetter.SubsetCff(face, requested, "ABCDEF+Fuzz"), "SubsetCff accepts a face with TrueType outlines");

        byte[] program;
        try
        {
            program = FontSubsetter.SubsetTrueType(face, requested, keepHinting);
        }
        catch (FontException)
        {
            return;
        }

        // The other tables of the font are added to read the subset as a font
        var programTables = ReadTables(program);
        var tables = CopyTables(face);
        foreach (var (tag, table) in programTables)
        {
            tables[tag] = table;
        }

        var subset = LoadSingle(FontSubsetter.WriteSfnt(FontTags.TrueType, tables), "The subset");
        CompareGlyphs(face, subset, closure);

        // Subsetting the subset to the same glyphs copies the same glyphs
        byte[] again;
        try
        {
            again = FontSubsetter.SubsetTrueType(subset, closure, keepHinting);
        }
        catch (FontException ex)
        {
            FuzzAssert.Fail($"The subset cannot be subset again: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // The glyphs are copied as they are when the instructions are kept. Removing them is not stable for a glyph whose
        // instructions run past its end: the padding the subset adds after it can make them fit.
        if (!keepHinting)
            return;

        var againTables = ReadTables(again);
        FuzzAssert.True(againTables[FontTags.Glyf].AsSpan().SequenceEqual(programTables[FontTags.Glyf]), "Subsetting the subset gives another 'glyf' table");
        FuzzAssert.True(againTables[FontTags.Loca].AsSpan().SequenceEqual(programTables[FontTags.Loca]), "Subsetting the subset gives another 'loca' table");
    }

    private static void SubsetCff(FontFace face, List<int> requested, SortedSet<int> closure)
    {
        CheckThrows<ArgumentException>(() => FontSubsetter.SubsetTrueType(face, requested), "SubsetTrueType accepts a face with CFF outlines");

        byte[] cff;
        try
        {
            cff = FontSubsetter.SubsetCff(face, requested, "ABCDEF+Fuzz");
        }
        catch (FontException)
        {
            return;
        }

        var tables = CopyTables(face);
        tables[FontTags.Cff] = cff;
        var subset = LoadSingle(FontSubsetter.WriteSfnt(FontTags.OpenTypeCff, tables), "The CFF subset");
        CompareGlyphs(face, subset, closure);
    }

    private static void CompareGlyphs(FontFace face, FontFace subset, SortedSet<int> closure)
    {
        FuzzAssert.Equal(face.GlyphCount, subset.GlyphCount, "The subset does not have the glyphs of the font");
        FuzzAssert.Equal(face.Outlines, subset.Outlines, "The subset does not have the outlines of the font");
        var allocator = SharedEnvironment.CreateAllocator("fuzz");
        using var scratch = new GlyphScratch(allocator);
        for (var glyph = 0; glyph < Math.Min(face.GlyphCount, MaxComparedGlyphs); glyph++)
        {
            FuzzAssert.Equal(face.GetAdvanceWidth(glyph), subset.GetAdvanceWidth(glyph), $"The subset changes the advance of the glyph {glyph}");
            var original = GetBounds(face, glyph, scratch);
            var actual = GetBounds(subset, glyph, scratch);
            if (closure.Contains(glyph))
            {
                FuzzAssert.Equal(original, actual, $"The subset changes the bounds of the glyph {glyph}");
            }
            else if (actual != "none")
            {
                // A glyph that is not kept is empty, unless what fails it is outside its outline, such as the font
                // DICT a CID-keyed font selects for it
                FuzzAssert.Equal(original, actual, $"The subset keeps the glyph {glyph}, which is not in the closure");
            }
        }
    }

    private static string GetBounds(FontFace face, int glyph, GlyphScratch scratch)
    {
        try
        {
            return face.TryGetGlyphBounds(glyph, scratch, out var bounds) ? bounds.ToString() : "none";
        }
        catch (FontException ex)
        {
            return "error " + ex.GetType().Name;
        }
    }

    private static void CheckThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        FuzzAssert.Fail(message);
    }

    private static FontFace LoadSingle(byte[] font, string name)
    {
        FontFace[] faces;
        try
        {
            faces = FontLoader.Load(font, SharedEnvironment);
        }
        catch (FontException ex)
        {
            FuzzAssert.Fail($"{name} cannot be loaded: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        FuzzAssert.Equal(1, faces.Length, $"{name} does not have one face");
        return faces[0];
    }

    private static SortedDictionary<uint, byte[]> CopyTables(FontFace face)
    {
        var tables = new SortedDictionary<uint, byte[]>();
        foreach (var tag in face.TableTags)
        {
            if (face.TryGetTable(tag, out var table))
            {
                tables[tag] = table.ToArray();
            }
        }

        return tables;
    }

    /// <summary>Reads the tables of an sfnt that <see cref="FontSubsetter"/> wrote.</summary>
    private static Dictionary<uint, byte[]> ReadTables(byte[] font)
    {
        var tables = new Dictionary<uint, byte[]>();
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = font.AsSpan(12 + (i * 16), 16);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            tables[BinaryPrimitives.ReadUInt32BigEndian(record)] = font.AsSpan(offset, length).ToArray();
        }

        return tables;
    }

    private static double ReadSize(byte value)
    {
        // 6 to 69.5 pixels per em, by halves, and the same sizes times 4
        var size = 6 + (value & 0x3F) + ((value & 0x40) != 0 ? 0.5 : 0);
        return (value & 0x80) != 0 ? size * 4 : size;
    }

    private static byte[] ReadSection(ref ReadOnlySpan<byte> data)
    {
        var length = (FuzzInput.ReadByte(ref data) << 8) | FuzzInput.ReadByte(ref data);
        length = Math.Min(length, data.Length);
        var section = data[..length].ToArray();
        data = data[length..];
        return section;
    }

    private static void SetTable(SortedDictionary<uint, byte[]> tables, uint tag, byte[] table)
    {
        if (table.Length == 0)
        {
            tables.Remove(tag);
        }
        else
        {
            tables[tag] = table;
        }
    }

    /// <summary>Replaces the instructions of a simple glyph (OpenType specification, "glyf", "Simple glyph description").</summary>
    private static byte[] WithInstructions(byte[] glyph, ReadOnlySpan<byte> instructions)
    {
        var offset = GetInstructionsOffset(glyph);
        var length = BinaryPrimitives.ReadUInt16BigEndian(glyph.AsSpan(offset));
        var rest = glyph.AsSpan(offset + 2 + length);
        var result = new byte[offset + 2 + instructions.Length + rest.Length];
        glyph.AsSpan(0, offset).CopyTo(result);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)instructions.Length);
        instructions.CopyTo(result.AsSpan(offset + 2));
        rest.CopyTo(result.AsSpan(offset + 2 + instructions.Length));
        return result;
    }

    private static int GetInstructionsOffset(byte[] glyph) => 10 + (2 * BinaryPrimitives.ReadInt16BigEndian(glyph));

    private static (byte[] Glyf, byte[] Loca) WriteGlyf(byte[][] glyphs)
    {
        var loca = new byte[(glyphs.Length + 1) * 4];
        var glyf = new List<byte>();
        for (var i = 0; i < glyphs.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), (uint)glyf.Count);
            glyf.AddRange(glyphs[i]);
            while (glyf.Count % 4 != 0)
            {
                glyf.Add(0);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(glyphs.Length * 4), (uint)glyf.Count);
        return ([.. glyf], loca);
    }

    private static List<BaseFont> LoadBytecodeBases()
    {
        var fonts = GetFontSeeds().ToDictionary(seed => seed.Name, seed => seed.Data, StringComparer.Ordinal);
        var bases = new List<BaseFont>();
        foreach (var name in BytecodeBaseNames)
        {
            var face = FontLoader.Load(fonts[name].Span, SharedEnvironment)[0];
            var tables = CopyTables(face);

            // maxp version 1.0 has the sizes of the state of the programs
            var maxp = tables[FontTags.Maxp];
            if (maxp.Length < 32)
            {
                var extended = new byte[32];
                maxp.CopyTo(extended, 0);
                BinaryPrimitives.WriteUInt32BigEndian(extended, 0x00010000);
                BinaryPrimitives.WriteUInt16BigEndian(extended.AsSpan(14), 2);
                tables[FontTags.Maxp] = extended;
            }

            var glyf = tables[FontTags.Glyf];
            var loca = tables[FontTags.Loca];
            var isLong = BinaryPrimitives.ReadInt16BigEndian(tables[FontTags.Head].AsSpan(50)) != 0;
            var glyphs = new byte[face.GlyphCount][];
            var simpleGlyphs = new List<int>();
            for (var glyph = 0; glyph < face.GlyphCount; glyph++)
            {
                var start = isLong ? (int)BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(glyph * 4)) : BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(glyph * 2)) * 2;
                var end = isLong ? (int)BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan((glyph + 1) * 4)) : BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan((glyph + 1) * 2)) * 2;
                glyphs[glyph] = glyf.AsSpan(start, end - start).ToArray();
                if (glyphs[glyph].Length >= 12 && BinaryPrimitives.ReadInt16BigEndian(glyphs[glyph]) > 0 && GetInstructionsOffset(glyphs[glyph]) + 2 <= glyphs[glyph].Length)
                {
                    simpleGlyphs.Add(glyph);
                }
            }

            bases.Add(new BaseFont(name, tables, glyphs, [.. simpleGlyphs]));
        }

        return bases;
    }

    private static IEnumerable<FuzzSeed> GetHintingSeeds()
    {
        var index = 0;
        foreach (var seed in GetFontSeeds())
        {
            foreach (var mode in new[] { GlyphHinting.Full, GlyphHinting.Light })
            {
                byte[] prefix = [(byte)mode, (byte)(index++ * 11)];
                yield return new FuzzSeed($"{mode}-{seed.Name}", (byte[])[.. prefix, .. seed.Data.Span]);
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetBytecodeSeeds()
    {
        var bases = BytecodeBases.Value;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var baseFont = bases[baseIndex];

            // The programs of the font itself, with the instructions of its first simple glyphs
            for (var glyphChoice = 0; glyphChoice < Math.Min(baseFont.SimpleGlyphs.Length, 3); glyphChoice++)
            {
                var glyph = baseFont.Glyphs[baseFont.SimpleGlyphs[glyphChoice]];
                var offset = GetInstructionsOffset(glyph);
                var instructions = glyph.AsSpan(offset + 2, BinaryPrimitives.ReadUInt16BigEndian(glyph.AsSpan(offset)));
                byte[] data =
                [
                    (byte)baseIndex, (byte)(12 + glyphChoice), 0x02 | 0x04 | 0x08, (byte)glyphChoice,
                    .. Section(baseFont.Tables.GetValueOrDefault(Fpgm)),
                    .. Section(baseFont.Tables.GetValueOrDefault(Prep)),
                    .. Section(baseFont.Tables.GetValueOrDefault(Cvt)),
                    .. instructions,
                ];
                yield return new FuzzSeed($"{baseFont.Name}-{glyphChoice}", data);
            }
        }

        var boxes = bases.Count - 1;

        // A function that touches a point, called by the glyph (PUSHB 0, FDEF, PUSHB 1, MDAP[r], ENDF / PUSHB 0, CALL), with a control value program that does nothing (PUSHB 0, POP)
        yield return new FuzzSeed("boxes-function", (byte[])[(byte)boxes, 12, 0x01 | 0x02 | 0x04, 0, 0, 4, 2, 0, 16, 0, 7, 0xB0, 0, 0x2C, 0xB0, 1, 0x2F, 0x2D, 0, 3, 0xB0, 0, 0x21, 0xB0, 0, 0x2B]);

        // A function that calls itself, which exhausts the budget (FontTests)
        yield return new FuzzSeed("boxes-recursion", (byte[])[(byte)boxes, 12, 0x01 | 0x02 | 0x04, 0, 0, 0, 2, 0, 16, 0, 14, 0xB0, 0, 0x2C, 0x2D, 0xB0, 1, 0x2C, 0xB8, 0x7F, 0xFF, 0xB0, 0, 0x2A, 0x2D, 0, 3, 0xB0, 0, 0x21, 0xB8, 0x7F, 0xFF, 0xB0, 1, 0x2A]);

        static byte[] Section(byte[]? table) => table is null ? [0, 0] : [(byte)(table.Length >> 8), (byte)table.Length, .. table];
    }

    private static IEnumerable<FuzzSeed> GetFontSeeds()
    {
        var assembly = typeof(FontsFuzzTargets).Assembly;
        const string Prefix = "Seeds/fonts/";
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[Prefix.Length..], memory.ToArray());
        }
    }

    private sealed record BaseFont(string Name, SortedDictionary<uint, byte[]> Tables, byte[][] Glyphs, int[] SimpleGlyphs);

    private sealed record GlyphOutcome(string? Error, double Advance, double[] Path, int Contours);

    /// <summary>Records the commands of an outline, and checks that its coordinates are finite.</summary>
    private sealed class RecordingSink : OutlineSink
    {
        public List<double> Values { get; } = [];

        public int Contours { get; private set; }

        public override void MoveTo(double x, double y)
        {
            Contours++;
            Add(0, x, y);
        }

        public override void LineTo(double x, double y) => Add(1, x, y);

        public override void QuadraticTo(double controlX, double controlY, double x, double y)
        {
            Add(2, controlX, controlY);
            Add(x, y);
        }

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
        {
            Add(3, control1X, control1Y);
            Add(control2X, control2Y);
            Add(x, y);
        }

        public override void Close() => Values.Add(4);

        private void Add(int command, double x, double y)
        {
            Values.Add(command);
            Add(x, y);
        }

        private void Add(double x, double y)
        {
            FuzzAssert.True(double.IsFinite(x) && double.IsFinite(y), $"The outline has the point ({x}, {y})");
            Values.Add(x);
            Values.Add(y);
        }
    }
}
