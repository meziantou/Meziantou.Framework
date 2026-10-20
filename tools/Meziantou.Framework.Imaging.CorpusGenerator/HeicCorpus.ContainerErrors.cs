using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Heif;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The malformed, unsupported and over-limit HEIC fixtures: files assembled from the description of a valid fixture with
// one defect each (HeifFileBuilder), byte edits of such a file, files around a committed HEVC vector that the library does
// not decode, and two files heif-enc writes that the library rejects. They carry no expected pixels. Each defect is stated
// from the specifications; the stage at which the library must report it (header identification, full scan, decoding) is
// recorded by the tests (Tests.Shared/Suites/Decode/ErrorFixtureStages.cs).
//
// The container rules that do not depend on the codec are tested once, by the AVIF fixtures (invalid/avif): this list
// keeps one fixture of each family, and every rule that the HEVC side of the container has of its own (the brands, the
// configuration record, the NAL units of an item or of a sample, the sample entries, the composition times).
internal static partial class HeicCorpus
{
    /// <summary>The small color image (16x16, 4:4:4, a colr box of type nclx) that most error fixtures are assembled from.</summary>
    private const string ErrorSource = "nclx-overrides-bitstream";

    /// <summary>The image sequence (eight 20x18 images with B pictures, an edit list) that the sequence fixtures are assembled from.</summary>
    private const string SequenceErrorSource = "sequence-window-reordered-2-plays";

    private static void AddHeicError(ContainerContext context, string id, string kind, byte[]? data, string exception, string notes, string? feature = null, Obj? limits = null, string? limitKind = null,
        string? reuse = null, Obj? parameters = null, IList<string>? tools = null, string format = "Heic", string extension = ".heic")
    {
        var expected = new Obj { ["exception"] = exception };
        if (exception is "InvalidImageContentException" or "UnsupportedImageFeatureException")
            expected["format"] = format;
        if (limitKind is not null)
            expected["limitKind"] = limitKind;

        context.Corpus.AddError(id, kind, reuse is null ? id + extension : null, data, "heic",
            context.Corpus.Provenance("hand-authored", kind == "limit" ? "BuildLimits" : kind == "unsupported" ? "BuildUnsupported" : "BuildInvalid", parameters, tools ?? [context.Tools.Hevc.X265Label + " (payloads)", ToolSet.RuntimeLabel]),
            expected,
            features: feature is null ? null : [feature],
            decodeOptions: limits is null ? null : new Obj { ["limits"] = limits },
            notes: notes,
            existingInput: reuse is null ? null : new Obj { ["path"] = context.InputEntries[reuse]["path"], ["sha256"] = context.InputEntries[reuse]["sha256"] });
    }

    private static void Invalid(ContainerContext context, string name, string source, Action<HeifFileBuilder> change, string notes, string exception = "InvalidImageContentException")
    {
        var builder = HeifFileBuilder.From(context.Inputs[source]);
        change(builder);
        AddHeicError(context, "invalid/heic/" + name, "invalid", builder.Build(), exception, notes, extension: builder.Tracks.Count > 0 ? ".heics" : ".heic");
    }

    private static void Unsupported(ContainerContext context, string name, string source, Action<HeifFileBuilder> change, string feature, string notes, string format = "Heic")
    {
        var builder = HeifFileBuilder.From(context.Inputs[source]);
        change(builder);
        AddHeicError(context, "invalid/heic/" + name, "unsupported", builder.Build(), "UnsupportedImageFeatureException", notes, "heic.unsupported=" + feature, format: format, extension: builder.Tracks.Count > 0 ? ".heics" : ".heic");
    }

    /// <summary>The first top-level box of a type.</summary>
    private static ReferenceHeif.Box TopLevelBox(byte[] data, string type) => ReferenceHeif.Parse(data).TopLevelBoxes.First(box => box.Type == type);

    /// <summary>The offset of the first occurrence of a four-character code: the type field of a box.</summary>
    private static int FindType(byte[] data, string type)
    {
        var index = Bytes.Find(data, Bytes.Ascii(type));
        Py.Assert(index >= 4, type);
        return index;
    }

    /// <summary>The hvcC property of the primary item of a file, as a box.</summary>
    private static byte[] ConfigurationBox(HeifFileBuilder builder)
    {
        var item = builder.GetItem(builder.PrimaryItemId!.Value);
        return [.. builder.Properties[item.Associations[FindAssociation(builder, item, "hvcC")].PropertyIndex - 1]];
    }

