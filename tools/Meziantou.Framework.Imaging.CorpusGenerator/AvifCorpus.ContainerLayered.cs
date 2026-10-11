using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Avif;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The valid AVIF fixtures with layered (progressive) images: an AV1 bitstream with a frame per spatial layer, each one
// refining the previous ones. The image is the frame of the highest layer of the operating point; the a1op property
// selects another operating point and the lsel property a layer, whose frame is then the image.
//
// - Written by avifenc: --progressive (two layers, the first one at half the size) and --layered (one input per layer,
//   2 to 4 layers, with a quality and a scale per layer). avifenc writes one extent per layer and an a1lx property; it
//   writes neither a1op nor lsel, and neither a layered grid nor a layered sequence.
// - Assembled around the payloads of such files (AvifFileBuilder): the lsel and a1op properties, a grid whose tiles are
//   layered images, and an image sequence whose samples are layered temporal units.
//
// References: as for every AVIF fixture, the planes both decoders agree on, converted by ReferenceYuvConverter. Both
// decoders output the frame of the highest spatial layer of a temporal unit. For a file that selects an operating point
// or a layer, the units a decoder of that operating point drops (AV1 specification, section 5.3.1) and the units of the
// layers above the selected one are removed from the payload first (DecodeCodedItem, SelectObus): what is left is an
// ordinary stream whose last layer is the selected one, which dav1d and libaom decode by themselves. FFmpeg gives no
// access to the layer selection of libaom, so asking each decoder for the layer was not an option; what dav1d outputs
// for each layer when it is asked for every layer ("-alllayers 1") is what both decoders output after the removal, which
// the generator of the AV1 vectors checks on every truncated layered vector (AvifCorpus.Layered.cs).
// Cross-checks: avifdec reads a1op and lsel itself, and "avifdec --progressive --index N" decodes layer N of the file the
// payloads come from (recorded for the layers that have the size of the image: avifdec scales a smaller layer up).
//
// Sizes: 8x8 wherever the feature allows it (a layered image costs every generic suite one decoding per layer).
internal static partial class AvifCorpus
{
    /// <summary>A layered image written by avifenc: the tokens <c>@source0.png</c>, <c>@source1.png</c>... of the arguments
    /// name the patterns.</summary>
    /// <param name="expected">The pattern the decoded image must equal (a lossless last layer), or <see langword="null"/>.</param>
    private static AvifReference Layered(ContainerContext context, string name, string arguments, Img[] sources, string[] requires, string? notes = null, Img? expected = null)
    {
        var files = sources.Select((source, index) => (Name: Inv($"source{index}.png"), Data: Png(source))).ToDictionary(file => file.Name, file => file.Data, StringComparer.Ordinal);
        var (data, command) = Avifenc(context, name, files, arguments);
        var reference = AddValidAvif(context, name, data, "generated", "BuildLayered", EncoderTools(context), [command], null, notes, ["avif.a1lx", .. requires]);
        if (expected is not null)
            AssertDecodesTo(name, reference, expected);

        return reference;
    }

    /// <summary>A property added to an item, marked essential.</summary>
    private static void AddEssential(AvifFileBuilder builder, AvifFileBuilder.Item item, byte[] box) => item.Associations.Add((builder.AddProperty(box), true));

    /// <summary>A grid assembled from files that each hold one coded image: the first file becomes the grid, its image
    /// the first tile, and the images of the other files the next tiles, each with its own properties.</summary>
    private static AvifFileBuilder AssembleGrid(List<AvifFileBuilder> tiles, int rows, int columns, uint width, uint height)
    {
        var grid = tiles[0];
        var first = grid.GetItem(1);
        first.Id = 2;
        first.IsHidden = true;
        var gridItem = grid.AddItem(1, "grid", AvifFileBuilder.GridDescriptor(rows, columns, width, height), grid.AddProperty(AvifFileBuilder.ImageSize(width, height)));
        foreach (var (index, _) in first.Associations.Where(association => PropertyType(grid.Properties[association.PropertyIndex - 1]) is "colr" or "pixi"))
            gridItem.Associations.Add((index, false));

        var ids = new List<uint> { 2 };
        for (var i = 1; i < tiles.Count; i++)
        {
            var source = tiles[i];
            var item = source.GetItem(1);
            var copy = grid.AddItem((uint)(2 + i), "av01", item.Data);
            copy.IsHidden = true;
            foreach (var (index, isEssential) in item.Associations)
            {
                // A property the grid already has is shared; the a1lx property of a layered tile is its own
                var box = source.Properties[index - 1];
                var existing = Enumerable.Range(0, grid.Properties.Count).Where(candidate => grid.Properties[candidate].AsSpan().SequenceEqual(box)).Select(candidate => candidate + 1).FirstOrDefault();
                copy.Associations.Add((existing != 0 ? existing : grid.AddProperty(box), isEssential));
            }

            ids.Add((uint)(2 + i));
        }

        grid.References.Add(("dimg", 1, [.. ids]));
        return grid;
    }

