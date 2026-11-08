using System.Buffers.Binary;
using System.Text;
using System.Xml;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the parameters of a gain map: the binary metadata of ISO 21496-1, which <see cref="GainMapMetadataReader.ReadIso"/>
/// reads, and the <c>hdrgm</c> XMP properties of Adobe and Ultra HDR 1.0, with the container directory of the primary image
/// of an Ultra HDR file, merged into an XMP packet.
/// </summary>
internal static class GainMapMetadataWriter
{
    private const string GainMapNamespace = "http://ns.adobe.com/hdr-gain-map/1.0/";
    private const string ContainerNamespace = "http://ns.google.com/photos/1.0/container/";
    private const string ItemNamespace = "http://ns.google.com/photos/1.0/container/item/";
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string XmpMetaNamespace = "adobe:ns:meta/";

    /// <summary>
    /// Writes ISO 21496-1 metadata: the minimum version and the writer version (0), the flags (several channels, color space
    /// of the base rendition), the two headrooms, and the minimum and maximum gains, the gamma and the two offsets of each
    /// channel, as fractions of a 32-bit numerator (signed for the gains and the offsets) and an unsigned 32-bit denominator.
    /// </summary>
    /// <param name="metadata">The parameters.</param>
    /// <param name="format">The format of the file, for the errors.</param>
    /// <returns>The metadata.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A value is out of the range of the fractions.</exception>
    public static byte[] WriteIso(GainMapMetadata metadata, ImageFormat format)
    {
        var channels = metadata.Channels;
        var data = new byte[5 + 16 + (channels.Count * 40)];
        data[4] = (byte)((channels.Count is 3 ? 0x80 : 0) | (metadata.UseBaseColorSpace ? 0x40 : 0));
        var position = 5;
        WriteFraction(data, ref position, metadata.BaseHdrHeadroom, signed: false, format);
        WriteFraction(data, ref position, metadata.AlternateHdrHeadroom, signed: false, format);
        foreach (var channel in channels)
        {
            WriteFraction(data, ref position, channel.GainMapMin, signed: true, format);
            WriteFraction(data, ref position, channel.GainMapMax, signed: true, format);
            WriteFraction(data, ref position, channel.Gamma, signed: false, format);
            WriteFraction(data, ref position, channel.BaseOffset, signed: true, format);
            WriteFraction(data, ref position, channel.AlternateOffset, signed: true, format);
        }

        return data;
    }

    /// <summary>
    /// Merges the <c>hdrgm</c> properties of a gain map image into its XMP packet: <c>Version</c>, <c>BaseRenditionIsHDR</c>,
    /// the headrooms (<c>HDRCapacityMin</c> and <c>HDRCapacityMax</c>) and the values of the channels, a number or a sequence
    /// of three numbers. A gain map whose base rendition has the larger headroom is written with
    /// <c>BaseRenditionIsHDR</c>, the headrooms and the offsets swapped, as <see cref="GainMapMetadataReader.ReadXmp"/> reads it.
    /// </summary>
    /// <param name="packet">The XMP packet of the image, or an empty span.</param>
    /// <param name="metadata">The parameters.</param>
    /// <returns>The packet, in UTF-8.</returns>
    /// <exception cref="InvalidImageContentException"><paramref name="packet"/> is not a well-formed XMP packet.</exception>
    public static byte[] MergeGainMapXmp(ReadOnlySpan<byte> packet, GainMapMetadata metadata)
    {
        var isBaseHdr = metadata.BaseHdrHeadroom > metadata.AlternateHdrHeadroom;
        var channels = metadata.Channels;
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"<rdf:Description rdf:about=\"\" xmlns:rdf=\"{RdfNamespace}\" xmlns:hdrgm=\"{GainMapNamespace}\" hdrgm:Version=\"1.0\"");
        builder.Append(CultureInfo.InvariantCulture, $" hdrgm:BaseRenditionIsHDR=\"{(isBaseHdr ? "True" : "False")}\"");
        builder.Append(CultureInfo.InvariantCulture, $" hdrgm:HDRCapacityMin=\"{Format(Math.Min(metadata.BaseHdrHeadroom, metadata.AlternateHdrHeadroom))}\"");
        builder.Append(CultureInfo.InvariantCulture, $" hdrgm:HDRCapacityMax=\"{Format(Math.Max(metadata.BaseHdrHeadroom, metadata.AlternateHdrHeadroom))}\"");