    /// <summary>The NAL units of the data of the primary item of a file whose lengths have four bytes.</summary>
    private static List<byte[]> ItemUnits(HeifFileBuilder builder)
    {
        var data = builder.GetItem(builder.PrimaryItemId!.Value).Data;
        var units = new List<byte[]>();
        for (var position = 0; position < data.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            units.Add(data[(position + 4)..(position + 4 + length)]);
            position += 4 + length;
        }

        return units;
    }

    private static void BuildInvalid(ContainerContext context)
    {
        var source = context.Inputs[ErrorSource];
        var meta = TopLevelBox(source, "meta");

        // Boxes: one fixture of each family of the container rules
        AddHeicError(context, "invalid/heic/truncated-meta", "invalid", source[..(int)(meta.Offset + (meta.Size / 2))], "InvalidImageContentException",
            "The file ends in the middle of the meta box.");
        AddHeicError(context, "invalid/heic/truncated-item-data", "invalid", source[..^9], "InvalidImageContentException",
            "The file ends 9 bytes before the end of the data of the primary item: inside its slice.");
        var oversized = (byte[])source.Clone();
        var ipco = FindType(oversized, "ipco") - 4;
        BinaryPrimitives.WriteUInt32BigEndian(oversized.AsSpan(ipco), BinaryPrimitives.ReadUInt32BigEndian(oversized.AsSpan(ipco)) + 100);
        AddHeicError(context, "invalid/heic/child-box-exceeds-parent", "invalid", oversized, "InvalidImageContentException",
            "The ipco box declares 100 bytes more than its iprp container holds.");

        // File type
        Invalid(context, "missing-ftyp", ErrorSource, builder => builder.Layout.Remove(HeifFileBuilder.FileTypeBox),
            "The file starts with its meta box: without a file type box it is not recognized as an HEIC file.", "UnknownImageFormatException");

        // Items and properties
        Invalid(context, "missing-pitm", ErrorSource, builder => builder.PrimaryItemId = null, "The meta box has no pitm box: no primary item.");
        Invalid(context, "missing-ispe", ErrorSource, builder => RemoveProperty(builder, builder.GetItem(1), "ispe"), "The primary item has no ispe property (ISO/IEC 23008-12 requires one for every image item).");
        Invalid(context, "missing-hvcc", ErrorSource, builder => RemoveProperty(builder, builder.GetItem(1), "hvcC"), "The hvc1 item has no hvcC property.");
        Invalid(context, "item-outside-file", ErrorSource, builder => builder.GetItem(1).ExtentOverride = [(4096, builder.GetItem(1).Data.Length)],
            "The extent of the primary item starts at offset 4096, after the end of the file.");
        Invalid(context, "ispe-mismatch", ErrorSource, builder => ReplaceProperty(builder, builder.GetItem(1), "ispe", HeifFileBuilder.ImageSize(17, 16)),
            "The ispe property declares 17x16 and the picture is 16x16.");

        // The configuration record (ISO/IEC 14496-15, section 8.3.3.1). The offsets are those of the payload of the box
        void Record(string name, Func<byte[], byte[]> change, string notes, string exception = "InvalidImageContentException") =>
            Invalid(context, name, ErrorSource, builder =>
            {
                var payload = change(ConfigurationBox(builder)[8..]);
                ReplaceProperty(builder, builder.GetItem(1), "hvcC", HeifFileBuilder.Box("hvcC", payload));
            }, notes, exception);

        Record("hvcc-truncated", payload => payload[..28], "The hvcC property ends after 28 bytes: inside the first NAL unit of its first array, whose length it has just declared.");
        Record("hvcc-length-size-reserved", payload =>
        {
            payload[21] = (byte)((payload[21] & 0xFC) | 2);
            return payload;
        }, "lengthSizeMinusOne is 2: the lengths of the NAL units would have three bytes, a value the record reserves.");
        Record("hvcc-bit-depth-mismatch", payload =>
        {
            payload[17] = payload[18] = 0xFA;
            return payload;
        }, "The hvcC property declares 10-bit samples and its sequence parameter set 8-bit samples.");
        Record("hvcc-chroma-format-mismatch", payload =>
        {
            payload[16] = 0xFD;
            return payload;
        }, "The hvcC property declares 4:2:0 samples and its sequence parameter set 4:4:4 samples.");

        // The data of a coded item: NAL units preceded by their length, which hold exactly one picture, an intra one
        void Data(string name, Func<List<byte[]>, byte[]> change, string notes) =>
            Invalid(context, name, ErrorSource, builder => builder.GetItem(1).Data = change(ItemUnits(builder)), notes);

        Data("no-picture", units =>
        {
            var others = units.Where(unit => HevcCorpus.NalUnitType(unit) >= 32).ToList();
            Py.Assert(others.Count > 0, "the picture has an SEI message");
            return LengthPrefixed(others, 4);
        }, "The data of the item hold the SEI message of the picture (its decoded picture hash) and no slice: an item with no picture.");
        Data("two-pictures", units => LengthPrefixed(units.Concat(units), 4),
            "The data of the item hold the NAL units of the picture twice: two pictures, where an image item is one.");
        Data("nal-length-exceeds-item", units =>
        {
            var data = LengthPrefixed(units, 4);
            var last = data.Length - units[^1].Length - 4;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(last), (uint)units[^1].Length + 5);
            return data;
        }, "The length of the last NAL unit of the item is 5 bytes more than the data of the item hold.");
        Data("nal-length-truncated", units => [.. LengthPrefixed(units, 4), 0, 0],
            "Two bytes follow the last NAL unit of the item: not enough for the length of another one.");
        var headerLength = context.Tools.Hevc.SliceHeaderLengths(context.NewScratch("slice-header"), "slice", ReferenceHeif.Parse(source).PrimaryItem!.HevcConfiguration!.ToByteStream(ReferenceHeif.Parse(source).ReadItemData(ReferenceHeif.Parse(source).PrimaryItem!))).Single();
        Data("slice-data-truncated", units =>
        {
            var slice = units.FindIndex(unit => HevcCorpus.NalUnitType(unit) < 32);
            Py.Assert(units[slice].Length > headerLength + 8, "slice data");
            units[slice] = units[slice][..(headerLength + ((units[slice].Length - headerLength) / 2))];
            return LengthPrefixed(units, 4);
        }, "The slice of the picture ends in the middle of its slice data (its NAL unit is shorter, and its length says so): the picture is not complete.");
        Data("cabac-garbage", units =>
        {
            var slice = units.FindIndex(unit => HevcCorpus.NalUnitType(unit) < 32);
            var unit = (byte[])units[slice].Clone();
            Py.Assert(Bytes.Find(unit.AsSpan(0, headerLength), [0, 0, 3]) < 0, "no emulation prevention byte in the slice header");
            var random = new PyRandom(20240613);
            for (var i = headerLength; i < unit.Length; i++)
            {
                // Bytes of 4 and more: no emulation prevention is needed, and none is taken for one
                unit[i] = (byte)random.RandInt(4, 255);
            }

            units[slice] = unit;
            return LengthPrefixed(units, 4);
        }, Inv($"The {headerLength} bytes of the NAL unit header and of the slice segment header are kept, and every byte of the slice data is replaced by a pseudo-random one: the arithmetic decoder reads syntax elements that are not a picture."));

