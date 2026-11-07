using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The byte stream of a vector (H.265 Annex B) and the vectors derived from the stream of another recipe, for what no
// encoder writes: a conformance window on every side, a stream that starts at a CRA picture, a CRA picture retyped as a
// BLA picture, an end of sequence between two pictures, the pictures of the higher temporal sub-layers removed, two
// streams one after the other. A derivation never touches slice data: it removes, inserts or retypes whole NAL units, or
// has FFmpeg's hevc_metadata bitstream filter rewrite a parameter set. The result is recorded like any vector, from its
// own headers and with the digests two decoders agree on.
internal static partial class HevcCorpus
{
    /// <param name="Start">The offset of the first byte of the unit in the stream: the zero bytes and the start code that
    /// precede the NAL unit header.</param>
    /// <param name="Header">The offset of the first byte of the NAL unit header.</param>
    /// <param name="End">The offset after the last byte of the unit.</param>
    /// <param name="Type"><c>nal_unit_type</c>.</param>
    /// <param name="TemporalId"><c>nuh_temporal_id_plus1 - 1</c>.</param>
    private readonly record struct HevcUnit(int Start, int Header, int End, int Type, int TemporalId);

    /// <summary>The NAL units of a byte stream (clause B.2): each one starts with the zero bytes and the start code prefix
    /// 00 00 01 that precede its header. Emulation prevention keeps the prefix out of the payloads, and a payload does not
    /// end with a zero byte.</summary>
    private static List<HevcUnit> SplitAnnexB(byte[] stream)
    {
        var headers = new List<int>();
        for (var i = 0; i + 2 < stream.Length; i++)
        {
            if (stream[i] == 0 && stream[i + 1] == 0 && stream[i + 2] == 1)
            {
                headers.Add(i + 3);
                i += 2;
            }
        }

        if (headers.Count == 0)
            throw new ToolException("the stream has no start code.");

        var units = new List<HevcUnit>();
        var start = 0;
        for (var i = 0; i < headers.Count; i++)
        {
            // The zero bytes before the next start code belong to it
            var end = i + 1 < headers.Count ? headers[i + 1] - 3 : stream.Length;
            while (i + 1 < headers.Count && end > headers[i] && stream[end - 1] == 0)
                end--;

            if (end - headers[i] < 2)
                throw new ToolException("a NAL unit of the stream has no header.");
            if (i == 0 && stream.AsSpan(0, headers[0] - 3).ContainsAnyExcept((byte)0))
                throw new ToolException("the stream does not start with a start code.");

            units.Add(new HevcUnit(start, headers[i], end, (stream[headers[i]] >> 1) & 0x3F, (stream[headers[i] + 1] & 7) - 1));
            start = end;
        }

        return units;
    }

    /// <summary>How a vector is derived from the stream of another recipe.</summary>
    /// <param name="Kind">The value of the <c>hevc.derived</c> tag.</param>
    private sealed record HevcDerivation(string Kind)
    {
        /// <summary>The options of FFmpeg's <c>hevc_metadata</c> or <c>filter_units</c> bitstream filter (<c>metadata</c>, <c>filterUnits</c>).</summary>
        public string? BitstreamFilter { get; init; }

        /// <summary>The index, among the pictures of the NAL unit type <see cref="NalType"/>, of the picture the derivation
        /// applies to; <see langword="null"/> for every one after the first picture of the stream.</summary>
        public int? Occurrence { get; init; }

        /// <summary>The NAL unit type of the pictures the derivation applies to.</summary>
        public int NalType { get; init; } = NalCra;

        /// <summary>The NAL unit type written over the type of the slice segments (<c>nalType</c>).</summary>
        public int NewNalType { get; init; }

        /// <summary>The NAL unit types of the units that are removed with the derivation (<c>nalType</c>: the RASL
        /// pictures a splicer drops with a broken link).</summary>
        public int[] RemovedTypes { get; init; } = [];

        /// <summary>The highest temporal sub-layer that is kept (<c>subLayers</c>).</summary>
        public int MaxTemporalId { get; init; }

        /// <summary>The name of the recipe whose stream is appended (<c>concatenation</c>).</summary>
        public string? Append { get; init; }