        (string Name, Func<GainMapChannel, double> Value)[] properties =
        [
            ("GainMapMin", channel => channel.GainMapMin),
            ("GainMapMax", channel => channel.GainMapMax),
            ("Gamma", channel => channel.Gamma),
            ("OffsetSDR", channel => isBaseHdr ? channel.AlternateOffset : channel.BaseOffset),
            ("OffsetHDR", channel => isBaseHdr ? channel.BaseOffset : channel.AlternateOffset),
        ];

        // A value that is the same for every channel is an attribute, the others are sequences of three values
        var sequences = new List<(string Name, Func<GainMapChannel, double> Value)>();
        foreach (var property in properties)
        {
            if (channels.All(channel => property.Value(channel).Equals(property.Value(channels[0]))))
            {
                builder.Append(CultureInfo.InvariantCulture, $" hdrgm:{property.Name}=\"{Format(property.Value(channels[0]))}\"");
            }
            else
            {
                sequences.Add(property);
            }
        }

        if (sequences.Count is 0)
        {
            builder.Append("/>");
        }
        else
        {
            builder.Append('>');
            foreach (var (name, value) in sequences)
            {
                builder.Append(CultureInfo.InvariantCulture, $"<hdrgm:{name}><rdf:Seq>");
                foreach (var channel in channels)
                {
                    builder.Append(CultureInfo.InvariantCulture, $"<rdf:li>{Format(value(channel))}</rdf:li>");
                }

                builder.Append(CultureInfo.InvariantCulture, $"</rdf:Seq></hdrgm:{name}>");
            }

            builder.Append("</rdf:Description>");
        }