        // An item whose picture is not an intra random access point: the second picture of a sequence, which refers to the first
        var pair = X265(context, "not-irap", [Drifting(20, 18, 0), Drifting(20, 18, 1)], "yuv444p", "keyint=8:bframes=0:qp=22:ctu=16:scenecut=0");
        var pairUnits = HevcCorpus.SplitAccessUnits(pair.Stream);
        Py.Assert(pairUnits.Count == 2 && HevcCorpus.NalUnitType(pairUnits[1].First(unit => HevcCorpus.NalUnitType(unit) < 32)) < 16, "a trailing picture");
        var (pairRecord, pairSamples) = Package(pair, new StreamLayout());
        var notIrap = new HeifFileBuilder { PrimaryItemId = 1 };
        SetBrands(notIrap, "heix", "mif1", "heix", "miaf");
        notIrap.AddItem(1, "hvc1", pairSamples[1], -notIrap.AddProperty(pairRecord), notIrap.AddProperty(HeifFileBuilder.ImageSize(20, 18)));
        AddHeicError(context, "invalid/heic/first-picture-not-irap", "invalid", notIrap.Build(), "InvalidImageContentException",
            "The data of the item are the second picture of a sequence of two, a P picture that refers to the first one: the picture of an image item is an intra random access point.");

