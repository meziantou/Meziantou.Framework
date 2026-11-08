using System.Buffers;
using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Assembles an AVIF file with a gain map from two still AVIF files written by the AVIF encoder (<see cref="HeifFileWriter"/>):
/// the one of the primary image, and the one of the gain map image, whose primary item becomes a hidden item of the file.
/// A tone map derived item (<c>tmap</c>, ISO/IEC 23008-12 amendment 3) derives the alternate rendition from the primary
/// item and the gain map item (a <c>dimg</c> reference, in that order), and its data is a version byte (0) and the metadata
/// of ISO 21496-1. An <c>altr</c> entity group offers the tone map item, then the primary item, and the file type lists the
/// <c>tmap</c> brand. libavif 1.3 writes the same structure; <see cref="HeifAuxiliaryScanner"/> reads it.
/// </summary>
/// <remarks>
/// The boxes of the primary file are kept, with the new items appended: the item locations (version 0, 4-byte offsets from
/// the start of the file) move by the bytes the new boxes add before the media data, and the new data follow the media data
/// of the primary file. The tone map item has the size of the primary item (<c>ispe</c>) and the color description of the
/// alternate rendition (<c>colr</c>), when given.
/// </remarks>
internal static class AvifGainMapAssembler
{
    private const uint Grpl = 0x6772706C;
    private const uint Altr = 0x616C7472;
    private const int BoxHeaderLength = 8;

