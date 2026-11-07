using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

internal static partial class Jp2Corpus
{
    private const uint ColorSpaceSrgb = 16;
    private const uint ColorSpaceGray = 17;
    private const uint ColorSpaceSycc = 18;

    private static readonly byte[] XmpPacket = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>\n");

    private static byte[] Input(Corpus<Jp2Tools> c, string name)
    {
        var fixture = c.Fixtures.First(f => (string)f["id"]! == "jp2/" + name);
        return File.ReadAllBytes(c.Out / (string)((Obj)fixture["input"]!)["path"]!);
    }

    private static Obj Size(Img img) => new() { ["width"] = img.Width, ["height"] = img.Height };

    // -----------------------------------------------------------------------------------------------------------------
    // Lossless images: the reference is the pattern given to the encoder
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A fixture written by opj_compress with the reversible transformations: the decoders return the pattern.</summary>
    private static void AddLossless(Corpus<Jp2Tools> c, string name, string function, Img pattern, string description, IReadOnlyList<string> options, string extension = ".j2k", int? depth = null,
        Readers? readers = null, string? notes = null)
    {
        var planes = ToPlanes(pattern, depth);
        var encoded = Opj(c, planes, options, extension);
        var frame = Present(planes);
        AddValid(c, name, function, encoded, extension, frame, PatternReference("The pattern given to the encoder: " + description),
            CrossChecks(c, name, encoded.Data, extension, frame, planes.Depth, readers), new Expectation { BitsPerComponent = planes.Depth }, Size(pattern), notes: notes);
    }