        // Grids, assembled from one 16x16 coded item used for every cell
        void Grid(string name, int tiles, uint ispeWidth, uint ispeHeight, string notes, bool secondTileSize = false)
        {
            Invalid(context, name, ErrorSource, builder =>
            {
                var tile = builder.GetItem(1);
                tile.Id = 2;
                tile.IsHidden = true;
                var grid = builder.AddItem(1, "grid", HeifFileBuilder.GridDescriptor(2, 2, 32, 32), builder.AddProperty(HeifFileBuilder.ImageSize(ispeWidth, ispeHeight)));
                foreach (var (index, _) in tile.Associations.Where(association => PropertyType(builder.Properties[association.PropertyIndex - 1]) is "colr"))
                    grid.Associations.Add((index, false));

                var ids = new List<uint>();
                for (var i = 0; i < tiles; i++)
                {
                    ids.Add((uint)(2 + i));
                    if (i == 0)
                        continue;

                    var copy = builder.AddItem((uint)(2 + i), "hvc1", tile.Data);
                    copy.IsHidden = true;
                    foreach (var association in tile.Associations)
                        copy.Associations.Add(association);
                    if (secondTileSize && i == 1)
                        ReplaceProperty(builder, copy, "ispe", HeifFileBuilder.ImageSize(16, 15));
                }

                builder.References.Add(("dimg", 1, [.. ids]));
            }, notes);
        }

        Grid("grid-tile-count-mismatch", 3, 32, 32, "A 2x2 grid with three tiles.");
        Invalid(context, "grid-missing-tile", "grid-2x1-alpha", builder =>
        {
            // The second tile of the color grid is item 11
            var index = Enumerable.Range(0, builder.References.Count).Single(i => builder.References[i] is { Type: "dimg", FromItemId: 1 });
            Py.Assert(builder.References[index].ToItemIds.SequenceEqual(new uint[] { 10, 11 }), "tiles");
            builder.References[index] = ("dimg", 1, [10, 99]);
        }, "The second tile of the grid is item 99, which does not exist.");
        Grid("grid-size-mismatch", 4, 32, 30, "The grid descriptor declares 32x32 and the ispe property of the grid item 32x30.");
        Grid("grid-tiles-differ", 4, 32, 32, "The second tile of the grid declares 16x15 and the others 16x16: the tiles of a grid have the same size.", secondTileSize: true);

