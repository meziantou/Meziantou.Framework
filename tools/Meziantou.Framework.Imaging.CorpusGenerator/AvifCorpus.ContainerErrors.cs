using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Avif;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The malformed, unsupported and over-limit AVIF fixtures: files assembled from the description of a valid fixture with
// one defect each (AvifFileBuilder), or byte edits of such a file. They carry no expected pixels. Each defect is stated
// from the specifications; the stage at which the library must report it (header identification, full scan, decoding) is
// recorded by the tests (Tests/Conformance/ErrorFixtureStages.cs).
internal static partial class AvifCorpus
{
    /// <summary>The small color image (8x8, 4:4:4, BT.601) that most error fixtures are assembled from.</summary>
    private const string ErrorSource = "lossy-444-bt601-full";

    private static void AddAvifError(ContainerContext context, string id, string kind, byte[]? data, string exception, string notes, string? feature = null, Obj? limits = null, string? limitKind = null,
        string? reuse = null, Obj? parameters = null, IList<string>? tools = null)
    {
        var expected = new Obj { ["exception"] = exception };
        if (exception is "InvalidImageContentException" or "UnsupportedImageFeatureException")
            expected["format"] = "Avif";
        if (limitKind is not null)
            expected["limitKind"] = limitKind;

        var relative = id + ".avif";
        context.Corpus.AddError(id, kind, reuse is null ? relative : null, data, "avif",
            context.Corpus.Provenance("hand-authored", kind == "limit" ? "BuildLimits" : kind == "unsupported" ? "BuildUnsupported" : "BuildInvalid", parameters, tools ?? [context.Tools.AvifencLabel + " (payloads)", ToolSet.RuntimeLabel]),
            expected,
            features: feature is null ? null : [feature],
            decodeOptions: limits is null ? null : new Obj { ["limits"] = limits },
            notes: notes,
            existingInput: reuse is null ? null : new Obj { ["path"] = context.InputEntries[reuse]["path"], ["sha256"] = context.InputEntries[reuse]["sha256"] });
    }

    /// <summary>The description of a valid file. The meta box of an image sequence, which a reader of the sequence does
    /// not need, is left out so that the error fixtures stay small.</summary>
    private static AvifFileBuilder ErrorBuilder(ContainerContext context, string source)
    {
        var builder = AvifFileBuilder.From(context.Inputs[source]);
        if (builder.Tracks.Count > 0)
        {
            builder.Layout.Remove(AvifFileBuilder.MetaBox);
            builder.Items.Clear();
            builder.Properties.Clear();
            builder.References.Clear();
            builder.PrimaryItemId = null;
        }

        return builder;
    }

    private static void Invalid(ContainerContext context, string name, string source, Action<AvifFileBuilder> change, string notes, string exception = "InvalidImageContentException")
    {
        var builder = ErrorBuilder(context, source);
        change(builder);
        AddAvifError(context, "invalid/avif/" + name, "invalid", builder.Build(), exception, notes);
    }

    private static void Unsupported(ContainerContext context, string name, string source, Action<AvifFileBuilder> change, string feature, string notes)
    {
        var builder = ErrorBuilder(context, source);
        change(builder);
        AddAvifError(context, "invalid/avif/" + name, "unsupported", builder.Build(), "UnsupportedImageFeatureException", notes, "avif.unsupported=" + feature);
    }

    /// <summary>The offset and the size of the first top-level box of a type.</summary>
    private static ReferenceAvif.Box TopLevelBox(byte[] data, string type) => ReferenceAvif.Parse(data).TopLevelBoxes.First(box => box.Type == type);

    /// <summary>The offset of the first occurrence of a four-character code: the type field of a box.</summary>
    private static int FindType(byte[] data, string type)
    {
        var index = Bytes.Find(data, Bytes.Ascii(type));
        Py.Assert(index >= 4, type);
        return index;
    }

