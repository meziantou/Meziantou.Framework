using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Avif;

// A builder of byte blobs: its properties hold the arrays that are written to the file
#pragma warning disable CA1819 // Properties should not return arrays

/// <summary>
/// Assembles AVIF files box by box from AV1 payloads and a description of the items, the properties and the tracks, for the
/// tests that need a container the encoders do not write and for the hand-assembled fixtures of the corpus. It is written
/// from the specifications (ISO/IEC 14496-12, ISO/IEC 23008-12, AV1 Image File Format) and never uses the library under
/// test. It writes what it is told to: nothing is validated, so that malformed files can be produced on purpose.
/// </summary>
/// <remarks>
/// A file is described by its top-level <see cref="Layout"/>, the content of its <c>meta</c> box (<see cref="Items"/>,
/// <see cref="Properties"/>, <see cref="References"/>) and of its <c>moov</c> box (<see cref="Tracks"/>). The offsets of the
/// item data and of the samples are computed when the file is built. <see cref="From"/> imports an existing file, typically
/// one written by an encoder, so that it can be laid out differently.
/// </remarks>
public sealed class AvifFileBuilder
{
    /// <summary>The layout entry of the file type box.</summary>
    public const string FileTypeBox = "ftyp";

    /// <summary>The layout entry of the <c>meta</c> box.</summary>
    public const string MetaBox = "meta";

    /// <summary>The layout entry of the <c>moov</c> box.</summary>
    public const string MovieBox = "moov";

    /// <summary>The layout entry of the <c>mdat</c> box, which holds the item data and the samples.</summary>
    public const string MediaDataBox = "mdat";

    /// <summary>Gets or sets the major brand.</summary>
    public string MajorBrand { get; set; } = "avif";

    /// <summary>Gets the compatible brands.</summary>
    public IList<string> CompatibleBrands { get; } = ["avif", "mif1", "miaf"];

    /// <summary>
    /// Gets the top-level boxes in file order: <see cref="FileTypeBox"/>, <see cref="MetaBox"/>, <see cref="MovieBox"/>,
    /// <see cref="MediaDataBox"/>, or a key of <see cref="ExtraBoxes"/>. A box may be listed twice or left out.
    /// </summary>
    public IList<string> Layout { get; } = [FileTypeBox, MetaBox, MediaDataBox];