        return Merge(packet, builder.ToString());
    }

    /// <summary>
    /// Merges the description of the primary image of an Ultra HDR file into its XMP packet: <c>hdrgm:Version</c> and the
    /// container directory (<c>Container:Directory</c>) that lists the primary image and the gain map image, with its length.
    /// </summary>
    /// <param name="packet">The XMP packet of the image, or an empty span.</param>
    /// <param name="gainMapLength">The number of bytes of the gain map image.</param>
    /// <returns>The packet, in UTF-8.</returns>
    /// <exception cref="InvalidImageContentException"><paramref name="packet"/> is not a well-formed XMP packet.</exception>
    public static byte[] MergePrimaryXmp(ReadOnlySpan<byte> packet, int gainMapLength)
    {
        var description = string.Create(CultureInfo.InvariantCulture, $"<rdf:Description rdf:about=\"\" xmlns:rdf=\"{RdfNamespace}\" xmlns:hdrgm=\"{GainMapNamespace}\" xmlns:Container=\"{ContainerNamespace}\" xmlns:Item=\"{ItemNamespace}\" hdrgm:Version=\"1.0\">")
            + "<Container:Directory><rdf:Seq>"
            + "<rdf:li rdf:parseType=\"Resource\"><Container:Item Item:Semantic=\"Primary\" Item:Mime=\"image/jpeg\"/></rdf:li>"
            + string.Create(CultureInfo.InvariantCulture, $"<rdf:li rdf:parseType=\"Resource\"><Container:Item Item:Semantic=\"GainMap\" Item:Mime=\"image/jpeg\" Item:Length=\"{gainMapLength}\"/></rdf:li>")
            + "</rdf:Seq></Container:Directory></rdf:Description>";
        return Merge(packet, description);
    }

    // The properties of the gain map namespaces that the packet already has (a file that was read, then written again) are
    // replaced: they are removed from every description, and the new description is added to the RDF element
    private static byte[] Merge(ReadOnlySpan<byte> packet, string description)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        if (packet.IsEmpty)
        {
            document.LoadXml($"<x:xmpmeta xmlns:x=\"{XmpMetaNamespace}\"><rdf:RDF xmlns:rdf=\"{RdfNamespace}\"/></x:xmpmeta>");
        }
        else
        {
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using var stream = new MemoryStream(packet.ToArray(), writable: false);
                using var reader = XmlReader.Create(stream, settings);
                document.Load(reader);
            }
            catch (XmlException exception)
            {
                throw new InvalidImageContentException($"The XMP packet is not well-formed ({exception.Message}).", ImageFormat.Unknown, exception);
            }
        }

        var rdf = document.GetElementsByTagName("RDF", RdfNamespace).Cast<XmlElement>().FirstOrDefault()
            ?? throw new InvalidImageContentException("The XMP packet has no rdf:RDF element.", ImageFormat.Unknown);

        foreach (var existing in rdf.ChildNodes.OfType<XmlElement>().ToList())
        {
            if (existing.LocalName is not "Description" || existing.NamespaceURI is not RdfNamespace)
                continue;

            foreach (var attribute in existing.Attributes.Cast<XmlAttribute>().Where(IsGainMapNode).ToList())
            {
                existing.Attributes.Remove(attribute);
            }

            foreach (var child in existing.ChildNodes.OfType<XmlElement>().Where(IsGainMapNode).ToList())
            {
                existing.RemoveChild(child);
            }
        }

        var fragment = new XmlDocument { XmlResolver = null };
        fragment.LoadXml(description);
        rdf.AppendChild(document.ImportNode(fragment.DocumentElement!, deep: true));

        using var output = new MemoryStream();
        var writerSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = document.FirstChild is not XmlDeclaration,
        };

        using (var writer = XmlWriter.Create(output, writerSettings))
        {
            document.Save(writer);
        }

        return output.ToArray();

        static bool IsGainMapNode(XmlNode node) => node.NamespaceURI is GainMapNamespace or ContainerNamespace or ItemNamespace;
    }

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static void WriteFraction(Span<byte> data, ref int position, double value, bool signed, ImageFormat format)
    {
        var (numerator, denominator) = ToFraction(value, signed, format);
        if (signed)
        {
            BinaryPrimitives.WriteInt32BigEndian(data[position..], (int)numerator);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(data[position..], (uint)numerator);
        }

        BinaryPrimitives.WriteUInt32BigEndian(data[(position + 4)..], denominator);
        position += 8;
    }

    /// <summary>
    /// Gets the fraction closest to a value whose numerator and denominator fit their fields: the last convergent of its
    /// continued fraction that fits them, which is the value itself when its denominator is small (a power of two, for
    /// example).
    /// </summary>
    internal static (long Numerator, uint Denominator) ToFraction(double value, bool signed, ImageFormat format)
    {
        double maxNumerator = signed ? (value < 0 ? 2147483648.0 : int.MaxValue) : uint.MaxValue;
        var magnitude = Math.Abs(value);
        if (!double.IsFinite(value) || (!signed && value < 0) || Math.Round(magnitude, MidpointRounding.AwayFromZero) > maxNumerator)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The gain map value {value} cannot be stored as a fraction of ISO 21496-1."), format, "Gain map value range");

        // h(n) = a(n) h(n-1) + h(n-2), k(n) = a(n) k(n-1) + k(n-2), from h(-1) = 1, h(-2) = 0, k(-1) = 0, k(-2) = 1
        long previousNumerator = 0, numerator = 1, previousDenominator = 1, denominator = 0;
        var remainder = magnitude;
        for (var i = 0; i < 64; i++)
        {
            var term = Math.Floor(remainder);
            if ((term * numerator) + previousNumerator > maxNumerator || (term * denominator) + previousDenominator > uint.MaxValue)
                break;

            var nextNumerator = ((long)term * numerator) + previousNumerator;
            var nextDenominator = ((long)term * denominator) + previousDenominator;
            (previousNumerator, numerator) = (numerator, nextNumerator);
            (previousDenominator, denominator) = (denominator, nextDenominator);
            var fraction = remainder - term;
            if (fraction == 0 || numerator / (double)denominator == magnitude)
                break;

            remainder = 1 / fraction;
        }

        if (denominator is 0)
        {
            // The integer part itself does not fit: only possible at the bounds, which the check above rounds into range
            numerator = (long)Math.Min(Math.Round(magnitude, MidpointRounding.AwayFromZero), maxNumerator);
            denominator = 1;
        }

        return (value < 0 ? -numerator : numerator, (uint)denominator);
    }
}
