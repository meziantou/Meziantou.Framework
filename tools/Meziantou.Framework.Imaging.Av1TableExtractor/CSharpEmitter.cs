using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Imaging.Av1TableExtractor;

internal static class CSharpEmitter
{
    private const int MaxLineLength = 150;
    private const string Indent = "    ";

    public static IReadOnlyList<(string FileName, string Content)> Emit(Specification specification)
    {
        var files = new List<(string, string)>
        {
            ("Av1Constants.g.cs", EmitConstants(specification.Constants)),
        };

        foreach (var group in specification.Tables.GroupBy(GetGroup).OrderBy(group => group.Key.FileSuffix, StringComparer.Ordinal))
        {
            files.Add(($"Av1Tables.{group.Key.FileSuffix}.g.cs", EmitTables(group.Key.Summary, [.. group])));
        }

        files.Add(("Av1CdfLayout.g.cs", EmitCdfLayout(specification)));
        return files;
    }

    // The coefficient distributions: their default tables have one set per quantizer range (init_coeff_cdfs)
    private static readonly string[] CoefficientCdfs =
    [
        "Default_Txb_Skip_Cdf", "Default_Eob_Pt_16_Cdf", "Default_Eob_Pt_32_Cdf", "Default_Eob_Pt_64_Cdf", "Default_Eob_Pt_128_Cdf",
        "Default_Eob_Pt_256_Cdf", "Default_Eob_Pt_512_Cdf", "Default_Eob_Pt_1024_Cdf", "Default_Eob_Extra_Cdf", "Default_Dc_Sign_Cdf",
        "Default_Coeff_Base_Eob_Cdf", "Default_Coeff_Base_Cdf", "Default_Coeff_Br_Cdf",
    ];

    // The motion vector distributions exist once per motion vector context, and for some of them once per component
    // (init_non_coeff_cdfs: "MvClass0BitCdf[ i ][ comp ] is set to a copy of Default_Mv_Class0_Bit_Cdf")
    private static readonly Dictionary<string, bool> MotionVectorCdfs = new(StringComparer.Ordinal)
    {
        ["Default_Mv_Joint_Cdf"] = false,
        ["Default_Mv_Class_Cdf"] = false,
        ["Default_Mv_Fr_Cdf"] = false,
        ["Default_Mv_Class0_Fr_Cdf"] = false,
        ["Default_Mv_Class0_Bit_Cdf"] = true,
        ["Default_Mv_Class0_Hp_Cdf"] = true,
        ["Default_Mv_Sign_Cdf"] = true,
        ["Default_Mv_Bit_Cdf"] = true,
        ["Default_Mv_Hp_Cdf"] = true,
    };

    private sealed record CdfArray(string Name, string DefaultTable, int Offset, int SymbolsPlusOne, int CopyLength, int Copies, bool IsCoefficient);

