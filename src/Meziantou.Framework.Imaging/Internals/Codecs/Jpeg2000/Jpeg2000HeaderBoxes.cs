using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The content of the JP2 header superbox (<c>jp2h</c>; T.800, I.5.3), and of the codestream header and compositing layer
/// header superboxes of a JPX file (<c>jpch</c> and <c>jplh</c>; T.801, M.11.6 and M.11.7), whose boxes replace the ones
/// of the JP2 header.
/// </summary>
internal sealed class Jpeg2000HeaderBoxes
{
    private const uint ColorGroupBox = 0x63677270;

    /// <summary>Gets a value indicating whether an image header box was read.</summary>
    public bool HasImageHeader { get; private set; }

    /// <summary>Gets the width of the image area the image header box declares.</summary>
    public long Width { get; private set; }

    /// <summary>Gets the height of the image area the image header box declares.</summary>
    public long Height { get; private set; }

    /// <summary>Gets the number of components the image header box declares.</summary>
    public int ComponentCount { get; private set; }

    /// <summary>Gets the color specifications, in file order.</summary>
    public List<Jpeg2000ColorSpecification> Colors { get; private set; } = [];

    /// <summary>Gets the palette, or <see langword="null"/>.</summary>
    public Jpeg2000Palette? Palette { get; private set; }

    /// <summary>Gets the channels the component mapping box defines, or <see langword="null"/> when each component is a channel.</summary>
    public Jpeg2000ComponentMapping[]? Mappings { get; private set; }

    /// <summary>Gets the channel definitions, or <see langword="null"/> when the channels are the colors in order.</summary>
    public Jpeg2000ChannelDefinition[]? Definitions { get; private set; }

    /// <summary>Gets the resolution of the image: the capture resolution, or else the default display resolution.</summary>
    public ImageResolution? Resolution { get; private set; }

    /// <summary>Reads the boxes of a header superbox.</summary>
    /// <param name="content">The content of the superbox.</param>
    /// <param name="metadata">The metadata the ICC profiles are charged to.</param>
    /// <exception cref="InvalidImageContentException">A box is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The profiles exceed the metadata limit.</exception>
    public void Read(ReadOnlySpan<byte> content, DecodedMetadataBuilder metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var colorsReplaced = false;
        ImageResolution? capture = null;
        ImageResolution? display = null;
        var reader = new Jpeg2000BoxReader(content);
        while (reader.TryRead(out var type, out var box))
        {
            switch (type)
            {
                case Jpeg2000Format.ImageHeaderBox:
                    ReadImageHeader(box);
                    break;

                case Jpeg2000Format.ColorSpecificationBox:
                    ReadColor(box, metadata, ref colorsReplaced);
                    break;

                case ColorGroupBox:
                {
                    var group = new Jpeg2000BoxReader(box);
                    while (group.TryRead(out var groupType, out var groupBox))
                    {
                        if (groupType == Jpeg2000Format.ColorSpecificationBox)
                        {
                            ReadColor(groupBox, metadata, ref colorsReplaced);
                        }
                    }

                    break;
                }

                case Jpeg2000Format.PaletteBox:
                    Palette = ReadPalette(box);
                    break;

                case Jpeg2000Format.ComponentMappingBox:
                    Mappings = ReadMappings(box);
                    break;

                case Jpeg2000Format.ChannelDefinitionBox:
                    Definitions = ReadDefinitions(box);
                    break;

                case Jpeg2000Format.ResolutionBox:
                {
                    var resolution = new Jpeg2000BoxReader(box);
                    while (resolution.TryRead(out var resolutionType, out var resolutionBox))
                    {
                        if (resolutionType == Jpeg2000Format.CaptureResolutionBox)
                        {
                            capture ??= ReadResolution(resolutionBox);
                        }
                        else if (resolutionType == Jpeg2000Format.DisplayResolutionBox)
                        {
                            display ??= ReadResolution(resolutionBox);
                        }
                    }

                    break;
                }
            }
        }

        if ((capture ?? display) is { } value)
        {
            Resolution = value;
        }
    }

    private static InvalidImageContentException Invalid(string message) => Jpeg2000Format.Invalid(message);

    private void ReadImageHeader(ReadOnlySpan<byte> box)
    {
        if (box.Length != 14)
            throw Invalid("the length of the image header box is not the one of its fields.");

        Height = BinaryPrimitives.ReadUInt32BigEndian(box);
        Width = BinaryPrimitives.ReadUInt32BigEndian(box[4..]);
        ComponentCount = BinaryPrimitives.ReadUInt16BigEndian(box[8..]);
        if (box[11] != 7)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the image header box declares the compression type {box[11]}; 7 is expected."));

        HasImageHeader = true;
    }