        /// <summary>Whether an end of sequence NAL unit separates the two streams (<c>concatenation</c>).</summary>
        public bool EndOfSequence { get; init; }
    }

    private static readonly byte[] EndOfSequenceUnit = [0, 0, 1, NalEos << 1, 1];

    private static readonly byte[] EndOfBitstreamUnit = [0, 0, 1, NalEob << 1, 1];

    // Filler data (clause 7.3.2.8): ff bytes, then the trailing bits
    private static readonly byte[] FillerDataUnit = [0, 0, 1, NalFiller << 1, 1, 0xFF, 0xFF, 0xFF, 0x80];

    /// <summary>The indices of the NAL units of the access units of the selected pictures: from the first unit of the
    /// access unit of a picture to the unit before the access unit of the next picture.</summary>
    private static HashSet<int> AccessUnits(HevcTrace trace, int unitCount, Func<HevcPicture, bool> selector)
    {
        var indices = new HashSet<int>();
        for (var i = 0; i < trace.Pictures.Count; i++)
        {
            if (!selector(trace.Pictures[i]))
                continue;

            var end = i + 1 < trace.Pictures.Count ? trace.Pictures[i + 1].AccessUnitStart : unitCount;
            for (var index = trace.Pictures[i].AccessUnitStart; index < end; index++)
                indices.Add(index);
        }

        return indices;
    }

