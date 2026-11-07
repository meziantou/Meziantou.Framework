using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What an <see cref="Image"/> can hold of a JPEG 2000 file, decided from the main header of the codestream and from the
/// header boxes of the file, and used by identification and decoding alike.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Samples: unsigned, of 1 to 16 bits. Signed samples and deeper samples are rejected: no pixel format holds them.
/// </description></item>
/// <item><description>
/// Channels: one gray level, three colors or four inks, and at most one opacity channel that applies to the whole image
/// and is not multiplied into the colors. Any other channel is rejected rather than dropped.
/// </description></item>
/// <item><description>
/// A codestream that is not in a file has no color specification: one component is a gray level, two are a gray level and
/// its opacity, three are red, green and blue, and four add an opacity. Likewise, a file without channel definition box
/// that has one channel more than its color space has colors holds an opacity in it.
/// </description></item>
/// <item><description>
/// Sub-sampling: only the two chrominance channels of an sYCC image may be sub-sampled.
/// </description></item>
/// </list>
/// </remarks>
internal static class Jpeg2000FeaturePolicy
{
    private const int CapabilityPart2 = 0x8000;
    private const int CapabilityHighThroughput = 0x4000;
    private const uint IccGray = 0x47524159;
    private const uint IccRgb = 0x52474220;
    private const uint IccCmyk = 0x434D594B;

    /// <summary>Rejects, as soon as the SIZ marker segment is read, the codestreams whose samples an image cannot hold.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The codestream uses such a feature.</exception>
    public static void EnsureSupported(Jpeg2000Siz siz)
    {
        ArgumentNullException.ThrowIfNull(siz);
        if ((siz.Capabilities & CapabilityPart2) != 0)
            throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream uses the extensions of Rec. ITU-T T.801 | ISO/IEC 15444-2.", "extensions of ISO/IEC 15444-2");

        if (siz.Components.Length > Jpeg2000Format.MaxSupportedComponents)
            throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 image has {siz.Components.Length} components; at most {Jpeg2000Format.MaxSupportedComponents} (four inks and an opacity) are supported."), "component count");

        for (var i = 0; i < siz.Components.Length; i++)
        {
            var component = siz.Components[i];
            if (component.IsSigned)
                throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The component {i} of the JPEG 2000 image has signed samples."), "signed samples");

            if (component.Precision > 16)
                throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The component {i} of the JPEG 2000 image has samples of {component.Precision} bits; at most 16 are supported."), "sample depth");
        }
    }

    /// <summary>Checks, once the main header is read, that the codestream declares the capabilities it uses and uses none that is not supported.</summary>
    /// <exception cref="InvalidImageContentException">The SIZ and CAP marker segments disagree.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The codestream needs the capabilities of a part that is not supported.</exception>
    public static void EnsureSupportedCapabilities(Jpeg2000Siz siz, Jpeg2000Capabilities? capabilities)
    {
        ArgumentNullException.ThrowIfNull(siz);
        var declaresHighThroughput = (siz.Capabilities & CapabilityHighThroughput) != 0;
        if (declaresHighThroughput != (capabilities is { HighThroughput: >= 0 }))
            throw Jpeg2000Format.Invalid("the SIZ and CAP marker segments disagree on the use of Rec. ITU-T T.814 | ISO/IEC 15444-15.");

        if (capabilities is { HasOtherParts: true })
            throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream needs the capabilities of a part of ISO/IEC 15444 other than Part 1 and Part 15.", "codestream capabilities");
    }

    /// <summary>Checks the code-block style of a tile-component that uses the high-throughput block coder (T.814, Table A.3).</summary>
    /// <returns>The features the style uses.</returns>
    /// <exception cref="InvalidImageContentException">The codestream does not declare the capabilities of Rec. ITU-T T.814.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The style is not supported.</exception>
    public static Jpeg2000Tools EnsureSupportedHighThroughputStyle(int blockStyle, Jpeg2000Capabilities? capabilities)
    {
        if (capabilities is not { HighThroughput: >= 0 })
            throw Jpeg2000Format.Invalid("a code-block style uses the high-throughput block coder and the codestream does not declare the capabilities of Rec. ITU-T T.814 | ISO/IEC 15444-15.");

        // A.4: the bit 7 has a meaning with the bit 6 only
        if ((blockStyle & Jpeg2000CodingStyle.StyleHighThroughput) == 0)
            throw Jpeg2000Format.Invalid("a code-block style has a reserved bit.");

        // Table A.4: a tile-component whose code-blocks choose their block coder one by one
        if ((blockStyle & Jpeg2000CodingStyle.StyleMixed) != 0)
            throw Jpeg2000Format.Unsupported("A tile-component of the JPEG 2000 codestream mixes code-blocks of the high-throughput block coder and of the block coder of Rec. ITU-T T.800 | ISO/IEC 15444-1.", "mixed block coders");

        // Table A.3: every other bit than the one of the vertically causal contexts is reserved
        return Jpeg2000Tools.HighThroughput;
    }