    private static void BuildLossless(Corpus<Jp2Tools> c)
    {
        const string Function = nameof(BuildLossless);
        AddLossless(c, "lossless-gray8", Function, CommonCorpus.PatternCheckerboardGray(11, 7), "PatternCheckerboardGray.", ["-n", "3"]);
        AddLossless(c, "lossless-rgb8", Function, CommonCorpus.PatternRgbOdd(13, 9), "PatternRgbOdd.", ["-n", "3"],
            notes: "The three components go through the reversible component transformation.");
        AddLossless(c, "lossless-rgb8-no-component-transform", Function, CommonCorpus.PatternRgbOdd(13, 9), "PatternRgbOdd.", ["-n", "3", "-mct", "0"]);
        AddLossless(c, "lossless-rgba8", Function, CommonCorpus.PatternCornerMarkers(12, 9), "PatternCornerMarkers, whose alpha varies.", ["-n", "3"],
            notes: "A codestream of four components has no channel definition: the fourth one is the opacity.");
        AddLossless(c, "lossless-graya8", Function, CommonCorpus.PatternGrayAlphaRamp(8, 2), "PatternGrayAlphaRamp: a fully transparent pixel keeps its gray value.", ["-n", "1"]);
        AddLossless(c, "lossless-gray16", Function, CommonCorpus.PatternGray16LowBits(8, 8), "PatternGray16LowBits: the low byte of every sample is significant.", ["-n", "3"]);
        AddLossless(c, "lossless-graya16", Function, CommonCorpus.PatternGraya16LowBits(8, 6), "PatternGraya16LowBits.", ["-n", "2"]);
        AddLossless(c, "lossless-rgb16", Function, CommonCorpus.PatternRgb16(9, 7), "PatternRgb16.", ["-n", "3"]);
        AddLossless(c, "lossless-rgba16", Function, CommonCorpus.PatternRgba16LowBits(8, 8), "PatternRgba16LowBits.", ["-n", "3"]);

        // Samples of four and twelve bits: the library scales them exactly to eight and sixteen bits
        AddLossless(c, "lossless-gray4", Function, new Img(9, 5, "gray", 8, CommonCorpus.Grid(9, 5, (x, y) => new Px(((x * 5) + (y * 3) + 1) % 16))),
            "Samples (5x + 3y + 1) mod 16 of four bits, scaled to eight bits: (v * 255 + 7) / 15.", ["-n", "2"], depth: 4);
        AddLossless(c, "lossless-gray12", Function, new Img(9, 7, "gray", 16, CommonCorpus.Grid(9, 7, (x, y) => new Px(((x * 683) + (y * 1000) + 5) % 4096))),
            "Samples (683x + 1000y + 5) mod 4096 of twelve bits, scaled to sixteen bits: (v * 65535 + 2047) / 4095.", ["-n", "3"], depth: 12);

        AddLossless(c, "lossless-no-decomposition", Function, CommonCorpus.PatternRgbOdd(13, 9), "PatternRgbOdd.", ["-n", "1"],
            notes: "No decomposition level: the code-blocks hold the samples, after the component transformation and the level shift.");
        AddLossless(c, "lossless-six-levels", Function, CommonCorpus.PatternSmoothColor(70, 66), "PatternSmoothColor.", ["-n", "7"],
            notes: "Six decomposition levels: the lowest sub-bands have two samples in each direction.");
        AddLossless(c, "lossless-one-pixel", Function, new Img(1, 1, "rgb", 8, [new Px(200, 100, 50)]), "One pixel (200, 100, 50).", ["-n", "1"]);
        AddLossless(c, "lossless-small-sub-bands", Function, CommonCorpus.PatternRgbOdd(5, 4), "PatternRgbOdd.", ["-n", "3"],
            notes: "Two decomposition levels of an image of 5x4 pixels: sub-bands have one sample in a direction.");
        // opj_compress has no decomposition level for an image of one column or of one row; ojph_compress has (ht-one-column)
        AddLossless(c, "lossless-one-column", Function, CommonCorpus.PatternGrayColumn(1, 37), "PatternGrayColumn.", ["-n", "1"]);
        AddLossless(c, "lossless-one-row", Function, CommonCorpus.PatternRgbOdd(41, 1), "PatternRgbOdd.", ["-n", "1"]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The tools of the codestream: progressions, tiles, markers, code-block styles
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildCodestreams(Corpus<Jp2Tools> c)
    {
        const string Function = nameof(BuildCodestreams);

        // The reference frames of the corpus are also the sources of every encoder suite, and the one of GIF has fixed
        // quality thresholds: a noisy color pattern stays small
        var color = CommonCorpus.PatternDetailColor(33, 17, 11);
        var gray = CommonCorpus.PatternDetailGray(33, 17, 3);

        // Three layers whose last one holds every remaining coding pass, and precincts smaller than the sub-bands
        foreach (var progression in new[] { "LRCP", "RLCP", "RPCL", "PCRL", "CPRL" })
        {
            AddLossless(c, "progression-" + progression.ToLowerInvariant(), Function, color, "PatternDetailColor(seed 11).",
                ["-n", "3", "-p", progression, "-r", "8,4,1", "-c", "[16,16],[8,8]", "-b", "8,8"],
                notes: $"The packets follow the {progression} progression: three layers, three resolution levels, three components and several precincts.");
        }

        AddLossless(c, "tiles", Function, color, "PatternDetailColor(seed 11).", ["-n", "3", "-t", "16,12"],
            notes: "A grid of 3x2 tiles: the tiles of the last column and of the last row are smaller.");
        AddLossless(c, "tiles-offsets", Function, color, "PatternDetailColor(seed 11).", ["-n", "3", "-d", "5,7", "-T", "5,3", "-t", "11,13"],
            notes: "The image area and the tile grid are offset on the reference grid: the sub-bands of a tile start at odd coordinates.");
        AddLossless(c, "tile-parts-resolution", Function, color, "PatternDetailColor(seed 11).", ["-n", "3", "-t", "24,16", "-TP", "R", "-r", "4,1"],
            notes: "Each resolution level of a tile is a tile-part.");
        AddLossless(c, "tile-parts-layer", Function, color, "PatternDetailColor(seed 11).", ["-n", "3", "-t", "24,16", "-TP", "L", "-r", "4,1"],
            notes: "Each layer of a tile is a tile-part.");
        AddLossless(c, "tile-parts-component", Function, color, "PatternDetailColor(seed 11).", ["-n", "3", "-t", "24,16", "-TP", "C", "-p", "CPRL", "-mct", "0"],
            notes: "Each component of a tile is a tile-part.");
        AddLossless(c, "markers-sop-eph", Function, color, "PatternDetailColor(seed 11).", ["-n", "3", "-SOP", "-EPH", "-r", "4,1"],
            notes: "Each packet starts with an SOP marker segment and each packet header ends with an EPH marker.");
        AddLossless(c, "markers-plt-tlm", Function, color, "PatternDetailColor(seed 11).", ["-n", "3", "-PLT", "-TLM", "-t", "24,16"],
            notes: "The lengths of the tile-parts (TLM) and of the packets (PLT) are in the headers.");
        AddLossless(c, "region-of-interest", Function, gray, "PatternDetailGray(seed 3).", ["-n", "3", "-ROI", "c=0,U=3"],
            notes: "The coefficients of the component are shifted by three bit-planes (RGN marker segment).");
        AddLossless(c, "guard-bits", Function, gray, "PatternDetailGray(seed 3).", ["-n", "3", "-GuardBits", "5"],
            notes: "Five guard bits: the sub-bands have three bit-planes more than with the default of two.");

        foreach (var (name, mode, text) in new[]
        {
            ("bypass", "1", "From the fifth bit-plane, the significance propagation and magnitude refinement passes bypass the arithmetic coder."),
            ("reset", "2", "The context probabilities are reset at the start of each coding pass."),
            ("terminate-all", "4", "The arithmetic coder is terminated after each coding pass: one codeword segment per pass."),
            ("causal", "8", "The contexts of a stripe ignore the next one."),
            ("predictable-termination", "16", "The terminations are predictable: the bits that follow the last symbol are known."),
            ("segmentation-symbols", "32", "A segmentation symbol ends each cleanup pass."),
            ("all", "63", "Every style of the coding passes together."),
        })
        {
            AddLossless(c, "style-" + name, Function, gray, "PatternDetailGray(seed 3).", ["-n", "2", "-M", mode, "-b", "16,16"], notes: text);
        }

        // Forms opj_compress does not write: its marker segments and its packets, moved by the generator
        void AddEdited(string name, Encoded source, byte[] edited, Img pattern, string description, string edit, string notes, Readers? readers = null)
        {
            var encoded = new Encoded(edited, [.. source.Commands, "{output}.j2k, edited by the generator: " + edit]);
            AddValid(c, name, Function, encoded, ".j2k", pattern, PatternReference("The pattern given to the encoder: " + description),
                CrossChecks(c, name, edited, ".j2k", pattern, pattern.Depth, readers), null, Size(pattern), "hand-authored", notes: notes);
        }

        var marked = Opj(c, color, ["-n", "3", "-SOP", "-EPH", "-r", "4,1", "-t", "24,16"]);
        AddEdited("packed-headers-tile-parts", marked, PackHeaders(marked.Data, inMainHeader: false), color, "PatternDetailColor(seed 11).",
            "the packet headers of each tile-part, with their EPH markers, are moved to a PPT marker segment of its header", "The packet headers are in PPT marker segments: the data of a tile-part holds the SOP marker segments and the coding passes.");
        AddEdited("packed-headers-main", marked, PackHeaders(marked.Data, inMainHeader: true), color, "PatternDetailColor(seed 11).",
            "the packet headers of every tile-part, with their EPH markers, are moved to a PPM marker segment of the main header", "The packet headers of every tile-part are in a PPM marker segment of the main header.");
        var parts = Opj(c, color, ["-n", "3", "-t", "24,16", "-TP", "R", "-r", "4,1"]);
        AddEdited("tile-parts-interleaved", parts, InterleaveTileParts(parts.Data), color, "PatternDetailColor(seed 11).",
            "the tile-parts are ordered by their index in their tile, then by tile", "The first tile-part of every tile, then their second ones, then their third ones: the tile-parts of the tiles are interleaved.");
        var plain = Opj(c, color, ["-n", "3", "-t", "24,16"]);
        AddEdited("component-marker-segments", plain, AddComponentSegments(plain.Data, component: 1), color, "PatternDetailColor(seed 11).",
            "a COC and a QCC marker segment for the second component are added to the main header, with the content of the COD and QCD marker segments", "COC and QCC marker segments give the coding style and the quantization of the second component.");
        AddEdited("tile-part-coding-style", plain, MoveCodingStyleToTileParts(plain.Data), color, "PatternDetailColor(seed 11).",
            "the COD and QCD marker segments are repeated in the first tile-part header of each tile, and those of the main header declare one decomposition level less",
            "The coding style and the quantization of each tile are in its first tile-part header; those of the main header, which no tile uses, have two decomposition levels.");
        // opj_compress leaves packets out of a codestream with progression order changes: the reference is decoded
        AddDecoded(c, "progression-changes", Function, Opj(c, color, ["-n", "3", "-r", "4,2,1", "-POC", "T1=0,0,1,2,3,RLCP/T1=0,0,3,3,3,CPRL"]), ".j2k", Size(color),
            notes: "A POC marker segment: the first layer of the first two resolution levels in the RLCP progression, then the CPRL progression. The encoder left the last packets out: the tile ends before them.");

        AddLossless(c, "blocks-4x4", Function, gray, "PatternDetailGray(seed 3).", ["-n", "2", "-b", "4,4"], notes: "The smallest code-blocks.");
        AddLossless(c, "blocks-64x4", Function, CommonCorpus.PatternDetailGray(80, 20, 4), "PatternDetailGray(seed 4).", ["-n", "1", "-b", "64,4"],
            notes: "Code-blocks of one stripe.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Decoded references: the irreversible transformations, layers that are cut
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A fixture whose reference is decoded by opj_decompress, and cross-checked with the other decoders.</summary>
    private static void AddDecoded(Corpus<Jp2Tools> c, string name, string function, Encoded encoded, string extension, Obj parameters, Readers? readers = null, string? notes = null,
        string origin = "generated", IEnumerable<string>? extraFeatures = null)
    {
        var decoded = OpjDecode(c, encoded.Data, extension) ?? throw new ToolException($"jp2/{name}: opj_decompress refuses the file");
        var frame = Present(decoded);
        var reference = new Obj
        {
            ["method"] = "decoded",
            ["description"] = "Decoded from the same encoded input by opj_decompress to the samples of each component.",
            ["decoder"] = OpjLabel(c),
            ["backend"] = $"OpenJPEG {c.Tools.OpenJpegVersion}",
            ["command"] = decoded.Command,
        };
        var checks = CrossChecks(c, name, encoded.Data, extension, frame, decoded.Depth, readers, maxError: 2, includeOpenJpeg: false);
        AddValid(c, name, function, encoded, extension, frame, reference, checks, new Expectation { BitsPerComponent = decoded.Depth }, parameters, origin, LossyPolicy(name, checks), notes, extraFeatures);
    }

    private static void BuildLossy(Corpus<Jp2Tools> c)
    {
        const string Function = nameof(BuildLossy);
        var color = CommonCorpus.PatternDetailColor(33, 17, 11);
        var gray = CommonCorpus.PatternDetailGray(33, 17, 3);
        AddDecoded(c, "lossy-gray8", Function, Opj(c, gray, ["-n", "3", "-I", "-r", "3"]), ".j2k", Size(gray));
        AddDecoded(c, "lossy-rgb8", Function, Opj(c, color, ["-n", "3", "-I", "-r", "5"]), ".j2k", Size(color),
            notes: "The irreversible 9-7 transformation and the irreversible component transformation.");
        AddDecoded(c, "lossy-rgb8-no-component-transform", Function, Opj(c, color, ["-n", "3", "-I", "-r", "5", "-mct", "0"]), ".j2k", Size(color));
        AddDecoded(c, "lossy-rgb8-every-pass", Function, Opj(c, color, ["-n", "3", "-I"]), ".j2k", Size(color),
            notes: "Every coding pass is in the codestream: the error is the one of the quantization.");
        AddDecoded(c, "lossy-rgba8", Function, Opj(c, CommonCorpus.PatternCornerMarkers(20, 16), ["-n", "3", "-I", "-r", "3"]), ".j2k", new Obj { ["width"] = 20, ["height"] = 16 });
        var smooth = CommonCorpus.PatternSmoothColor(24, 16);
        var deep = new Img(24, 16, "rgb", 16, smooth.Pixels.Select((p, i) => new Px((p[0] * 257) + (i % 64), (p[1] * 257) - (i % 32), (p[2] * 257) + ((i * 7) % 128))));
        AddDecoded(c, "lossy-rgb16", Function, Opj(c, deep, ["-n", "3", "-I", "-r", "3"]), ".j2k", Size(deep), notes: "PatternSmoothColor scaled to sixteen bits, with low bits that vary.");
        AddDecoded(c, "lossy-layers", Function, Opj(c, color, ["-n", "3", "-I", "-p", "RPCL", "-r", "20,10,5"]), ".j2k", Size(color), notes: "Three layers.");
        AddDecoded(c, "lossy-tiles", Function, Opj(c, color, ["-n", "3", "-I", "-r", "5", "-t", "16,16"]), ".j2k", Size(color), notes: "The tiles are transformed one by one.");
        AddDecoded(c, "lossy-region-of-interest", Function, Opj(c, gray, ["-n", "3", "-I", "-r", "3", "-ROI", "c=0,U=6"]), ".j2k", Size(gray),
            notes: "The coefficients are shifted by six bit-planes: the bit-planes of the layer hold their most significant bits.");
        AddDecoded(c, "truncated-reversible", Function, Opj(c, color, ["-n", "3", "-r", "4"]), ".j2k", Size(color),
            notes: "The reversible transformations with the last coding passes left out: the missing bits of a coefficient are replaced by the middle of their range.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Files of the JP2 family
    // -----------------------------------------------------------------------------------------------------------------

    private static byte[] Jp2File(byte[] codestream, int width, int height, int components, int depth, params ReadOnlySpan<byte[]> boxes)
        => Jp2FileBuilder.File(codestream, [Jp2FileBuilder.ImageHeader(width, height, components, depth - 1), .. boxes]);

    private static void BuildFiles(Corpus<Jp2Tools> c)
    {
        const string Function = nameof(BuildFiles);
        var rgb = CommonCorpus.PatternRgbOdd(13, 9);
        AddLossless(c, "file-rgb8", Function, rgb, "PatternRgbOdd.", ["-n", "3"], ".jp2", notes: "A JP2 file written by opj_compress: the sRGB color space.");
        AddLossless(c, "file-gray16", Function, CommonCorpus.PatternGray16LowBits(8, 8), "PatternGray16LowBits.", ["-n", "3"], ".jp2");
        AddLossless(c, "file-rgba8", Function, CommonCorpus.PatternCornerMarkers(12, 9), "PatternCornerMarkers, whose alpha varies.", ["-n", "3"], ".jp2",
            notes: "opj_compress writes four components and no channel definition box: the fourth channel is the opacity.");

        // Files assembled around a codestream of opj_compress
        void AddAssembled(string name, Encoded codestream, byte[] file, Img frame, string description, Expectation? expectation = null, Readers? readers = null, string? notes = null, int? depth = null, Obj? comparison = null,
            List<Obj>? checks = null)
        {
            var encoded = new Encoded(file, [.. codestream.Commands, "{output}.j2k is the codestream of the file; its boxes are written by the generator"]);
            expectation ??= new Expectation();
            checks ??= CrossChecks(c, name, file, ".jp2", frame, depth ?? frame.Depth, readers);
            AddValid(c, name, Function, encoded, ".jp2", frame, PatternReference(description), checks, expectation, Size(frame), "hand-authored", comparison, notes);
        }

        var codestream = Opj(c, rgb, ["-n", "3"]);
        var sRgb = Jp2FileBuilder.EnumeratedColor(ColorSpaceSrgb);

        // A palette: the samples of the component are indexes
        Px[] colors = [new(255, 0, 0), new(0, 160, 0), new(0, 0, 255), new(255, 255, 0), new(16, 16, 16), new(255, 255, 255)];
        var indexes = new Img(12, 8, "gray", 8, CommonCorpus.Grid(12, 8, (x, y) => new Px(((x + (2 * y)) / 3) % colors.Length)));
        var indexStream = Opj(c, indexes, ["-n", "1"]);
        var paletted = new Img(12, 8, "rgb", 8, indexes.Pixels.Select(pixel => colors[pixel[0]]));
        AddAssembled("file-palette", indexStream,
            Jp2File(indexStream.Data, 12, 8, 1, 8, sRgb, Jp2FileBuilder.Palette([7, 7, 7], [.. colors.Select(color => color.ToArray())]), Jp2FileBuilder.ComponentMapping((0, 1, 0), (0, 1, 1), (0, 1, 2))),
            paletted, "Six colors in diagonal stripes: each sample of the component is the index of an entry of the palette.",
            new Expectation { ColorModel = "Indexed" }, notes: "A palette box of six entries of three columns of eight bits, and a component mapping box that makes three channels of the component.");

        int[][] translucent = [[255, 0, 0, 255], [0, 160, 0, 128], [0, 0, 255, 0], [255, 255, 0, 64]];
        var translucentIndexes = new Img(8, 6, "gray", 8, CommonCorpus.Grid(8, 6, (x, y) => new Px((x + y) % 4)));
        var translucentStream = Opj(c, translucentIndexes, ["-n", "1"], depth: 2);
        AddAssembled("file-palette-opacity", translucentStream,
            Jp2File(translucentStream.Data, 8, 6, 1, 2, sRgb, Jp2FileBuilder.Palette([7, 7, 7, 7], translucent), Jp2FileBuilder.ComponentMapping((0, 1, 0), (0, 1, 1), (0, 1, 2), (0, 1, 3)),
                Jp2FileBuilder.ChannelDefinition((0, 0, 1), (1, 0, 2), (2, 0, 3), (3, 1, 0))),
            new Img(8, 6, "rgba", 8, translucentIndexes.Pixels.Select(pixel => Px.From(translucent[pixel[0]]))),
            "Four colors in diagonal stripes, of the opacities 255, 128, 0 and 64: each sample of the component, of two bits, is the index of an entry of the palette.",
            new Expectation { ColorModel = "Indexed" }, new Readers { FfmpegLimit = "The decoder of FFmpeg has no pixel format for a component of two bits with a palette of four columns: it refuses the file." },
            notes: "The fourth column of the palette is the opacity (channel definition box).");

        // Channel definitions: the components in another order than the colors
        var reversed = new Img(13, 9, "rgb", 8, rgb.Pixels.Select(pixel => new Px(pixel[2], pixel[1], pixel[0])));
        var reversedStream = Opj(c, reversed, ["-n", "3", "-mct", "0"]);
        AddAssembled("file-channels-reordered", reversedStream, Jp2File(reversedStream.Data, 13, 9, 3, 8, sRgb, Jp2FileBuilder.ChannelDefinition((0, 0, 3), (1, 0, 2), (2, 0, 1))), rgb,
            "PatternRgbOdd; the encoder was given its blue, green and red samples, in this order.", notes: "The channel definition box associates the first component with the blue color and the third one with the red color.");

        var corners = CommonCorpus.PatternCornerMarkers(12, 9);
        var opacityFirst = new Planes(12, 9, 8, [.. new[] { 3, 0, 1, 2 }.Select(channel => corners.Pixels.Select(pixel => pixel[channel]).ToArray())]);
        var opacityFirstStream = Opj(c, opacityFirst, ["-n", "3", "-mct", "0"]);
        AddAssembled("file-opacity-first", opacityFirstStream,
            Jp2File(opacityFirstStream.Data, 12, 9, 4, 8, sRgb, Jp2FileBuilder.ChannelDefinition((0, 1, 0), (1, 0, 1), (2, 0, 2), (3, 0, 3))), corners,
            "PatternCornerMarkers; the encoder was given its alpha, red, green and blue samples, in this order.",
            readers: new Readers { FfmpegLimit = "The decoder of FFmpeg takes the last of four components as the opacity, whatever the channel definition box says." },
            notes: "The channel definition box makes the first component the opacity of the image.");

        var grayAlpha = CommonCorpus.PatternGrayAlphaRamp(8, 2);
        var grayAlphaStream = Opj(c, grayAlpha, ["-n", "1"]);
        AddAssembled("file-gray-opacity", grayAlphaStream,
            Jp2File(grayAlphaStream.Data, 8, 2, 2, 8, Jp2FileBuilder.EnumeratedColor(ColorSpaceGray), Jp2FileBuilder.ChannelDefinition((0, 0, 1), (1, 1, 0))), grayAlpha,
            "PatternGrayAlphaRamp: a fully transparent pixel keeps its gray value.");

        // sYCC: the luminance and the two chrominances are converted to RGB
        var ycc = CommonCorpus.PatternDetailColor(16, 12, 21);
        var yccStream = Opj(c, ycc, ["-n", "2", "-mct", "0"]);
        var converted = new Img(16, 12, "rgb", 8, ycc.Pixels.Select(pixel => SyccToRgb(pixel[0], pixel[1], pixel[2])));
        var yccFile = Jp2File(yccStream.Data, 16, 12, 3, 8, Jp2FileBuilder.EnumeratedColor(ColorSpaceSycc));
        var yccChecks = CrossChecks(c, "file-sycc", yccFile, ".jp2", converted, 8, new Readers { SkipsFfmpeg = true }, maxError: 1);
        AddAssembled("file-sycc", yccStream, yccFile, converted,
            "PatternDetailColor(seed 21), whose samples are a luminance and two chrominances, converted to RGB: R = Y + 1.402 (Cr - 128), G = Y - 0.34413 (Cb - 128) - 0.71414 (Cr - 128), B = Y + 1.772 (Cb - 128), rounded to the nearest integer and limited to 0..255.",
            new Expectation { ColorModel = "YCbCr" }, comparison: LossyPolicy("file-sycc", yccChecks), checks: yccChecks,
            notes: "The sYCC color space. FFmpeg returns the three components, not the colors: it is not compared.");

        // The chrominances have one sample for 2x2 luminance samples: each is used for its four pixels
        var subsampled = CommonCorpus.PatternDetailColor(16, 12, 22);
        int[] Plane(int channel, int step) => [.. Enumerable.Range(0, 12 / step).SelectMany(y => Enumerable.Range(0, 16 / step).Select(x => subsampled.Pixels[(y * step * 16) + (x * step)][channel]))];
        var (luminance, blue, red) = (Plane(0, 1), Plane(1, 2), Plane(2, 2));
        var subsampledStream = OpjRaw(c, 16, 12, 8, isSigned: false, [(luminance, 1, 1), (blue, 2, 2), (red, 2, 2)], ["-n", "2", "-mct", "0"]);
        var upsampled = new Img(16, 12, "rgb", 8, CommonCorpus.Grid(16, 12, (x, y) => SyccToRgb(luminance[(y * 16) + x], blue[((y / 2) * 8) + (x / 2)], red[((y / 2) * 8) + (x / 2)])));
        var subsampledFile = Jp2File(subsampledStream.Data, 16, 12, 3, 8, Jp2FileBuilder.EnumeratedColor(ColorSpaceSycc));
        var subsampledChecks = CrossChecks(c, "file-sycc-420", subsampledFile, ".jp2", upsampled, 8, new Readers { SkipsFfmpeg = true }, maxError: 1);
        AddAssembled("file-sycc-420", subsampledStream, subsampledFile, upsampled,
            "PatternDetailColor(seed 22), whose samples are a luminance and two chrominances; the encoder was given the chrominances of the even columns of the even rows. Each pixel is converted to RGB with the chrominances of its 2x2 block, as file-sycc is.",
            new Expectation { ColorModel = "YCbCr" }, comparison: LossyPolicy("file-sycc-420", subsampledChecks), checks: subsampledChecks,
            notes: "The chrominances are sub-sampled by two in both directions. FFmpeg returns the three components, not the colors: it is not compared.");

        // Color profiles, resolution and metadata
        var displayP3 = File.ReadAllBytes(CorpusDriver.CorpusDir / "icc" / "profiles" / "DisplayP3-v4.icc");
        AddAssembled("file-icc-rgb", codestream, Jp2File(codestream.Data, 13, 9, 3, 8, Jp2FileBuilder.ProfileColor(2, displayP3)), rgb, "PatternRgbOdd.", new Expectation { Icc = displayP3 },
            notes: "The color specification box holds an ICC profile (icc/profiles/DisplayP3-v4.icc): it describes the samples, which are stored as they are.");
        var grayProfile = CommonCorpus.IccProfile("GRAY");
        var grayPattern = CommonCorpus.PatternCheckerboardGray(11, 7);
        var grayStream = Opj(c, grayPattern, ["-n", "3"]);
        AddAssembled("file-icc-gray", grayStream, Jp2File(grayStream.Data, 11, 7, 1, 8, Jp2FileBuilder.ProfileColor(2, grayProfile)), grayPattern, "PatternCheckerboardGray.", new Expectation { Icc = grayProfile });
        AddAssembled("file-resolution", codestream,
            Jp2File(codestream.Data, 13, 9, 3, 8, sRgb, Jp2FileBuilder.Box("res ", Jp2FileBuilder.Resolution("resc", 5670, 1, 0, 2835, 1, 0))), rgb, "PatternRgbOdd.",
            new Expectation { PointsPerMeter = (2835, 5670) }, notes: "The capture resolution box: 2835 grid points per meter horizontally and 5670 vertically.");

        var exif = CommonCorpus.ExifTiff(littleEndian: true, orientation: 6, software: "Meziantou.Framework.Imaging corpus");
        var header = Jp2FileBuilder.Box("jp2h", Jp2FileBuilder.ImageHeader(13, 9, 3, 7), sRgb);
        var fileType = Jp2FileBuilder.FileType("jp2 ", "jp2 ");
        AddAssembled("file-exif-xmp", codestream,
            Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, Jp2FileBuilder.Uuid(Jp2FileBuilder.ExifUuid(), exif), Jp2FileBuilder.Uuid(Jp2FileBuilder.XmpUuid(), XmpPacket), Jp2FileBuilder.Box("jp2c", codestream.Data)),
            rgb, "PatternRgbOdd.", new Expectation { Exif = exif, Xmp = XmpPacket, Orientation = 6 },
            notes: "UUID boxes hold an EXIF profile, whose orientation is 6, and an XMP packet, before the codestream. The fixtures expect the stored samples: the orientation is not applied.");
        AddAssembled("file-metadata-after-codestream", codestream,
            Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, Jp2FileBuilder.Box("jp2c", codestream.Data), Jp2FileBuilder.Uuid(Jp2FileBuilder.XmpUuid(), XmpPacket), Jp2FileBuilder.Uuid(Jp2FileBuilder.ExifUuid(), exif)),
            rgb, "PatternRgbOdd.", new Expectation { Exif = exif, Xmp = XmpPacket, Orientation = 6 }, notes: "The UUID boxes of the XMP packet and of the EXIF profile follow the codestream.");
        AddAssembled("file-large-boxes", codestream,
            Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, Jp2FileBuilder.LargeBox("jp2h", Jp2FileBuilder.ImageHeader(13, 9, 3, 7), sRgb), Jp2FileBuilder.LargeBox("jp2c", codestream.Data)),
            rgb, "PatternRgbOdd.", notes: "The header box and the codestream box give their length on 64 bits.");
        AddAssembled("file-codestream-to-end", codestream, Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, Jp2FileBuilder.BoxToEnd("jp2c", codestream.Data)),
            rgb, "PatternRgbOdd.", notes: "The codestream box has the length 0: it extends to the end of the file.");
    }

    /// <summary>The conversion of sYCC to sRGB (IEC 61966-2-1 Amendment 1): real numbers, rounded half away from zero.</summary>
    private static Px SyccToRgb(int y, int cb, int cr)
    {
        static int Limit(decimal value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
        return new Px(Limit(y + (1.402m * (cr - 128))), Limit(y - (0.34413m * (cb - 128)) - (0.71414m * (cr - 128))), Limit(y + (1.772m * (cb - 128))));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The high-throughput block coder
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildHighThroughput(Corpus<Jp2Tools> c)
    {
        const string Function = nameof(BuildHighThroughput);
        var both = new Readers { UsesOpenJph = true };

        void AddLosslessHt(string name, Img pattern, string description, IReadOnlyList<string> options, int? depth = null, string? notes = null, Readers? readers = null)
        {
            var planes = ToPlanes(pattern, depth);
            var encoded = Ojph(c, planes, isReversible: true, options);
            var frame = Present(planes);
            AddValid(c, name, Function, encoded, ".j2c", frame, PatternReference("The pattern given to the encoder: " + description),
                CrossChecks(c, name, encoded.Data, ".j2c", frame, planes.Depth, readers ?? both), new Expectation { BitsPerComponent = planes.Depth }, Size(pattern), notes: notes);
        }

        var color = CommonCorpus.PatternDetailColor(33, 17, 11);
        AddLosslessHt("ht-lossless-gray8", CommonCorpus.PatternCheckerboardGray(11, 7), "PatternCheckerboardGray.", ["-num_decomps", "3"],
            readers: new Readers
            {
                UsesOpenJph = true,
                FfmpegLimit = "The decoder of FFmpeg refuses an HT cleanup pass whose magnitudes use every bit-plane it can code (\"Bad HT cleanup segment\"): the exponent bound of a quad is then two more than the number of skipped bit-planes.",
            },
            notes: "The checkerboard gives coefficients that use every bit-plane of their sub-band.");
        AddLosslessHt("ht-lossless-rgb8", CommonCorpus.PatternRgbOdd(13, 9), "PatternRgbOdd.", ["-num_decomps", "3"]);
        AddLosslessHt("ht-lossless-rgb16", CommonCorpus.PatternRgb16(9, 7), "PatternRgb16.", ["-num_decomps", "3"]);
        AddLosslessHt("ht-lossless-gray12", new Img(9, 7, "gray", 16, CommonCorpus.Grid(9, 7, (x, y) => new Px(((x * 683) + (y * 1000) + 5) % 4096))),
            "Samples (683x + 1000y + 5) mod 4096 of twelve bits, scaled to sixteen bits: (v * 65535 + 2047) / 4095.", ["-num_decomps", "3"], depth: 12);
        AddLosslessHt("ht-tiles", color, "PatternDetailColor(seed 11).", ["-num_decomps", "3", "-tile_size", "{16,16}"], notes: "Six tiles.");
        AddLosslessHt("ht-precincts", color, "PatternDetailColor(seed 11).", ["-num_decomps", "3", "-prog_order", "CPRL", "-precincts", "{16,16},{32,32}", "-block_size", "{8,8}"],
            notes: "Several precincts, code-blocks of 8x8 samples and the CPRL progression.");
        AddLosslessHt("ht-blocks-4x1024", CommonCorpus.PatternDetailGray(600, 9, 6), "PatternDetailGray(seed 6).", ["-num_decomps", "1", "-block_size", "{4,1024}"],
            notes: "Code-blocks of 1024 columns and 4 rows: the widest rows of quads.");

        AddLosslessHt("ht-one-column", CommonCorpus.PatternGrayColumn(1, 37), "PatternGrayColumn.", ["-num_decomps", "2"],
            notes: "Two decomposition levels of an image of one column: the horizontal transformation has nothing to do.");
        AddLosslessHt("ht-one-row", CommonCorpus.PatternRgbOdd(41, 1), "PatternRgbOdd.", ["-num_decomps", "2"],
            notes: "Two decomposition levels of an image of one row: the vertical transformation has nothing to do.");

        var lossy = Ojph(c, ToPlanes(color), isReversible: false, ["-num_decomps", "3", "-qstep", "0.02"]);
        AddDecoded(c, "ht-lossy-rgb8", Function, lossy, ".j2c", Size(color), both, notes: "The irreversible transformations with the high-throughput block coder.");
        var gray = CommonCorpus.PatternDetailGray(33, 17, 3);
        AddDecoded(c, "ht-lossy-gray8", Function, Ojph(c, ToPlanes(gray), isReversible: false, ["-num_decomps", "3", "-qstep", "0.05"]), ".j2c", Size(gray), both);

        // A file of the brand of Rec. ITU-T T.814 around a codestream of ojph_compress
        var pattern = CommonCorpus.PatternRgbOdd(13, 9);
        var codestream = Ojph(c, ToPlanes(pattern), isReversible: true, ["-num_decomps", "3"]);
        var file = Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), Jp2FileBuilder.FileType("jph ", "jph "),
            Jp2FileBuilder.Box("jp2h", Jp2FileBuilder.ImageHeader(13, 9, 3, 7), Jp2FileBuilder.EnumeratedColor(ColorSpaceSrgb)), Jp2FileBuilder.Box("jp2c", codestream.Data));
        AddValid(c, "ht-file", Function, new Encoded(file, [.. codestream.Commands, "{output}.j2c is the codestream of the file; its boxes are written by the generator"]), ".jph", pattern,
            PatternReference("The pattern given to the encoder: PatternRgbOdd."), CrossChecks(c, "ht-file", file, ".jph", pattern, 8), null, Size(pattern), "hand-authored",
            notes: "A JPH file: the brand 'jph ' of Rec. ITU-T T.814 | ISO/IEC 15444-15, Annex D.");

        // Coding passes no pinned encoder writes
        void AddAuthored(string name, int width, int height, int seed, int passes, int placeholderSets, bool isVerticallyCausal, AuthoredPackets packets, Readers readers, string notes)
        {
            var random = new PyRandom(seed);
            var coefficients = new List<int>();
            for (var i = 0; i < width * height; i++)
            {
                // Zeros, isolated ones for the HT SigProp pass, and larger coefficients
                var kind = random.RandInt(0, 9);
                coefficients.Add(kind < 4 ? 0 : kind < 7 ? (random.RandInt(0, 1) == 0 ? 1 : -1) : random.RandInt(-31, 31));
            }

            var authored = AuthorHighThroughput(c, width, height, coefficients, passes, placeholderSets, isVerticallyCausal, packets);
            AddValid(c, name, Function, authored.Encoded, ".j2c", authored.Frame,
                PatternReference("The coefficients the generator codes, plus 128: the codestream has one code-block and no wavelet transformation. "
                    + (passes == 2 ? "The last bit of a coefficient of magnitude 2 or more is not coded: it is shown with the odd value. " : "")
                    + $"PyRandom(seed {CommonCorpus.Str(seed)}): for each sample, a number from 0 to 9; below 4 the coefficient is 0, below 7 it is 1 or -1, otherwise it is a number from -31 to 31."),
                CrossChecks(c, name, authored.Encoded.Data, ".j2c", authored.Frame, 8, readers),
                parameters: new Obj
                {
                    ["width"] = width,
                    ["height"] = height,
                    ["seed"] = seed,
                    ["codingPasses"] = passes,
                    ["placeholderPasses"] = 3 * placeholderSets,
                    ["sigPropBits"] = authored.SignificancePropagationBits,
                    ["magRefBits"] = passes == 3 ? authored.MagnitudeRefinementBits : 0,
                },
                origin: "hand-authored", notes: notes,
                extraFeatures: [.. passes > 1 ? ["jp2.ht.sigProp"] : Array.Empty<string>(), .. passes > 2 ? ["jp2.ht.magRef"] : Array.Empty<string>(), .. placeholderSets > 0 ? ["jp2.ht.placeholders"] : Array.Empty<string>()]);
        }

        const string PlaceholderLimit = "OpenJPEG refuses an HT code-block of more than three coding passes: it does not read placeholder passes.";
        const string LayerLimitOpenJpeg = "OpenJPEG does not read the HT refinement segment of a code-block from another packet than its HT cleanup segment.";
        const string LayerLimitOpenJph = "OpenJPH reads codestreams of one layer.";
        AddAuthored("ht-refinement", 20, 13, 31, 3, 0, isVerticallyCausal: false, AuthoredPackets.OnePacket, both,
            "An HT cleanup pass, an HT SigProp pass and an HT MagRef pass: the last two give the least significant bit-plane.");
        AddAuthored("ht-significance-propagation", 20, 13, 32, 2, 0, isVerticallyCausal: false, AuthoredPackets.OnePacket, both,
            "An HT cleanup pass and an HT SigProp pass, without HT MagRef pass: the samples the HT cleanup pass coded miss their last bit.");
        AddAuthored("ht-refinement-causal", 19, 14, 33, 3, 0, isVerticallyCausal: true, AuthoredPackets.OnePacket, both,
            "The three coding passes with the vertically causal contexts: the HT SigProp pass ignores the significance of the next stripe.");
        AddAuthored("ht-placeholders", 20, 13, 34, 1, 2, isVerticallyCausal: false, AuthoredPackets.OnePacket, new Readers { UsesOpenJph = true, OpenJpegLimit = PlaceholderLimit },
            "Six placeholder passes before the HT cleanup pass: the packet header gives seven coding passes, and two zero bit-planes less.");
        AddAuthored("ht-placeholders-refinement", 20, 13, 35, 3, 1, isVerticallyCausal: false, AuthoredPackets.OnePacket, new Readers { UsesOpenJph = true, OpenJpegLimit = PlaceholderLimit },
            "Three placeholder passes before the three coding passes: the packet header gives six coding passes.");
        AddAuthored("ht-layers", 20, 13, 36, 3, 0, isVerticallyCausal: false, AuthoredPackets.OnePassPerPacket, new Readers { UsesOpenJph = true, OpenJpegLimit = LayerLimitOpenJpeg, OpenJphLimit = LayerLimitOpenJph },
            "Three layers: the HT cleanup pass, the HT SigProp pass and the HT MagRef pass are each in a packet, so that the HT refinement segment is in two codeword segments.");
        AddAuthored("ht-layers-refinement", 20, 13, 37, 3, 0, isVerticallyCausal: false, AuthoredPackets.RefinementInSecondPacket, new Readers { UsesOpenJph = true, OpenJpegLimit = LayerLimitOpenJpeg, OpenJphLimit = LayerLimitOpenJph },
            "Two layers: the HT cleanup pass, then the HT SigProp and HT MagRef passes.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Malformed files
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildInvalid(Corpus<Jp2Tools> c)
    {
        var codestream = Input(c, "lossless-rgb8");
        var file = Input(c, "file-rgb8");
        var structure = ReferenceJpeg2000.Parse(codestream);
        var tilePart = structure.TileParts[0];

        void Add(string name, byte[] data, string extension, string notes, Obj? parameters = null, IList<string>? features = null, string? requirement = null)
        {
            // The defect is not a matter of interpretation: a reference decoder refuses the file. Both are lenient with some
            // requirements of the Recommendation, which is then the reference: the clause is given
            var tools = new List<string>();
            if (OpjDecode(c, data, extension) is null)
                tools.Add($"OpenJPEG {c.Tools.OpenJpegVersion} (opj_decompress, confirms the defect)");
            if (FfmpegDecode(c, data, 13, 9, 3, 8) is null)
                tools.Add($"{c.Tools.FfmpegLabel} (confirms the defect)");
            Py.Assert((tools.Count > 0) == (requirement is null), $"invalid/jp2/{name}: {tools.Count} decoders refuse the file");
            if (requirement is not null)
                notes += " " + requirement;
            tools.Add(ToolSet.RuntimeLabel);
            c.AddError("invalid/jp2/" + name, "invalid", "invalid/jp2/" + name + extension, data, "jp2", c.Provenance("hand-authored", nameof(BuildInvalid), parameters, tools),
                new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Jpeg2000" },
                features: features ?? (IList<string>)c.Fixtures.First(f => (string)f["id"]! == (extension == ".jp2" ? "jp2/file-rgb8" : "jp2/lossless-rgb8"))["features"]!, notes: notes);
        }

        byte[] Patch(int offset, params ReadOnlySpan<byte> bytes)
        {
            var copy = (byte[])codestream.Clone();
            bytes.CopyTo(copy.AsSpan(offset));
            return copy;
        }

        byte[] WithoutSegment(int marker)
        {
            var offset = FindMarkerSegment(codestream.AsSpan(0, tilePart.Offset), marker);
            var length = 2 + BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(offset + 2));
            return [.. codestream.AsSpan(0, offset), .. codestream.AsSpan(offset + length)];
        }

        // The SIZ marker segment: the marker, its length, Rsiz, then eight fields of four bytes and the number of components
        const int Siz = 2;
        Add("codestream-no-end", codestream[..^2], ".j2k", "The codestream of jp2/lossless-rgb8 without its EOC marker.");
        Add("codestream-cut", [.. codestream.AsSpan(0, tilePart.Offset + 40)], ".j2k", "The codestream of jp2/lossless-rgb8 ends in its tile-part.", new Obj { ["length"] = tilePart.Offset + 40 });
        Add("siz-zero-tile-width", Patch(Siz + 6 + 16, 0, 0, 0, 0), ".j2k", "XTsiz, the width of a tile, is 0.");
        Add("siz-no-component", [.. codestream.AsSpan(0, Siz + 2), 0, 38, .. codestream.AsSpan(Siz + 4, 34), 0, 0, .. codestream.AsSpan(Siz + 49)], ".j2k", "Csiz, the number of components, is 0.");
        Add("siz-image-offset", Patch(Siz + 6 + 8, 0, 0, 0, 13), ".j2k", "XOsiz, the horizontal offset of the image area, is Xsiz: the image area is empty.");
        Add("header-no-cod", WithoutSegment(0x52), ".j2k", "The main header has no COD marker segment.");
        Add("header-no-qcd", WithoutSegment(0x5C), ".j2k", "The main header has no QCD marker segment.");
        Add("tile-index", Patch(tilePart.Offset + 4, 0, 1), ".j2k", "Isot, the index of the tile of the tile-part, is 1: the image has one tile.");
        Add("tile-part-length", Patch(tilePart.Offset + 6, 0, 0, 0x40, 0), ".j2k", "Psot, the length of the tile-part, is larger than the codestream.");
        Add("tile-part-index", Patch(tilePart.Offset + 10, 1), ".j2k", "TPsot, the index of the first tile-part of the tile, is 1.");

        // The boxes of a file
        var parsed = ReferenceJpeg2000.Parse(file);
        var box = parsed.CodestreamOffset - 8;
        var header = Jp2FileBuilder.Box("jp2h", Jp2FileBuilder.ImageHeader(13, 9, 3, 7), Jp2FileBuilder.EnumeratedColor(ColorSpaceSrgb));
        var fileType = Jp2FileBuilder.FileType("jp2 ", "jp2 ");
        Add("file-no-codestream", file[..box], ".jp2", "The file of jp2/file-rgb8 without its codestream box.");
        Add("file-no-header", Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, Jp2FileBuilder.Box("jp2c", codestream)), ".jp2", "The file has no JP2 header box.");
        Add("file-no-color", Jp2FileBuilder.File(codestream, Jp2FileBuilder.ImageHeader(13, 9, 3, 7)), ".jp2", "The JP2 header box has no color specification box.",
            requirement: "Rec. ITU-T T.800, I.5.3: there shall be at least one colour specification box within the JP2 header box. The reference decoders read the file as sRGB.");
        Add("file-header-size", Jp2FileBuilder.File(codestream, Jp2FileBuilder.ImageHeader(14, 9, 3, 7), Jp2FileBuilder.EnumeratedColor(ColorSpaceSrgb)), ".jp2",
            "The image header box gives a width of 14 and the codestream a width of 13.");
        Add("file-box-length", Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, [0, 0, 0, 4, .. "jp2c"u8, .. codestream]), ".jp2",
            "The codestream box declares a length of 4 bytes, less than its own header.",
            requirement: "Rec. ITU-T T.800, I.4: the values 2 to 7 of the LBox field are reserved. The reference decoders skip to the codestream.");
        Add("file-codestream-cut", Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, Jp2FileBuilder.Box("jp2c", codestream[..^1]), Jp2FileBuilder.Uuid(Jp2FileBuilder.XmpUuid(), XmpPacket)), ".jp2",
            "The codestream box ends one byte before the end of its codestream.");

        // The high-throughput block coder: the last byte of the HT cleanup segment of the last code-block gives a suffix
        // of more than 4079 bytes
        var highThroughput = Input(c, "ht-lossless-gray8");
        var corrupt = (byte[])highThroughput.Clone();
        corrupt[^3] = 0xFF;
        Add("ht-suffix-length", corrupt, ".j2c", "The last code-block of jp2/ht-lossless-gray8 declares an HT cleanup suffix longer than its segment (T.814, 7.1.1).",
            features: (IList<string>)c.Fixtures.First(f => (string)f["id"]! == "jp2/ht-lossless-gray8")["features"]!);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Valid files the library does not read
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildUnsupported(Corpus<Jp2Tools> c)
    {
        void Add(string name, Encoded encoded, string extension, string feature, string notes, Func<ReferenceJpeg2000, bool> isAsIntended, string origin = "generated")
        {
            var file = ReferenceJpeg2000.Parse(encoded.Data);
            Py.Assert(isAsIntended(file), $"invalid/jp2/{name} does not have the feature it is named after");
            c.AddError("invalid/jp2/" + name, "unsupported", "invalid/jp2/" + name + extension, encoded.Data, "jp2", c.Provenance(origin, nameof(BuildUnsupported), null, ToolList(c), encoded.Commands),
                new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Jpeg2000", ["feature"] = feature }, features: Features(file), notes: notes);
        }

        Encoded Assembled(Encoded codestream, byte[] file) => new(file, [.. codestream.Commands, "{output}.j2k is the codestream of the file; its boxes are written by the generator"]);

        var gray = CommonCorpus.PatternCheckerboardGray(11, 7);
        int[] samples = [.. gray.Pixels.Select(pixel => pixel[0] - 128)];
        Add("signed-samples", OpjRaw(c, 11, 7, 8, isSigned: true, [(samples, 1, 1)], ["-n", "2"]), ".j2k", "signed samples",
            "The samples of the component are signed: an image of the library holds unsigned samples.", file => file.Components is [{ IsSigned: true }]);

        var rgb = CommonCorpus.PatternRgbOdd(13, 9);
        var planes = ToPlanes(rgb);
        Add("five-components", OpjRaw(c, 13, 9, 8, isSigned: false, [(planes.Channels[0], 1, 1), (planes.Channels[1], 1, 1), (planes.Channels[2], 1, 1), (planes.Channels[0], 1, 1), (planes.Channels[1], 1, 1)], ["-n", "2", "-mct", "0"]),
            ".j2k", "component count", "A codestream of five components and no color specification: nothing says what they are.", file => file.Components.Count == 5);

        // The first component has one sample for 2x2 points of the reference grid
        int[] Half(int[] plane) => [.. Enumerable.Range(0, 5).SelectMany(y => Enumerable.Range(0, 7).Select(x => plane[(y * 2 * 13) + (x * 2)]))];
        Add("subsampled-first-component", OpjRaw(c, 13, 9, 8, isSigned: false, [(Half(planes.Channels[0]), 2, 2), (planes.Channels[1], 1, 1), (planes.Channels[2], 1, 1)], ["-n", "2", "-mct", "0"]),
            ".j2k", "sub-sampled component", "The first component is sub-sampled: the library reads sub-sampled chrominances only.", file => file.Components[0] is { XSubsampling: 2, YSubsampling: 2 });

        var corners = CommonCorpus.PatternCornerMarkers(12, 9);
        var rgba = Opj(c, corners, ["-n", "3"]);
        Add("premultiplied-opacity", Assembled(rgba, Jp2File(rgba.Data, 12, 9, 4, 8, Jp2FileBuilder.EnumeratedColor(ColorSpaceSrgb), Jp2FileBuilder.ChannelDefinition((0, 0, 1), (1, 0, 2), (2, 0, 3), (3, 2, 0)))),
            ".jp2", "premultiplied alpha", "The fourth channel is a premultiplied opacity: an image of the library holds straight alpha.",
            file => file.ChannelDefinitions.Any(channel => channel is { Channel: 3, Type: 2 }), "hand-authored");
        Add("two-opacities", Assembled(rgba, Jp2File(rgba.Data, 12, 9, 4, 8, Jp2FileBuilder.EnumeratedColor(ColorSpaceGray), Jp2FileBuilder.ChannelDefinition((0, 0, 1), (1, 1, 0), (2, 1, 0), (3, 1, 0)))),
            ".jp2", "several opacity channels", "A gray channel and three opacity channels.", file => file.ChannelDefinitions.Count(channel => channel.Type == 1) == 3, "hand-authored");

        var stream = Opj(c, rgb, ["-n", "3"]);
        var extended = (byte[])stream.Data.Clone();
        extended[6] |= 0x80;
        Add("part-2-extensions", new Encoded(extended, [.. stream.Commands, "the bit 15 of Rsiz is set in {output}.j2k"]), ".j2k", "extensions of ISO/IEC 15444-2",
            "Rsiz declares the extensions of Rec. ITU-T T.801 | ISO/IEC 15444-2.", file => (file.Capabilities & 0x8000) != 0, "hand-authored");
        Add("motion-jpeg-2000", Assembled(stream, Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), Jp2FileBuilder.FileType("mjp2", "mjp2"),
                Jp2FileBuilder.Box("jp2h", Jp2FileBuilder.ImageHeader(13, 9, 3, 7), Jp2FileBuilder.EnumeratedColor(ColorSpaceSrgb)), Jp2FileBuilder.Box("jp2c", stream.Data))),
            ".jp2", "brand 'mjp2'", "A file of the brand of Motion JPEG 2000, with no compatible brand of a still image.", file => file.Brand == "mjp2", "hand-authored");
        Add("two-codestreams", Assembled(stream, Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), Jp2FileBuilder.FileType("jpx ", "jpx "),
                Jp2FileBuilder.Box("jp2h", Jp2FileBuilder.ImageHeader(13, 9, 3, 7), Jp2FileBuilder.EnumeratedColor(ColorSpaceSrgb)), Jp2FileBuilder.Box("jp2c", stream.Data), Jp2FileBuilder.Box("jp2c", stream.Data))),
            ".jp2", "several codestreams", "A file of two codestreams: a compound image.", file => file.CodestreamCount == 2, "hand-authored");

        // The high-throughput block coder
        var ojph = Ojph(c, ToPlanes(gray), isReversible: true, ["-num_decomps", "2"]);
        var mixed = (byte[])ojph.Data.Clone();
        var cod = FindMarkerSegment(mixed, 0x52);
        mixed[cod + 12] = 0xC0;
        Add("ht-mixed", new Encoded(mixed, [.. ojph.Commands, "the bit 7 of the code-block style is set in {output}.j2c"]), ".j2c", "mixed block coders",
            "Each code-block chooses the high-throughput block coder or the one of Rec. ITU-T T.800 (T.814, Table A.4).", file => file.CodingStyles[0].CodeBlockStyle == 0xC0, "hand-authored");
        var coefficients = Enumerable.Range(0, 20 * 13).Select(i => ((i * 7) % 23) - 11).ToList();
        Add("ht-two-sets", AuthorHighThroughput(c, 20, 13, coefficients, 3, packets: AuthoredPackets.SecondSetInSecondPacket).Encoded, ".j2c", "several HT sets",
            "The code-block has a second HT set, whose HT cleanup segment is empty (T.814, B.3): only the first one is to be decoded.", file => file.LayerCount == 2, "hand-authored");
        Add("ht-placeholders-alone", AuthorHighThroughput(c, 20, 13, coefficients, 1, placeholderSets: 1, packets: AuthoredPackets.PlaceholdersInFirstPacket).Encoded, ".j2c", "placeholder passes over several packets",
            "The first packet of the code-block holds three placeholder passes and the second one its HT cleanup pass (T.814, B.3).", file => file.LayerCount == 2, "hand-authored");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Limits
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildLimits(Corpus<Jp2Tools> c)
    {
        void Add(string name, string sourceName, Obj limits, string limitKind, string notes)
        {
            var source = c.Fixtures.First(f => (string)f["id"]! == "jp2/" + sourceName);
            var input = (Obj)source["input"]!;
            c.AddError("limit/jp2/" + name, "limit", null, null, "jp2", c.Provenance("hand-authored", nameof(BuildLimits), null, [ToolSet.RuntimeLabel]),
                new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = limitKind }, features: source["features"] as IList<string>,
                decodeOptions: new Obj { ["limits"] = limits }, existingInput: new Obj { ["path"] = input["path"], ["sha256"] = input["sha256"] }, notes: notes);
        }

        Add("width-over-limit", "lossless-rgb8", new Obj { ["MaxWidth"] = 12 }, "Width", "The image is 13 pixels wide.");
        Add("height-over-limit", "lossless-rgb8", new Obj { ["MaxHeight"] = 8 }, "Height", "The image is 9 pixels high.");
        Add("frame-pixels-over-limit", "lossless-rgb8", new Obj { ["MaxFramePixels"] = 116 }, "FramePixels", "The image has 117 pixels.");
        Add("metadata-bytes-over-limit", "file-exif-xmp", new Obj { ["MaxMetadataBytes"] = 64 }, "MetadataBytes", "The EXIF and XMP payloads are larger than 64 bytes together.");
        Add("metadata-bytes-after-codestream", "file-metadata-after-codestream", new Obj { ["MaxMetadataBytes"] = 64 }, "MetadataBytes", "The EXIF and XMP payloads, which follow the codestream, are larger than 64 bytes together.");
    }
}