    /// <summary>Writes the file.</summary>
    /// <param name="output">The output.</param>
    /// <param name="primary">The AVIF file of the primary image.</param>
    /// <param name="gainMap">The AVIF file of the gain map image.</param>
    /// <param name="metadata">The parameters of the gain map.</param>
    /// <param name="alternateColor">The CICP color description of the alternate rendition, or <see langword="null"/>.</param>
    /// <param name="alternateIccProfile">The ICC profile of the alternate rendition, or an empty span.</param>
    /// <exception cref="UnsupportedImageFeatureException">The gain map image is not opaque, or the file would exceed 4 GiB.</exception>
    public static void Write(IBufferWriter<byte> output, ReadOnlySpan<byte> primary, ReadOnlySpan<byte> gainMap, GainMapMetadata metadata, CicpColorDescription? alternateColor, ReadOnlySpan<byte> alternateIccProfile)
    {
        var primaryFile = HeifFile.Parse(primary);
        var gainMapFile = HeifFile.Parse(gainMap);

        // The gain map image is the primary item of its file: one coded item, whose data is one extent
        var gainMapItem = gainMapFile.Locations.Single(location => location.ItemId == gainMapFile.PrimaryItemId);
        if (gainMapFile.References.Exists(reference => reference.Type == HeifFormat.Auxl))
            throw new UnsupportedImageFeatureException("The gain map image has transparent pixels; a gain map image must be opaque.", ImageFormat.Avif, "Gain map with alpha");

        if (gainMapItem.Extents.Count is not 1 || gainMapFile.ItemTypes[gainMapFile.PrimaryItemId] != AvifBinding.Instance.CodedItemType)
            throw new InvalidOperationException("The gain map image was not written as one coded item.");

        var (gainMapOffset, gainMapLength) = gainMapItem.Extents[0];
        var gainMapPayload = gainMap.Slice((int)gainMapOffset, (int)gainMapLength);
        var toneMapData = (byte[])[0, .. GainMapMetadataWriter.WriteIso(metadata, ImageFormat.Avif)];

        var primaryId = primaryFile.PrimaryItemId;
        var maxId = primaryFile.Locations.Max(location => location.ItemId);
        var gainMapId = maxId + 1;
        var toneMapId = maxId + 2;
        var groupId = (uint)maxId + 3;
        if (toneMapId > ushort.MaxValue)
            throw new UnsupportedImageFeatureException("The AVIF image has too many items to add a gain map.", ImageFormat.Avif, "AVIF item count");

        // The properties: those of the primary file, those of the gain map item, then those of the tone map item
        var properties = new List<byte[]>(primaryFile.Properties);
        var associations = new List<(int ItemId, List<(bool Essential, int Index)> Properties)>(primaryFile.Associations);
        var gainMapAssociations = new List<(bool Essential, int Index)>();
        foreach (var (essential, index) in gainMapFile.Associations.Single(entry => entry.ItemId == gainMapFile.PrimaryItemId).Properties)
        {
            properties.Add(gainMapFile.Properties[index - 1]);
            gainMapAssociations.Add((essential, properties.Count));
        }

        var toneMapAssociations = new List<(bool Essential, int Index)>();
        var primarySize = primaryFile.Associations.Single(entry => entry.ItemId == primaryId).Properties
            .Select(association => primaryFile.Properties[association.Index - 1])
            .First(property => BinaryPrimitives.ReadUInt32BigEndian(property.AsSpan(4)) == HeifFormat.Ispe);
        properties.Add(primarySize);
        toneMapAssociations.Add((false, properties.Count));
        if (alternateColor is { } color)
        {
            properties.Add(CreateBox(HeifFormat.Colr, [.. ToBytes(HeifFormat.Nclx), 0, color.ColorPrimaries, 0, color.TransferCharacteristics, 0, color.MatrixCoefficients, (byte)(color.FullRange ? 0x80 : 0)]));
            toneMapAssociations.Add((false, properties.Count));
        }

        if (!alternateIccProfile.IsEmpty)
        {
            properties.Add(CreateBox(HeifFormat.Colr, [.. ToBytes(HeifFormat.Prof), .. alternateIccProfile]));
            toneMapAssociations.Add((false, properties.Count));
        }

        associations.Add((gainMapId, gainMapAssociations));
        associations.Add((toneMapId, toneMapAssociations));

        // The boxes before the media data; the locations are written last, once the size of everything before them is known
        var fileType = CreateBox(HeifFormat.Ftyp, [.. primaryFile.FileTypePayload, .. ToBytes(HeifFormat.Tmap)]);
        var itemInfo = CreateFullBox(HeifFormat.Iinf, 0, [.. ToBytes16(primaryFile.ItemInfos.Count + 2), .. primaryFile.ItemInfos.SelectMany(info => info), .. CreateItemInfo(gainMapId, AvifBinding.Instance.CodedItemType, hidden: true), .. CreateItemInfo(toneMapId, HeifFormat.Tmap, hidden: false)]);
        var references = CreateFullBox(HeifFormat.Iref, 0, [.. primaryFile.References.SelectMany(reference => reference.Box), .. CreateBox(HeifFormat.Dimg, [.. ToBytes16(toneMapId), .. ToBytes16(2), .. ToBytes16(primaryId), .. ToBytes16(gainMapId)])]);
        var itemProperties = CreateBox(HeifFormat.Iprp, [.. CreateBox(HeifFormat.Ipco, [.. properties.SelectMany(property => property)]), .. CreateAssociations(associations, properties.Count)]);
        var groups = CreateBox(Grpl, CreateFullBox(Altr, 0, [.. ToBytes(groupId), .. ToBytes(2), .. ToBytes((uint)toneMapId), .. ToBytes((uint)primaryId)]));
        var otherBoxes = primaryFile.OtherMetaBoxes.SelectMany(box => box).ToArray();

        var locationCount = primaryFile.Locations.Count + 2;
        var locationsLength = BoxHeaderLength + 4 + 4 + primaryFile.Locations.Sum(location => 6 + (location.Extents.Count * 8)) + (2 * (6 + 8));
        var metaLength = BoxHeaderLength + 4 + otherBoxes.Length + locationsLength + itemInfo.Length + references.Length + itemProperties.Length + groups.Length;
        var mediaDataStart = (long)fileType.Length + metaLength + BoxHeaderLength;
        var mediaDataLength = (long)primaryFile.MediaData.Length + toneMapData.Length + gainMapPayload.Length;
        if (mediaDataStart + mediaDataLength > uint.MaxValue)
            throw new UnsupportedImageFeatureException("The AVIF file would exceed 4 GiB, which this encoder does not write.", ImageFormat.Avif, "AVIF file size");

        // The data of the primary file move with its media data; the tone map data and the gain map follow them
        var shift = mediaDataStart - primaryFile.MediaDataOffset;
        var toneMapOffset = mediaDataStart + primaryFile.MediaData.Length;
        var locations = new ArrayBufferWriter<byte>(locationsLength);
        locations.Write((ReadOnlySpan<byte>)[0x44, 0x00]);
        locations.Write(ToBytes16(locationCount));
        foreach (var location in primaryFile.Locations)
        {
            WriteLocation(locations, location.ItemId, location.Extents.Select(extent => (extent.Offset + shift, extent.Length)));
        }

        WriteLocation(locations, gainMapId, [(toneMapOffset + toneMapData.Length, gainMapPayload.Length)]);
        WriteLocation(locations, toneMapId, [(toneMapOffset, toneMapData.Length)]);
        var locationBox = CreateFullBox(HeifFormat.Iloc, 0, locations.WrittenSpan);

        output.Write(fileType);
        output.Write(ToBytes((uint)metaLength));
        output.Write(ToBytes(HeifFormat.Meta));
        output.Write((ReadOnlySpan<byte>)[0, 0, 0, 0]);
        output.Write(otherBoxes);
        output.Write(locationBox);
        output.Write(itemInfo);
        output.Write(references);
        output.Write(itemProperties);
        output.Write(groups);
        output.Write(ToBytes((uint)(BoxHeaderLength + mediaDataLength)));
        output.Write(ToBytes(HeifFormat.Mdat));
        output.Write(primaryFile.MediaData.Span);
        output.Write(toneMapData);
        output.Write(gainMapPayload);

        static void WriteLocation(ArrayBufferWriter<byte> writer, int itemId, IEnumerable<(long Offset, long Length)> extents)
        {
            var list = extents.ToList();
            writer.Write(ToBytes16(itemId));
            writer.Write(ToBytes16(0)); // data_reference_index: this file
            writer.Write(ToBytes16(list.Count));
            foreach (var (offset, length) in list)
            {
                writer.Write(ToBytes((uint)offset));
                writer.Write(ToBytes((uint)length));
            }
        }
    }