    /// <summary>Resolves the channels of a codestream that is not in a file.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The components are not those of an image the library can hold.</exception>
    public static Jpeg2000ChannelLayout CreateLayout(Jpeg2000Siz siz)
    {
        ArgumentNullException.ThrowIfNull(siz);
        var components = siz.Components;
        var count = components.Length;
        if (count > 4)
            throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream has {count} components and no color specification."), "component count");

        var channels = new Jpeg2000Channel[count];
        for (var i = 0; i < count; i++)
        {
            channels[i] = new Jpeg2000Channel(i, PaletteColumn: -1, components[i].Precision);
        }

        // Three components whose last two are sub-sampled alike are a luminance and two chrominances
        var kind = count <= 2 ? Jpeg2000ColorKind.Gray : Jpeg2000ColorKind.Rgb;
        if (count == 3 && IsSubsampled(components[1]) && components[1].XSubsampling == components[2].XSubsampling && components[1].YSubsampling == components[2].YSubsampling)
        {
            kind = Jpeg2000ColorKind.Sycc;
        }

        var colorCount = kind == Jpeg2000ColorKind.Gray ? 1 : 3;
        Jpeg2000Channel? alpha = count > colorCount ? channels[colorCount] : null;
        var layout = new Jpeg2000ChannelLayout(kind, channels[..colorCount], alpha, palette: null, profile: null);
        EnsureSupportedSampling(siz, layout);
        return layout;
    }

    /// <summary>Resolves the channels of a file from its header boxes.</summary>
    /// <exception cref="InvalidImageContentException">The boxes are inconsistent with each other or with the codestream.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The file describes an image the library cannot hold.</exception>
    public static Jpeg2000ChannelLayout CreateLayout(Jpeg2000Siz siz, Jpeg2000HeaderBoxes boxes)
    {
        ArgumentNullException.ThrowIfNull(siz);
        ArgumentNullException.ThrowIfNull(boxes);
        if (!boxes.HasImageHeader)
            throw Jpeg2000Format.Invalid("the file has no image header box.");

        if (boxes.Width != siz.Width || boxes.Height != siz.Height || boxes.ComponentCount != siz.Components.Length)
            throw Jpeg2000Format.Invalid("the image header box and the SIZ marker segment of the codestream disagree on the size or on the number of components.");

        var kind = SelectColorKind(boxes, out var profile);
        var colorCount = kind switch
        {
            Jpeg2000ColorKind.Gray => 1,
            Jpeg2000ColorKind.Cmyk => 4,
            _ => 3,
        };

        var channels = CreateChannels(siz, boxes);
        var colors = new Jpeg2000Channel[colorCount];
        Jpeg2000Channel? alpha = null;
        if (boxes.Definitions is { } definitions)
        {
            var defined = new bool[channels.Length];
            var colorSeen = new bool[colorCount];
            foreach (var definition in definitions)
            {
                if (definition.Channel >= channels.Length)
                    throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the channel definition box describes the channel {definition.Channel}; the image has {channels.Length} channels."));

                if (defined[definition.Channel])
                    throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the channel definition box describes the channel {definition.Channel} twice."));

                defined[definition.Channel] = true;
                var channel = channels[definition.Channel];
                switch (definition.Type)
                {
                    case Jpeg2000ChannelDefinition.TypeColor:
                        if (definition.Association < 1 || definition.Association > colorCount)
                            throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The channel {definition.Channel} of the JPEG 2000 image is a color channel that is not one of the {colorCount} colors of its color space."), "unassociated color channel");

                        if (colorSeen[definition.Association - 1])
                            throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the channel definition box gives two channels to the color {definition.Association}."));

                        colorSeen[definition.Association - 1] = true;
                        colors[definition.Association - 1] = channel;
                        break;

                    case Jpeg2000ChannelDefinition.TypeOpacity:
                        if (definition.Association != Jpeg2000ChannelDefinition.AssociationImage)
                            throw Jpeg2000Format.Unsupported("The JPEG 2000 image has an opacity channel that applies to one color only.", "opacity of a single color");

                        if (alpha is not null)
                            throw Jpeg2000Format.Unsupported("The JPEG 2000 image has two opacity channels.", "several opacity channels");

                        alpha = channel;
                        break;

                    case Jpeg2000ChannelDefinition.TypePremultipliedOpacity:
                        throw Jpeg2000Format.Unsupported("The JPEG 2000 image has premultiplied opacity.", "premultiplied alpha");

                    default:
                        throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The channel {definition.Channel} of the JPEG 2000 image is neither a color nor an opacity."), "channel type");
                }
            }

            if (colorSeen.AsSpan().Contains(false))
                throw Jpeg2000Format.Invalid("the channel definition box does not give a channel to every color.");

            if (defined.AsSpan().Contains(false))
                throw Jpeg2000Format.Unsupported("The JPEG 2000 image has a channel that its channel definition box does not describe.", "undefined channel");
        }
        else
        {
            // Without definitions, the channels are the colors, in order. One more channel is the opacity of the image, as in
            // a codestream that is not in a file: writers store images with an opacity that way.
            if (channels.Length < colorCount)
                throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the image has {channels.Length} channels; its color space has {colorCount} colors."));

            if (channels.Length > colorCount + 1)
                throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 image has {channels.Length} channels for {colorCount} colors and no channel definition box."), "undefined channel");

            channels.AsSpan(0, colorCount).CopyTo(colors);
            if (channels.Length > colorCount)
            {
                alpha = channels[colorCount];
            }
        }

        var layout = new Jpeg2000ChannelLayout(kind, colors, alpha, boxes.Palette, profile);
        EnsureSupportedSampling(siz, layout);
        return layout;
    }