    private void ReadColor(ReadOnlySpan<byte> box, DecodedMetadataBuilder metadata, ref bool colorsReplaced)
    {
        if (box.Length < 3)
            throw Invalid("a color specification box is too short.");

        // The color specifications of a superbox replace those of the superbox read before it
        if (!colorsReplaced)
        {
            colorsReplaced = true;
            Colors = [];
        }

        var method = box[0];
        switch (method)
        {
            case Jpeg2000ColorSpecification.MethodEnumerated:
                if (box.Length < 7)
                    throw Invalid("a color specification box has no enumerated color space.");

                Colors.Add(new Jpeg2000ColorSpecification(method, BinaryPrimitives.ReadUInt32BigEndian(box[3..]), profile: null));
                break;

            case Jpeg2000ColorSpecification.MethodRestrictedIcc or Jpeg2000ColorSpecification.MethodAnyIcc:
            {
                var profile = box[3..];
                metadata.Charge(profile.Length);
                Colors.Add(new Jpeg2000ColorSpecification(method, enumeratedColorSpace: 0, profile.ToArray()));
                break;
            }

            default:
                Colors.Add(new Jpeg2000ColorSpecification(method, enumeratedColorSpace: 0, profile: null));
                break;
        }
    }

    private static Jpeg2000Palette ReadPalette(ReadOnlySpan<byte> box)
    {
        if (box.Length < 3)
            throw Invalid("the palette box is too short.");

        int entryCount = BinaryPrimitives.ReadUInt16BigEndian(box);
        int columnCount = box[2];
        if (entryCount is < 1 or > 1024 || columnCount < 1)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the palette has {entryCount} entries of {columnCount} columns; 1 to 1024 entries of 1 to 255 columns are allowed."));

        if (box.Length < 3 + columnCount)
            throw Invalid("the palette box is too short.");

        var depths = box.Slice(3, columnCount).ToArray();
        var entryLength = 0;
        foreach (var depth in depths)
        {
            var precision = (depth & 0x7F) + 1;
            if (precision > 38)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"a column of the palette has values of {precision} bits; at most 38 are allowed."));

            entryLength += (precision + 7) / 8;
        }

        var values = box[(3 + columnCount)..];
        if (values.Length != entryLength * entryCount)
            throw Invalid("the length of the palette box is not the one of its entries.");

        // Columns of more than 16 bits are rejected by the feature policy: their values are not kept
        var entries = new ushort[entryCount * columnCount];
        var offset = 0;
        for (var entry = 0; entry < entryCount; entry++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                var precision = (depths[column] & 0x7F) + 1;
                var length = (precision + 7) / 8;
                if (length <= 2)
                {
                    var value = length == 1 ? values[offset] : BinaryPrimitives.ReadUInt16BigEndian(values[offset..]);
                    entries[(column * entryCount) + entry] = (ushort)(value & ((1 << precision) - 1));
                }

                offset += length;
            }
        }

        return new Jpeg2000Palette(entryCount, depths, entries);
    }

    private static Jpeg2000ComponentMapping[] ReadMappings(ReadOnlySpan<byte> box)
    {
        if (box.IsEmpty || box.Length % 4 != 0)
            throw Invalid("the length of the component mapping box is not a number of channels.");

        var mappings = new Jpeg2000ComponentMapping[box.Length / 4];
        for (var i = 0; i < mappings.Length; i++)
        {
            var entry = box.Slice(4 * i, 4);
            if (entry[2] > 1)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the component mapping box uses the mapping type {entry[2]}, which is not defined."));

            mappings[i] = new Jpeg2000ComponentMapping(BinaryPrimitives.ReadUInt16BigEndian(entry), entry[2] == 1, entry[3]);
        }

        return mappings;
    }

    private static Jpeg2000ChannelDefinition[] ReadDefinitions(ReadOnlySpan<byte> box)
    {
        if (box.Length < 2)
            throw Invalid("the channel definition box is too short.");

        int count = BinaryPrimitives.ReadUInt16BigEndian(box);
        if (count == 0 || box.Length != 2 + (6 * count))
            throw Invalid("the length of the channel definition box is not the one of its channels.");

        var definitions = new Jpeg2000ChannelDefinition[count];
        for (var i = 0; i < count; i++)
        {
            var entry = box.Slice(2 + (6 * i), 6);
            definitions[i] = new Jpeg2000ChannelDefinition(BinaryPrimitives.ReadUInt16BigEndian(entry), BinaryPrimitives.ReadUInt16BigEndian(entry[2..]), BinaryPrimitives.ReadUInt16BigEndian(entry[4..]));
        }

        return definitions;
    }

    private static ImageResolution? ReadResolution(ReadOnlySpan<byte> box)
    {
        if (box.Length != 10)
            throw Invalid("the length of a resolution box is not the one of its fields.");

        return ResolutionConversion.FromJp2Resolution(
            horizontalNumerator: BinaryPrimitives.ReadUInt16BigEndian(box[4..]),
            horizontalDenominator: BinaryPrimitives.ReadUInt16BigEndian(box[6..]),
            horizontalExponent: (sbyte)box[9],
            verticalNumerator: BinaryPrimitives.ReadUInt16BigEndian(box),
            verticalDenominator: BinaryPrimitives.ReadUInt16BigEndian(box[2..]),
            verticalExponent: (sbyte)box[8]);
    }
}