    // ISO/IEC 23008-12, section 9.3.1: the associations of each item, with 7-bit indexes up to 127 properties
    private static byte[] CreateAssociations(List<(int ItemId, List<(bool Essential, int Index)> Properties)> associations, int propertyCount)
    {
        var large = propertyCount > 127;
        var payload = new ArrayBufferWriter<byte>();
        payload.Write(ToBytes((uint)associations.Count));
        foreach (var (itemId, list) in associations)
        {
            payload.Write(ToBytes16(itemId));
            payload.Write((ReadOnlySpan<byte>)[(byte)list.Count]);
            foreach (var (essential, index) in list)
            {
                if (large)
                {
                    payload.Write(ToBytes16((essential ? 0x8000 : 0) | index));
                }
                else
                {
                    payload.Write((ReadOnlySpan<byte>)[(byte)((essential ? 0x80 : 0) | index)]);
                }
            }
        }

        return CreateFullBox(HeifFormat.Ipma, large ? 1u : 0u, payload.WrittenSpan);
    }

    // An item information entry of version 2: the identifier, the protection index, the type and an empty name
    private static byte[] CreateItemInfo(int itemId, uint type, bool hidden)
        => CreateFullBox(HeifFormat.Infe, (2u << 24) | (hidden ? 1u : 0u), [.. ToBytes16(itemId), 0, 0, .. ToBytes(type), 0]);