    private static string EmitCdfLayout(Specification specification)
    {
        var constants = specification.Constants.ToDictionary(constant => constant.Name, constant => (int)constant.Value, StringComparer.Ordinal);
        var mvContexts = constants["MV_CONTEXTS"];
        var frameLfCount = constants["FRAME_LF_COUNT"];
        var qContexts = constants["COEFF_CDF_Q_CTXS"];

        var defaults = specification.Tables.Where(table => table.Name.StartsWith("Default_", StringComparison.Ordinal) && table.Name.EndsWith("_Cdf", StringComparison.Ordinal)).ToList();
        foreach (var name in CoefficientCdfs.Concat(MotionVectorCdfs.Keys))
        {
            if (!defaults.Exists(table => table.Name == name))
                throw new FatalException($"The default distribution {name} is not defined.");
        }

        var arrays = new List<CdfArray>();
        var offset = 0;
        void Add(SpecTable table, string name, int copies, bool isCoefficient)
        {
            var length = table.Values.Count;
            if (isCoefficient)
            {
                if (table.Dimensions[0] != qContexts)
                    throw new FatalException($"{table.Name}: the first dimension is not COEFF_CDF_Q_CTXS.");

                length /= qContexts;
            }

            arrays.Add(new CdfArray(name, ToPascalCase(table.Name), offset, table.Dimensions[^1], length, copies, isCoefficient));
            offset += length * copies;
        }

        foreach (var table in defaults.Where(table => !CoefficientCdfs.Contains(table.Name, StringComparer.Ordinal)))
        {
            var name = ToPascalCase(table.Name["Default_".Length..^"_Cdf".Length]);
            var copies = MotionVectorCdfs.TryGetValue(table.Name, out var perComponent) ? mvContexts * (perComponent ? 2 : 1) : 1;
            Add(table, name, copies, isCoefficient: false);
            if (table.Name is "Default_Delta_Lf_Cdf")
                Add(table, "DeltaLfMulti", frameLfCount, isCoefficient: false);
        }

        var nonCoefficientLength = offset;
        foreach (var name in CoefficientCdfs)
        {
            var table = defaults.Single(table => table.Name == name);
            Add(table, ToPascalCase(name["Default_".Length..^"_Cdf".Length]), copies: 1, isCoefficient: true);
        }

        var builder = new StringBuilder();
        AppendHeader(builder, [PinnedSpecification.Symbols, PinnedSpecification.AdditionalTables]);
        builder.Append("namespace Meziantou.Framework.Imaging.Internals;\n\n");
        builder.Append("/// <summary>\n");
        builder.Append("/// The layout of the cumulative distribution arrays of a frame context in one buffer: the arrays listed by init_non_coeff_cdfs\n");
        builder.Append("/// and init_coeff_cdfs (AV1 specification, section 6.8.2), each in the row-major order of its default table. An array named X\n");
        builder.Append("/// starts at <c>X</c>; each of its distributions has <c>XSize</c> entries: the cumulative values of its symbols, then the\n");
        builder.Append("/// adaptation counter.\n");
        builder.Append("/// </summary>\n");
        builder.Append("internal static class Av1CdfLayout\n{\n");
        foreach (var array in arrays)
        {
            var copies = array.Copies > 1 ? $", {array.Copies} copies of {array.CopyLength} entries" : "";
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>The offset of the {array.Name} distributions ({array.CopyLength * array.Copies} entries{copies}).</summary>\n");
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}public const int {array.Name} = {array.Offset};\n\n");
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>The number of entries of one {array.Name} distribution.</summary>\n");
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}public const int {array.Name}Size = {array.SymbolsPlusOne};\n\n");
            if (array.Copies > 1)
            {
                builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>The number of entries of one copy of the {array.Name} distributions.</summary>\n");
                builder.Append(CultureInfo.InvariantCulture, $"{Indent}public const int {array.Name}CopyLength = {array.CopyLength};\n\n");
            }
        }

        builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>The number of entries of the distributions that do not code coefficients.</summary>\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}public const int NonCoefficientLength = {nonCoefficientLength};\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>The number of entries of a frame context.</summary>\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}public const int TotalLength = {offset};\n\n");

        builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>Copies the default distributions that do not code coefficients (init_non_coeff_cdfs).</summary>\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}public static void InitializeNonCoefficients(Span<ushort> cdfs)\n{Indent}{{\n");
        foreach (var array in arrays.Where(array => !array.IsCoefficient))
        {
            for (var copy = 0; copy < array.Copies; copy++)
            {
                var start = copy is 0 ? array.Name : $"{array.Name} + {copy * array.CopyLength}";
                builder.Append(CultureInfo.InvariantCulture, $"{Indent}{Indent}Av1Tables.{array.DefaultTable}.CopyTo(cdfs[({start})..]);\n");
            }
        }

        builder.Append(CultureInfo.InvariantCulture, $"{Indent}}}\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>Copies the default coefficient distributions of the quantizer range <paramref name=\"index\"/>, 0 to {qContexts - 1} (init_coeff_cdfs).</summary>\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}public static void InitializeCoefficients(Span<ushort> cdfs, int index)\n{Indent}{{\n");
        foreach (var array in arrays.Where(array => array.IsCoefficient))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}{Indent}Av1Tables.{array.DefaultTable}.Slice(index * {array.CopyLength}, {array.CopyLength}).CopyTo(cdfs[{array.Name}..]);\n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"{Indent}}}\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>Sets the adaptation counter of every distribution to 0 (load_cdfs).</summary>\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}public static void ResetCounters(Span<ushort> cdfs)\n{Indent}{{\n");
        foreach (var group in arrays.GroupBy(array => array.SymbolsPlusOne).OrderBy(group => group.Key))
        {
            // Consecutive arrays whose distributions have the same size are reset by one loop
            var ranges = new List<(int Start, int End)>();
            foreach (var array in group.OrderBy(array => array.Offset))
            {
                var end = array.Offset + (array.CopyLength * array.Copies);
                if (ranges.Count > 0 && ranges[^1].End == array.Offset)
                {
                    ranges[^1] = (ranges[^1].Start, end);
                }
                else
                {
                    ranges.Add((array.Offset, end));
                }
            }

            foreach (var (start, end) in ranges)
            {
                builder.Append(CultureInfo.InvariantCulture, $"{Indent}{Indent}ResetCounters(cdfs[{start}..{end}], {group.Key});\n");
            }
        }

        builder.Append(CultureInfo.InvariantCulture, $"{Indent}}}\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}private static void ResetCounters(Span<ushort> cdfs, int size)\n{Indent}{{\n");
        builder.Append(CultureInfo.InvariantCulture, $"{Indent}{Indent}for (var i = size - 1; i < cdfs.Length; i += size)\n{Indent}{Indent}{{\n{Indent}{Indent}{Indent}cdfs[i] = 0;\n{Indent}{Indent}}}\n{Indent}}}\n");
        builder.Append("}\n");
        return builder.ToString();
    }

    private static (string FileSuffix, string Summary) GetGroup(SpecTable table)
    {
        return table.File switch
        {
            PinnedSpecification.Syntax => ("Syntax", "The constant tables of the syntax structures"),
            PinnedSpecification.Decoding => ("Decoding", "The constant tables of the decoding process"),
            PinnedSpecification.Parsing => ("Parsing", "The constant tables of the parsing process"),
            PinnedSpecification.AdditionalTables => table.Section switch
            {
                "Scan tables" => ("Scans", "The scan order tables"),
                "Conversion tables" => ("Conversion", "The conversion tables"),
                "Default CDF tables" => ("DefaultCdfs", "The default cumulative distribution tables"),
                "Tables" => ("QuantizerMatrix", "The quantizer matrix tables"),
                _ => throw new FatalException($"Table {table.Name}: unexpected section '{table.Section}'."),
            },
            _ => throw new FatalException($"Table {table.Name}: unexpected file '{table.File}'."),
        };
    }

    private static string EmitConstants(IReadOnlyList<SpecConstant> constants)
    {
        var builder = new StringBuilder();
        AppendHeader(builder, constants.Select(constant => constant.File).Distinct(StringComparer.Ordinal));
        builder.Append("namespace Meziantou.Framework.Imaging.Internals;\n\n");
        builder.Append("/// <summary>\n");
        builder.Append("/// The named constants of the AV1 specification: the symbols of its \"Symbols and abbreviated terms\" chapter and the named values\n");
        builder.Append("/// of the syntax elements. Each constant keeps the name of the specification, converted to Pascal case.\n");
        builder.Append("/// </summary>\n");
        builder.Append("internal static class Av1Constants\n{\n");

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var first = true;
        foreach (var constant in constants.OrderBy(constant => constant.Name, StringComparer.Ordinal))
        {
            var name = ToPascalCase(constant.Name);
            if (!names.TryAdd(name, constant.Name))
                throw new FatalException($"Constants {constant.Name} and {names[name]} have the same C# name {name}.");

            if (!first)
                builder.Append('\n');

            first = false;
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary><c>{constant.Name}</c>.</summary>\n");
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}public const int {name} = {checked((int)constant.Value)};\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    private static string EmitTables(string summary, IReadOnlyList<SpecTable> tables)
    {
        var builder = new StringBuilder();
        AppendHeader(builder, tables.Select(table => table.File).Distinct(StringComparer.Ordinal));
        builder.Append("namespace Meziantou.Framework.Imaging.Internals;\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"/// <content>{summary} of the AV1 specification, flattened in row-major order.</content>\n");
        builder.Append("internal static partial class Av1Tables\n{\n");

        var first = true;
        foreach (var table in tables)
        {
            if (!first)
                builder.Append('\n');

            first = false;
            var name = ToPascalCase(table.Name);
            var type = GetElementType(table);
            if (table.Dimensions.Count > 1)
            {
                for (var i = 0; i < table.Dimensions.Count; i++)
                {
                    builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>The size of dimension {i} of <see cref=\"{name}\"/>.</summary>\n");
                    builder.Append(CultureInfo.InvariantCulture, $"{Indent}public const int {name}Length{i} = {table.Dimensions[i]};\n\n");
                }
            }

            builder.Append(CultureInfo.InvariantCulture, $"{Indent}/// <summary>Gets <c>{Escape(table.Declaration)}</c> (section \"{Escape(table.Section)}\").</summary>\n");
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}public static ReadOnlySpan<{type}> {name} =>\n{Indent}[\n");
            AppendValues(builder, table);
            builder.Append(CultureInfo.InvariantCulture, $"{Indent}];\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    private static void AppendValues(StringBuilder builder, SpecTable table)
    {
        // One row of the innermost dimension starts on a new line when the table has several dimensions
        var rowLength = table.Dimensions.Count > 1 ? table.Dimensions[^1] : int.MaxValue;
        var line = new StringBuilder();
        for (var i = 0; i < table.Values.Count; i++)
        {
            var text = table.Values[i].ToString(CultureInfo.InvariantCulture) + ",";
            var startsRow = i > 0 && i % rowLength == 0;
            if (line.Length > 0 && (startsRow || (Indent.Length * 2) + line.Length + 1 + text.Length > MaxLineLength))
            {
                builder.Append(Indent).Append(Indent).Append(line).Append('\n');
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');

            line.Append(text);
        }

        if (line.Length > 0)
            builder.Append(Indent).Append(Indent).Append(line).Append('\n');
    }

    private static string GetElementType(SpecTable table)
    {
        var min = table.Values.Min();
        var max = table.Values.Max();

        // Every cumulative distribution has the same element type: the tables are copied into one buffer
        if (table.Name.EndsWith("_Cdf", StringComparison.Ordinal))
        {
            if (min < 0 || max > ushort.MaxValue)
                throw new FatalException($"Table {table.Name}: values out of the 16-bit range.");

            return "ushort";
        }

        return (min, max) switch
        {
            ( >= 0, <= byte.MaxValue) => "byte",
            ( >= sbyte.MinValue, <= sbyte.MaxValue) => "sbyte",
            ( >= 0, <= ushort.MaxValue) => "ushort",
            ( >= short.MinValue, <= short.MaxValue) => "short",
            ( >= int.MinValue, <= int.MaxValue) => "int",
            _ => throw new FatalException($"Table {table.Name}: values out of the 32-bit range."),
        };
    }

    private static void AppendHeader(StringBuilder builder, IEnumerable<string> files)
    {
        builder.Append("// <auto-generated>\n");
        builder.Append("// Transcribed by tools/Meziantou.Framework.Imaging.Av1TableExtractor from the AV1 Bitstream & Decoding Process Specification\n");
        builder.Append(CultureInfo.InvariantCulture, $"// ({PinnedSpecification.Repository}, commit {PinnedSpecification.Commit}):\n");
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            builder.Append(CultureInfo.InvariantCulture, $"//   {file}, SHA-256 {PinnedSpecification.GetSha256(file)}\n");
        }

        builder.Append("// Do not edit this file: change the extractor and run it again.\n");
        builder.Append("// </auto-generated>\n\n");
    }

    /// <summary>Converts a name of the specification (<c>BLOCK_4X4</c>, <c>Default_Scan_4x4</c>, <c>tplSamplePos</c>) to Pascal case.</summary>
    public static string ToPascalCase(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var part in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            var isUpperCase = !part.Any(char.IsAsciiLetterLower);
            builder.Append(char.ToUpperInvariant(part[0]));
            builder.Append(isUpperCase ? part[1..].ToLowerInvariant() : part[1..]);
        }

        return builder.ToString();
    }

    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