    private static void BuildLayered(ContainerContext context)
    {
        const string Function = "BuildLayered";
        const string Identity = "-y 444 --cicp 1/13/0";
        var smooth = CommonCorpus.PatternSmoothColor(8, 8);
        var detail = CommonCorpus.PatternDetailColor(8, 8, 2);
        var other = CommonCorpus.PatternDetailColor(8, 8, 7);

        // --progressive: two layers of one input, the first one at half the size. 4:2:0 at 8 bits (an odd half size),
        // identity-matrix 4:4:4 at 10 bits (compared exactly), and with an alpha plane, whose item is layered too
        Layered(context, "layered-progressive-420", "--progressive -y 420 --cicp 1/13/6 @source0.png", [CommonCorpus.PatternSmoothColor(12, 10)],
            ["avif.layers=2", "avif.layers.scaled", "avif.subsampling=4:2:0", "avif.bitDepth=8", "avif.layers.hiddenPixels=30"],
            "Written by avifenc --progressive: a 6x5 layer, then the 12x10 image, which predicts from it with reference scaling. The image is the last layer; the first one is decoded on the way (30 pixels).");
        Layered(context, "layered-progressive-444-10bit", "--progressive -d 10 " + Identity + " @source0.png", [DeepColor(8, 8, 10)],
            ["avif.layers=2", "avif.layers.scaled", "avif.subsampling=4:4:4", "avif.bitDepth=10", "avif.matrix=0", "avif.layers.hiddenPixels=16"],
            "Written by avifenc --progressive at 10 bits with the identity matrix: a 4x4 layer, then the 8x8 image.");
        Layered(context, "layered-progressive-alpha", "--progressive -y 420 --cicp 1/13/6 @source0.png", [WithAlpha(smooth)],
            ["avif.layers=2", "avif.layers.scaled", "avif.alpha", "avif.layers.hiddenPixels=32"],
            "Written by avifenc --progressive from an image with alpha: the color item and the alpha item both hold a 4x4 layer and the 8x8 image.");

        // --layered: one input per layer. Quality layers (every layer has the size of the image): 2, 3 and 4 of them
        Layered(context, "layered-2-quality-420", Qualities("-y 420 --cicp 1/13/1 -r limited", 20, 80), [smooth],
            ["avif.layers=2", "avif.subsampling=4:2:0", "avif.range=limited", "avif.layers.hiddenPixels=64"],
            "Written by avifenc --layered: the same input at quality 20, then at quality 80. Both layers are 8x8.");
        Layered(context, "layered-3-quality-rgb", Qualities(Identity, 20, 60, 95), [detail],
            ["avif.layers=3", "avif.matrix=0", "avif.layers.hiddenPixels=128"],
            "Written by avifenc --layered: three layers of 8x8 at qualities 20, 60 and 95, identity matrix. The source of the fixtures that select an operating point or a layer.");
        Layered(context, "layered-4-quality-gray12", Qualities("-d 12", 10, 40, 70, 95), [DeepGray(8, 8, 12)],
            ["avif.layers=4", "avif.subsampling=4:0:0", "avif.bitDepth=12", "avif.layers.hiddenPixels=192"],
            "Written by avifenc --layered: four layers (the most AV1 has) of a 12-bit monochrome image.");

        // Layers of growing size, and a second layer that is another picture than the first one
        Layered(context, "layered-3-scaled-422-10bit", Scaled("-y 422 -d 10 --cicp 1/13/1 -r limited", ("1/4", 30), ("1/2", 55), ("1/1", 80)), [DeepColor(16, 16, 10)],
            ["avif.layers=3", "avif.layers.scaled", "avif.subsampling=4:2:2", "avif.bitDepth=10", "avif.layers.hiddenPixels=80"],
            "Written by avifenc --layered with a scale per layer: 4x4, 8x8, then the 16x16 image. The two lower layers are 16 + 64 pixels.");
        Layered(context, "layered-2-contents-rgb", "--layered " + Identity + " -q 40 @source0.png -q:u 100 @source1.png", [detail, other],
            ["avif.layers=2", "avif.matrix=0", "avif.layers.hiddenPixels=64"],
            "Written by avifenc --layered from two pictures: the first layer is one picture at quality 40 and the second layer another picture, coded losslessly. The image is the second picture, sample for sample.", expected: other);

        // The lsel property (ISO/IEC 23008-12, section 6.5.11): 0xFFFF selects no layer, a layer_id selects the frame of
        // that spatial layer, which is then the image. The a1op property (AV1 Image File Format, section 2.3.2.1) selects
        // an operating point; avifenc declares one per number of layers (the first one holds every layer, the last one
        // the first layer only), so operating point 1 of three is the first two layers
        const string Source = "layered-3-quality-rgb";
        Assembled(context, "layered-lsel-none", Source, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(0xFFFF)),
            ["avif.lsel=65535", "avif.layers=3", "avif.layers.hiddenPixels=128"], "An lsel property whose layer_id is 0xFFFF: no layer is selected, the image is the last layer, as without the property.", Function, sourceLayer: 2);
        Assembled(context, "layered-lsel-2", Source, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(2)),
            ["avif.lsel=2", "avif.layers=3", "avif.layers.hiddenPixels=128"], "An lsel property selects layer 2, the last of three: the same image as without the property.", Function, sourceLayer: 2);
        Assembled(context, "layered-lsel-0", Source, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(0)),
            ["avif.lsel=0", "avif.layers=3"], "An lsel property selects layer 0 of three layers of 8x8: the image is the frame of the first layer (quality 20), and the other layers are not decoded.", Function, sourceLayer: 0);
        Assembled(context, "layered-lsel-1", Source, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(1)),
            ["avif.lsel=1", "avif.layers=3", "avif.layers.hiddenPixels=64"], "An lsel property selects layer 1 of three layers of 8x8: the image is the frame of the second layer (quality 60); the first layer is decoded on the way.", Function, sourceLayer: 1);
        Assembled(context, "layered-a1op-0", Source, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(0)),
            ["avif.a1op=0", "avif.layers=3", "avif.layers.hiddenPixels=128"], "An a1op property selects operating point 0, the default one: the same image as without the property.", Function, sourceLayer: 2);
        Assembled(context, "layered-a1op-1", Source, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(1)),
            ["avif.a1op=1", "avif.layers=3", "avif.layers.hiddenPixels=64"], "An a1op property selects operating point 1 of three (operating_point_idc 0x301: the first two layers): the image is the frame of layer 1, its highest layer.", Function, sourceLayer: 1);
        Assembled(context, "layered-a1op-2", Source, builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(2)),
            ["avif.a1op=2", "avif.layers=3"], "An a1op property selects operating point 2 of three (operating_point_idc 0x101: the first layer only): the image is the frame of layer 0.", Function, sourceLayer: 0);
        Assembled(context, "layered-a1op-1-lsel-0", Source, builder =>
        {
            AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(1));
            AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(0));
        }, ["avif.a1op=1", "avif.lsel=0", "avif.layers=3"], "An a1op property selects operating point 1 (the first two layers) and an lsel property layer 0 of it: the image is the frame of layer 0.", Function, sourceLayer: 0);

        Assembled(context, "layered-lsel-3", "layered-4-quality-gray12", builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(3)),
            ["avif.lsel=3", "avif.layers=4", "avif.layers.hiddenPixels=192"], "An lsel property selects layer 3 of avif/layered-4-quality-gray12, the last of the four spatial layers AV1 has.", Function, sourceLayer: 3);

        // A layer that is smaller than the last one: the ispe property of the item is the size of the selected frame
        Assembled(context, "layered-lsel-0-smaller", "layered-progressive-444-10bit", builder =>
        {
            AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(0));
            ReplaceProperty(builder, builder.GetItem(1), "ispe", AvifFileBuilder.ImageSize(4, 4));
        }, ["avif.lsel=0", "avif.layers=2", "avif.layers.scaled", "avif.bitDepth=10"], "An lsel property selects layer 0 of avif/layered-progressive-444-10bit, a 4x4 frame, and the ispe property declares 4x4: the image is that frame, at its own size.", Function);
        Assembled(context, "layered-a1op-1-smaller", "layered-progressive-444-10bit", builder =>
        {
            AddEssential(builder, builder.GetItem(1), AvifFileBuilder.OperatingPointSelector(1));
            ReplaceProperty(builder, builder.GetItem(1), "ispe", AvifFileBuilder.ImageSize(4, 4));
        }, ["avif.a1op=1", "avif.layers=2", "avif.layers.scaled"], "An a1op property selects operating point 1 of avif/layered-progressive-444-10bit, which holds its first layer only, a 4x4 frame, and the ispe property declares 4x4.", Function);

        // Matrixed content: the selected layer is converted like any image
        Assembled(context, "layered-lsel-0-420", "layered-2-quality-420", builder => AddEssential(builder, builder.GetItem(1), AvifFileBuilder.LayerSelector(0)),
            ["avif.lsel=0", "avif.layers=2", "avif.subsampling=4:2:0"], "An lsel property selects layer 0 of avif/layered-2-quality-420 (quality 20): 4:2:0 samples in limited range, converted like any image.", Function, sourceLayer: 0);

        // A grid whose tiles are layered images. avifenc refuses to write one ("Layered grid image unimplemented in
        // avifenc"): two 8x8 images are coded one by one and assembled. The tiles are smaller than the 64 samples MIAF asks
        // for, so avifdec rejects the file (as it rejects avif/grid-3x2-small-tiles)
        var mosaic = CommonCorpus.PatternDetailColor(16, 8, 4);
        List<AvifFileBuilder> Tiles(string prefix, string arguments, List<string> commands)
        {
            var tiles = new List<AvifFileBuilder>();
            for (var i = 0; i < 2; i++)
            {
                var tile = new Img(8, 8, "rgb", 8, CommonCorpus.Grid(8, 8, (x, y) => mosaic.Pixels[(y * 16) + (8 * i) + x]));
                var (data, command) = Avifenc(context, Inv($"{prefix}-tile{i}"), new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["source0.png"] = Png(tile) }, arguments);
                tiles.Add(AvifFileBuilder.From(data));
                commands.Add(command);
            }

            return tiles;
        }

        var gridCommands = new List<string>();
        var grid = AssembleGrid(Tiles("layered-grid-2x1", Qualities(Identity, 30, 100), gridCommands), 1, 2, 16, 8);
        var gridReference = AddValidAvif(context, "layered-grid-2x1", grid.Build(), "hand-authored", Function, AssemblyTools(context), gridCommands, new Obj { ["tileWidth"] = 8, ["tileHeight"] = 8, ["columns"] = 2, ["rows"] = 1 },
            "A grid of two layered tiles of 8x8, coded one by one by avifenc --layered (quality 30, then lossless) and assembled: each tile gives the frame of its last layer, and the first layer of each tile is decoded on the way.",
            ["avif.grid=2x1", "avif.layers=2", "avif.a1lx", "avif.layers.hiddenPixels=128"]);
        AssertDecodesTo("layered-grid-2x1", gridReference, mosaic);

        // Each tile of a grid has its own selection, and the grid item none: two tiles of three layers that select
        // different layers, then different operating points. The two tiles were coded with the same settings: their
        // sequence headers are the same bytes, whatever each tile selects in them
        void SelectingTiles(string name, Action<AvifFileBuilder, AvifFileBuilder.Item> first, Action<AvifFileBuilder, AvifFileBuilder.Item> second, string notes, string[] requires)
        {
            var commands = new List<string>();
            var tiles = Tiles(name, Qualities(Identity, 20, 60, 95), commands);
            var headers = tiles.Select(tile => ReadObuUnits(tile.GetItem(1).Data).Where(unit => unit.Type == ObuSequenceHeader).Select(unit => Convert.ToHexString(tile.GetItem(1).Data.AsSpan(unit.Offset, unit.Length))).Single()).ToList();
            Py.Assert(headers[0] == headers[1], name + ": the tiles have the same sequence header");
            first(tiles[0], tiles[0].GetItem(1));
            second(tiles[1], tiles[1].GetItem(1));
            AddValidAvif(context, name, AssembleGrid(tiles, 1, 2, 16, 8).Build(), "hand-authored", Function, AssemblyTools(context), commands, new Obj { ["tileWidth"] = 8, ["tileHeight"] = 8, ["columns"] = 2, ["rows"] = 1 },
                notes, ["avif.grid=2x1", "avif.layers=3", .. requires]);
        }

        SelectingTiles("layered-grid-2x1-tile-layers",
            (builder, tile) => AddEssential(builder, tile, AvifFileBuilder.LayerSelector(1)),
            (builder, tile) => AddEssential(builder, tile, AvifFileBuilder.LayerSelector(0)),
            "A grid of two tiles of three layers each (quality 20, 60 and 95). The first tile has an lsel property that selects layer 1 and the second tile one that selects layer 0: each tile of a grid has its own selection, and the grid item has none.",
            ["avif.lsel=0", "avif.lsel=1", "avif.layers.hiddenPixels=64"]);
        SelectingTiles("layered-grid-2x1-tile-operating-points",
            (builder, tile) => AddEssential(builder, tile, AvifFileBuilder.OperatingPointSelector(1)),
            (builder, tile) => AddEssential(builder, tile, AvifFileBuilder.OperatingPointSelector(2)),
            "A grid of two tiles of three layers each, whose sequence headers are the same bytes. The first tile has an a1op property that selects operating point 1 (the first two layers) and the second tile one that selects operating point 2 (the first layer only): each tile of a grid has its own operating point.",
            ["avif.a1op=1", "avif.a1op=2", "avif.layers.hiddenPixels=64"]);

        // An image sequence whose samples are layered temporal units: avifenc codes one layer per frame of a sequence, so
        // the track is assembled around the payloads of two layered images. Every sample gives the frame of its highest
        // layer; the lower layers (8x8, then 4x4) are decoded on the way
        // (Content of few colors: the references are also the sources of the encoder suites; see BuildStills)
        var (firstSample, firstCommand) = Avifenc(context, "sequence-layered-sample0", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["source0.png"] = Png(Moving(8, 8, 0)) }, Qualities(Identity, 30, 90));
        var (secondSample, secondCommand) = Avifenc(context, "sequence-layered-sample1", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["source0.png"] = Png(Moving(8, 8, 2)) }, "--progressive " + Identity + " @source0.png");
        var samples = new[] { ReferenceAvif.Parse(firstSample), ReferenceAvif.Parse(secondSample) };
        var configuration = samples[0].PrimaryItem!.Find("av1C")!.Payload.ToArray();
        Py.Assert(samples[1].PrimaryItem!.Find("av1C")!.Payload.Span.SequenceEqual(configuration), "the samples have the same configuration record");
        var sequence = new AvifFileBuilder { MajorBrand = "avis", MovieTimescale = 10 };
        sequence.CompatibleBrands.Clear();
        foreach (var brand in new[] { "avis", "msf1", "iso8" })
            sequence.CompatibleBrands.Add(brand);
        sequence.Layout.Clear();
        foreach (var box in new[] { AvifFileBuilder.FileTypeBox, AvifFileBuilder.MovieBox, AvifFileBuilder.MediaDataBox })
            sequence.Layout.Add(box);

        var track = new AvifFileBuilder.Track(1, "pict", 8, 8) { Timescale = 10, Duration = 5 };
        track.SampleEntryBoxes.Add(AvifFileBuilder.Box("av1C", configuration));
        track.SampleEntryBoxes.Add(AvifFileBuilder.ColorNclx(1, 13, 0, fullRange: true));
        track.Samples.Add((samples[0].ReadItemData(samples[0].PrimaryItem!), 2));
        track.Samples.Add((samples[1].ReadItemData(samples[1].PrimaryItem!), 3));
        sequence.Tracks.Add(track);
        AddValidAvif(context, "sequence-layered", sequence.Build(), "hand-authored", Function, AssemblyTools(context), [firstCommand, secondCommand], null,
            "An image sequence of two samples that are layered temporal units, assembled around the payloads of two layered images: two quality layers of 8x8, then a 4x4 layer and an 8x8 one. Every sample gives the frame of its highest layer.",
            ["avif.sequence", "avif.layers=2", "avif.layers.scaled", "avif.layers.hiddenPixels=80", "avif.seq.editList=none"]);
    }
}