    private static byte[] CreateBox(uint type, ReadOnlySpan<byte> payload)
    {
        var box = new byte[BoxHeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4), type);
        payload.CopyTo(box.AsSpan(BoxHeaderLength));
        return box;
    }

    private static byte[] CreateFullBox(uint type, uint versionAndFlags, ReadOnlySpan<byte> payload) => CreateBox(type, [.. ToBytes(versionAndFlags), .. payload]);

    private static byte[] ToBytes(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] ToBytes16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    /// <summary>
    /// The boxes of a still image file written by <see cref="HeifFileWriter"/>: a file type box, a meta box (version 0 item
    /// locations with 4-byte offsets and lengths, version 0 item information and references) and the media data.
    /// </summary>
    private sealed class HeifFile
    {
        public required byte[] FileTypePayload { get; init; }

        public required int PrimaryItemId { get; init; }

        /// <summary>Gets the meta boxes that are kept as they are (<c>hdlr</c>, <c>pitm</c>).</summary>
        public required List<byte[]> OtherMetaBoxes { get; init; }

        public required List<(int ItemId, List<(long Offset, long Length)> Extents)> Locations { get; init; }

        public required List<byte[]> ItemInfos { get; init; }

        public required Dictionary<int, uint> ItemTypes { get; init; }

        public required List<(uint Type, byte[] Box)> References { get; init; }

        public required List<byte[]> Properties { get; init; }

        public required List<(int ItemId, List<(bool Essential, int Index)> Properties)> Associations { get; init; }

        public required ReadOnlyMemory<byte> MediaData { get; init; }

        public required long MediaDataOffset { get; init; }

        public static HeifFile Parse(ReadOnlySpan<byte> file)
        {
            byte[]? fileType = null;
            var other = new List<byte[]>();
            var locations = new List<(int, List<(long, long)>)>();
            var infos = new List<byte[]>();
            var types = new Dictionary<int, uint>();
            var references = new List<(uint, byte[])>();
            var properties = new List<byte[]>();
            var associations = new List<(int, List<(bool, int)>)>();
            var primaryId = -1;
            ReadOnlyMemory<byte> mediaData = default;
            long mediaDataOffset = -1;
            var copy = file.ToArray();

            foreach (var (type, start, end) in Boxes(file, 0, file.Length))
            {
                switch (type)
                {
                    case HeifFormat.Ftyp:
                        fileType = file[(start + BoxHeaderLength)..end].ToArray();
                        break;

                    case HeifFormat.Meta:
                        foreach (var (childType, childStart, childEnd) in Boxes(file, start + BoxHeaderLength + 4, end))
                        {
                            var body = file[(childStart + BoxHeaderLength)..childEnd];
                            switch (childType)
                            {
                                case HeifFormat.Pitm:
                                    primaryId = BinaryPrimitives.ReadUInt16BigEndian(body[4..]);
                                    other.Add(file[childStart..childEnd].ToArray());
                                    break;

                                case HeifFormat.Iloc:
                                    ParseLocations(body, locations);
                                    break;

                                case HeifFormat.Iinf:
                                    foreach (var (_, infoStart, infoEnd) in Boxes(file, childStart + BoxHeaderLength + 6, childEnd))
                                    {
                                        var info = file[infoStart..infoEnd];
                                        infos.Add(info.ToArray());
                                        types[BinaryPrimitives.ReadUInt16BigEndian(info[12..])] = BinaryPrimitives.ReadUInt32BigEndian(info[16..]);
                                    }

                                    break;

                                case HeifFormat.Iref:
                                    foreach (var (referenceType, referenceStart, referenceEnd) in Boxes(file, childStart + BoxHeaderLength + 4, childEnd))
                                    {
                                        references.Add((referenceType, file[referenceStart..referenceEnd].ToArray()));
                                    }

                                    break;

                                case HeifFormat.Iprp:
                                    foreach (var (propertyType, propertyStart, propertyEnd) in Boxes(file, childStart + BoxHeaderLength, childEnd))
                                    {
                                        if (propertyType == HeifFormat.Ipco)
                                        {
                                            foreach (var (_, itemStart, itemEnd) in Boxes(file, propertyStart + BoxHeaderLength, propertyEnd))
                                            {
                                                properties.Add(file[itemStart..itemEnd].ToArray());
                                            }
                                        }
                                        else if (propertyType == HeifFormat.Ipma)
                                        {
                                            ParseAssociations(file[(propertyStart + BoxHeaderLength)..propertyEnd], associations);
                                        }
                                    }

                                    break;

                                default:
                                    other.Add(file[childStart..childEnd].ToArray());
                                    break;
                            }
                        }

                        break;

                    case HeifFormat.Mdat:
                        mediaDataOffset = start + BoxHeaderLength;
                        mediaData = copy.AsMemory((int)mediaDataOffset, end - (int)mediaDataOffset);
                        break;

                    default:
                        throw new InvalidOperationException("The AVIF encoder wrote an unexpected box.");
                }
            }

            if (fileType is null || primaryId < 0 || mediaDataOffset < 0)
                throw new InvalidOperationException("The AVIF encoder did not write a still image file.");

            return new HeifFile
            {
                FileTypePayload = fileType,
                PrimaryItemId = primaryId,
                OtherMetaBoxes = other,
                Locations = locations,
                ItemInfos = infos,
                ItemTypes = types,
                References = references,
                Properties = properties,
                Associations = associations,
                MediaData = mediaData,
                MediaDataOffset = mediaDataOffset,
            };
        }

        private static List<(uint Type, int Start, int End)> Boxes(ReadOnlySpan<byte> file, int start, int end)
        {
            var result = new List<(uint, int, int)>();
            var position = start;
            while (position < end)
            {
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(file[position..]);
                if (size < BoxHeaderLength || size > end - position)
                    throw new InvalidOperationException("The AVIF encoder wrote a box of an unexpected size.");

                result.Add((BinaryPrimitives.ReadUInt32BigEndian(file[(position + 4)..]), position, position + size));
                position += size;
            }

            return result;
        }

        private static void ParseLocations(ReadOnlySpan<byte> body, List<(int, List<(long, long)>)> locations)
        {
            // Version 0, 4-byte offsets and lengths, no base offset
            if (body[0] is not 0 || body[4] is not 0x44 || body[5] is not 0)
                throw new InvalidOperationException("The AVIF encoder wrote unexpected item locations.");

            var count = BinaryPrimitives.ReadUInt16BigEndian(body[6..]);
            var position = 8;
            for (var i = 0; i < count; i++)
            {
                var itemId = BinaryPrimitives.ReadUInt16BigEndian(body[position..]);
                var extentCount = BinaryPrimitives.ReadUInt16BigEndian(body[(position + 4)..]);
                position += 6;
                var extents = new List<(long, long)>();
                for (var j = 0; j < extentCount; j++)
                {
                    extents.Add((BinaryPrimitives.ReadUInt32BigEndian(body[position..]), BinaryPrimitives.ReadUInt32BigEndian(body[(position + 4)..])));
                    position += 8;
                }

                locations.Add((itemId, extents));
            }
        }

        private static void ParseAssociations(ReadOnlySpan<byte> body, List<(int, List<(bool, int)>)> associations)
        {
            var version = body[0];
            var large = (body[3] & 1) is not 0;
            var count = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
            var position = 8;
            for (var i = 0; i < count; i++)
            {
                int itemId;
                if (version is 0)
                {
                    itemId = BinaryPrimitives.ReadUInt16BigEndian(body[position..]);
                    position += 2;
                }
                else
                {
                    itemId = (int)BinaryPrimitives.ReadUInt32BigEndian(body[position..]);
                    position += 4;
                }

                var associationCount = body[position++];
                var list = new List<(bool, int)>();
                for (var j = 0; j < associationCount; j++)
                {
                    if (large)
                    {
                        var value = BinaryPrimitives.ReadUInt16BigEndian(body[position..]);
                        list.Add(((value & 0x8000) is not 0, value & 0x7FFF));
                        position += 2;
                    }
                    else
                    {
                        list.Add(((body[position] & 0x80) is not 0, body[position] & 0x7F));
                        position++;
                    }
                }

                associations.Add((itemId, list));
            }
        }
    }
}