    private static bool IsSubsampled(Jpeg2000ComponentSize component) => component.XSubsampling != 1 || component.YSubsampling != 1;

    private static Jpeg2000ColorKind SelectColorKind(Jpeg2000HeaderBoxes boxes, out byte[]? profile)
    {
        profile = null;
        if (boxes.Colors.Count == 0)
            throw Jpeg2000Format.Invalid("the file has no color specification box.");

        // The first color specification the library can interpret gives the colors of the image
        Jpeg2000ColorKind? selected = null;
        foreach (var color in boxes.Colors)
        {
            if (GetColorKind(color) is { } kind)
            {
                selected = kind;
                break;
            }
        }

        if (selected is not { } result)
        {
            var first = boxes.Colors[0];
            throw first.Method switch
            {
                Jpeg2000ColorSpecification.MethodEnumerated => Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 image is in the enumerated color space {first.EnumeratedColorSpace}; sRGB (16), grayscale (17), sYCC (18) and CMYK (12) are supported."), string.Create(CultureInfo.InvariantCulture, $"enumerated color space {first.EnumeratedColorSpace}")),
                Jpeg2000ColorSpecification.MethodRestrictedIcc or Jpeg2000ColorSpecification.MethodAnyIcc => Jpeg2000Format.Unsupported("The JPEG 2000 image has an ICC profile that is neither a grayscale, an RGB nor a CMYK profile.", "color space of the ICC profile"),
                _ => Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 image specifies its colors with the method {first.Method}."), string.Create(CultureInfo.InvariantCulture, $"color specification method {first.Method}")),
            };
        }

        // The profile is the first one that describes these colors; the samples of sYCC are converted and need none
        if (result != Jpeg2000ColorKind.Sycc)
        {
            foreach (var color in boxes.Colors)
            {
                if (color.Profile is not null && GetColorKind(color) == result)
                {
                    profile = color.Profile;
                    break;
                }
            }
        }

        return result;
    }

