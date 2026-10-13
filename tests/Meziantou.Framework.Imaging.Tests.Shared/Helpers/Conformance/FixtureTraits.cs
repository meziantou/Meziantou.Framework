using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Properties of a fixture input that change what the cross-codec input contracts can assert. They come from formats that
/// have no end-of-image marker or that locate their data by offsets, and they are documented behavior, not decoder defects.
/// </summary>
internal static class FixtureTraits
{
    /// <summary>
    /// Gets a value indicating whether the last bytes of the input are only delimited by the end of the input: the bytes
    /// after the image data of a TGA file (a TGA 2.0 developer area, extension area and footer, which are located from the
    /// last 26 bytes), and the last sample of a plain (ASCII) Netpbm raster with the white space that may follow it. Every
    /// byte of the input is still read and needed. What differs is how a limit that cuts these bytes is detected on a
    /// stream: by the single discarded byte that tells the end of the input from a longer input, the only byte ever read
    /// past <see cref="ImageResourceLimits.MaxEncodedBytes"/>.
    /// </summary>
    public static bool IsDelimitedByEndOfInput(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.HasFeature("tga.trailingData") || fixture.Entry.HasFeature("pnm.raster=plain");
    }

    /// <summary>
    /// Gets a value indicating whether the input has bytes after the image data that the decoder never reads: the boxes of
    /// an AVIF or HEIC file that follow the last byte of the items of the image (the walk ends there). The decoder then consumes
    /// fewer bytes than the file holds, so the encoded-byte requirement is smaller than the file length and no I/O failure
    /// in the trailing bytes can be observed.
    /// </summary>
    public static bool HasUnreadTrailingBytes(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.HasFeature("avif.trailingData") || fixture.Entry.HasFeature("heic.trailingData");
    }

    /// <summary>
    /// Gets a value indicating whether a proper prefix of the input can itself be a complete, valid image of the same
    /// format, so that truncation is not detectable: a TGA, AVIF or HEIC file whose trailing bytes are cut is a valid file, and a
    /// plain (ASCII) Netpbm raster cut inside its last sample is a complete raster with a smaller last sample.
    /// </summary>
    /// <summary>
    /// Gets a value indicating whether boxes follow the codestream of a JPEG XL container: the file that ends before any of
    /// them is a complete file too (<c>JpegXlDecoderConformanceTests</c> checks every other prefix).
    /// </summary>
    public static bool HasBoxesAfterTheCodestream(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.HasFeature("jxl.boxesAfterCodestream");
    }

    public static bool ProperPrefixMayBeValid(GoldenFixture fixture) => IsDelimitedByEndOfInput(fixture) || HasUnreadTrailingBytes(fixture) || HasBoxesAfterTheCodestream(fixture) || IsFragmentedSequence(fixture);

    /// <summary>
    /// Gets a value indicating whether the input is a fragmented AVIF image sequence: its images are described by movie
    /// fragments that follow each other until the end of the file, which nothing announces. The file that ends after one
    /// of its fragments is a complete sequence of fewer images, and a header identification, which stops at the
    /// <c>moov</c> box, does not know how many images follow.
    /// </summary>
    public static bool IsFragmentedSequence(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.Features?.Any(feature => feature.StartsWith("avif.seq.fragments=", StringComparison.Ordinal)) ?? false;
    }

    /// <summary>
    /// Gets a value indicating whether a header identification needs the whole input: an AVIF or HEIC file whose last box is
    /// the one that describes the image (the <c>meta</c> box, or the <c>moov</c> box of an image sequence), because its media
    /// data come first or are stored inside that box. The structure is read from the bytes by the independent reader.
    /// </summary>
    public static bool HeaderIdentificationReadsWholeInput(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (!IsHeif(fixture))
            return false;

        var file = ReferenceHeif.Parse(fixture.ReadInput());
        return file.TopLevelBoxes[^1].Type == (file.IsSequence ? "moov" : "meta");
    }

    /// <summary>
    /// Gets a value indicating whether the metadata of the input follow its images: an AVIF or HEIC image sequence whose Exif
    /// and XMP item data follow the samples. A sequential reader gives them with the images that follow their data, at the latest
    /// with the last one (a documented rule), so they are compared on the last image it returns instead of the first one.
    /// </summary>
    public static bool MetadataFollowsTheImages(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return fixture.Entry.HasFeature("avif.seq.metadataAfterSamples") || fixture.Entry.HasFeature("heic.seq.metadataAfterSamples") || fixture.Entry.HasFeature("jxl.metadataAfterCodestream");
    }