    /// <summary>Applies a derivation to the stream of the source recipe.</summary>
    /// <param name="trace">The headers of <paramref name="source"/>.</param>
    /// <param name="appended">The stream of the appended recipe, for a concatenation.</param>
    /// <param name="description">What was done, for the provenance of the vector.</param>
    /// <param name="command">The FFmpeg command of a bitstream filter derivation, or <see langword="null"/>.</param>
    private static byte[] Derive(HevcTools tools, HevcDerivation derivation, byte[] source, HevcTrace trace, byte[]? appended, FullPath scratch, out string description, out string? command)
    {
        command = null;
        var units = SplitAnnexB(source);

        // The pictures of the type the derivation looks for; never the first picture of the stream
        var targets = trace.Pictures.Where(picture => picture.DecodeIndex > 0 && picture.NalType == derivation.NalType).ToList();
        if (derivation.Occurrence is { } occurrence)
            targets = occurrence < targets.Count ? [targets[occurrence]] : [];

        var typeName = NalNames.GetValueOrDefault(derivation.NalType, Inv($"{derivation.NalType}"));
        switch (derivation.Kind)
        {
            case "metadata" or "filterUnits":
            {
                var filter = (derivation.Kind == "metadata" ? "hevc_metadata=" : "filter_units=") + derivation.BitstreamFilter;
                var input = scratch / "derivation-source.hevc";
                var output = scratch / "derivation.hevc";
                File.WriteAllBytes(input, source);
                var arguments = tools.FfmpegCmd(["-f", "hevc", "-i", input.Value, "-c:v", "copy", "-bsf:v", filter, "-f", "hevc", output.Value]);
                var result = Proc.Exec(arguments);
                if (result.ExitCode != 0 || result.Stderr.Length > 0 || !File.Exists(output))
                    throw new ToolException("the bitstream filter failed: " + Encoding.UTF8.GetString(result.Stderr).Trim());

                command = CommonCorpus.Display(arguments, tools, [(input.Value, "{source}.hevc"), (output.Value, "{input}")]);
                description = $"The stream of the source vector through FFmpeg's bitstream filter {filter}.";
                return File.ReadAllBytes(output);
            }

            case "startAtIrap":
            {
                if (targets.Count == 0)
                    throw new ToolException($"the source has no {typeName} picture after its first picture.");

                var first = units[targets[0].AccessUnitStart].Start;
                if (!trace.Nals.Skip(targets[0].AccessUnitStart).TakeWhile(nal => !nal.IsVcl).Select(nal => nal.Type).ToHashSet().IsSupersetOf([NalVps, NalSps, NalPps]))
                    throw new ToolException("the access unit the derived stream starts at does not hold the parameter sets.");

                description = Inv($"The stream of the source vector from the access unit of its {typeName} picture number {targets[0].DecodeIndex} in decoding order (byte {first}): the stream starts at that picture, as after a random access.");
                return source[first..];
            }

            case "nalType":
            {
                if (targets.Count == 0)
                    throw new ToolException($"the source has no {typeName} picture after its first picture.");

                // The slice segments of an IRAP picture that is not an IDR picture all have the same syntax: only the type changes
                var patched = (byte[])source.Clone();
                var retyped = targets.SelectMany(picture => picture.Segments).Select(segment => segment.Nal.Index).ToHashSet();
                foreach (var index in retyped)
                    patched[units[index].Header] = (byte)((patched[units[index].Header] & 0x81) | (derivation.NewNalType << 1));

                // A removed picture goes with its whole access unit: its slice segments and the hash that follows them
                var dropped = AccessUnits(trace, units.Count, picture => derivation.RemovedTypes.Contains(picture.NalType));
                if (derivation.RemovedTypes.Length > 0 && dropped.Count == 0)
                    throw new ToolException("the source has none of the pictures the derivation removes.");

                description = Inv($"The stream of the source vector with nal_unit_type = {derivation.NewNalType} ({NalNames[derivation.NewNalType]}) written over the type of the {retyped.Count} slice segment NAL units of its {typeName} pictures (after the first picture)")
                    + (dropped.Count > 0 ? Inv($", and without the {dropped.Count} NAL units of the access units of its {string.Join(" and ", derivation.RemovedTypes.Select(type => NalNames[type]))} pictures.") : ".");
                return Bytes.Concat(units.Where((_, index) => !dropped.Contains(index)).Select(unit => patched[unit.Start..unit.End]));
            }

            case "endOfSequence":
            {
                if (targets.Count == 0)
                    throw new ToolException($"the source has no {typeName} picture after its first picture.");

                var positions = targets.Select(picture => units[picture.AccessUnitStart].Start).ToList();
                var parts = new List<byte[]>();
                var previous = 0;
                foreach (var position in positions)
                {
                    parts.Add(source[previous..position]);
                    parts.Add(EndOfSequenceUnit);
                    previous = position;
                }

                parts.Add(source[previous..]);
                description = Inv($"The stream of the source vector with an end of sequence NAL unit (00 00 01 48 01) inserted before the access unit of each of its {typeName} pictures after the first picture (bytes {string.Join(", ", positions)} of the source).");
                return Bytes.Concat(parts);
            }

            case "subLayers":
            {
                // Whole access units: x265 gives the TemporalId 0 to the suffix SEI message of every picture
                var dropped = AccessUnits(trace, units.Count, picture => picture.TemporalId > derivation.MaxTemporalId);
                if (dropped.Count == 0)
                    throw new ToolException("the source has no picture of a higher temporal sub-layer.");

                description = Inv($"The stream of the source vector without the {dropped.Count} NAL units of the access units of the pictures whose TemporalId is greater than {derivation.MaxTemporalId} (sub-bitstream extraction, clause 10).");
                return Bytes.Concat(units.Where((_, index) => !dropped.Contains(index)).Select(unit => source[unit.Start..unit.End]));
            }

            case "concatenation":
            {
                if (appended is null)
                    throw new ToolException("a concatenation needs a second stream.");

                description = "The stream of the source vector, " + (derivation.EndOfSequence ? "an end of sequence NAL unit (00 00 01 48 01), " : "") + "then the stream of the appended vector.";
                return derivation.EndOfSequence ? Bytes.Concat(source, EndOfSequenceUnit, appended) : Bytes.Concat(source, appended);
            }

            case "trailingUnits":
            {
                // After the last picture: filler data, an end of sequence and an end of bitstream
                description = "The stream of the source vector followed by a filler data NAL unit (00 00 01 4c 01 ff ff ff 80), an end of sequence NAL unit (00 00 01 48 01) and an end of bitstream NAL unit (00 00 01 4a 01).";
                return Bytes.Concat(source, FillerDataUnit, EndOfSequenceUnit, EndOfBitstreamUnit);
            }

            default:
                throw new ToolException("Unknown derivation " + derivation.Kind);
        }
    }
}