    /// <summary>Gets complete boxes that <see cref="Layout"/> can refer to by their key.</summary>
    public IDictionary<string, byte[]> ExtraBoxes { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>Gets the layout entries whose box is written with a 64-bit size (size field 1).</summary>
    public ISet<string> LargeSizeBoxes { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets or sets a value indicating whether the <c>mdat</c> box is written with a size field of 0: it extends to the end of the file.</summary>
    public bool MediaDataExtendsToEnd { get; set; }

    /// <summary>Gets the boxes that are not written although the description has them: <c>hdlr</c>, <c>pitm</c>, <c>iloc</c>, <c>iinf</c>, <c>iref</c>, <c>iprp</c>, <c>stts</c>, <c>stsc</c>, <c>stss</c>...</summary>
    public ISet<string> OmittedBoxes { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the identifier of the primary item; <see langword="null"/> writes no <c>pitm</c> box.</summary>
    public uint? PrimaryItemId { get; set; }

    /// <summary>Gets the item properties: complete boxes, referred to by their 1-based index.</summary>
    public IList<byte[]> Properties { get; } = [];

    /// <summary>Gets the items.</summary>
    public IList<Item> Items { get; } = [];

    /// <summary>Gets the item references.</summary>
    public IList<(string Type, uint FromItemId, uint[] ToItemIds)> References { get; } = [];

    /// <summary>Gets boxes appended to the <c>meta</c> box after its standard children.</summary>
    public IList<byte[]> ExtraMetaBoxes { get; } = [];

    /// <summary>Gets or sets the time scale of the movie.</summary>
    public uint MovieTimescale { get; set; } = 1000;

    /// <summary>Gets the tracks.</summary>
    public IList<Track> Tracks { get; } = [];

    /// <summary>Gets or sets bytes written at the start of the <c>mdat</c> payload, before the item data.</summary>
    public byte[] MediaDataPrefix { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the data of the items (those of the file and those of the tracks) are written
    /// after the samples in the <c>mdat</c> box, instead of before them.
    /// </summary>
    public bool ItemDataFollowsSamples { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the chunks of the tracks are written to the <c>mdat</c> box from the last one
    /// to the first one: the first sample of a track is then the last one in the file. The sample tables keep the decoding
    /// order (ISO/IEC 14496-12 does not order the chunks in the file).
    /// </summary>
    public bool ChunksInReverseOrder { get; set; }

    /// <summary>Imports the description of an existing file: its brands, items, properties, references and tracks, with their data.</summary>
    /// <param name="data">The file.</param>
    /// <returns>A builder that writes an equivalent file.</returns>
    public static AvifFileBuilder From(ReadOnlySpan<byte> data)
    {
        var source = ReferenceAvif.Parse(data);
        var builder = new AvifFileBuilder { MajorBrand = source.MajorBrand, PrimaryItemId = source.PrimaryItemId, MovieTimescale = source.MovieTimescale };
        builder.CompatibleBrands.Clear();
        foreach (var brand in source.CompatibleBrands)
        {
            builder.CompatibleBrands.Add(brand);
        }

        builder.Layout.Clear();
        builder.Layout.Add(FileTypeBox);
        if (source.HasMeta)
            builder.Layout.Add(MetaBox);

        if (source.Tracks.Count > 0)
            builder.Layout.Add(MovieBox);

        builder.Layout.Add(MediaDataBox);
        foreach (var property in source.Properties)
        {
            builder.Properties.Add(Box(property.Type, property.Payload.ToArray()));
        }

        foreach (var item in source.Items)
        {
            var copy = new Item(item.Id, item.Type, item.Extents.Count == 0 ? [] : source.ReadItemData(item))
            {
                Name = item.Name,
                ContentType = item.ContentType,
                IsHidden = item.IsHidden,
                Storage = item.Extents.Count == 0 ? ItemStorage.None : item.ConstructionMethod == 1 ? ItemStorage.ItemData : ItemStorage.MediaData,
            };
            foreach (var association in item.Associations)
            {
                copy.Associations.Add((association.Property.Index, association.IsEssential));
            }

            builder.Items.Add(copy);
        }

        foreach (var reference in source.ItemReferences)
        {
            builder.References.Add((reference.Type, reference.FromItemId, [.. reference.ToItemIds]));
        }

        foreach (var track in source.Tracks)
        {
            var copy = new Track(track.Id, track.HandlerType, track.Width, track.Height)
            {
                Timescale = track.Timescale,
                Duration = track.Duration ?? ulong.MaxValue,
                SampleSizeBox = track.SampleSizeBox,
                ChunkOffsetBox = track.ChunkOffsetBox,
                Matrix = track.Orientation == 1 ? null : [.. track.Matrix],
            };
            copy.SampleEntryBoxes.Add(Box("av1C", track.ConfigurationRecord.ToArray()));
            if (track.Nclx is { } nclx)
                copy.SampleEntryBoxes.Add(ColorNclx(nclx.ColorPrimaries, nclx.TransferCharacteristics, nclx.MatrixCoefficients, nclx.FullRange));

            if (track.IccProfile is { } icc)
                copy.SampleEntryBoxes.Add(ColorProfile(icc.Span));

            if (track.AuxiliaryType is { } auxiliaryType)
                copy.SampleEntryBoxes.Add(FullBox("auxi", 0, 0, Encoding.UTF8.GetBytes(auxiliaryType), [0]));

            foreach (var reference in track.References)
            {
                copy.References.Add((reference.Type, [.. reference.TrackIds]));
            }

            if (track.EditList is { } edits)
            {
                copy.EditList = new EditList { IsRepeating = edits.IsRepeating };
                foreach (var edit in edits.Entries)
                {
                    copy.EditList.Entries.Add((edit.SegmentDuration ?? ulong.MaxValue, edit.MediaTime, edit.MediaRateInteger, edit.MediaRateFraction));
                }
            }

            foreach (var sample in track.Samples)
            {
                copy.Samples.Add((source.ReadSample(sample), sample.Duration));
            }

            // The items of the 'meta' box of the track get their own copy of their data (an encoder may share it with an
            // item of the 'meta' box of the file)
            copy.HasMeta = track.HasMeta;
            foreach (var item in track.MetaItems)
            {
                copy.MetaItems.Add(new Item(item.Id, item.Type, item.Extents.Count == 0 ? [] : source.ReadItemData(item))
                {
                    Name = item.Name,
                    ContentType = item.ContentType,
                    IsHidden = item.IsHidden,
                    Storage = item.Extents.Count == 0 ? ItemStorage.None : item.ConstructionMethod == 1 ? ItemStorage.ItemData : ItemStorage.MediaData,
                });
            }

            builder.Tracks.Add(copy);
        }

        return builder;
    }

    /// <summary>Adds a property and returns its 1-based index.</summary>
    /// <param name="box">The complete property box.</param>
    /// <returns>The index to associate with items.</returns>
    public int AddProperty(byte[] box)
    {
        Properties.Add(box);
        return Properties.Count;
    }

    /// <summary>Adds an item.</summary>
    /// <param name="id">The item identifier.</param>
    /// <param name="type">The item type.</param>
    /// <param name="data">The item data.</param>
    /// <param name="properties">The 1-based indices of the properties of the item; a negative index marks the property as essential.</param>
    /// <returns>The item.</returns>
    public Item AddItem(uint id, string type, byte[] data, params int[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var item = new Item(id, type, data);
        foreach (var index in properties)
        {
            item.Associations.Add((Math.Abs(index), index < 0));
        }

        Items.Add(item);
        return item;
    }

    /// <summary>Finds an item.</summary>
    /// <param name="id">The item identifier.</param>
    /// <returns>The item.</returns>
    public Item GetItem(uint id) => Items.First(item => item.Id == id);

    /// <summary>
    /// Gets the movie fragments (ISO/IEC 14496-12, section 8.8), which are written after the boxes of the layout: for
    /// each of them a <c>moof</c> box and the <c>mdat</c> box of its samples. A file with fragments has an <c>mvex</c>
    /// box in its <c>moov</c> box, with a <c>trex</c> box for each track.
    /// </summary>
    public IList<Fragment> Fragments { get; } = [];

    /// <summary>Gets or sets a value indicating whether the <c>moov</c> box has an <c>mvex</c> box although the file has no fragment.</summary>
    public bool HasMovieExtends { get; set; }

    /// <summary>Writes the file.</summary>
    /// <returns>The file.</returns>
    public byte[] Build()
    {
        // The mdat payload: the prefix, the extents of the items (separated by a filler byte when an item has several, so
        // that they are not contiguous), then the chunks of the tracks, one chunk of each track in turn; or the chunks
        // first. The items of the 'meta' box of a track follow the items of the file
        var media = new List<byte>(MediaDataPrefix);
        var itemExtents = new Dictionary<Item, List<(long Offset, long Length)>>();
        var itemDataBox = new List<byte>();
        var trackItemDataBoxes = Tracks.ToDictionary(track => track, _ => new List<byte>());
        void AddItems()
        {
            foreach (var (item, itemData) in Items.Select(item => (item, itemDataBox)).Concat(Tracks.SelectMany(track => track.MetaItems.Select(item => (item, trackItemDataBoxes[track])))))
            {
                var extents = new List<(long Offset, long Length)>();
                if (item.Storage != ItemStorage.None)
                {
                    var target = item.Storage == ItemStorage.ItemData ? itemData : media;
                    var position = 0;
                    var lengths = item.ExtentLengths.Count > 0 ? item.ExtentLengths : [item.Data.Length];
                    for (var i = 0; i < lengths.Count; i++)
                    {
                        if (i > 0)
                            target.Add(0xEE);

                        extents.Add((target.Count, lengths[i]));
                        target.AddRange(item.Data.AsSpan(position, lengths[i]));
                        position += lengths[i];
                    }
                }

                itemExtents.Add(item, extents);
            }
        }

        if (!ItemDataFollowsSamples)
            AddItems();

        var sampleOffsets = new Dictionary<Track, long[]>();
        foreach (var track in Tracks)
        {
            sampleOffsets.Add(track, new long[track.Samples.Count]);
        }

        var chunkCount = 0;
        while (Tracks.Any(track => chunkCount * (long)track.SamplesPerChunk < track.Samples.Count))
        {
            chunkCount++;
        }

        for (var step = 0; step < chunkCount; step++)
        {
            var chunk = ChunksInReverseOrder ? chunkCount - 1 - step : step;
            foreach (var track in Tracks)
            {
                for (var i = chunk * (long)track.SamplesPerChunk; i < Math.Min((chunk + 1) * (long)track.SamplesPerChunk, track.Samples.Count); i++)
                {
                    sampleOffsets[track][i] = media.Count;
                    media.AddRange(track.Samples[(int)i].Data);
                }
            }
        }

        if (ItemDataFollowsSamples)
            AddItems();

        // The sizes of the boxes do not depend on the offsets: a first pass finds where the mdat payload starts, the second
        // one writes the offsets
        byte[] Assemble(long mediaStart, out long measuredMediaStart)
        {
            measuredMediaStart = 0;
            var output = new List<byte>();
            foreach (var entry in Layout)
            {
                var large = LargeSizeBoxes.Contains(entry);
                switch (entry)
                {
                    case FileTypeBox:
                        output.AddRange(Sized(FileTypeBox, large, FileTypePayload()));
                        break;
                    case MetaBox:
                        output.AddRange(Sized(MetaBox, large, MetaPayload(Items, PrimaryItemId, References, Properties, ExtraMetaBoxes, isTrackMeta: false, itemExtents, [.. itemDataBox], mediaStart)));
                        break;
                    case MovieBox:
                        output.AddRange(Sized(MovieBox, large, MoviePayload(sampleOffsets, itemExtents, trackItemDataBoxes, mediaStart)));
                        break;
                    case MediaDataBox:
                        if (MediaDataExtendsToEnd)
                        {
                            measuredMediaStart = output.Count + 8;
                            output.AddRange(Concat(U32(0), Ascii(MediaDataBox), [.. media]));
                        }
                        else
                        {
                            measuredMediaStart = output.Count + (large ? 16 : 8);
                            output.AddRange(Sized(MediaDataBox, large, [.. media]));
                        }

                        break;
                    default:
                        output.AddRange(ExtraBoxes[entry]);
                        break;
                }
            }

            return [.. output];
        }

        Assemble(0, out var start);
        var file = new List<byte>(Assemble(start, out _));

        // The fragments: the samples of each track fragment follow each other in the mdat box of their fragment
        var decodeTimes = Tracks.ToDictionary(track => track.Id, track => (ulong)track.Samples.Sum(sample => (long)sample.Duration));
        for (var index = 0; index < Fragments.Count; index++)
        {
            var fragment = Fragments[index];
            var fragmentMedia = fragment.Tracks.SelectMany(track => track.Samples.SelectMany(sample => sample.Data)).ToArray();
            var fragmentStart = file.Count;
            byte[] Write(long movieFragmentStart, long mediaStart)
            {
                var boxes = new List<byte[]> { FullBox("mfhd", 0, 0, U32((uint)(index + 1))) };
                long position = 0;
                for (var t = 0; t < fragment.Tracks.Count; t++)
                {
                    var track = fragment.Tracks[t];
                    var parts = new List<byte[]>();

                    // tfhd: the base of the data offsets, and the default duration of the samples
                    var flags = track.Base switch { FragmentBase.MovieFragment => 0x020000, FragmentBase.Explicit => 0x000001, _ => 0 } | (track.UseDefaultDuration ? 0x000008 : 0);
                    parts.Add(FullBox("tfhd", 0, flags, U32(track.TrackId), track.Base == FragmentBase.Explicit ? U64((ulong)mediaStart) : [], track.UseDefaultDuration ? U32(track.Samples[0].Duration) : []));
                    if (!track.OmitDecodeTime)
                        parts.Add(FullBox("tfdt", 1, 0, U64(track.DecodeTime ?? decodeTimes.GetValueOrDefault(track.TrackId))));

                    // The data offset of the first run is relative to the base: the start of the moof box, or the start of
                    // the media data for an explicit base. Without either flag the base of a track fragment that is not
                    // the first one is the end of the data of the previous one, and its run has no offset
                    var hasOffset = track.Base != FragmentBase.Implicit || t == 0;
                    var dataOffset = track.Base == FragmentBase.Explicit ? position : mediaStart - movieFragmentStart + position;
                    var first = 0;
                    for (var run = 0; run < track.RunCount; run++)
                    {
                        var count = run == track.RunCount - 1 ? track.Samples.Count - first : track.Samples.Count / track.RunCount;
                        var runFlags = (run == 0 && hasOffset ? 0x000001 : 0) | (track.UseDefaultDuration || track.UseMovieDefaultDuration ? 0 : 0x000100) | 0x000200 | (track.CompositionOffset is null ? 0 : 0x000800);
                        var entries = new List<byte[]>();
                        for (var i = first; i < first + count; i++)
                        {
                            if ((runFlags & 0x000100) != 0)
                                entries.Add(U32(track.Samples[i].Duration));

                            entries.Add(U32((uint)track.Samples[i].Data.Length));
                            if (track.CompositionOffset is { } offset)
                                entries.Add(U32(unchecked((uint)offset)));
                        }

                        parts.Add(FullBox("trun", 0, runFlags, U32((uint)count), run == 0 && hasOffset ? U32(unchecked((uint)dataOffset)) : [], Concat([.. entries])));
                        first += count;
                    }

                    position += track.Samples.Sum(sample => (long)sample.Data.Length);
                    boxes.Add(Box("traf", [.. parts]));
                }

                return Box("moof", [.. boxes]);
            }

            // The size of the moof box does not depend on the offsets
            var size = Write(0, 0).Length;
            if (fragment.MediaDataFirst)
            {
                file.AddRange(Box(MediaDataBox, fragmentMedia));
                file.AddRange(Write(file.Count, fragmentStart + 8));
            }
            else
            {
                file.AddRange(Write(fragmentStart, fragmentStart + size + 8));
                file.AddRange(Box(MediaDataBox, fragmentMedia));
            }

            foreach (var track in fragment.Tracks)
            {
                decodeTimes[track.TrackId] = decodeTimes.GetValueOrDefault(track.TrackId) + (ulong)track.Samples.Sum(sample => (long)sample.Duration);
            }
        }

        return [.. file];
    }

    /// <summary>Writes a box: its size, its type and its payload.</summary>
    /// <param name="type">The four-character code.</param>
    /// <param name="payload">The parts of the payload.</param>
    /// <returns>The box.</returns>
    public static byte[] Box(string type, params byte[][] payload)
    {
        var content = Concat(payload);
        return Concat(U32((uint)(content.Length + 8)), Ascii(type), content);
    }

    /// <summary>Writes a full box: a box whose payload starts with a version and flags.</summary>
    /// <param name="type">The four-character code.</param>
    /// <param name="version">The version.</param>
    /// <param name="flags">The 24-bit flags.</param>
    /// <param name="payload">The parts of the payload.</param>
    /// <returns>The box.</returns>
    public static byte[] FullBox(string type, int version, int flags, params byte[][] payload) => Box(type, U32(((uint)version << 24) | (uint)flags), Concat(payload));

    /// <summary>Writes a box whose size is stored in the 64-bit field (size field 1).</summary>
    /// <param name="type">The four-character code.</param>
    /// <param name="payload">The parts of the payload.</param>
    /// <returns>The box.</returns>
    public static byte[] LargeBox(string type, params byte[][] payload)
    {
        var content = Concat(payload);
        return Concat(U32(1), Ascii(type), U64((ulong)content.Length + 16), content);
    }

    /// <summary>Writes an <c>ispe</c> property.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The box.</returns>
    public static byte[] ImageSize(uint width, uint height) => FullBox("ispe", 0, 0, U32(width), U32(height));

    /// <summary>Writes a <c>pixi</c> property.</summary>
    /// <param name="bitsPerChannel">The number of bits of each channel.</param>
    /// <returns>The box.</returns>
    public static byte[] PixelInformation(params byte[] bitsPerChannel)
    {
        ArgumentNullException.ThrowIfNull(bitsPerChannel);
        return FullBox("pixi", 0, 0, [(byte)bitsPerChannel.Length], bitsPerChannel);
    }

    /// <summary>Writes an <c>av1C</c> box without configuration OBUs.</summary>
    /// <param name="sequenceProfile">The <c>seq_profile</c> field.</param>
    /// <param name="sequenceLevelIndex">The <c>seq_level_idx_0</c> field.</param>
    /// <param name="bitDepth">8, 10 or 12.</param>
    /// <param name="monochrome">The <c>monochrome</c> field.</param>
    /// <param name="subsamplingX">The <c>chroma_subsampling_x</c> field.</param>
    /// <param name="subsamplingY">The <c>chroma_subsampling_y</c> field.</param>
    /// <param name="chromaSamplePosition">The <c>chroma_sample_position</c> field.</param>
    /// <returns>The box.</returns>
    public static byte[] Av1Configuration(int sequenceProfile, int sequenceLevelIndex, int bitDepth, bool monochrome, int subsamplingX, int subsamplingY, int chromaSamplePosition = 0)
    {
        var flags = (bitDepth > 8 ? 0x40 : 0) | (bitDepth == 12 ? 0x20 : 0) | (monochrome ? 0x10 : 0) | (subsamplingX << 3) | (subsamplingY << 2) | chromaSamplePosition;
        return Box("av1C", [0x81, (byte)((sequenceProfile << 5) | sequenceLevelIndex), (byte)flags, 0]);
    }

    /// <summary>Writes a <c>colr</c> box of type <c>nclx</c>.</summary>
    /// <param name="colorPrimaries">The color primaries.</param>
    /// <param name="transferCharacteristics">The transfer characteristics.</param>
    /// <param name="matrixCoefficients">The matrix coefficients.</param>
    /// <param name="fullRange">The full range flag.</param>
    /// <returns>The box.</returns>
    public static byte[] ColorNclx(int colorPrimaries, int transferCharacteristics, int matrixCoefficients, bool fullRange)
        => Box("colr", Ascii("nclx"), U16((ushort)colorPrimaries), U16((ushort)transferCharacteristics), U16((ushort)matrixCoefficients), [(byte)(fullRange ? 0x80 : 0)]);

    /// <summary>Writes a <c>colr</c> box of type <c>prof</c>.</summary>
    /// <param name="profile">The ICC profile.</param>
    /// <returns>The box.</returns>
    public static byte[] ColorProfile(ReadOnlySpan<byte> profile) => Box("colr", Ascii("prof"), profile.ToArray());

    /// <summary>Writes an <c>irot</c> property.</summary>
    /// <param name="quarterTurns">The number of anti-clockwise quarter turns, 0 to 3.</param>
    /// <returns>The box.</returns>
    public static byte[] Rotation(int quarterTurns) => Box("irot", [(byte)quarterTurns]);

    /// <summary>Writes an <c>imir</c> property.</summary>
    /// <param name="axis">0 exchanges the top and the bottom, 1 the left and the right.</param>
    /// <returns>The box.</returns>
    public static byte[] Mirror(int axis) => Box("imir", [(byte)axis]);

    /// <summary>Writes an <c>lsel</c> property (ISO/IEC 23008-12, section 6.5.11): the layer that the item shows.</summary>
    /// <param name="layer">The layer, which is an AV1 spatial layer; 0xFFFF selects none (the last layer is the image).</param>
    /// <returns>The box.</returns>
    public static byte[] LayerSelector(ushort layer) => Box("lsel", U16(layer));

    /// <summary>Writes an <c>a1op</c> property (AV1 Image File Format, section 2.3.2.1): the operating point of the bitstream of the item.</summary>
    /// <param name="index">The index of the operating point in the sequence header.</param>
    /// <returns>The box.</returns>
    public static byte[] OperatingPointSelector(byte index) => Box("a1op", [index]);

    /// <summary>Writes a <c>clap</c> property.</summary>
    /// <param name="widthNumerator">The numerator of the width.</param>
    /// <param name="widthDenominator">The denominator of the width.</param>
    /// <param name="heightNumerator">The numerator of the height.</param>
    /// <param name="heightDenominator">The denominator of the height.</param>
    /// <param name="horizontalOffsetNumerator">The numerator of the horizontal offset of the center.</param>
    /// <param name="horizontalOffsetDenominator">The denominator of the horizontal offset.</param>
    /// <param name="verticalOffsetNumerator">The numerator of the vertical offset of the center.</param>
    /// <param name="verticalOffsetDenominator">The denominator of the vertical offset.</param>
    /// <returns>The box.</returns>
    public static byte[] CleanAperture(uint widthNumerator, uint widthDenominator, uint heightNumerator, uint heightDenominator, int horizontalOffsetNumerator, uint horizontalOffsetDenominator, int verticalOffsetNumerator, uint verticalOffsetDenominator)
        => Box("clap", U32(widthNumerator), U32(widthDenominator), U32(heightNumerator), U32(heightDenominator), U32((uint)horizontalOffsetNumerator), U32(horizontalOffsetDenominator), U32((uint)verticalOffsetNumerator), U32(verticalOffsetDenominator));

    /// <summary>Writes an <c>auxC</c> property.</summary>
    /// <param name="auxiliaryType">The auxiliary type URN.</param>
    /// <returns>The box.</returns>
    public static byte[] AuxiliaryType(string auxiliaryType) => FullBox("auxC", 0, 0, Encoding.UTF8.GetBytes(auxiliaryType), [0]);

    /// <summary>Writes the descriptor of a <c>grid</c> item.</summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="width">The width of the reconstructed image.</param>
    /// <param name="height">The height of the reconstructed image.</param>
    /// <param name="largeFields">Whether the size is written in 32-bit fields.</param>
    /// <returns>The item data.</returns>
    public static byte[] GridDescriptor(int rows, int columns, uint width, uint height, bool largeFields = false)
        => largeFields
            ? Concat([0, 1, (byte)(rows - 1), (byte)(columns - 1)], U32(width), U32(height))
            : Concat([0, 0, (byte)(rows - 1), (byte)(columns - 1)], U16((ushort)width), U16((ushort)height));

    /// <summary>Writes the data of an <c>Exif</c> item: an offset of 0 to the TIFF header, then the TIFF data.</summary>
    /// <param name="tiff">The Exif data from the TIFF header.</param>
    /// <returns>The item data.</returns>
    public static byte[] ExifItemData(ReadOnlySpan<byte> tiff) => Concat(U32(0), tiff.ToArray());

    /// <summary>Writes a big-endian 16-bit field.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The bytes.</returns>
    public static byte[] U16(ushort value) => [(byte)(value >> 8), (byte)value];

    /// <summary>Writes a big-endian 32-bit field.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The bytes.</returns>
    public static byte[] U32(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>Writes a big-endian 64-bit field.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The bytes.</returns>
    public static byte[] U64(ulong value) => Concat(U32((uint)(value >> 32)), U32((uint)value));

    /// <summary>Writes the bytes of a four-character code or of another Latin-1 text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>Concatenates byte arrays.</summary>
    /// <param name="parts">The parts.</param>
    /// <returns>The concatenation.</returns>
    public static byte[] Concat(params byte[][] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static byte[] Sized(string type, bool large, byte[] payload) => large ? LargeBox(type, payload) : Box(type, payload);

    private byte[] FileTypePayload() => Concat(Ascii(MajorBrand), U32(0), Concat([.. CompatibleBrands.Select(Ascii)]));

    // ISO/IEC 14496-12, section 8.11: hdlr, pitm, iloc, iinf, iref, iprp, idat. The 'meta' box of a track holds the items
    // that describe the track: it has no primary item, and no 'iprp' box unless one of its items has properties
    private byte[] MetaPayload(IList<Item> items, uint? primaryItemId, IList<(string Type, uint FromItemId, uint[] ToItemIds)> references, IList<byte[]> properties, IList<byte[]> extraBoxes, bool isTrackMeta,
        Dictionary<Item, List<(long Offset, long Length)>> extents, byte[] itemData, long mediaStart)
    {
        var parts = new List<byte[]> { U32(0) };
        void Add(string type, Func<byte[]> box)
        {
            if (!OmittedBoxes.Contains(type))
                parts.Add(box());
        }

        Add("hdlr", () => FullBox("hdlr", 0, 0, U32(0), Ascii("pict"), new byte[12], [0]));
        if (primaryItemId is { } primary)
            Add("pitm", () => FullBox("pitm", 0, 0, U16((ushort)primary)));

        Add("iloc", () =>
        {
            // Version 1 when an item is stored in idat (the construction method field); 4-byte offsets and lengths, no base offset
            var located = items.Where(item => item.Storage != ItemStorage.None || item.ExtentOverride is not null).ToList();
            var version = located.Any(item => item.Storage == ItemStorage.ItemData || item.ConstructionMethodOverride is not null) ? 1 : 0;
            var entries = new List<byte[]> { new byte[] { 0x44, 0x00 }, U16((ushort)located.Count) };
            foreach (var item in located)
            {
                var list = item.ExtentOverride ?? [.. extents[item].Select(extent => (item.Storage == ItemStorage.ItemData ? extent.Offset : extent.Offset + mediaStart, extent.Length))];
                entries.Add(U16((ushort)item.Id));
                if (version == 1)
                    entries.Add(U16((ushort)(item.ConstructionMethodOverride ?? (item.Storage == ItemStorage.ItemData ? 1 : 0))));

                entries.Add(U16(0));
                entries.Add(U16((ushort)list.Count));
                foreach (var (offset, length) in list)
                {
                    entries.Add(U32((uint)offset));
                    entries.Add(U32((uint)length));
                }
            }

            return FullBox("iloc", version, 0, [.. entries]);
        });
        Add("iinf", () => FullBox("iinf", 0, 0, U16((ushort)items.Count(item => item.Type.Length > 0)), Concat([.. items.Where(item => item.Type.Length > 0).Select(item =>
            FullBox("infe", 2, item.IsHidden ? 1 : 0, U16((ushort)item.Id), U16(0), Ascii(item.Type), Encoding.UTF8.GetBytes(item.Name), [0], item.ContentType is null ? [] : Concat(Encoding.UTF8.GetBytes(item.ContentType), [0])))])));
        if (references.Count > 0)
            Add("iref", () => FullBox("iref", 0, 0, [.. references.Select(reference => Box(reference.Type, U16((ushort)reference.FromItemId), U16((ushort)reference.ToItemIds.Length), Concat([.. reference.ToItemIds.Select(id => U16((ushort)id))])))]));

        if (!isTrackMeta || items.Any(item => item.Associations.Count > 0))
        {
            Add("iprp", () =>
            {
                var associated = items.Where(item => item.Associations.Count > 0).ToList();
                var associations = new List<byte[]> { U32((uint)associated.Count) };
                foreach (var item in associated)
                {
                    associations.Add(U16((ushort)item.Id));
                    associations.Add([(byte)item.Associations.Count]);
                    associations.Add([.. item.Associations.Select(association => (byte)(association.PropertyIndex | (association.IsEssential ? 0x80 : 0)))]);
                }

                return Box("iprp", Box("ipco", [.. properties]), FullBox("ipma", 0, 0, [.. associations]));
            });
        }

        if (itemData.Length > 0)
            Add("idat", () => Box("idat", itemData));

        parts.AddRange(extraBoxes);
        return Concat([.. parts]);
    }

    // ISO/IEC 14496-12, section 8.2 to 8.7: mvhd, then for each track tkhd, tref, edts, mdia (mdhd, hdlr, minf (vmhd, dinf, stbl))
    private byte[] MoviePayload(Dictionary<Track, long[]> sampleOffsets, Dictionary<Item, List<(long Offset, long Length)>> itemExtents, Dictionary<Track, List<byte>> itemDataBoxes, long mediaStart)
    {
        var identity = Concat(U32(0x00010000), U32(0), U32(0), U32(0), U32(0x00010000), U32(0), U32(0), U32(0), U32(0x40000000));
        var movieDuration = Tracks.Count == 0 ? 0 : Tracks.Max(track => track.Duration);
        var parts = new List<byte[]>
        {
            FullBox("mvhd", 1, 0, U64(0), U64(0), U32(MovieTimescale), U64(movieDuration), U32(0x00010000), U16(0x0100), new byte[10], identity, new byte[24], U32((uint)Tracks.Count + 1)),
        };
        if (Fragments.Count > 0 || HasMovieExtends)
        {
            // ISO/IEC 14496-12, section 8.8.3 "Track Extends Box": the defaults of the samples of the fragments. The
            // default duration is the one of the first sample of the first fragment of the track
            parts.Add(Box("mvex", [.. Tracks.Select(track => FullBox("trex", 0, 0, U32(track.Id), U32(1), U32(Fragments.SelectMany(fragment => fragment.Tracks).FirstOrDefault(fragment => fragment.TrackId == track.Id) is { Samples.Count: > 0 } first ? first.Samples[0].Duration : 0), U32(0), U32(0)))]));
        }

        foreach (var track in Tracks)
        {
            var boxes = new List<byte[]>
            {
                track.HeaderVersion == 1
                    ? FullBox("tkhd", 1, 1, U64(0), U64(0), U32(track.Id), U32(0), U64(track.Duration), new byte[16], track.Matrix is { } matrix1 ? Concat([.. matrix1.Select(value => U32(unchecked((uint)value)))]) : identity, U32((uint)track.Width << 16), U32((uint)track.Height << 16))
                    : FullBox("tkhd", 0, 1, U32(0), U32(0), U32(track.Id), U32(0), U32(track.Duration == ulong.MaxValue ? uint.MaxValue : (uint)track.Duration), new byte[16], track.Matrix is { } matrix0 ? Concat([.. matrix0.Select(value => U32(unchecked((uint)value)))]) : identity, U32((uint)track.Width << 16), U32((uint)track.Height << 16)),
            };
            if (track.References.Count > 0)
                boxes.Add(Box("tref", [.. track.References.Select(reference => Box(reference.Type, [.. reference.TrackIds.Select(U32)]))]));

            if (track.EditList is { } edits)
            {
                var entries = new List<byte[]> { U32((uint)edits.Entries.Count) };
                foreach (var (segmentDuration, mediaTime, rateInteger, rateFraction) in edits.Entries)
                {
                    if (edits.Version == 1)
                    {
                        entries.Add(U64(segmentDuration));
                        entries.Add(U64((ulong)mediaTime));
                    }
                    else
                    {
                        entries.Add(U32(segmentDuration == ulong.MaxValue ? uint.MaxValue : (uint)segmentDuration));
                        entries.Add(U32((uint)mediaTime));
                    }

                    entries.Add(U16((ushort)rateInteger));
                    entries.Add(U16((ushort)rateFraction));
                }

                boxes.Add(Box("edts", FullBox("elst", edits.Version, edits.IsRepeating ? 1 : 0, [.. entries])));
            }

            // Where libavif writes it: between the edit list and the media
            if (track.MetaBoxOverride is { } metaBox)
            {
                boxes.Add(metaBox);
            }
            else if (track.HasMeta || track.MetaItems.Count > 0)
            {
                boxes.Add(Box(MetaBox, MetaPayload(track.MetaItems, primaryItemId: null, [], [], [], isTrackMeta: true, itemExtents, [.. itemDataBoxes[track]], mediaStart)));
            }

            boxes.Add(Box("mdia", MediaPayload(track, sampleOffsets[track], mediaStart)));
            parts.Add(Box("trak", [.. boxes]));
        }

        return Concat([.. parts]);
    }

    private byte[] MediaPayload(Track track, long[] offsets, long mediaStart)
    {
        var mediaDuration = (ulong)track.Samples.Sum(sample => (long)sample.Duration);
        var table = new List<byte[]>();
        void Add(string type, Func<byte[]> box)
        {
            if (!OmittedBoxes.Contains(type))
                table.Add(box());
        }

        // SampleEntry (8 bytes), VisualSampleEntry (70 bytes), then the boxes of the entry
        var entry = Concat(
            new byte[6], U16(1), new byte[16], U16((ushort)track.Width), U16((ushort)track.Height), U32(0x00480000), U32(0x00480000), U32(0), U16(1), new byte[32], U16(0x0018), U16(0xFFFF),
            Concat([.. track.SampleEntryBoxes]));
        Add("stsd", () => FullBox("stsd", 0, 0, U32(1), Box(track.SampleEntryType, entry)));
        Add("stts", () =>
        {
            // Runs of samples of the same duration
            var runs = new List<(uint Count, uint Delta)>();
            foreach (var sample in track.Samples)
            {
                if (runs.Count > 0 && runs[^1].Delta == sample.Duration)
                {
                    runs[^1] = (runs[^1].Count + 1, sample.Duration);
                }
                else
                {
                    runs.Add((1, sample.Duration));
                }
            }

            return FullBox("stts", 0, 0, U32((uint)runs.Count), Concat([.. runs.Select(run => Concat(U32(run.Count), U32(run.Delta)))]));
        });

        var chunkCount = (int)((track.Samples.Count + (long)track.SamplesPerChunk - 1) / track.SamplesPerChunk);
        Add("stsc", () =>
        {
            // One run for the full chunks and one for a shorter last chunk; none for a track whose samples are in fragments
            if (track.Samples.Count == 0)
                return FullBox("stsc", 0, 0, U32(0));

            var runs = new List<byte[]>();
            var perChunk = (uint)Math.Min(track.SamplesPerChunk, track.Samples.Count);
            runs.Add(Concat(U32(1), U32(perChunk), U32(1)));
            var last = (uint)(track.Samples.Count - ((long)(chunkCount - 1) * track.SamplesPerChunk));
            if (chunkCount > 1 && last != perChunk)
                runs.Add(Concat(U32((uint)chunkCount), U32(last), U32(1)));

            return FullBox("stsc", 0, 0, U32((uint)runs.Count), Concat([.. runs]));
        });
        Add(track.SampleSizeBox, () => track.SampleSizeBox == "stz2"
            ? FullBox("stz2", 0, 0, [0, 0, 0, (byte)track.CompactSizeBits], U32((uint)track.Samples.Count), CompactSizes(track))
            : FullBox("stsz", 0, 0, U32(0), U32((uint)track.Samples.Count), Concat([.. track.Samples.Select(sample => U32((uint)sample.Data.Length))])));
        Add(track.ChunkOffsetBox, () =>
        {
            var chunkOffsets = Enumerable.Range(0, chunkCount).Select(chunk => mediaStart + offsets[chunk * track.SamplesPerChunk]).ToList();
            return track.ChunkOffsetBox == "co64"
                ? FullBox("co64", 0, 0, U32((uint)chunkCount), Concat([.. chunkOffsets.Select(offset => U64((ulong)offset))]))
                : FullBox("stco", 0, 0, U32((uint)chunkCount), Concat([.. chunkOffsets.Select(offset => U32((uint)offset))]));
        });
        if (track.Samples.Count > 0)
            Add("stss", () => FullBox("stss", 0, 0, U32(1), U32(1)));

        return Concat(
            track.HeaderVersion == 1
                ? FullBox("mdhd", 1, 0, U64(0), U64(0), U32(track.Timescale), U64(mediaDuration), U16(0x55C4), U16(0))
                : FullBox("mdhd", 0, 0, U32(0), U32(0), U32(track.Timescale), U32((uint)mediaDuration), U16(0x55C4), U16(0)),
            OmittedBoxes.Contains("hdlr") ? [] : FullBox("hdlr", 0, 0, U32(0), Ascii(track.HandlerType), new byte[12], [0]),
            Box("minf",
                FullBox("vmhd", 0, 1, new byte[8]),
                Box("dinf", FullBox("dref", 0, 0, U32(1), FullBox("url ", 0, 1))),
                Box("stbl", [.. table])));
    }

    // ISO/IEC 14496-12, section 8.7.3.3 "Compact Sample Size Box": 4-bit sizes are packed two in a byte, the first one in the high bits
    private static byte[] CompactSizes(Track track)
    {
        var sizes = track.Samples.Select(sample => sample.Data.Length).ToList();
        switch (track.CompactSizeBits)
        {
            case 4:
            {
                var bytes = new byte[(sizes.Count + 1) / 2];
                for (var i = 0; i < sizes.Count; i++)
                {
                    bytes[i / 2] |= (byte)(i % 2 == 0 ? sizes[i] << 4 : sizes[i]);
                }

                return bytes;
            }

            case 8:
                return [.. sizes.Select(size => (byte)size)];
            default:
                return Concat([.. sizes.Select(size => U16((ushort)size))]);
        }
    }

    /// <summary>The base of the data offsets of a track fragment (ISO/IEC 14496-12, section 8.8.7).</summary>
    public enum FragmentBase
    {
        /// <summary>The start of the <c>moof</c> box: the <c>default-base-is-moof</c> flag.</summary>
        MovieFragment,

        /// <summary>An offset in the file, which is the start of the media data of the fragment: the <c>base-data-offset-present</c> flag.</summary>
        Explicit,

        /// <summary>Neither flag: the start of the <c>moof</c> box for the first track fragment, the end of the data of the previous one for the next ones.</summary>
        Implicit,
    }

    /// <summary>A movie fragment: a <c>moof</c> box and the <c>mdat</c> box of its samples.</summary>
    public sealed class Fragment
    {
        /// <summary>Gets the track fragments, whose samples are stored in this order.</summary>
        public IList<TrackFragment> Tracks { get; } = [];

        /// <summary>Gets or sets a value indicating whether the <c>mdat</c> box precedes the <c>moof</c> box.</summary>
        public bool MediaDataFirst { get; set; }
    }

    /// <summary>The samples of a track in a movie fragment (<c>traf</c>).</summary>
    /// <param name="trackId">The identifier of the track.</param>
    public sealed class TrackFragment(uint trackId)
    {
        /// <summary>Gets the identifier of the track.</summary>
        public uint TrackId => trackId;

        /// <summary>Gets the samples: the data and the duration of each.</summary>
        public IList<(byte[] Data, uint Duration)> Samples { get; } = [];

        /// <summary>Gets or sets the base of the data offsets.</summary>
        public FragmentBase Base { get; set; }

        /// <summary>Gets or sets the decode time of the first sample (<c>tfdt</c>), or <see langword="null"/> for the sum of the durations of the samples before it.</summary>
        public ulong? DecodeTime { get; set; }

        /// <summary>Gets or sets a value indicating whether the <c>tfdt</c> box is left out.</summary>
        public bool OmitDecodeTime { get; set; }

        /// <summary>Gets or sets a value indicating whether the duration of the samples is the default duration of the <c>tfhd</c> box (the one of the first sample).</summary>
        public bool UseDefaultDuration { get; set; }

        /// <summary>Gets or sets a value indicating whether the duration of the samples is the default duration of the <c>trex</c> box of the track.</summary>
        public bool UseMovieDefaultDuration { get; set; }

        /// <summary>Gets or sets the number of <c>trun</c> boxes the samples are split into.</summary>
        public int RunCount { get; set; } = 1;

        /// <summary>Gets or sets the composition time offset written for every sample, or <see langword="null"/> for none.</summary>
        public int? CompositionOffset { get; set; }
    }

    /// <summary>Where the data of an item is stored.</summary>
    public enum ItemStorage
    {
        /// <summary>In the <c>mdat</c> box: file offsets (construction method 0).</summary>
        MediaData,

        /// <summary>In the <c>idat</c> box of the <c>meta</c> box (construction method 1).</summary>
        ItemData,

        /// <summary>The item has no entry in the <c>iloc</c> box.</summary>
        None,
    }

    /// <summary>An item of the <c>meta</c> box.</summary>
    public sealed class Item
    {
        /// <summary>Initializes a new instance of the <see cref="Item"/> class.</summary>
        /// <param name="id">The item identifier.</param>
        /// <param name="type">The item type.</param>
        /// <param name="data">The item data.</param>
        public Item(uint id, string type, byte[] data)
        {
            Id = id;
            Type = type;
            Data = data;
        }

        /// <summary>Gets or sets the item identifier.</summary>
        public uint Id { get; set; }

        /// <summary>Gets or sets the item type; an empty type writes no <c>infe</c> entry.</summary>
        public string Type { get; set; }

        /// <summary>Gets or sets the item name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets the content type of a <c>mime</c> item.</summary>
        public string? ContentType { get; set; }

        /// <summary>Gets or sets a value indicating whether the item is hidden.</summary>
        public bool IsHidden { get; set; }

        /// <summary>Gets or sets the item data.</summary>
        public byte[] Data { get; set; }

        /// <summary>Gets or sets where the data is stored.</summary>
        public ItemStorage Storage { get; set; }

        /// <summary>Gets the lengths of the extents the data is split into; empty for one extent.</summary>
        public IList<int> ExtentLengths { get; } = [];

        /// <summary>Gets or sets the extents written to the <c>iloc</c> box instead of the computed ones (absolute offsets and lengths).</summary>
        public IList<(long Offset, long Length)>? ExtentOverride { get; set; }

        /// <summary>Gets or sets the construction method written to the <c>iloc</c> box instead of the one of <see cref="Storage"/>.</summary>
        public int? ConstructionMethodOverride { get; set; }

        /// <summary>Gets the properties of the item, in association order.</summary>
        public IList<(int PropertyIndex, bool IsEssential)> Associations { get; } = [];
    }

    /// <summary>An edit list.</summary>
    public sealed class EditList
    {
        /// <summary>Gets or sets the version of the box: 0 for 32-bit fields, 1 for 64-bit fields.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Gets or sets a value indicating whether the flag that repeats the edits is set.</summary>
        public bool IsRepeating { get; set; }

        /// <summary>Gets the edits; a segment duration of <see cref="ulong.MaxValue"/> writes every bit of the field.</summary>
        public IList<(ulong SegmentDuration, long MediaTime, short MediaRateInteger, short MediaRateFraction)> Entries { get; } = [];
    }

    /// <summary>A track of the <c>moov</c> box.</summary>
    public sealed class Track
    {
        /// <summary>Initializes a new instance of the <see cref="Track"/> class.</summary>
        /// <param name="id">The track identifier.</param>
        /// <param name="handlerType">The handler type: <c>pict</c> or <c>auxv</c>.</param>
        /// <param name="width">The width of the samples.</param>
        /// <param name="height">The height of the samples.</param>
        public Track(uint id, string handlerType, int width, int height)
        {
            Id = id;
            HandlerType = handlerType;
            Width = width;
            Height = height;
        }

        /// <summary>Gets or sets the track identifier.</summary>
        public uint Id { get; set; }

        /// <summary>Gets or sets the handler type of the media.</summary>
        public string HandlerType { get; set; }

        /// <summary>Gets or sets the width of the visual sample entry.</summary>
        public int Width { get; set; }

        /// <summary>Gets or sets the height of the visual sample entry.</summary>
        public int Height { get; set; }

        /// <summary>Gets or sets the type of the sample entry.</summary>
        public string SampleEntryType { get; set; } = "av01";

        /// <summary>Gets the boxes of the sample entry: <c>av1C</c>, <c>colr</c>, <c>auxi</c>...</summary>
        public IList<byte[]> SampleEntryBoxes { get; } = [];

        /// <summary>Gets or sets the time scale of the media.</summary>
        public uint Timescale { get; set; } = 1000;

        /// <summary>Gets or sets the duration written to the track header, in units of the time scale of the movie; <see cref="ulong.MaxValue"/> is indefinite.</summary>
        public ulong Duration { get; set; }

        /// <summary>Gets or sets the version of the <c>tkhd</c> and <c>mdhd</c> boxes: 0 for 32-bit fields, 1 for 64-bit fields.</summary>
        public int HeaderVersion { get; set; } = 1;

        /// <summary>Gets or sets the edit list; <see langword="null"/> writes no <c>edts</c> box.</summary>
        public EditList? EditList { get; set; }

        /// <summary>
        /// Gets or sets the nine values of the matrix of the track header { a, b, u, c, d, v, x, y, w } (16.16 fixed point; 2.30
        /// for u, v and w), or <see langword="null"/> for the identity.
        /// </summary>
        public int[]? Matrix { get; set; }

        /// <summary>Gets the track references.</summary>
        public IList<(string Type, uint[] TrackIds)> References { get; } = [];

        /// <summary>Gets the samples: their data and their duration in units of <see cref="Timescale"/>.</summary>
        public IList<(byte[] Data, uint Duration)> Samples { get; } = [];

        /// <summary>Gets or sets the box of the sample sizes: <c>stsz</c> or <c>stz2</c>.</summary>
        public string SampleSizeBox { get; set; } = "stsz";

        /// <summary>Gets or sets the field size of a <c>stz2</c> box: 4, 8 or 16.</summary>
        public int CompactSizeBits { get; set; } = 16;

        /// <summary>Gets or sets the box of the chunk offsets: <c>stco</c> or <c>co64</c>.</summary>
        public string ChunkOffsetBox { get; set; } = "stco";

        /// <summary>Gets or sets the number of samples of a chunk; the chunks of the tracks are interleaved in the <c>mdat</c> box.</summary>
        public int SamplesPerChunk { get; set; } = int.MaxValue;

        /// <summary>
        /// Gets the items of the <c>meta</c> box of the track (ISO/IEC 14496-12, section 8.11.1: a <c>meta</c> box may be in a
        /// <c>trak</c> box): the <c>Exif</c> and XMP items that describe an image sequence. The box is written with its
        /// <c>hdlr</c>, <c>iloc</c> and <c>iinf</c> boxes, as libavif writes it, when the track has items or <see cref="HasMeta"/> is set.
        /// </summary>
        public IList<Item> MetaItems { get; } = [];

        /// <summary>Gets or sets a value indicating whether the track has a <c>meta</c> box even without items.</summary>
        public bool HasMeta { get; set; }

        /// <summary>Gets or sets a complete box written in place of the <c>meta</c> box of the track (a malformed one, on purpose).</summary>
        public byte[]? MetaBoxOverride { get; set; }
    }
}