    /// <summary>
    /// Gets the number of pixels that are decoded without being returned, which count toward <c>MaxTotalPixels</c> (a
    /// documented rule), so that the requirement of a decode is larger than the displayed frames:
    /// <list type="bullet">
    /// <item><description>
    /// the hidden frames of the AV1 streams of an AVIF image sequence (alternate reference frames, which a later sample
    /// shows again), as the corpus generator counted them in the frame headers of the color and alpha tracks
    /// (<c>avif.seq.hiddenFrames</c>); the generator checks that they have the size of the images;
    /// </description></item>
    /// <item><description>
    /// the lower spatial layers of a layered image or sample, each with its own size, which can be smaller than the image:
    /// the generator sums the pixels of the shown frames that the selected operating point and layer leave below the frame
    /// that is returned, for the color and the alpha bitstreams (<c>avif.layers.hiddenPixels</c>).
    /// </description></item>
    /// </list>
    /// </summary>
    public static long GetHiddenPixelCount(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var hiddenFrames = GetFeatureValue(fixture, "avif.seq.hiddenFrames=");
        // JPEG XL: the frames that are decoded without being shown (reference frames, LF frames, layers) and what a shown
        // frame has beyond the image, as the corpus generator summed them from the frame headers (jxl.hiddenPixels)
        // The coded images of an image that is built from other images are decoded without being the image that is
        // returned: the generator sums their pixels (avif.derived.inputPixels)
        return GetFeatureValue(fixture, "avif.derived.inputPixels=") + (hiddenFrames * fixture.Expected.Width * fixture.Expected.Height) + GetFeatureValue(fixture, "avif.layers.hiddenPixels=") + GetFeatureValue(fixture, "jxl.hiddenPixels=");

        static long GetFeatureValue(GoldenFixture fixture, string prefix)
        {
            var feature = fixture.Entry.Features?.SingleOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
            return feature is null ? 0 : long.Parse(feature[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Gets a value indicating whether a header identification cannot tell the color model of the input: an AVIF or HEIC
    /// file without <c>colr</c> box of type <c>nclx</c> whose bitstream, which holds the color description, precedes the box
    /// that describes the image (the <c>meta</c> box, or the <c>moov</c> box of an image sequence). The bitstream has gone
    /// by when the walk learns that it needs it: the color model is <c>Unknown</c> and there is no color description, never
    /// a guess (a documented rule). A monochrome image is known from its configuration record. The color description of an
    /// HEVC bitstream is in its sequence parameter set, which the configuration record of an HEIC file holds unless its
    /// parameter sets are in the coded data only: the bitstream is then never needed. The structure is read from the bytes
    /// by the independent reader.
    /// </summary>
    public static bool HeaderCannotTellTheColorModel(GoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (!IsHeif(fixture) || fixture.Expected.ColorModel is "Grayscale" or "GrayscaleAlpha")
            return false;

        var file = ReferenceHeif.Parse(fixture.ReadInput());
        long index;
        long payload;
        if (file.IsSequence)
        {
            var track = file.ColorTrack!;
            if (track.Nclx is not null || HasSequenceParameterSet(track.HevcConfiguration))
                return false;

            index = file.TopLevelBoxes.First(box => box.Type == "moov").Offset;
            payload = track.Samples[0].Offset;
        }
        else
        {
            // An image that is built from other images has red, green, blue and alpha samples, whatever they are
            if (file.IsBuiltImage(file.PrimaryItem!))
                return false;

            // The color description of an identity derivation is its own, or else the one of its input
            var chain = file.GetIdentityChain(file.PrimaryItem!);
            if (chain.Any(item => item.Nclx is not null))
                return false;

            var coded = chain[^1].Type == "grid" ? file.GetGridTiles(chain[^1])[0] : chain[^1];
            if (coded.ConstructionMethod != 0 || HasSequenceParameterSet(coded.HevcConfiguration))
                return false;

            index = file.TopLevelBoxes.First(box => box.Type == "meta").Offset;
            payload = coded.Extents[0].Offset;
        }

        return payload < index;

        // NAL unit type 33 (Rec. ITU-T H.265, table 7-1)
        static bool HasSequenceParameterSet(ReferenceHeif.HevcConfiguration? configuration) => configuration is not null && configuration.Arrays.Any(array => array.NalUnitType == 33 && array.NalUnits.Count > 0);
    }

    /// <summary>Gets a value indicating whether the input is a file of the HEIF family, whose structure <see cref="ReferenceHeif"/> reads: AVIF or HEIC.</summary>
    private static bool IsHeif(GoldenFixture fixture) => fixture.Entry.Format is "avif" or "heic";

    /// <summary>
    /// Gets the size that the canvas limits apply to. It is the size of the image, except in two cases:
    /// <list type="bullet">
    /// <item><description>
    /// an AVIF or HEIC image with a clean aperture: every frame of the bitstream is checked against the canvas limits (a
    /// documented rule), and the coded image, whose size is the <c>ispe</c> property of the primary item or the size of the
    /// sample entry of the track, is larger than the aperture that is presented;
    /// </description></item>
    /// <item><description>
    /// an HEVC picture whose conformance window removes more than the padding of the coded picture to a whole number of
    /// minimum coding blocks: the samples outside the window are decoded all the same, so the coded picture counts when
    /// the pixels are decoded. The corpus generator read its size in the sequence parameter set
    /// (<c>heic.checkedSize</c>); an identification, which does not decode it, needs the declared size only. (A window
    /// that only removes the padding does not count: the limit of an image of 1080 rows accepts its pictures of 1088.)
    /// </description></item>
    /// </list>
    /// An image that is built from other images has several canvases: the ones of its coded images, of its grids and of
    /// its overlays. Each limit applies to each of them, so the requirement of each limit is its largest value, and the
    /// pixels are not the product of the two sides. The coded image of an identity derivation is the one it is derived
    /// from.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="decodes">Whether the pixels are decoded (a load or a sequential read), not only identified.</param>
    public static (int Width, int Height, long Pixels) GetLimitedCanvas(GoldenFixture fixture, bool decodes = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (fixture.Entry.Format == "avif" && ReferenceHeif.Parse(fixture.ReadInput()) is { PrimaryItem: { } root } built && built.IsBuiltImage(root))
        {
            var canvases = new List<(long Width, long Height)>();
            Collect(root);
            return ((int)canvases.Max(canvas => canvas.Width), (int)canvases.Max(canvas => canvas.Height), canvases.Max(canvas => canvas.Width * canvas.Height));

            void Collect(ReferenceHeif.Item item)
            {
                // An identity derivation has no canvas of its own
                if (item.Type != "iden")
                {
                    canvases.Add(item.Size!.Value);
                }

                // The tiles of a grid of coded images are smaller than the grid
                if (item.Type != "av01" && (item.Type != "grid" || built.IsBuiltImage(item)))
                {
                    foreach (var id in built.ItemReferences.Where(reference => reference.Type == "dimg" && reference.FromItemId == item.Id).SelectMany(reference => reference.ToItemIds))
                    {
                        Collect(built.FindItem(id)!);
                    }
                }
            }
        }

        (int Width, int Height) size = (fixture.Expected.Width, fixture.Expected.Height);
        if (fixture.Entry.HasFeature("avif.clap") || fixture.Entry.HasFeature("heic.clap") || fixture.Entry.HasFeature("avif.seq.clap"))
        {
            var file = ReferenceHeif.Parse(fixture.ReadInput());
            if (file.IsSequence && file.ColorTrack is { } track)
                size = (track.Width, track.Height);
            else if (file.PrimaryItem is { } primary && file.GetIdentityChain(primary)[^1].Size is var (width, height))
                size = ((int)width, (int)height);
        }

        const string CheckedSize = "heic.checkedSize=";
        if (decodes && fixture.Entry.Features?.SingleOrDefault(candidate => candidate.StartsWith(CheckedSize, StringComparison.Ordinal)) is { } feature)
        {
            var parts = feature[CheckedSize.Length..].Split('x');
            size = (Math.Max(size.Width, int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture)), Math.Max(size.Height, int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture)));
        }

        return (size.Width, size.Height, (long)size.Width * size.Height);
    }
}