        // Image sequences
        Invalid(context, "sequence-no-hevc-track", SequenceErrorSource, builder =>
        {
            foreach (var track in builder.Tracks)
                track.SampleEntryType = "avc1";
        }, "A file with the brands of the HEVC image sequences whose only track is not coded with HEVC (an avc1 sample entry): it has no image sequence to present.");
        Invalid(context, "sequence-missing-chunk-offsets", SequenceErrorSource, builder => builder.OmittedBoxes.Add("stco"),
            "The sample table of the track has no chunk offset box: the samples cannot be located.");
        var sequence = context.Inputs[SequenceErrorSource];
        AddHeicError(context, "invalid/heic/sequence-truncated", "invalid", sequence[..^5], "InvalidImageContentException",
            "The file ends 5 bytes before the end of the last sample of the image sequence.", extension: ".heics");
        Invalid(context, "sequence-ctts-collision", SequenceErrorSource, builder =>
        {
            // The composition time of the second sample becomes the one of the first
            var track = builder.Tracks[0];
            var offsets = track.CompositionOffsets!;
            offsets[1] = offsets[0] - (int)track.Samples[0].Duration;
        }, "The composition offsets give the first two samples the same composition time: two images of a sequence are not presented at the same time.");
        Invalid(context, "sequence-missing-reference", SequenceErrorSource, builder =>
        {
            // The sample that is decoded second is a picture the following ones refer to
            var track = builder.Tracks[0];
            track.Samples.RemoveAt(1);
            track.CompositionOffsets!.RemoveAt(1);
            track.EditList = null;
        }, "The second sample of the track, a picture that the following ones are predicted from, is removed with its composition offset: a later picture refers to a picture that the track does not have.");
        Invalid(context, "sequence-record-mismatch", SequenceErrorSource, builder =>
        {
            var boxes = builder.Tracks[0].SampleEntryBoxes;
            var index = Enumerable.Range(0, boxes.Count).Single(i => PropertyType(boxes[i]) == "hvcC");
            var record = (byte[])boxes[index].Clone();
            record[8 + 17] = record[8 + 18] = 0xFA;
            boxes[index] = record;
        }, "The hvcC box of the sample entry declares 10-bit samples and its sequence parameter set 8-bit samples.");
        Invalid(context, "sequence-track-meta-malformed", SequenceErrorSource, builder =>
            builder.Tracks[0].MetaBoxOverride = HeifFileBuilder.Box("meta", HeifFileBuilder.U32(0), HeifFileBuilder.U32(100), HeifFileBuilder.Ascii("iinf"), new byte[6]),
            "The meta box of the track holds an iinf box that declares 100 bytes: more than the meta box that contains it.");
    }

    /// <summary>A file around a committed HEVC vector of one picture that the library does not decode.</summary>
    private static byte[] WrapVector(string vector, int chromaFormat, int bitDepthLuma, int bitDepthChroma, (uint Width, uint Height) size)
    {
        var stream = File.ReadAllBytes(CorpusDriver.CorpusDir / (vector + ".hevc"));
        var units = HevcCorpus.SplitAccessUnits(stream);
        Py.Assert(units.Count == 1, "one picture");
        var sets = units[0].Where(unit => HevcCorpus.IsParameterSet(HevcCorpus.NalUnitType(unit))).ToList();
        var arrays = new[] { 32, 33, 34 }.Select(type => (type, sets.Where(unit => HevcCorpus.NalUnitType(unit) == type).ToArray())).ToList();
        var record = HeifFileBuilder.HevcConfiguration(chromaFormat, bitDepthLuma, bitDepthChroma, arrays, 4, ProfileTierLevel(sets.Single(unit => HevcCorpus.NalUnitType(unit) == 33)));
        var builder = new HeifFileBuilder { PrimaryItemId = 1 };
        SetBrands(builder, "heix", "mif1", "heix", "miaf");
        builder.AddItem(1, "hvc1", LengthPrefixed(units[0].Where(unit => !HevcCorpus.IsParameterSet(HevcCorpus.NalUnitType(unit))), 4), -builder.AddProperty(record), builder.AddProperty(HeifFileBuilder.ImageSize(size.Width, size.Height)));
        return builder.Build();
    }

    private static void BuildUnsupported(ContainerContext context)
    {
        // Brands and codecs: the format of a file is the one of the codec of its primary item among those it declares
        Unsupported(context, "ftyp-without-heic-brand", ErrorSource, builder => SetBrands(builder, "avif", "avif", "mif1", "miaf"),
            "codec", "A file that only declares the brands of AVIF and whose primary item is an hvc1 item: an AVIF file with an image of another codec.", format: "Avif");
        var av1 = HeifFileBuilder.From(File.ReadAllBytes(CorpusDriver.CorpusDir / "avif" / "lossy-444-bt601-full.avif"));
        SetBrands(av1, "heic", "mif1", "heic", "miaf");
        AddHeicError(context, "invalid/heic/item-av01", "unsupported", av1.Build(), "UnsupportedImageFeatureException",
            "The file avif/lossy-444-bt601-full under the brands of HEIC only: an HEIC file whose primary item is an av01 item.", "heic.unsupported=codec",
            tools: [ToolSet.RuntimeLabel + " (payload: the committed fixture avif/lossy-444-bt601-full)"]);
        Unsupported(context, "hvcc-version", ErrorSource, builder =>
        {
            var payload = ConfigurationBox(builder)[8..];
            payload[0] = 2;
            ReplaceProperty(builder, builder.GetItem(1), "hvcC", HeifFileBuilder.Box("hvcC", payload));
        }, "configuration-record", "The configurationVersion of the hvcC property is 2: a reader of version 1 cannot read the record.");
        Unsupported(context, "item-lhv1", ErrorSource, builder => builder.GetItem(1).Type = "lhv1",
            "layers", "The primary item is of type lhv1: a layer of a layered HEVC bitstream (L-HEVC), which is not decoded.");
        Unsupported(context, "layer-selector", ErrorSource, builder => builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.LayerSelector(1)), true)),
            "layers", "An lsel property selects layer 1: only the base layer of an HEVC bitstream is decoded.");

        // Premultiplied alpha, written by heif-enc
        var (premultiplied, _) = HeifEnc(context, "premultiplied", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["source.png"] = Png(WithAlpha(CommonCorpus.PatternDetailColor(8, 6, 3))) }, "-L --premultiplied-alpha @source.png");
        Py.Assert(ReferenceHeif.Parse(premultiplied).ItemReferences.Any(reference => reference.Type == "prem"), "prem reference");
        AddHeicError(context, "invalid/heic/premultiplied-alpha", "unsupported", premultiplied, "UnsupportedImageFeatureException",
            "Written by heif-enc --premultiplied-alpha: a prem item reference declares that the color image is premultiplied by its alpha plane.", "heic.unsupported=premultiplied-alpha",
            tools: [context.Tools.HeifEncLabel, ToolSet.RuntimeLabel]);

        void Derived(string type, byte[] data)
        {
            Unsupported(context, "derived-" + type, ErrorSource, builder =>
            {
                var coded = builder.GetItem(1);
                coded.Id = 2;
                var derived = builder.AddItem(1, type, data);
                derived.Storage = data.Length == 0 ? HeifFileBuilder.ItemStorage.None : HeifFileBuilder.ItemStorage.ItemData;
                foreach (var association in coded.Associations.Where(association => PropertyType(builder.Properties[association.PropertyIndex - 1]) is "ispe" or "colr"))
                    derived.Associations.Add(association);
                builder.References.Add(("dimg", 1, [2]));
            }, "derived-image", $"The primary item is a derived image of type {type} whose input is the coded image.");
        }

        Derived("iden", []);
        Derived("iovl", HeifFileBuilder.Concat([0, 0], new byte[8], HeifFileBuilder.U16(8), HeifFileBuilder.U16(8), new byte[4]));

        // A grid as heif-enc writes it for tiles smaller than 64x64: each tile is a 64x64 picture with a clap property,
        // and the grid has none
        var (tileClap, _) = HeifEnc(context, "grid-tile-clap", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["source.png"] = Png(Blocks(64, 32, alpha: false)) }, "-L --cut-tiles 32 @source.png");
        var tileClapFile = ReferenceHeif.Parse(tileClap);
        Py.Assert(tileClapFile.PrimaryItem is { Type: "grid" } tileGrid && tileGrid.CleanAperture is null && tileClapFile.GetGridTiles(tileGrid).All(tile => tile.CleanAperture is not null), "tiles with a clap property");
        AddHeicError(context, "invalid/heic/grid-tile-clap", "unsupported", tileClap, "UnsupportedImageFeatureException",
            "Written by heif-enc --cut-tiles 32 from a 64x32 image: each tile is a 64x64 picture with a clap property that crops it to 32x32, and the grid has no such property. The transformations of a grid apply to the assembled image; a tile that declares one that its grid does not is not presented.",
            "heic.unsupported=grid-tile-transform", tools: [context.Tools.HeifEncLabel, ToolSet.RuntimeLabel]);

        Unsupported(context, "clap-fractional", ErrorSource, builder => builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.CleanAperture(5, 2, 4, 1, 0, 1, 0, 1)), true)),
            "clap", "The clean aperture is 5/2 samples wide: not a whole number of samples.");
        Unsupported(context, "essential-unknown-property", ErrorSource, builder => builder.GetItem(1).Associations.Add((builder.AddProperty(HeifFileBuilder.Box("zzzz", [1, 2, 3])), true)),
            "essential-property", "An unknown property is marked essential: a reader that does not know it must not present the item.");
        Unsupported(context, "matrix-ictcp", ErrorSource, builder => ReplaceProperty(builder, builder.GetItem(1), "colr", HeifFileBuilder.ColorNclx(9, 16, 14, fullRange: true)),
            "matrix", "The colr box declares the ICtCp matrix coefficients (14).");
        Unsupported(context, "alpha-different-size", "alpha-mpegb-urn", builder => ReplaceProperty(builder, builder.GetItem(2), "ispe", HeifFileBuilder.ImageSize(6, 9)),
            "alpha-size", "The alpha auxiliary image declares 6x9 and the color image 16x16.");

        // Bitstreams the HEVC decoder rejects, around the committed vectors that record them
        foreach (var (name, vector, chroma, luma, chromaDepth, feature, notes) in new (string, string, int, int, int, string, string)[]
        {
            ("bitstream-16bit", "hevc/hm-unsupported-16bit-444-32x32", 3, 16, 16, "bit-depth", "16-bit samples: the decoder reads 8 to 12 bits"),
            ("bitstream-unequal-bit-depths", "hevc/hm-unsupported-luma10-chroma8-32x32", 1, 10, 8, "bit-depth", "10-bit luma samples and 8-bit chroma samples: the planes of an image have one bit depth"),
            ("bitstream-extended-precision", "hevc/hm-unsupported-extended-precision-12bit-32x32", 3, 12, 12, "extended-precision", "extended_precision_processing_flag is set"),
        })
        {
            // The hvcC record holds bitDepth - 8 on three bits: 16 bits are written as 8
            AddHeicError(context, "invalid/heic/" + name, "unsupported", WrapVector(vector, chroma, 8 + ((luma - 8) & 7), 8 + ((chromaDepth - 8) & 7), (32, 32)), "UnsupportedImageFeatureException",
                $"The stream of the committed vector {vector} as the item of a file: {notes}.", "heic.unsupported=" + feature,
                tools: [context.Tools.Hevc.HmLabel + " (payload: the committed HEVC vector)", ToolSet.RuntimeLabel]);
        }

        var bottomField = X265(context, "chroma-loc-3", [CommonCorpus.PatternSmoothColor(16, 16)], "yuv420p", "keyint=1:qp=20:chromaloc=3");
        AddHeicError(context, "invalid/heic/chroma-sample-location-3", "unsupported", WrapStill(bottomField).Build(), "UnsupportedImageFeatureException",
            "The bitstream signals chroma_sample_loc_type 3, the position of the chroma samples of a bottom field: the conversion to pixels supports types 0 to 2.", "heic.unsupported=chroma-sample-location");

        // Image sequences
        Unsupported(context, "sequence-fragmented", SequenceErrorSource, builder =>
        {
            builder.ExtraBoxes["moof"] = HeifFileBuilder.Box("moof", HeifFileBuilder.FullBox("mfhd", 0, 0, HeifFileBuilder.U32(1)));
            builder.Layout.Insert(builder.Layout.IndexOf(HeifFileBuilder.MovieBox), "moof");
        }, "fragmented", "The file has a movie fragment box (moof).");
        Unsupported(context, "sequence-size-mismatch", SequenceErrorSource, builder =>
        {
            builder.Tracks[0].Width = 24;
            builder.Tracks[0].Height = 24;
        }, "sequence-size", "The sample entry declares 24x24, the size of the coded pictures, and the pictures are 20x18 once cropped to their conformance window: the images of a sequence have the size of the sample entry.");

        // What heif-enc writes for a sequence of images smaller than 64x64: pictures of 64x64 without conformance window,
        // under a sample entry of the size of the images
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var i = 0; i < 2; i++)
            files[Inv($"frame-{i:D2}.png")] = Png(Drifting(16, 12, i));

        var (padded, _) = HeifEnc(context, "sequence-padded-pictures", files, "-L --timebase 10 --duration 1 --gop-structure intra-only -S @frame-00.png", ".heics");
        var paddedTrack = ReferenceHeif.Parse(padded).ColorTrack!;
        Py.Assert((paddedTrack.Width, paddedTrack.Height) == (16, 12), "the sample entry has the size of the images");
        Py.Assert(paddedTrack.CleanAperture is null, "the sample entry has no clap box");
        AddHeicError(context, "invalid/heic/sequence-padded-pictures", "unsupported", padded, "UnsupportedImageFeatureException",
            "Written by heif-enc -S from two 16x12 images: the pictures are 64x64 (libheif extends a small image to a coding tree block) and have no conformance window, and the sample entry declares 16x12, without a clap box that would select the images in the pictures. The images of a sequence have the size of the sample entry.",
            "heic.unsupported=sequence-size", tools: [context.Tools.HeifEncLabel, ToolSet.RuntimeLabel], extension: ".heics");

        Unsupported(context, "sequence-edit-list-offset", SequenceErrorSource, builder =>
        {
            foreach (var track in builder.Tracks)
            {
                var edit = track.EditList!.Entries[0];
                track.EditList.Entries[0] = (edit.SegmentDuration, edit.MediaTime + 1, edit.MediaRateInteger, edit.MediaRateFraction);
            }
        }, "edit-list", "The edit starts one unit after the presentation time of the first image: it does not play the whole track.");
    }

    private static void BuildLimits(ContainerContext context)
    {
        void Limit(string name, string source, string limit, long value, string kind, string notes)
            => AddHeicError(context, "limit/heic/" + name, "limit", null, "ImageResourceLimitException", notes, limits: new Obj { [limit] = value }, limitKind: kind, reuse: source, tools: [ToolSet.RuntimeLabel]);

        // heic/window-rgb8-lossless is a 20x17 image in a 24x24 picture with a conformance window
        Limit("width-over-limit", "window-rgb8-lossless", "MaxWidth", 19, "Width", "Reuses heic/window-rgb8-lossless (20x17) with MaxWidth = 19.");
        Limit("height-over-limit", "window-rgb8-lossless", "MaxHeight", 16, "Height", "Reuses heic/window-rgb8-lossless (20x17) with MaxHeight = 16.");
        Limit("frame-pixels-over-limit", "window-rgb8-lossless", "MaxFramePixels", 339, "FramePixels", "Reuses heic/window-rgb8-lossless (20x17, 340 pixels) with MaxFramePixels = 339.");
        Limit("encoded-bytes-over-limit", "window-rgb8-lossless", "MaxEncodedBytes", context.Inputs["window-rgb8-lossless"].Length - 1, "EncodedBytes",
            "Reuses heic/window-rgb8-lossless with MaxEncodedBytes one byte below the file length: the data of the item end with the file.");
        Limit("live-allocation-over-limit", "lossless-rgb8-64x64", "MaxLiveAllocationBytes", 49152, "LiveAllocationBytes",
            "Reuses heic/lossless-rgb8-64x64 with 48 KiB of live allocations: enough to read the file and to parse the container, not to decode a 64x64 picture.");
        Limit("clap-picture-over-limit", "lossless-rgb8", "MaxFramePixels", 4095, "FramePixels",
            "Reuses heic/lossless-rgb8, a 12x9 image that a clap property cuts out of a 64x64 picture, with MaxFramePixels = 4095: the limit applies to the picture that is decoded (4096 pixels), not to the aperture.");

        var metadata = ReferenceHeif.Parse(context.Inputs["metadata-exif-xmp-icc"]);
        var icc = metadata.PrimaryItem!.IccProfile!.Value.Length;
        Limit("icc-over-metadata-limit", "metadata-exif-xmp-icc", "MaxMetadataBytes", icc - 1, "MetadataBytes", Inv($"Reuses heic/metadata-exif-xmp-icc with MaxMetadataBytes one byte below its ICC profile ({icc} bytes)."));
        Limit("exif-over-metadata-limit", "metadata-exif-xmp-icc", "MaxMetadataBytes", icc + 1, "MetadataBytes", Inv($"Reuses heic/metadata-exif-xmp-icc with MaxMetadataBytes = {icc + 1}: enough for the ICC profile of the meta box, not for the Exif item that follows."));

        // heic/sequence-window-reordered-2-plays has eight 20x18 images
        Limit("sequence-frames-over-limit", SequenceErrorSource, "MaxFrames", 7, "Frames", "Reuses heic/" + SequenceErrorSource + " (8 images) with MaxFrames = 7.");
        Limit("sequence-total-pixels-over-limit", SequenceErrorSource, "MaxTotalPixels", 2879, "TotalPixels", "Reuses heic/" + SequenceErrorSource + " (8 images of 20x18, 2880 pixels) with MaxTotalPixels = 2879: displayed frames are charged when they are produced, so only decoding reaches the limit.");

        var sequenceXmp = ReferenceHeif.Parse(context.Inputs["sequence-track-meta-idat"]);
        var sequencePacket = sequenceXmp.ReadItemData(sequenceXmp.FindSequenceMetadata().Xmp!).Length;
        var sequenceExif = sequenceXmp.ReadItemData(sequenceXmp.FindSequenceMetadata().Exif!).Length;
        Limit("sequence-xmp-over-metadata-limit", "sequence-track-meta-idat", "MaxMetadataBytes", sequenceExif + sequencePacket - 1, "MetadataBytes",
            Inv($"Reuses heic/sequence-track-meta-idat with MaxMetadataBytes one byte below the data of the Exif item ({sequenceExif} bytes) and of the XMP item ({sequencePacket} bytes) of the meta box of its track."));
    }
}
