#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework;

/// <summary>Writes the IDNA mapping table of Unicode IDNA Compatibility Processing (Unicode Technical Standard #46).</summary>
internal static class IdnaWriter
{
    // The values of UnicodeIdna.IdnaStatus
    private const byte Valid = 0;
    private const byte Ignored = 1;
    private const byte Mapped = 2;
    private const byte Deviation = 3;
    private const byte Disallowed = 4;

    public static async Task<bool> WriteAsync(FullPath outputPath)
    {
        Console.WriteLine("Loading the IDNA mapping table...");
        const string Source = "idna/IdnaMappingTable.txt";

        // An integer that is not a code point is disallowed.
        var statuses = new byte[Trie.CodePointCount + 1];
        statuses.AsSpan().Fill(Disallowed);
        var mappings = new SortedDictionary<int, string>();
        var expected = 0;
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync(Source)))
        {
            if (line.IsMissing)
                continue;

            if (line.Start != expected)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{Source} is not contiguous at U+{line.Start:X4}"));

            expected = line.End + 1;
            var status = line.Fields[0] switch
            {
                "valid" => Valid,
                "ignored" => Ignored,
                "mapped" => Mapped,
                "deviation" => Deviation,
                "disallowed" => Disallowed,
                _ => throw new InvalidOperationException($"Unknown IDNA status '{line.Fields[0]}'"),
            };

            statuses.AsSpan(line.Start, line.End - line.Start + 1).Fill(status);
            if (status is Mapped or Deviation && line.Fields.Length > 1 && line.Fields[1].Length > 0)
            {
                var mapping = string.Concat(line.Fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(item => char.ConvertFromUtf32(int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
                for (var codePoint = line.Start; codePoint <= line.End; codePoint++)
                {
                    mappings.Add(codePoint, mapping);
                }
            }
            else if (status is Mapped)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The mapped code point U+{line.Start:X4} has no mapping"));
            }
        }

        if (expected != Trie.CodePointCount)
            throw new InvalidOperationException(Source + " does not cover all the code points");

        // The mappings are stored once in a pool of UTF-16 code units: many code points share a mapping.
        var pool = new StringBuilder();
        var poolOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var records = new List<byte>();
        var maxLength = 0;
        foreach (var (codePoint, mapping) in mappings)
        {
            if (!poolOffsets.TryGetValue(mapping, out var offset))
            {
                offset = pool.Length;
                poolOffsets.Add(mapping, offset);
                pool.Append(mapping);
            }

            if (mapping.Length > byte.MaxValue)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The mapping of U+{codePoint:X4} is too long"));

            if (offset > ushort.MaxValue)
                throw new InvalidOperationException("The pool of the IDNA mappings is too large for a two-byte offset");

            maxLength = Math.Max(maxLength, mapping.Length);
            records.AddRange(PropertyTablesWriter.EncodeUInt24([codePoint]));
            records.Add((byte)offset);
            records.Add((byte)(offset >> 8));
            records.Add((byte)mapping.Length);
        }

        var sizes = new List<(string Name, int Bytes)>();
        var tables = new StringBuilder();
        PropertyTablesWriter.AppendTrie(tables, sizes, "Idna", statuses, """
            The IDNA status of the code point: 0 valid, 1 ignored, 2 mapped, 3 deviation, 4 disallowed.
            """);
        PropertyTablesWriter.AppendBytes(tables, sizes, "IdnaMappings", [.. records],
            "The mappings of the mapped and deviation code points: 6-byte entries sorted by code point: the code point (3 bytes), then the offset (2 bytes) and the length (1 byte) of its mapping in IdnaPool, in UTF-16 code units, little-endian. A deviation code point that is not listed maps to nothing.");
        var poolBytes = new byte[pool.Length * 2];
        for (var i = 0; i < pool.Length; i++)
        {
            poolBytes[i * 2] = (byte)pool[i];
            poolBytes[(i * 2) + 1] = (byte)(pool[i] >> 8);
        }

        PropertyTablesWriter.AppendBytes(tables, sizes, "IdnaPool", poolBytes, "UTF-16 code units of the IDNA mappings, in little-endian order.");

        var sizeLines = string.Join('\n', sizes.Select(size => string.Create(CultureInfo.InvariantCulture, $"//   - {size.Name}: {size.Bytes:N0} bytes")));
        var content = string.Create(CultureInfo.InvariantCulture, $$"""
            // <auto-generated />
            // Generated from the IDNA mapping table {{Ucd.Version}}
            // Data source: {{Ucd.GetUrl(Source)}}
            // Sizes ({{sizes.Sum(size => size.Bytes):N0}} bytes):
            {{sizeLines}}
            //
            // Specification: https://www.unicode.org/reports/tr46/
            // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

            #nullable enable

            using System.Runtime.CompilerServices;

            namespace Meziantou.Framework;

            /// <summary>The IDNA mapping table of Unicode IDNA Compatibility Processing.</summary>
            internal static class UnicodeIdnaTables
            {
                /// <summary>The greatest length, in UTF-16 code units, of the mapping of a code point.</summary>
                internal const int MaxMappingLength = {{maxLength}};

            {{PropertyTablesWriter.ReadUInt16Method}}
            {{tables.ToString().TrimEnd()}}
            }

            """).ReplaceLineEndings("\n");

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  IDNA tables: {sizes.Sum(size => size.Bytes):N0} bytes ({mappings.Count:N0} mappings)"));
        return await GeneratorFiles.WriteTextIfChanged(outputPath / "UnicodeIdnaTables.g.cs", content);
    }
}