    private static Jpeg2000ColorKind? GetColorKind(Jpeg2000ColorSpecification color)
    {
        if (color.Method == Jpeg2000ColorSpecification.MethodEnumerated)
        {
            return color.EnumeratedColorSpace switch
            {
                Jpeg2000ColorSpecification.EnumeratedSrgb => Jpeg2000ColorKind.Rgb,
                Jpeg2000ColorSpecification.EnumeratedGray => Jpeg2000ColorKind.Gray,
                Jpeg2000ColorSpecification.EnumeratedSycc => Jpeg2000ColorKind.Sycc,
                Jpeg2000ColorSpecification.EnumeratedCmyk => Jpeg2000ColorKind.Cmyk,
                _ => null,
            };
        }

        // The data color space of an ICC profile is the fifth field of its header (ICC.1:2022, 7.2.8)
        if (color.Profile is not { Length: >= 20 } profile)
            return null;

        return BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(16)) switch
        {
            IccGray => Jpeg2000ColorKind.Gray,
            IccRgb => Jpeg2000ColorKind.Rgb,
            IccCmyk => Jpeg2000ColorKind.Cmyk,
            _ => null,
        };
    }

    private static Jpeg2000Channel[] CreateChannels(Jpeg2000Siz siz, Jpeg2000HeaderBoxes boxes)
    {
        var components = siz.Components;
        var palette = boxes.Palette;
        if (boxes.Mappings is not { } mappings)
        {
            if (palette is not null)
                throw Jpeg2000Format.Invalid("the file has a palette box and no component mapping box.");

            var direct = new Jpeg2000Channel[components.Length];
            for (var i = 0; i < direct.Length; i++)
            {
                direct[i] = new Jpeg2000Channel(i, PaletteColumn: -1, components[i].Precision);
            }

            return direct;
        }

        if (palette is null)
            throw Jpeg2000Format.Invalid("the file has a component mapping box and no palette box.");

        if (mappings.Length > Jpeg2000Format.MaxSupportedComponents)
            throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 image has {mappings.Length} channels; at most {Jpeg2000Format.MaxSupportedComponents} are supported."), "channel count");

        var channels = new Jpeg2000Channel[mappings.Length];
        for (var i = 0; i < mappings.Length; i++)
        {
            var mapping = mappings[i];
            if (mapping.Component >= components.Length)
                throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the component mapping box uses the component {mapping.Component}; the image has {components.Length}."));

            if (!mapping.UsesPalette)
            {
                channels[i] = new Jpeg2000Channel(mapping.Component, PaletteColumn: -1, components[mapping.Component].Precision);
                continue;
            }

            if (mapping.PaletteColumn >= palette.ColumnCount)
                throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the component mapping box uses the column {mapping.PaletteColumn} of the palette; the palette has {palette.ColumnCount}."));

            if (palette.IsColumnSigned(mapping.PaletteColumn))
                throw Jpeg2000Format.Unsupported("The palette of the JPEG 2000 image has signed values.", "signed samples");

            var precision = palette.GetColumnPrecision(mapping.PaletteColumn);
            if (precision > 16)
                throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The palette of the JPEG 2000 image has values of {precision} bits; at most 16 are supported."), "sample depth");

            channels[i] = new Jpeg2000Channel(mapping.Component, mapping.PaletteColumn, precision);
        }

        return channels;
    }

    private static void EnsureSupportedSampling(Jpeg2000Siz siz, Jpeg2000ChannelLayout layout)
    {
        var components = siz.Components;
        for (var i = 0; i < layout.Colors.Length; i++)
        {
            var isChrominance = layout.Kind == Jpeg2000ColorKind.Sycc && i > 0;
            if (!isChrominance && IsSubsampled(components[layout.Colors[i].Component]))
                throw Jpeg2000Format.Unsupported("The JPEG 2000 image has a sub-sampled component that is not a chrominance.", "sub-sampled component");
        }

        if (layout.Alpha is { } alpha && IsSubsampled(components[alpha.Component]))
            throw Jpeg2000Format.Unsupported("The JPEG 2000 image has a sub-sampled opacity channel.", "sub-sampled component");

        if (layout.Kind is Jpeg2000ColorKind.Sycc or Jpeg2000ColorKind.Cmyk)
        {
            // The colors of these images are computed from several channels: they have the same depth
            foreach (var channel in layout.Colors)
            {
                if (channel.Precision != layout.Colors[0].Precision)
                    throw Jpeg2000Format.Unsupported("The JPEG 2000 image has color channels of different depths that are converted together.", "channel depths");
            }
        }
    }
}
