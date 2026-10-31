using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Lists the images of a HEIC or AVIF file that relate to its primary item without being presented: its thumbnails (a
/// <c>thmb</c> reference, ISO/IEC 23008-12, section 6.6.2.3), its auxiliary images other than its alpha plane (an
/// <c>auxl</c> reference and the <c>auxC</c> property that gives their type, section 6.6.2.2), and its gain maps: the
/// second input image of a tone map derived item (<c>tmap</c>, ISO/IEC 23008-12 amendment, whose data is the metadata of
/// ISO 21496-1) whose first input is the primary item, and the auxiliary images of Apple's gain map type.
/// </summary>
internal static class HeifAuxiliaryScanner
{
    private const int BoxHeaderLength = 8;

    public static List<AuxiliaryImage> Scan(AuxiliaryImageCollection collection, ReadOnlySpan<byte> data, HeifCodecBinding binding, CancellationToken cancellationToken)
    {
        var result = new List<AuxiliaryImage>();
        if (!TryFindMetaBox(data, out var metaOffset, out var metaLength))
            return result;

        var payload = data.Slice(metaOffset, metaLength);
        var meta = HeifMetaBox.Parse(payload, binding, cancellationToken);
        if (meta.PrimaryItemId is not { } primaryId)
            return result;

        var entries = new Dictionary<uint, AuxiliaryImage>();
        foreach (var reference in meta.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (reference.Type)
            {
                case HeifFormat.Thmb when reference.ToItemIds.AsSpan().Contains(primaryId):
                    Add(collection, entries, result, meta, payload, binding, reference.FromItemId, AuxiliaryImageKind.Thumbnail, auxiliaryType: null, cancellationToken);
                    break;

                case HeifFormat.Auxl when reference.ToItemIds.AsSpan().Contains(primaryId):
                {
                    if (!meta.Items.TryGetValue(reference.FromItemId, out var item) || TryGetAuxiliaryType(meta, payload, binding, item) is not { } type)
                        break;

                    // The alpha plane is the transparency of the image, which every decode reads
                    if (binding.IsAlphaAuxiliaryType(Encoding.ASCII.GetBytes(type)))
                        break;

                    Add(collection, entries, result, meta, payload, binding, item.Id, Classify(type), type, cancellationToken);
                    break;
                }

                case HeifFormat.Dimg when reference.ToItemIds.Length is 2 && reference.ToItemIds[0] == primaryId:
                {
                    if (!meta.Items.TryGetValue(reference.FromItemId, out var toneMap) || toneMap.Type is not HeifFormat.Tmap)
                        break;

                    var gainMap = Add(collection, entries, result, meta, payload, binding, reference.ToItemIds[1], AuxiliaryImageKind.GainMap, auxiliaryType: null, cancellationToken);
                    if (gainMap is not null)
                        ReadToneMap(gainMap, meta, data, payload, binding, toneMap);

                    break;
                }

                default:
                    break;
            }
        }

        return result;
    }

    // A file of the HEIF family starts with its file type box; the 'meta' box is one of the boxes of the file
    private static bool TryFindMetaBox(ReadOnlySpan<byte> data, out int offset, out int length)
    {
        long position = 0;
        while (position + BoxHeaderLength <= data.Length)
        {
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(data[(int)position..]);
            var type = BinaryPrimitives.ReadUInt32BigEndian(data[((int)position + 4)..]);
            var headerLength = BoxHeaderLength;
            if (size is 1)
            {
                if (position + 16 > data.Length)
                    break;

                var largeSize = BinaryPrimitives.ReadUInt64BigEndian(data[((int)position + 8)..]);
                size = largeSize > long.MaxValue ? long.MaxValue : (long)largeSize;
                headerLength = 16;
            }
            else if (size is 0)
            {
                size = data.Length - position;
            }

            if (size < headerLength || size > data.Length - position)
                break;

            if (type is HeifFormat.Meta)
            {
                offset = (int)position + headerLength;
                length = (int)size - headerLength;
                return true;
            }

            position += size;
        }

        offset = 0;
        length = 0;
        return false;
    }

    private static AuxiliaryImage? Add(AuxiliaryImageCollection collection, Dictionary<uint, AuxiliaryImage> entries, List<AuxiliaryImage> result, HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifCodecBinding binding, uint itemId, AuxiliaryImageKind kind, string? auxiliaryType, CancellationToken cancellationToken)
    {
        if (entries.TryGetValue(itemId, out var existing))
        {
            // An image may be both an auxiliary image of Apple's gain map type and the gain map of a tone map item
            if (kind is AuxiliaryImageKind.GainMap)
                existing.Kind = AuxiliaryImageKind.GainMap;

            existing.AuxiliaryType ??= auxiliaryType;
            return existing;
        }

        if (!meta.Items.TryGetValue(itemId, out var item) || item.Type is 0 || TryGetSize(meta, payload, binding, item) is not { } declaredSize)
            return null;

        Size size;

        // The size of the image as it is presented, after its clean aperture (an encoder pads the coded pictures of a
        // small image), when the item can be decoded; its declared size otherwise, and the decoding reports the error
        try
        {
            size = HeifImagePlan.Create(binding, meta, payload, collection.Configuration.Limits, cancellationToken, itemId).Size;
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
        {
            size = declaredSize;
        }

        var image = new AuxiliaryImage(collection, kind, auxiliaryType, size) { HeifItemId = itemId };
        entries.Add(itemId, image);
        result.Add(image);
        return image;
    }

    private static AuxiliaryImageKind Classify(string type)
    {
        // ISO/IEC 23008-12, annex B.2.2 (HEVC) and ISO/IEC 23002-3 / MPEG-B (any codec) give the depth type; Apple names
        // its own types (portrait mattes, semantic segmentation mattes, gain maps)
        if (type is "urn:mpeg:hevc:2015:auxid:2" or "urn:mpeg:mpegB:cicp:systems:auxiliary:depth")
            return AuxiliaryImageKind.Depth;

        if (type is AuxiliaryImage.AppleGainMapType)
            return AuxiliaryImageKind.GainMap;

        if (type.StartsWith("urn:com:apple:photo:", StringComparison.Ordinal) && type.Contains("matte", StringComparison.Ordinal))
            return AuxiliaryImageKind.Matte;

        return AuxiliaryImageKind.Other;
    }

    private static string? TryGetAuxiliaryType(HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifCodecBinding binding, HeifItem item)
    {
        if (FindProperty(meta, item, HeifFormat.AuxC) is not var (offset, length))
            return null;

        try
        {
            var reader = new HeifBoxReader(payload.Slice(offset, length), binding);
            reader.ReadFullBoxHeader();
            return Encoding.ASCII.GetString(reader.ReadCString());
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    private static Size? TryGetSize(HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifCodecBinding binding, HeifItem item)
    {
        if (FindProperty(meta, item, HeifFormat.Ispe) is not var (offset, length))
            return null;

        try
        {
            var reader = new HeifBoxReader(payload.Slice(offset, length), binding);
            reader.ReadFullBoxHeader();
            var width = reader.ReadUInt32();
            var height = reader.ReadUInt32();
            if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
                return null;

            return new Size((int)width, (int)height);
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    private static (int Offset, int Length)? FindProperty(HeifMetaBox meta, HeifItem item, uint type)
    {
        foreach (var (index, _) in item.Properties)
        {
            if (index < 1 || index > meta.Properties.Count)
                continue;

            var (propertyType, offset, length) = meta.Properties[index - 1];
            if (propertyType == type)
                return (offset, length);
        }

        return null;
    }

    // The data of a tone map item is a version byte (0) and the metadata of ISO 21496-1. Its 'colr' property describes the
    // alternate rendition, whose primaries are those the gain map applies to when it does not use the color space of the
    // base image. A tone map item that cannot be read leaves the gain map without parameters, and the error is kept for
    // the operations that need them
    private static void ReadToneMap(AuxiliaryImage gainMap, HeifMetaBox meta, ReadOnlySpan<byte> data, ReadOnlySpan<byte> payload, HeifCodecBinding binding, HeifItem toneMap)
    {
        try
        {
            var itemData = GetItemData(meta, data, payload, binding, toneMap);
            if (itemData.Length is 0)
                throw binding.Invalid("the tone map item has no data.");

            if (itemData[0] is 0)
                gainMap.GainMapMetadata = GainMapMetadataReader.ReadIso(itemData[1..], binding.Format);

            if (FindProperty(meta, toneMap, HeifFormat.Colr) is var (offset, length))
            {
                var reader = new HeifBoxReader(payload.Slice(offset, length), binding);
                if (reader.ReadUInt32() is HeifFormat.Nclx)
                {
                    var primaries = reader.ReadUInt16();
                    if (primaries is > 0 and <= 255)
                        gainMap.AlternatePrimaries = (byte)primaries;
                }
            }
        }
        catch (InvalidImageContentException exception)
        {
            gainMap.GainMapMetadata = null;
            gainMap.MetadataError = exception;
        }
    }

    private static ReadOnlySpan<byte> GetItemData(HeifMetaBox meta, ReadOnlySpan<byte> data, ReadOnlySpan<byte> payload, HeifCodecBinding binding, HeifItem item)
    {
        // The metadata of a tone map item is a few hundred bytes: a single extent, in the file or in the 'idat' box
        if (item.Extents.Count is not 1 || item.ConstructionMethod > 1)
            throw binding.Invalid("the data of the tone map item is not one extent of the file or of its 'idat' box.");

        var (offset, length) = item.Extents[0];
        var source = item.ConstructionMethod is 1 ? payload.Slice(meta.ItemData.Offset, meta.ItemData.Length) : data;
        if (offset < 0 || length < 0 || offset > source.Length || length > source.Length - offset)
            throw binding.Invalid("the data of the tone map item is outside the file.");

        return source.Slice((int)offset, (int)length);
    }
}
