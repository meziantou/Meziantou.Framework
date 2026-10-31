using System.Buffers.Binary;
using System.Xml;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the parameters of a gain map: the binary metadata of ISO 21496-1 (the payload of a <c>tmap</c> item of HEIF, after
/// its version byte, and of the APP2 segment of a JPEG gain map image), and the <c>hdrgm</c> XMP namespace of Adobe and
/// Ultra HDR.
/// </summary>
internal static class GainMapMetadataReader
{
    /// <summary>The namespace of the URN that starts an APP2 segment of ISO 21496-1 metadata in a JPEG file.</summary>
    public static ReadOnlySpan<byte> IsoUrn => "urn:iso:std:iso:ts:21496:-1\0"u8;

    private const string GainMapNamespace = "http://ns.adobe.com/hdr-gain-map/1.0/";
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    // ISO 21496-1: the flags of the metadata, then the headrooms, then five fractions per channel
    private const int MultichannelFlag = 0x80;
    private const int UseBaseColorSpaceFlag = 0x40;
    private const int HeaderLength = 5;
    private const int HeadroomsLength = 16;
    private const int ChannelLength = 40;

    /// <summary>
    /// Reads ISO 21496-1 metadata: the minimum version and the writer version (two 16-bit fields), the flags (multiple
    /// channels, color space of the base rendition), the two headrooms, and the minimum and maximum gains, the gamma and the
    /// two offsets of each channel, as fractions of a signed or unsigned 32-bit numerator and an unsigned denominator.
    /// </summary>
    /// <param name="data">The metadata.</param>
    /// <param name="format">The format of the file, for the errors.</param>
    /// <returns>
    /// The parameters, or <see langword="null"/> when the metadata only has the version fields (the marker of the primary
    /// image of a JPEG file), or when its minimum version or its flags are not those of the version this reader knows.
    /// </returns>
    /// <exception cref="InvalidImageContentException">The metadata is truncated, has a denominator of 0, or values out of their range.</exception>
    public static GainMapMetadata? ReadIso(ReadOnlySpan<byte> data, ImageFormat format)
    {
        if (data.Length < 4)
            throw Invalid(format, "the ISO 21496-1 metadata is truncated.");

        // A reader must ignore metadata whose minimum version is newer than its own (the writer version is informative)
        var minimumVersion = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (minimumVersion is not 0 || data.Length is 4)
            return null;

        var flags = data[4];
        if ((flags & ~(MultichannelFlag | UseBaseColorSpaceFlag)) is not 0)
            return null;

        var channelCount = (flags & MultichannelFlag) is not 0 ? 3 : 1;
        if (data.Length < HeaderLength + HeadroomsLength + (channelCount * ChannelLength))
            throw Invalid(format, "the ISO 21496-1 metadata is truncated.");

        var position = HeaderLength;
        var baseHeadroom = ReadUnsigned(data, ref position, format);
        var alternateHeadroom = ReadUnsigned(data, ref position, format);
        var channels = new GainMapChannel[channelCount];
        for (var i = 0; i < channelCount; i++)
        {
            var minimum = ReadSigned(data, ref position, format);
            var maximum = ReadSigned(data, ref position, format);
            var gamma = ReadUnsigned(data, ref position, format);
            var baseOffset = ReadSigned(data, ref position, format);
            var alternateOffset = ReadSigned(data, ref position, format);
            channels[i] = CreateChannel(minimum, maximum, gamma, baseOffset, alternateOffset, format);
        }

        return new GainMapMetadata(baseHeadroom, alternateHeadroom, channels, useBaseColorSpace: (flags & UseBaseColorSpaceFlag) is not 0);
    }

    /// <summary>
    /// Reads the <c>hdrgm</c> properties of an XMP packet: <c>Version</c>, <c>BaseRenditionIsHDR</c>, <c>GainMapMin</c>,
    /// <c>GainMapMax</c>, <c>Gamma</c>, <c>OffsetSDR</c>, <c>OffsetHDR</c>, <c>HDRCapacityMin</c> and
    /// <c>HDRCapacityMax</c>, as attributes or elements, each a number or a sequence of three numbers (red, green, blue).
    /// </summary>
    /// <param name="xmp">The XMP packet, in UTF-8.</param>
    /// <param name="format">The format of the file, for the errors.</param>
    /// <returns>The parameters, or <see langword="null"/> when the packet has no <c>hdrgm:Version</c> property.</returns>
    /// <exception cref="InvalidImageContentException">The packet is not well-formed XML, or a property is missing or not a number.</exception>
    public static GainMapMetadata? ReadXmp(ReadOnlySpan<byte> xmp, ImageFormat format)
    {
        var properties = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        try
        {
            ReadXmpProperties(xmp.ToArray(), properties);
        }
        catch (XmlException exception)
        {
            throw new InvalidImageContentException($"The {FormatName(format)} gain map is not valid: its XMP packet is not well-formed ({exception.Message}).", format, exception);
        }

        if (!properties.ContainsKey("Version"))
            return null;

        var isBaseHdr = properties.TryGetValue("BaseRenditionIsHDR", out var isHdr) && string.Equals(isHdr[0], "True", StringComparison.OrdinalIgnoreCase);
        var minimum = GetValues(properties, "GainMapMin", 0, format);
        var maximum = GetValues(properties, "GainMapMax", defaultValue: null, format);
        var gamma = GetValues(properties, "Gamma", 1, format);
        var sdrOffset = GetValues(properties, "OffsetSDR", 1.0 / 64, format);
        var hdrOffset = GetValues(properties, "OffsetHDR", 1.0 / 64, format);
        var capacityMin = GetValues(properties, "HDRCapacityMin", 0, format)[0];
        var capacityMax = GetValues(properties, "HDRCapacityMax", defaultValue: null, format)[0];

        var channelCount = Math.Max(Math.Max(minimum.Length, maximum.Length), Math.Max(gamma.Length, Math.Max(sdrOffset.Length, hdrOffset.Length)));
        var channels = new GainMapChannel[channelCount];
        for (var i = 0; i < channelCount; i++)
        {
            // A high dynamic range base rendition has the headroom and the offset of the HDR rendition: the gain map goes
            // from it to the SDR rendition
            var sdr = GetChannelValue(sdrOffset, i);
            var hdr = GetChannelValue(hdrOffset, i);
            channels[i] = CreateChannel(GetChannelValue(minimum, i), GetChannelValue(maximum, i), GetChannelValue(gamma, i), isBaseHdr ? hdr : sdr, isBaseHdr ? sdr : hdr, format);
        }

        if (!double.IsFinite(capacityMin) || !double.IsFinite(capacityMax) || capacityMin < 0 || capacityMax < 0)
            throw Invalid(format, "the HDR capacity of the gain map is not a number of at least 0.");

        return isBaseHdr
            ? new GainMapMetadata(capacityMax, capacityMin, channels)
            : new GainMapMetadata(capacityMin, capacityMax, channels);
    }

    private static void ReadXmpProperties(byte[] xmp, Dictionary<string, List<string>> properties)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            ConformanceLevel = ConformanceLevel.Fragment,
        };

        using var stream = new MemoryStream(xmp, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType is not XmlNodeType.Element)
                continue;

            if (reader.NamespaceURI is GainMapNamespace)
            {
                // An element: a number, or a sequence (rdf:Seq) of numbers
                var name = reader.LocalName;
                var values = new List<string>();
                if (!reader.IsEmptyElement)
                {
                    var depth = reader.Depth;
                    while (reader.Read() && reader.Depth > depth)
                    {
                        if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                            values.Add(reader.Value.Trim());
                    }
                }

                if (values.Count > 0)
                    properties[name] = values;

                continue;
            }

            if (reader.LocalName is "Description" && reader.NamespaceURI is RdfNamespace && reader.MoveToFirstAttribute())
            {
                do
                {
                    if (reader.NamespaceURI is GainMapNamespace)
                        properties[reader.LocalName] = [reader.Value.Trim()];
                }
                while (reader.MoveToNextAttribute());

                reader.MoveToElement();
            }
        }
    }

    private static double[] GetValues(Dictionary<string, List<string>> properties, string name, double? defaultValue, ImageFormat format)
    {
        if (!properties.TryGetValue(name, out var texts))
        {
            if (defaultValue is { } value)
                return [value];

            throw Invalid(format, $"its XMP packet has no hdrgm:{name} property.");
        }

        if (texts.Count is not (1 or 3))
            throw Invalid(format, $"its hdrgm:{name} property has {texts.Count} values instead of one or three.");

        var values = new double[texts.Count];
        for (var i = 0; i < values.Length; i++)
        {
            if (!double.TryParse(texts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                throw Invalid(format, $"its hdrgm:{name} property is not a number.");
        }

        return values;
    }

    private static double GetChannelValue(double[] values, int channel) => values.Length is 3 ? values[channel] : values[0];

    private static GainMapChannel CreateChannel(double minimum, double maximum, double gamma, double baseOffset, double alternateOffset, ImageFormat format)
    {
        if (gamma <= 0 || maximum < minimum)
            throw Invalid(format, "its gamma is not greater than 0, or its maximum gain is less than its minimum gain.");

        return new GainMapChannel(minimum, maximum, gamma, baseOffset, alternateOffset);
    }

    private static double ReadSigned(ReadOnlySpan<byte> data, ref int position, ImageFormat format)
    {
        var numerator = BinaryPrimitives.ReadInt32BigEndian(data[position..]);
        var denominator = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
        position += 8;
        if (denominator is 0)
            throw Invalid(format, "a fraction of its ISO 21496-1 metadata has a denominator of 0.");

        return numerator / (double)denominator;
    }

    private static double ReadUnsigned(ReadOnlySpan<byte> data, ref int position, ImageFormat format)
    {
        var numerator = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
        var denominator = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
        position += 8;
        if (denominator is 0)
            throw Invalid(format, "a fraction of its ISO 21496-1 metadata has a denominator of 0.");

        return numerator / (double)denominator;
    }

    private static string FormatName(ImageFormat format) => format is ImageFormat.Unknown ? "image" : format.ToString().ToUpperInvariant();

    private static InvalidImageContentException Invalid(ImageFormat format, string reason) => new($"The {FormatName(format)} gain map is not valid: {reason}", format);
}