    private static void BuildInvalid(ContainerContext context)
    {
        var source = context.Inputs[ErrorSource];
        var meta = TopLevelBox(source, "meta");

        // Boxes
        AddAvifError(context, "invalid/avif/truncated-meta", "invalid", source[..(int)(meta.Offset + (meta.Size / 2))], "InvalidImageContentException",
            "The file ends in the middle of the meta box.");
        AddAvifError(context, "invalid/avif/truncated-item-data", "invalid", source[..^9], "InvalidImageContentException",
            "The file ends 9 bytes before the end of the data of the primary item.");
        Invalid(context, "box-size-too-small", ErrorSource, builder =>
        {
            builder.ExtraBoxes["short"] = [0, 0, 0, 4, (byte)'f', (byte)'r', (byte)'e', (byte)'e'];
            builder.Layout.Insert(1, "short");
        }, "A top-level box declares 4 bytes: less than its own header.");
        var oversized = (byte[])source.Clone();
        var ipco = FindType(oversized, "ipco") - 4;
        BinaryPrimitives.WriteUInt32BigEndian(oversized.AsSpan(ipco), BinaryPrimitives.ReadUInt32BigEndian(oversized.AsSpan(ipco)) + 100);
        AddAvifError(context, "invalid/avif/child-box-exceeds-parent", "invalid", oversized, "InvalidImageContentException",
            "The ipco box declares 100 bytes more than its iprp container holds.");

        // File type
        Invalid(context, "missing-ftyp", ErrorSource, builder => builder.Layout.Remove(AvifFileBuilder.FileTypeBox),
            "The file starts with its meta box: without a file type box it is not recognized as an AVIF file.", "UnknownImageFormatException");
        Invalid(context, "ftyp-without-avif-brand", ErrorSource, builder =>
        {
            builder.MajorBrand = "mif1";
            builder.CompatibleBrands.Clear();
            builder.CompatibleBrands.Add("mif1");
            builder.CompatibleBrands.Add("heic");
        }, "A HEIF file whose brands (mif1, heic) include neither avif nor avis: an ISO base media file that is not an AVIF image.", "UnknownImageFormatException");

        // Items and properties
        Invalid(context, "missing-pitm", ErrorSource, builder => builder.PrimaryItemId = null, "The meta box has no pitm box: no primary item.");
        Invalid(context, "missing-primary-item", ErrorSource, builder => builder.PrimaryItemId = 99, "The pitm box names item 99, which does not exist.");
        Invalid(context, "missing-ispe", ErrorSource, builder => RemoveProperty(builder, builder.GetItem(1), "ispe"), "The primary item has no ispe property (ISO/IEC 23008-12 requires one for every image item).");
        Invalid(context, "missing-av1c", ErrorSource, builder => RemoveProperty(builder, builder.GetItem(1), "av1C"), "The av01 item has no av1C property.");
        Invalid(context, "iden-two-inputs", ErrorSource, builder =>
        {
            WrapInIdentity(builder, 1, 2, 8, 8, describe: true);
            var index = Enumerable.Range(0, builder.References.Count).Single(i => builder.References[i].Type == "dimg");
            builder.References[index] = ("dimg", 1, [2, 2]);
        }, "The identity derivation that is the primary item has two input images.");
        Invalid(context, "iden-missing-input", ErrorSource, builder =>
        {
            WrapInIdentity(builder, 1, 2, 8, 8, describe: true);
            var index = Enumerable.Range(0, builder.References.Count).Single(i => builder.References[i].Type == "dimg");
            builder.References[index] = ("dimg", 1, [99]);
        }, "The input image of the identity derivation that is the primary item is item 99, which does not exist.");
        Invalid(context, "iden-loop", ErrorSource, builder =>
        {
            WrapInIdentity(builder, 1, 2, 8, 8, describe: true);
            WrapInIdentity(builder, 1, 3, 8, 8, describe: true);
            var index = Enumerable.Range(0, builder.References.Count).Single(i => builder.References[i] is { Type: "dimg", FromItemId: 3 });
            builder.References[index] = ("dimg", 3, [1]);
        }, "Two identity derivations are the input image of each other: the primary item is derived from itself.");
        Invalid(context, "iden-size-mismatch", ErrorSource, builder => WrapInIdentity(builder, 1, 2, 9, 8, describe: true),
            "The identity derivation declares 9x8 and its input image is 8x8.");
        Invalid(context, "iovl-size-mismatch", ErrorSource, builder =>
        {
            AddDerivedItem(builder, 10, "iovl", OverlayDescriptor((0, 0, 0, 0), 9, 8, (0, 0)), 8, 8, [1]);
            Present(builder, 10);
        }, "The descriptor of the overlay declares a canvas of 9x8 and its ispe property 8x8.");
        Invalid(context, "iovl-descriptor-truncated", ErrorSource, builder =>
        {
            AddDerivedItem(builder, 10, "iovl", OverlayDescriptor((0, 0, 0, 0), 8, 8, (0, 0)), 8, 8, [1, 1]);
            Present(builder, 10);
        }, "The overlay has two input images and its descriptor one offset.");
        Invalid(context, "derived-loop", ErrorSource, builder =>
        {
            AddDerivedItem(builder, 10, "iovl", OverlayDescriptor((0, 0, 0, 0), 8, 8, (0, 0), (0, 0)), 8, 8, [1, 11]);
            AddDerivedItem(builder, 11, "iden", [], 8, 8, [10]);
            Present(builder, 10);
        }, "An input image of the overlay is an identity derivation of the overlay: the image is derived from itself.");
        Invalid(context, "grid-derived-tiles-differ", "grid-2x2-lossless", builder =>
        {
            var grid = builder.PrimaryItemId!.Value;
            var tiles = builder.References.Single(reference => reference.Type == "dimg" && reference.FromItemId == grid).ToItemIds;
            var next = builder.Items.Max(item => item.Id) + 1;
            AddDerivedItem(builder, next, "iden", [], 64, 64, [tiles[0]]);
            AddDerivedItem(builder, next + 1, "iden", [], 64, 64, [tiles[1]], ApertureOf(64, 64, 0, 0, 60, 64));
            AddDerivedItem(builder, next + 2, "grid", AvifFileBuilder.GridDescriptor(1, 2, 120, 64), 120, 64, [next, next + 1]);
            Present(builder, next + 2);
        }, "The two tiles of the grid are identity derivations of 64x64 and, once cropped, of 60x64: the tiles of a grid have the same size.");
        Invalid(context, "two-meta-boxes", ErrorSource, builder => builder.Layout.Insert(2, AvifFileBuilder.MetaBox), "The file has two meta boxes at its top level (ISO/IEC 14496-12 allows at most one).");
        Invalid(context, "item-outside-file", ErrorSource, builder => builder.GetItem(1).ExtentOverride = [(4096, builder.GetItem(1).Data.Length)],
            "The extent of the primary item starts at offset 4096, after the end of the file.");
        Invalid(context, "item-outside-idat", ErrorSource, builder =>
        {
            var item = builder.GetItem(1);
            item.Storage = AvifFileBuilder.ItemStorage.ItemData;
            item.ExtentOverride = [(0, item.Data.Length + 50)];
            item.ConstructionMethodOverride = 1;
        }, "The extent of the primary item is 50 bytes longer than the idat box that stores it.");
        Invalid(context, "ispe-mismatch", ErrorSource, builder => ReplaceProperty(builder, builder.GetItem(1), "ispe", AvifFileBuilder.ImageSize(9, 8)),
            "The ispe property declares 9x8 and the AV1 frame is 8x8.");
        Invalid(context, "av1c-mismatch", ErrorSource, builder => ReplaceProperty(builder, builder.GetItem(1), "av1C", AvifFileBuilder.Av1Configuration(1, 0, 10, monochrome: false, 0, 0)),
            "The av1C property declares 10-bit samples and the sequence header of the bitstream 8-bit samples.");

        // Operating points and layers (AV1 Image File Format, section 2.3.2.1; ISO/IEC 23008-12, section 6.5.11): a file
        // that selects an operating point or a layer its bitstream does not have, a layer AV1 does not have, a layer
        // whose frame is not the size the item declares, and a derived item that selects one
        const string Layers = "layered-3-quality-rgb";
        Invalid(context, "a1op-missing-operating-point", ErrorSource, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(1)),
            "An a1op property selects operating point 1 of a bitstream whose sequence header has one operating point, of index 0.");
        Invalid(context, "a1op-beyond-operating-points", Layers, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(3)),
            "An a1op property selects operating point 3 of the layered bitstream of avif/layered-3-quality-rgb, whose sequence header has three operating points (0 to 2).");
        Invalid(context, "lsel-missing-layer", ErrorSource, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(1)),
            "An lsel property selects layer 1 of a bitstream that has one frame, of spatial layer 0.");
        Invalid(context, "lsel-beyond-layers", Layers, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(3)),
            "An lsel property selects layer 3 of the bitstream of avif/layered-3-quality-rgb, which has three spatial layers (0 to 2).");
        Invalid(context, "lsel-out-of-range", Layers, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(4)),
            "An lsel property selects layer 4: AV1 has four spatial layers (0 to 3), and 0xFFFF is the only other value, which selects none.");
        Invalid(context, "lsel-outside-operating-point", Layers, builder =>
        {
            AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(2));
            AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(1));
        }, "An a1op property selects operating point 2 of avif/layered-3-quality-rgb, which holds the first layer only, and an lsel property layer 1: the operating point does not have the layer, although the bitstream does.");
        Invalid(context, "lsel-layer-size-mismatch", "layered-progressive-444-10bit", builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(0)),
            "An lsel property selects layer 0 of avif/layered-progressive-444-10bit, a 4x4 frame, and the ispe property declares 8x8, the size of the last layer: the selected frame is the image and must have the declared size.");
        Invalid(context, "lsel-on-grid", "layered-grid-2x1", builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(0)),
            "An lsel property selects layer 0 on the grid item of avif/layered-grid-2x1: a derived image has no layer; each tile selects its own.");
        Invalid(context, "a1op-on-grid", "layered-grid-2x1", builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(1)),
            "An a1op property selects operating point 1 on the grid item of avif/layered-grid-2x1: a derived image has no bitstream; each tile selects its own operating point.");

        // Grids, assembled from one 8x8 coded item used for every cell
        void Grid(string name, int tiles, uint width, uint height, uint ispeWidth, uint ispeHeight, string notes, bool secondTileSize = false)
        {
            Invalid(context, name, ErrorSource, builder =>
            {
                var tile = builder.GetItem(1);
                tile.Id = 2;
                tile.IsHidden = true;
                var grid = builder.AddItem(1, "grid", AvifFileBuilder.GridDescriptor(2, 2, width, height), builder.AddProperty(AvifFileBuilder.ImageSize(ispeWidth, ispeHeight)));
                foreach (var (index, _) in tile.Associations.Where(association => PropertyType(builder.Properties[association.PropertyIndex - 1]) is "colr" or "pixi"))
                    grid.Associations.Add((index, false));

                var ids = new List<uint>();
                for (var i = 0; i < tiles; i++)
                {
                    ids.Add((uint)(2 + i));
                    if (i == 0)
                        continue;

                    var copy = builder.AddItem((uint)(2 + i), "av01", tile.Data);
                    copy.IsHidden = true;
                    foreach (var association in tile.Associations)
                        copy.Associations.Add(association);
                    if (secondTileSize && i == 1)
                        ReplaceProperty(builder, copy, "ispe", AvifFileBuilder.ImageSize(8, 7));
                }

                builder.References.Add(("dimg", 1, [.. ids]));
            }, notes);
        }

        Grid("grid-tile-count-mismatch", 3, 16, 16, 16, 16, "A 2x2 grid with three tiles.");
        Grid("grid-size-mismatch", 4, 16, 16, 16, 14, "The grid descriptor declares 16x16 and the ispe property of the grid item 16x14.");
        Grid("grid-tiles-differ", 4, 16, 16, 16, 16, "The second tile of the grid declares 8x7 and the others 8x8: the tiles of a grid have the same size.", secondTileSize: true);

        // Image sequences
        const string SequenceSource = "sequence-lossless-infinite";
        Invalid(context, "sequence-no-av1-track", SequenceSource, builder =>
        {
            foreach (var track in builder.Tracks)
                track.SampleEntryType = "hvc1";
        }, "A file with the avis brand whose only tracks are not coded with AV1 (hvc1 sample entries): it has no image sequence to present.");
        Invalid(context, "sequence-edit-negative-media-time", SequenceSource, builder =>
        {
            foreach (var track in builder.Tracks)
            {
                var edit = track.EditList!.Entries[0];
                track.EditList.Entries[0] = (edit.SegmentDuration, -2, edit.MediaRateInteger, edit.MediaRateFraction);
            }
        }, "The media time of the edit is -2: a media time is positive, or -1 for an empty edit.");
        Invalid(context, "sequence-edit-shows-nothing", SequenceSource, builder =>
        {
            foreach (var track in builder.Tracks)
            {
                var edit = track.EditList!.Entries[0];
                track.EditList.Entries[0] = (4, 60, edit.MediaRateInteger, edit.MediaRateFraction);
            }
        }, "The edit starts at the media time 60, after the last image (the media lasts 6 units): it shows no image.");
        Invalid(context, "sequence-fragment-without-mvex", SequenceSource, builder =>
        {
            builder.ExtraBoxes["moof"] = AvifFileBuilder.Box("moof", AvifFileBuilder.FullBox("mfhd", 0, 0, AvifFileBuilder.U32(1)));
            Py.Assert(builder.Layout.IndexOf(AvifFileBuilder.MovieBox) < builder.Layout.IndexOf(AvifFileBuilder.MediaDataBox), "the samples follow the moov box");
            builder.Layout.Insert(builder.Layout.IndexOf(AvifFileBuilder.MovieBox) + 1, "moof");
        }, "A movie fragment box (moof) follows a moov box that has no mvex box, before the samples: the movie does not announce fragments.");
        Invalid(context, "sequence-fragmented-alpha-missing", "sequence-alpha-repeat-2", builder =>
        {
            // The second color image is in a fragment; its alpha plane is nowhere
            var color = builder.Tracks.Single(track => track.HandlerType == "pict");
            var alpha = builder.Tracks.Single(track => track.HandlerType == "auxv");
            var fragment = new AvifFileBuilder.Fragment();
            var part = new AvifFileBuilder.TrackFragment(color.Id);
            part.Samples.Add((color.Samples[1].Data, color.Samples[1].Duration));
            fragment.Tracks.Add(part);
            builder.Fragments.Add(fragment);
            color.Samples.RemoveAt(1);
            alpha.Samples.RemoveAt(1);
        }, "The color track has an image in the moov box and one in a fragment; the alpha track has one alpha plane only.");
        Invalid(context, "sequence-missing-chunk-offsets", SequenceSource, builder => builder.OmittedBoxes.Add("stco"),
            "The sample tables of the tracks have no chunk offset box: the samples cannot be located.");
        var sequence = ErrorBuilder(context, SequenceSource).Build();
        AddAvifError(context, "invalid/avif/sequence-truncated", "invalid", sequence[..^5], "InvalidImageContentException",
            "The file ends 5 bytes before the end of the last sample of the image sequence.");

        // The metadata of an image sequence: the meta box of its track, or else the meta box of the file
        static AvifFileBuilder.Track ColorTrack(AvifFileBuilder builder) => builder.Tracks.Single(track => track.HandlerType == "pict");
        var packet = Bytes.Utf8("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>");
        Invalid(context, "sequence-track-meta-malformed", SequenceSource, builder =>
            ColorTrack(builder).MetaBoxOverride = AvifFileBuilder.Box("meta", AvifFileBuilder.U32(0), AvifFileBuilder.U32(100), AvifFileBuilder.Ascii("iinf"), new byte[6]),
            "The meta box of the track holds an iinf box that declares 100 bytes: more than the meta box that contains it.");
        Invalid(context, "sequence-track-metadata-outside-file", SequenceSource, builder =>
            ColorTrack(builder).MetaItems.Add(new AvifFileBuilder.Item(1, "mime", packet) { ContentType = "application/rdf+xml", ExtentOverride = [(65536, packet.Length)] }),
            "The XMP item of the meta box of the track is located at offset 65536, after the end of the file.");
        var outside = AvifFileBuilder.From(context.Inputs[SequenceSource]);
        var outsideItem = outside.AddItem(100, "Exif", AvifFileBuilder.ExifItemData(CommonCorpus.ExifTiff(littleEndian: true, orientation: 1)));
        outsideItem.ExtentOverride = [(65536, outsideItem.Data.Length)];
        AddAvifError(context, "invalid/avif/sequence-file-metadata-outside-file", "invalid", outside.Build(), "InvalidImageContentException",
            "The Exif item of the meta box of the file, which precedes the moov box of the image sequence, is located at offset 65536, after the end of the file.");
    }

    private static void BuildUnsupported(ContainerContext context)
    {

        // Derived images that are not supported
        Unsupported(context, "iovl-fill-other-color-space", ErrorSource, builder =>
        {
            ReplaceProperty(builder, builder.GetItem(1), "colr", AvifFileBuilder.ColorNclx(9, 16, 9, fullRange: true));
            AddDerivedItem(builder, 10, "iovl", OverlayDescriptor((0xFFFF, 0, 0, 0xFFFF), 12, 10, (2, 1)), 12, 10, [1]);
            Present(builder, 10);
        }, "derived-image", "The image of the overlay has the BT.2020 primaries and the PQ transfer characteristics, and the canvas is filled with an opaque red, which is an sRGB color: it would have to be converted.");
        Unsupported(context, "iovl-alpha-plane", "lossless-rgba8", builder =>
        {
            AddDerivedItem(builder, 10, "iovl", OverlayDescriptor((0, 0, 0, 0), 12, 9, (0, 0)), 12, 9, [1]);
            var index = Enumerable.Range(0, builder.References.Count).Single(i => builder.References[i].Type == "auxl");
            builder.References[index] = ("auxl", 2, [10]);
            Present(builder, 10);
        }, "derived-image", "The alpha plane is an auxiliary image of the overlay, not of its input image.");
        Unsupported(context, "iovl-layers-color-spaces", "grid-2x2-lossless", builder =>
        {
            var grid = builder.PrimaryItemId!.Value;
            var tiles = builder.References.Single(reference => reference.Type == "dimg" && reference.FromItemId == grid).ToItemIds;
            ReplaceProperty(builder, builder.GetItem(tiles[1]), "colr", AvifFileBuilder.ColorNclx(12, 13, 0, fullRange: true));
            var next = builder.Items.Max(item => item.Id) + 1;
            AddDerivedItem(builder, next, "iovl", OverlayDescriptor((0, 0, 0, 0), 100, 80, (0, 0), (30, 10)), 100, 80, [tiles[0], tiles[1]]);
            Present(builder, next);
        }, "derived-image", "The two input images of the overlay have other color primaries (BT.709 and Display P3): one of them would have to be converted.");
        Unsupported(context, "iovl-layers-icc-profiles", "grid-2x2-lossless", builder =>
        {
            var grid = builder.PrimaryItemId!.Value;
            var tiles = builder.References.Single(reference => reference.Type == "dimg" && reference.FromItemId == grid).ToItemIds;

            // The same profile, created one second later (the creation time is the bytes 24 to 35 of an ICC profile)
            var profile = CommonCorpus.IccProfile("RGB ");
            var other = (byte[])profile.Clone();
            other[35] ^= 1;
            builder.GetItem(tiles[0]).Associations.Add((builder.AddProperty(AvifFileBuilder.ColorProfile(profile)), false));
            builder.GetItem(tiles[1]).Associations.Add((builder.AddProperty(AvifFileBuilder.ColorProfile(other)), false));
            var next = builder.Items.Max(item => item.Id) + 1;
            AddDerivedItem(builder, next, "iovl", OverlayDescriptor((0, 0, 0, 0), 100, 80, (0, 0), (30, 10)), 100, 80, [tiles[0], tiles[1]]);
            Present(builder, next);
        }, "derived-image", "The two input images of the overlay have ICC profiles that are not the same bytes: one of them would have to be converted.");
        Unsupported(context, "iovl-bitstream-other-color-space", "lossy-422-bt709-limited", builder =>
        {
            RemoveProperty(builder, builder.GetItem(1), "colr");
            var overlay = AddDerivedItem(builder, 10, "iovl", OverlayDescriptor((0, 0, 0, 0), 8, 8, (0, 0)), 8, 8, [1]);
            overlay.Associations.Add((builder.AddProperty(AvifFileBuilder.ColorNclx(12, 13, 0, fullRange: true)), false));
            Present(builder, 10);
        }, "derived-image", "The overlay declares the Display P3 primaries, and its input image, which has no colr box, has the BT.709 ones of its AV1 sequence header: it would have to be converted.");
        Unsupported(context, "iovl-depth", ErrorSource, builder =>
        {
            AddDerivedItem(builder, 10, "iovl", OverlayDescriptor((0, 0, 0, 0), 8, 8, (0, 0)), 8, 8, [1]);
            for (uint i = 0; i < 9; i++)
                AddDerivedItem(builder, 11 + i, "iden", [], 8, 8, [10 + i]);

            Present(builder, 19);
        }, "derivation-depth", "The primary item is derived from the overlay through nine nested identity derivations: ten derivations between the image and its coded image, more than the eight that are followed.");

        Unsupported(context, "clap-fractional", ErrorSource, builder => builder.GetItem(1).Associations.Add((builder.AddProperty(AvifFileBuilder.CleanAperture(5, 2, 4, 1, 0, 1, 0, 1)), true)),
            "clap", "The clean aperture is 5/2 samples wide: not a whole number of samples.");
        Unsupported(context, "essential-unknown-property", ErrorSource, builder => builder.GetItem(1).Associations.Add((builder.AddProperty(AvifFileBuilder.Box("zzzz", [1, 2, 3])), true)),
            "essential-property", "An unknown property is marked essential: a reader that does not know it must not present the item.");
        Unsupported(context, "matrix-reserved", ErrorSource, builder => ReplaceProperty(builder, builder.GetItem(1), "colr", AvifFileBuilder.ColorNclx(1, 13, 3, fullRange: true)),
            "matrix", "The colr box declares the matrix coefficients 3, which are reserved.");
        Unsupported(context, "matrix-ictcp-sdr-transfer", ErrorSource, builder => ReplaceProperty(builder, builder.GetItem(1), "colr", AvifFileBuilder.ColorNclx(9, 13, 14, fullRange: true)),
            "matrix", "The colr box declares the ICtCp matrix coefficients (14) with the sRGB transfer characteristics: ICtCp is defined for PQ and for HLG only.");
        Unsupported(context, "matrix-chromaticity-unspecified-primaries", ErrorSource, builder => ReplaceProperty(builder, builder.GetItem(1), "colr", AvifFileBuilder.ColorNclx(2, 13, 13, fullRange: true)),
            "matrix", "The colr box declares the chromaticity-derived constant luminance matrix coefficients (13) and unspecified color primaries: there are no chromaticities to derive them from.");
        Unsupported(context, "iden-depth", ErrorSource, builder =>
        {
            for (uint i = 0; i < 9; i++)
                WrapInIdentity(builder, 1, 2 + i, 8, 8, describe: true);
        }, "derivation-depth", "The primary item is derived from the coded image through nine nested identity derivations, more than the eight that are followed.");
        Unsupported(context, "iden-alpha-untransformed", "lossless-rgba8", builder => WrapInIdentity(builder, 1, 10, 12, 9, describe: true, ApertureOf(12, 9, 1, 2, 8, 6)),
            "alpha-transform", "The primary item is an identity derivation that keeps a clean aperture of 8x6 at (1, 2) of the coded image, and its alpha plane is a coded image of 12x9 without transformation. Readers do not agree on the samples of the plane that the image shows (Apple ImageIO scales the plane to 8x6): an alpha plane must be presented as the image it belongs to.");
        Unsupported(context, "alpha-different-size", "lossless-rgba8", builder => ReplaceProperty(builder, builder.GetItem(2), "ispe", AvifFileBuilder.ImageSize(6, 9)),
            "alpha-size", "The alpha auxiliary image declares 6x9 and the color image 12x9.");

        // Image sequences
        const string SequenceSource = "sequence-lossless-infinite";
        Unsupported(context, "sequence-fragment-before-moov", SequenceSource, builder =>
        {
            builder.ExtraBoxes["moof"] = AvifFileBuilder.Box("moof", AvifFileBuilder.FullBox("mfhd", 0, 0, AvifFileBuilder.U32(1)));
            builder.Layout.Insert(builder.Layout.IndexOf(AvifFileBuilder.MovieBox), "moof");
        }, "fragmented", "A movie fragment box (moof) precedes the moov box: the tracks it adds images to are not known yet.");

        // Fragments that do not follow each other, or whose images are not shown in the order they are decoded
        void RejectedFragments(string name, Action<AvifFileBuilder> configure, string notes) =>
            Unsupported(context, name, SequenceSource, builder =>
            {
                foreach (var track in builder.Tracks)
                {
                    var fragment = new AvifFileBuilder.Fragment();
                    var part = new AvifFileBuilder.TrackFragment(track.Id);
                    foreach (var sample in track.Samples.Skip(1))
                        part.Samples.Add((sample.Data, sample.Duration));

                    fragment.Tracks.Add(part);
                    builder.Fragments.Add(fragment);
                    while (track.Samples.Count > 1)
                        track.Samples.RemoveAt(track.Samples.Count - 1);
                }

                configure(builder);
            }, "fragmented", notes);

        RejectedFragments("sequence-fragmented-gap", builder => builder.Fragments[0].Tracks[0].DecodeTime = 7,
            "The first image is in the moov box and lasts 2 units; the track fragment that holds the two other ones starts at the decode time 7: the fragments do not follow each other.");
        RejectedFragments("sequence-fragmented-composition-offsets", builder => builder.Fragments[0].Tracks[0].CompositionOffset = 3,
            "The samples of the fragment have a composition time offset of 3: they are not shown when they are decoded.");
        RejectedFragments("sequence-fragmented-data-first", builder => builder.Fragments[0].MediaDataFirst = true,
            "The mdat box of the fragment precedes its moof box: the samples went by before they were described.");
        RejectedFragments("sequence-fragmented-edit-list", builder =>
        {
            foreach (var track in builder.Tracks)
            {
                var edit = track.EditList!.Entries[0];
                track.EditList.Entries[0] = (edit.SegmentDuration, 1, edit.MediaRateInteger, edit.MediaRateFraction);
            }
        }, "The edit of the fragmented sequence starts at the media time 1: the media that it selects a part of is not known when the edit list is read.");
        // Edit lists that do not play the media forward at its rate (the images last 2, 3 and 1 units of the time scale of
        // the media, which is the one of the movie)
        void RejectedEdits(string name, (ulong Duration, long MediaTime, short Rate)[] edits, string notes) =>
            Unsupported(context, name, SequenceSource, builder =>
            {
                Py.Assert(builder.Tracks.All(track => track.Timescale == builder.MovieTimescale && track.Samples.Select(sample => sample.Duration).SequenceEqual(new uint[] { 2, 3, 1 })), "durations");
                foreach (var track in builder.Tracks)
                {
                    track.EditList!.Entries.Clear();
                    foreach (var (duration, mediaTime, rate) in edits)
                        track.EditList.Entries.Add((duration, mediaTime, rate, 0));
                }
            }, "edit-list", notes);

        RejectedEdits("sequence-edit-empty", [(2, -1, 1), (6, 0, 1)], "The first edit is an empty one (its media time is -1): nothing is shown for 2 units before the images.");
        RejectedEdits("sequence-edit-rate", [(6, 0, 2)], "The edit plays the media at twice its rate.");
        RejectedEdits("sequence-edit-dwell", [(6, 0, 0)], "The media rate of the edit is 0: the image at its media time is held for the duration of the edit.");
        RejectedEdits("sequence-edits-out-of-order", [(3, 2, 1), (2, 0, 1)], "The second edit plays the start of the media, which the first edit comes after: the images are not shown in their order.");
        RejectedEdits("sequence-edits-overlap", [(4, 0, 1), (4, 3, 1)], "The second edit starts at the media time 3, before the end of the first one: a part of the second image is shown twice.");
        Unsupported(context, "sequence-alpha-edit-differs", "sequence-alpha-repeat-2", builder =>
        {
            var alpha = builder.Tracks.Single(track => track.HandlerType == "auxv");
            var edit = alpha.EditList!.Entries[0];
            alpha.EditList.Entries[0] = (edit.SegmentDuration, 10, edit.MediaRateInteger, edit.MediaRateFraction);
        }, "alpha-edit-list", "The edit of the alpha track starts at the media time 10 and the edit of the color track at 0: the alpha planes are not shown with their images.");
        Unsupported(context, "sequence-matrix-constant-luminance-unspecified-transfer", SequenceSource, builder =>
        {
            var boxes = builder.Tracks.Single(track => track.HandlerType == "pict").SampleEntryBoxes;
            var index = Enumerable.Range(0, boxes.Count).Single(i => PropertyType(boxes[i]) == "colr");
            boxes[index] = AvifFileBuilder.ColorNclx(9, 2, 10, fullRange: true);
        }, "matrix", "The colr box of the sample entry of the track declares the BT.2020 constant luminance matrix coefficients (10) and unspecified transfer characteristics: the luminance has no function to go through.");
        Unsupported(context, "grid-tile-transform-differs", "grid-2x1-transformed", builder =>
        {
            var tile = builder.GetItem(ReferenceAvif.Parse(context.Inputs["grid-2x1-transformed"]).GetGridTiles(ReferenceAvif.Parse(context.Inputs["grid-2x1-transformed"]).PrimaryItem!)[1].Id);
            ReplaceProperty(builder, tile, "irot", AvifFileBuilder.Rotation(2));
        }, "grid-tile-transform", "The second tile of the grid declares a half turn and the grid a quarter turn: the transformations of a tile are those of its grid, or none.");

        // A sequence whose images change size: the reference scaling vector of the AV1 corpus, one temporal unit per sample
        var resized = WrapVector(context, "av1/inter-svt-resize-fixed-420-8bit-128x96", out var description);
        AddAvifError(context, "invalid/avif/sequence-size-changes", "unsupported", resized, "UnsupportedImageFeatureException",
            "The samples are the temporal units of av1/inter-svt-resize-fixed-420-8bit-128x96, whose frames are " + description + ": the images of a sequence must have the size of the sample entry.",
            "avif.unsupported=size-change", tools: [context.Tools.FfmpegLabel + " (payloads: the committed AV1 vector)", ToolSet.RuntimeLabel]);
    }

    private static void BuildLimits(ContainerContext context)
    {
        void Limit(string name, string source, string limit, long value, string kind, string notes)
            => AddAvifError(context, "limit/avif/" + name, "limit", null, "ImageResourceLimitException", notes, limits: new Obj { [limit] = value }, limitKind: kind, reuse: source, tools: [ToolSet.RuntimeLabel]);

        // avif/lossless-rgb8 is 12x9
        Limit("width-over-limit", "lossless-rgb8", "MaxWidth", 11, "Width", "Reuses avif/lossless-rgb8 (12x9) with MaxWidth = 11.");
        Limit("height-over-limit", "lossless-rgb8", "MaxHeight", 8, "Height", "Reuses avif/lossless-rgb8 (12x9) with MaxHeight = 8.");
        Limit("frame-pixels-over-limit", "lossless-rgb8", "MaxFramePixels", 107, "FramePixels", "Reuses avif/lossless-rgb8 (12x9, 108 pixels) with MaxFramePixels = 107.");
        Limit("encoded-bytes-over-limit", "lossless-rgb8", "MaxEncodedBytes", context.Inputs["lossless-rgb8"].Length - 1, "EncodedBytes",
            "Reuses avif/lossless-rgb8 with MaxEncodedBytes one byte below the file length: the data of the item end with the file.");
        Limit("live-allocation-over-limit", "lossless-rgb8", "MaxLiveAllocationBytes", 65536, "LiveAllocationBytes",
            "Reuses avif/lossless-rgb8 with 64 KiB of live allocations: enough to read the file and to parse the container, not to decode an AV1 frame.");

        var metadata = ReferenceAvif.Parse(context.Inputs["metadata-exif-xmp-icc"]);
        var icc = metadata.PrimaryItem!.IccProfile!.Value.Length;
        Limit("icc-over-metadata-limit", "metadata-exif-xmp-icc", "MaxMetadataBytes", icc - 1, "MetadataBytes", Inv($"Reuses avif/metadata-exif-xmp-icc with MaxMetadataBytes one byte below its ICC profile ({icc} bytes)."));
        Limit("exif-over-metadata-limit", "metadata-exif-xmp-icc", "MaxMetadataBytes", icc + 1, "MetadataBytes", Inv($"Reuses avif/metadata-exif-xmp-icc with MaxMetadataBytes = {icc + 1}: enough for the ICC profile of the meta box, not for the Exif item that follows."));

        // avif/sequence-lossless-infinite has three 8x8 images
        Limit("sequence-frames-over-limit", "sequence-lossless-infinite", "MaxFrames", 2, "Frames", "Reuses avif/sequence-lossless-infinite (3 images) with MaxFrames = 2.");
        Limit("sequence-total-pixels-over-limit", "sequence-lossless-infinite", "MaxTotalPixels", 191, "TotalPixels", "Reuses avif/sequence-lossless-infinite (3 images of 8x8, 192 pixels) with MaxTotalPixels = 191: displayed frames are charged when they are produced, so only decoding reaches the limit.");

        var sequenceXmp = ReferenceAvif.Parse(context.Inputs["sequence-xmp"]);
        var sequencePacket = sequenceXmp.ReadItemData(sequenceXmp.FindSequenceMetadata().Xmp!).Length;
        Limit("sequence-xmp-over-metadata-limit", "sequence-xmp", "MaxMetadataBytes", sequencePacket - 1, "MetadataBytes", Inv($"Reuses avif/sequence-xmp with MaxMetadataBytes one byte below the XMP packet of the meta box of its track ({sequencePacket} bytes)."));

        // The lower layers of a layered image are decoded without being returned: they count toward MaxTotalPixels, each
        // with its own size
        const string LayeredSource = "layered-3-scaled-422-10bit";
        var layered = context.References[LayeredSource];
        var displayed = (long)layered.Frames[0].Width * layered.Frames[0].Height;
        Py.Assert(layered.LowerLayerPixels > 0 && layered.LowerLayerPixels % displayed != 0, "lower layers of other sizes");
        AddAvifError(context, "limit/avif/layered-lower-layers-total-pixels", "limit", null, "ImageResourceLimitException",
            Inv($"Reuses avif/{LayeredSource}: a 16x16 image ({displayed} pixels) whose two lower layers, 4x4 and 8x8, are decoded on the way ({layered.LowerLayerPixels} pixels). MaxTotalPixels = {displayed + layered.LowerLayerPixels - 1} is enough for the image and one pixel short of the {displayed + layered.LowerLayerPixels} pixels that are decoded."),
            limits: new Obj { ["MaxTotalPixels"] = displayed + layered.LowerLayerPixels - 1 }, limitKind: "TotalPixels", reuse: LayeredSource,
            parameters: new Obj { ["fixture"] = "avif/" + LayeredSource, ["displayedPixels"] = displayed, ["lowerLayerPixels"] = layered.LowerLayerPixels }, tools: [ToolSet.RuntimeLabel]);

        // Frames that are decoded without being shown count toward MaxTotalPixels
        const string Vector = "av1/inter-alt-ref-420-8bit-16x16";
        var hidden = WrapVector(context, Vector, out _);
        var trace = ParseTrace(Proc.RunText(TraceCommand(context.Tools.Av1, (CorpusDriver.CorpusDir / (Vector + ".obu")).Value), stderr: true));
        var shown = trace.Frames.Count(frame => frame.IsOutput);
        var notShown = trace.Frames.Count(frame => !frame.ShowsExistingFrame && frame.Header.Get("show_frame", absent: 1) == 0);
        Py.Assert(notShown > 0 && trace.Frames.All(frame => frame.Size == (16, 16)), "hidden frames");
        var pixels = (shown + notShown) * 256L;
        AddAvifError(context, "limit/avif/sequence-hidden-frames-total-pixels", "limit", hidden, "ImageResourceLimitException",
            Inv($"The samples are the temporal units of {Vector}: {shown} images of 16x16 and {notShown} frame(s) that are decoded without being shown. MaxTotalPixels = {pixels - 1} is enough for the displayed images ({shown * 256} pixels) and one pixel short of the {pixels} pixels that are decoded."),
            limits: new Obj { ["MaxTotalPixels"] = pixels - 1 }, limitKind: "TotalPixels",
            parameters: new Obj { ["vector"] = Vector, ["width"] = 16, ["height"] = 16, ["shownFrames"] = shown, ["hiddenFrames"] = notShown },
            tools: [context.Tools.FfmpegLabel + " (payloads: the committed AV1 vector)", ToolSet.RuntimeLabel]);
    }

    /// <summary>
    /// Wraps a committed AV1 vector in an image sequence: one sample per temporal unit (the OBUs from a temporal delimiter
    /// to the next one), a sample entry of the size of the first output frame and an av1C record from the sequence header
    /// as trace_headers reads it.
    /// </summary>
    private static byte[] WrapVector(ContainerContext context, string vector, out string frameSizes)
    {
        var path = CorpusDriver.CorpusDir / (vector + ".obu");
        var stream = File.ReadAllBytes(path);
        var trace = ParseTrace(Proc.RunText(TraceCommand(context.Tools.Av1, path.Value), stderr: true));
        var color = ReadColorConfig(trace.Sequence);
        var sizes = trace.Frames.Where(frame => frame.IsOutput).Select(frame => frame.Size).ToList();
        frameSizes = string.Join(", ", sizes.Select(size => Inv($"{size.Width}x{size.Height}")));

        // Low overhead bitstream format (section 5.2): each OBU has a header byte (an extension byte when bit 2 is set) and,
        // in these streams, a LEB128 size; a temporal delimiter (type 2) starts a temporal unit
        var units = new List<byte[]>();
        var start = 0;
        var offset = 0;
        while (offset < stream.Length)
        {
            var header = stream[offset];
            Py.Assert((header & 0x02) != 0, "the OBUs of a vector have a size field");
            var type = (header >> 3) & 15;
            if (type == 2 && offset > start)
            {
                units.Add(stream[start..offset]);
                start = offset;
            }

            var position = offset + 1 + ((header & 0x04) != 0 ? 1 : 0);
            var size = 0;
            for (var shift = 0; ; shift += 7)
            {
                var value = stream[position++];
                size |= (value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                    break;
            }

            offset = position + size;
        }

        units.Add(stream[start..]);
        Py.Assert(units.Count == sizes.Count, "one output frame per temporal unit");
        var builder = new AvifFileBuilder { MajorBrand = "avis", MovieTimescale = 10 };
        builder.CompatibleBrands.Clear();
        foreach (var brand in new[] { "avis", "msf1", "iso8" })
            builder.CompatibleBrands.Add(brand);
        builder.Layout.Clear();
        foreach (var box in new[] { AvifFileBuilder.FileTypeBox, AvifFileBuilder.MovieBox, AvifFileBuilder.MediaDataBox })
            builder.Layout.Add(box);

        var track = new AvifFileBuilder.Track(1, "pict", sizes[0].Width, sizes[0].Height) { Timescale = 10, Duration = (ulong)units.Count };
        track.SampleEntryBoxes.Add(AvifFileBuilder.Av1Configuration((int)trace.Sequence.Get("seq_profile"), (int)trace.Sequence.Get("seq_level_idx[0]"), color.BitDepth, color.Monochrome, color.SubsamplingX, color.SubsamplingY, (int)trace.Sequence.Get("chroma_sample_position")));
        foreach (var unit in units)
            track.Samples.Add((unit, 1));

        builder.Tracks.Add(track);
        return builder.Build();
    }
}
