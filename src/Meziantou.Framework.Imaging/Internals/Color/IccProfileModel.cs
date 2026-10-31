using System.Diagnostics;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color model of one validated ICC profile: which of its tags convert device values to the profile connection
/// space and back for a rendering intent, and the stages they translate to. Both directions meet in CIEXYZ relative to
/// the D50 illuminant (Y = 1 for the media white), whatever the connection space of the profile.
/// </summary>
/// <remarks>
/// Written from ICC.1:2001-04 (version 2 profiles) and ICC.1:2022 (version 4 profiles). Supported: input, display, output
/// and color space profiles whose data color space is gray, RGB or CMYK, with a CIEXYZ or CIELAB connection space.
/// </remarks>
internal sealed class IccProfileModel
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly string _role;

    private IccProfileModel(ReadOnlyMemory<byte> data, IccHeader header, string role, int channelCount, int outputChannelCount)
    {
        _data = data;
        _role = role;
        Header = header;
        ChannelCount = channelCount;
        OutputChannelCount = outputChannelCount;
        IsLabConnectionSpace = header.ConnectionSpace == IccReader.SignatureLab;
    }

    /// <summary>Gets the header of the profile.</summary>
    public IccHeader Header { get; }

    /// <summary>
    /// Gets the number of channels of the data color space: 1 (gray), 3 (RGB) or 4 (CMYK) for a device or device link
    /// profile, 3 for an abstract profile (CIEXYZ or CIELAB).
    /// </summary>
    public int ChannelCount { get; }

    /// <summary>
    /// Gets the number of channels of the output of a device link profile (its output color space, stored in the field of
    /// the connection space); the number of channels of the connection space (3) for the other profiles.
    /// </summary>
    public int OutputChannelCount { get; }

    /// <summary>Gets a value indicating whether the profile is a device link profile (ICC.1:2022 section 8.6).</summary>
    public bool IsDeviceLink => Header.ProfileClass == IccReader.ClassDeviceLink;

    /// <summary>Gets a value indicating whether the profile is an abstract profile (ICC.1:2022 section 8.7).</summary>
    public bool IsAbstract => Header.ProfileClass == IccReader.ClassAbstract;

    /// <summary>
    /// Gets the color space of the device values a profile reads: the data color space of a device or device link
    /// profile. The connection space for an abstract profile.
    /// </summary>
    public uint InputColorSpace => Header.DataColorSpace;

    /// <summary>Gets the color space of the device values of a device link profile output: its field of the connection space.</summary>
    public uint OutputColorSpace => Header.ConnectionSpace;

    /// <summary>Gets a value indicating whether the connection space of the profile is CIELAB instead of CIEXYZ.</summary>
    public bool IsLabConnectionSpace { get; }

    /// <summary>Validates a profile and creates its model.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="role">The role of the profile in the conversion ("source" or "destination"), for exception messages.</param>
    /// <param name="allowLinkAndAbstract">
    /// Whether device link and abstract profiles are accepted: they only have a meaning in a sequence of profiles
    /// (<see cref="IccPipeline.Create(IReadOnlyList{IccProfile}, IccRenderingIntent, bool, GamutMapping)"/>).
    /// </param>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile is valid but of a kind that is not supported.</exception>
    public static IccProfileModel Create(IccProfile profile, string role, bool allowLinkAndAbstract = false)
    {
        var data = profile.Data.Memory;
        if (!MetadataValidation.TryValidateIccProfile(data.Span, out var error))
            throw new InvalidImageContentException($"The {role} ICC profile is invalid: {error}", ImageFormat.Unknown);

        _ = IccHeader.TryRead(data.Span, out var header);
        if (header.Version.Major > 4)
            throw CreateUnsupported(role, string.Create(CultureInfo.InvariantCulture, $"has version {header.Version.Major}.{header.Version.Minor} (iccMAX); only versions 2 and 4 are supported."), "ICC profile version 5");

        switch (header.ProfileClass)
        {
            case IccReader.ClassInput or IccReader.ClassDisplay or IccReader.ClassOutput or IccReader.ClassColorSpace:
                break;

            case IccReader.ClassDeviceLink or IccReader.ClassAbstract when allowLinkAndAbstract:
                return CreateLinkOrAbstract(data, header, role);

            case IccReader.ClassDeviceLink or IccReader.ClassAbstract:
                throw CreateUnsupported(role, $"has the class {IccReader.FormatSignature(header.ProfileClass)}, which does not describe the colors of a device on its own; use it in a sequence of profiles (IccColorTransform.Create with a list of profiles).", "ICC profile class");

            default:
                throw CreateUnsupported(role, $"has the class {IccReader.FormatSignature(header.ProfileClass)}; only input, display, output and color space profiles are supported{(allowLinkAndAbstract ? ", and device link and abstract profiles in a sequence of profiles" : "")}.", "ICC profile class");
        }

        var channelCount = GetDeviceChannelCount(header.DataColorSpace) ?? throw CreateUnsupported(role, $"has the data color space {IccReader.FormatSignature(header.DataColorSpace)}; only gray, RGB and CMYK are supported.", "ICC profile data color space");
        if (header.ConnectionSpace is not (IccReader.SignatureXyz or IccReader.SignatureLab))
            throw new InvalidImageContentException($"The {role} ICC profile is invalid: the profile connection space {IccReader.FormatSignature(header.ConnectionSpace)} is neither 'XYZ ' nor 'Lab '.", ImageFormat.Unknown);

        return new IccProfileModel(data, header, role, channelCount, outputChannelCount: 3);
    }

    // ICC.1:2022 section 8.6: the data color space of a device link profile is its input, the field of the connection space
    // its output, both device color spaces. Section 8.7: both color spaces of an abstract profile are connection spaces.
    private static IccProfileModel CreateLinkOrAbstract(ReadOnlyMemory<byte> data, IccHeader header, string role)
    {
        if (header.ProfileClass == IccReader.ClassAbstract)
        {
            if (header.DataColorSpace is not (IccReader.SignatureXyz or IccReader.SignatureLab) || header.ConnectionSpace is not (IccReader.SignatureXyz or IccReader.SignatureLab))
                throw new InvalidImageContentException($"The {role} ICC profile is invalid: an abstract profile converts between connection spaces, but its color spaces are {IccReader.FormatSignature(header.DataColorSpace)} and {IccReader.FormatSignature(header.ConnectionSpace)} instead of 'XYZ ' or 'Lab '.", ImageFormat.Unknown);

            return new IccProfileModel(data, header, role, channelCount: 3, outputChannelCount: 3);
        }

        var inputs = GetDeviceChannelCount(header.DataColorSpace) ?? throw CreateUnsupported(role, $"is a device link profile whose input color space is {IccReader.FormatSignature(header.DataColorSpace)}; only gray, RGB and CMYK are supported.", "ICC profile data color space");
        var outputs = GetDeviceChannelCount(header.ConnectionSpace) ?? throw CreateUnsupported(role, $"is a device link profile whose output color space is {IccReader.FormatSignature(header.ConnectionSpace)}; only gray, RGB and CMYK are supported.", "ICC profile data color space");
        return new IccProfileModel(data, header, role, inputs, outputs);
    }

    private static int? GetDeviceChannelCount(uint colorSpace) => colorSpace switch
    {
        IccReader.SignatureGray => 1,
        IccReader.SignatureRgb => 3,
        IccReader.SignatureCmyk => 4,
        _ => null,
    };

    /// <summary>Gets a value indicating whether the profile is a CMYK output (printer) profile.</summary>
    public bool IsCmykOutputProfile => ChannelCount == 4 && Header.ProfileClass == IccReader.ClassOutput;

    /// <summary>
    /// Determines whether the conversion of an intent uses a lookup table of the profile, as opposed to its matrix and
    /// tone curves.
    /// </summary>
    public bool UsesLookupTable(bool deviceToConnection, IccRenderingIntent intent)
        => TryFindLookupTable(deviceToConnection, intent, out _, out _);

    /// <summary>
    /// Determines whether the profile has the tags of a conversion (a lookup table, or the tone curves of a matrix-based
    /// or monochrome profile). The tags are not validated.
    /// </summary>
    public bool HasConversion(bool deviceToConnection, IccRenderingIntent intent)
    {
        if (UsesLookupTable(deviceToConnection, intent))
            return true;

        var data = _data.Span;
        return ChannelCount switch
        {
            1 => IccReader.TryGetTag(data, IccReader.TagGrayCurve, out _),
            3 => IccReader.TryGetTag(data, IccReader.TagRedCurve, out _) && IccReader.TryGetTag(data, IccReader.TagRedColorant, out _),
            _ => false,
        };
    }

    /// <summary>
    /// Gets the media white point used by the ICC-absolute colorimetric intent (ICC.1:2022 section 6.3.2): the
    /// <c>mediaWhitePointTag</c>, or the D50 illuminant of the connection space when the profile has none. A display is
    /// assumed to be viewed fully adapted, so its media white is the illuminant whatever the tag says (version 4 requires
    /// it; version 2 display profiles often store the unadapted white of the display).
    /// </summary>
    /// <exception cref="InvalidImageContentException">The tag is malformed or is not a positive color.</exception>
    public (double X, double Y, double Z) GetMediaWhitePoint()
    {
        if (Header.ProfileClass == IccReader.ClassDisplay || !IccReader.TryGetTag(_data.Span, IccReader.TagMediaWhitePoint, out _))
            return (IccColorimetry.D50X, IccColorimetry.D50Y, IccColorimetry.D50Z);

        var white = ReadXyz(IccReader.TagMediaWhitePoint);
        if (!(white.X > 0 && white.Y > 0 && white.Z > 0))
            throw Invalid("the media white point ('wtpt') is not a positive color.");

        return white;
    }

    /// <summary>Creates the conversion of normalized device values to CIELAB.</summary>
    public IccStage[] CreateDeviceToLab(IccRenderingIntent intent)
    {
        var stages = new IccStageList();
        AppendToConnectionSpace(stages, intent);
        stages.Add(IccXyzToLabStage.Instance);
        return stages.ToArray();
    }

    /// <summary>Creates the conversion of CIELAB to normalized device values.</summary>
    public IccStage[] CreateLabToDevice(IccRenderingIntent intent)
    {
        var stages = new IccStageList();
        stages.Add(IccLabToXyzStage.Instance);
        AppendFromConnectionSpace(stages, intent);
        return stages.ToArray();
    }

    /// <summary>Appends the stages converting normalized device values to CIEXYZ of the connection space.</summary>
    /// <param name="stages">The stages of the conversion.</param>
    /// <param name="intent">The rendering intent selecting the tag (ICC.1:2022 section 8.10.2).</param>
    public void AppendToConnectionSpace(IccStageList stages, IccRenderingIntent intent)
    {
        if (TryFindLookupTable(deviceToConnection: true, intent, out var tag, out var signature))
        {
            if (IccLutParser.Append(tag, ChannelCount, 3, deviceToConnection: true, !IsLabConnectionSpace, stages) is { } error)
                throw Invalid($"in the {IccReader.FormatSignature(signature)} tag, {error}");

            AppendDecoding(stages, IccReader.ReadUInt32(tag.Span), signature);
            return;
        }

        if (ChannelCount == 1)
        {
            // Monochrome profile (ICC.1:2022 annex F.2): the curve gives the fraction of the media white, as Y of a
            // neutral color in CIEXYZ or as L* / 100 in CIELAB
            stages.Add(new IccCurvesStage([ReadCurve(IccReader.TagGrayCurve)]));
            if (IsLabConnectionSpace)
            {
                stages.Add(new IccGrayExpandStage(100, 0, 0));
                stages.Add(IccLabToXyzStage.Instance);
            }
            else
            {
                stages.Add(new IccGrayExpandStage(IccColorimetry.D50X, IccColorimetry.D50Y, IccColorimetry.D50Z));
            }

            return;
        }

        if (ChannelCount == 3)
        {
            // Three-component matrix-based profile (annex F.3): linearize each channel, then the colorants give CIEXYZ
            Span<double> matrix = stackalloc double[9];
            ReadColorantMatrix(matrix);
            stages.Add(new IccCurvesStage(ReadRgbCurves()));
            stages.Add(new IccMatrixStage(matrix));
            return;
        }

        throw Invalid("the profile has no lookup table ('A2B0') to convert its device values to the profile connection space.");
    }

    /// <summary>
    /// Creates the stage that maps the colors of the connection space into the gamut of the profile as a destination,
    /// before <see cref="AppendFromConnectionSpace"/>; <see langword="null"/> when nothing is mapped (see
    /// <see cref="GamutMapping.ReduceChroma"/>).
    /// </summary>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile has lookup tables from the connection space but none to it, so its gamut is unknown.</exception>
    public IccStage? CreateGamutMapping(IccRenderingIntent intent, GamutMapping mapping)
    {
        if (mapping == GamutMapping.Clip || ChannelCount == 1)
            return null;

        if (UsesLookupTable(deviceToConnection: false, intent))
        {
            // The perceptual and saturation tables map the gamut themselves
            if (intent is not (IccRenderingIntent.RelativeColorimetric or IccRenderingIntent.AbsoluteColorimetric))
                return null;

            if (!UsesLookupTable(deviceToConnection: true, IccRenderingIntent.RelativeColorimetric))
                throw Unsupported("has no lookup table to the profile connection space ('A2B1' or 'A2B0'), so its gamut is unknown and colors cannot be mapped into it.", "Gamut mapping to an ICC profile without a conversion to the profile connection space");

            var toDevice = new IccStageList();
            AppendFromConnectionSpace(toDevice, IccRenderingIntent.RelativeColorimetric);
            var toConnectionSpace = new IccStageList();
            AppendToConnectionSpace(toConnectionSpace, IccRenderingIntent.RelativeColorimetric);
            return new IccRoundTripGamutMapStage(toDevice.ToArray(), toConnectionSpace.ToArray(), ChannelCount);
        }

        // A CMYK profile without tables cannot be a destination: AppendFromConnectionSpace reports it
        if (ChannelCount != 3)
            return null;

        Span<double> matrix = stackalloc double[9];
        ReadColorantMatrix(matrix);
        var curves = ReadRgbCurves();
        Span<double> low = stackalloc double[3];
        Span<double> high = stackalloc double[3];
        for (var i = 0; i < 3; i++)
        {
            var first = curves[i].Evaluate(0);
            var last = curves[i].Evaluate(1);
            low[i] = Math.Min(first, last);
            high[i] = Math.Max(first, last);
        }

        return IccMatrixGamutMapStage.Create(matrix, low, high);
    }

    /// <summary>
    /// Appends the stages of a device link profile: its <c>AToB0</c> tag converts the normalized device values of its input
    /// color space to those of its output color space (ICC.1:2022 section 8.6), whatever the rendering intent, which was
    /// chosen when the link was made.
    /// </summary>
    public void AppendDeviceLink(IccStageList stages)
    {
        Debug.Assert(IsDeviceLink);
        if (!IccReader.TryFindTag(_data.Span, IccReader.TagAToB0, out var offset, out var length))
            throw Invalid("the device link profile has no 'A2B0' tag.");

        // The device values on both sides are not encoded: the tag is read as the table of a device profile
        if (IccLutParser.Append(_data.Slice(offset, length), ChannelCount, OutputChannelCount, deviceToConnection: true, connectionIsXyz: false, stages) is { } error)
            throw Invalid($"in the 'A2B0' tag, {error}");
    }

    /// <summary>
    /// Appends the stages of an abstract profile: CIEXYZ of the connection space encoded as the input of its <c>AToB0</c>
    /// tag (its data color space), the tag, then its output decoded from the connection space of the profile to CIEXYZ
    /// (ICC.1:2022 section 8.7).
    /// </summary>
    public void AppendAbstract(IccStageList stages)
    {
        Debug.Assert(IsAbstract);
        if (!IccReader.TryFindTag(_data.Span, IccReader.TagAToB0, out var offset, out var length))
            throw Invalid("the abstract profile has no 'A2B0' tag.");

        if (length < 4)
            throw Invalid("the 'A2B0' tag is too short to have a type.");

        var tag = _data.Slice(offset, length);
        var type = IccReader.ReadUInt32(tag.Span);
        var inputIsLab = Header.DataColorSpace == IccReader.SignatureLab;
        AppendEncoding(stages, type, IccReader.TagAToB0, inputIsLab);
        if (IccLutParser.AppendConnectionSpaceTable(tag, inputIsXyz: !inputIsLab, stages) is { } error)
            throw Invalid($"in the 'A2B0' tag, {error}");

        AppendDecoding(stages, type, IccReader.TagAToB0);
    }

    /// <summary>
    /// Gets the tone curves that relate the channels of the profile to linear light: the three curves of a
    /// matrix-based profile (red, green, blue; annex F.3), or the curve of a monochrome profile (annex F.2).
    /// </summary>
    /// <exception cref="InvalidImageContentException">A curve is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile has no tone curve per channel: it describes its colors with lookup tables only.</exception>
    public IccCurve[] GetToneCurves()
    {
        if (ChannelCount == 1 && IccReader.TryGetTag(_data.Span, IccReader.TagGrayCurve, out _))
            return [ReadCurve(IccReader.TagGrayCurve)];

        if (ChannelCount == 3 && IccReader.TryGetTag(_data.Span, IccReader.TagRedCurve, out _) && IccReader.TryGetTag(_data.Span, IccReader.TagGreenCurve, out _) && IccReader.TryGetTag(_data.Span, IccReader.TagBlueCurve, out _))
            return ReadRgbCurves();

        throw Unsupported("has no tone curve for each of its channels (it describes its colors with lookup tables only), so its samples have no linear light to be combined in.", "Linear light of an ICC profile without tone curves");
    }

    /// <summary>Appends the stages converting CIEXYZ of the connection space to normalized device values.</summary>
    /// <param name="stages">The stages of the conversion.</param>
    /// <param name="intent">The rendering intent selecting the tag (ICC.1:2022 section 8.10.2).</param>
    public void AppendFromConnectionSpace(IccStageList stages, IccRenderingIntent intent)
    {
        if (TryFindLookupTable(deviceToConnection: false, intent, out var tag, out var signature))
        {
            AppendEncoding(stages, IccReader.ReadUInt32(tag.Span), signature);
            if (IccLutParser.Append(tag, 3, ChannelCount, deviceToConnection: false, !IsLabConnectionSpace, stages) is { } error)
                throw Invalid($"in the {IccReader.FormatSignature(signature)} tag, {error}");

            return;
        }

        if (ChannelCount == 1)
        {
            var curve = ReadCurve(IccReader.TagGrayCurve);
            if (IsLabConnectionSpace)
            {
                stages.Add(IccXyzToLabStage.Instance);
                stages.Add(new IccGrayReduceStage(index: 0, scale: 1.0 / 100));
            }
            else
            {
                stages.Add(new IccGrayReduceStage(index: 1, scale: 1));
            }

            stages.Add(new IccInverseCurvesStage([Invert(curve, IccReader.TagGrayCurve)]));
            return;
        }

        if (ChannelCount == 3)
        {
            Span<double> matrix = stackalloc double[9];
            Span<double> inverse = stackalloc double[9];
            ReadColorantMatrix(matrix);
            if (!IccMatrixStage.TryInvert(matrix, inverse))
                throw Unsupported("has colorants that do not span a color space, so it cannot be a destination.", "ICC profile with a singular colorant matrix");

            // A curve read once is inverted once: the channels of the same curve share the inverse (and its tables)
            var curves = ReadRgbCurves();
            var red = Invert(curves[0], IccReader.TagRedCurve);
            var green = ReferenceEquals(curves[1], curves[0]) ? red : Invert(curves[1], IccReader.TagGreenCurve);
            var blue = ReferenceEquals(curves[2], curves[0]) ? red : ReferenceEquals(curves[2], curves[1]) ? green : Invert(curves[2], IccReader.TagBlueCurve);
            stages.Add(new IccMatrixStage(inverse));
            stages.Add(new IccInverseCurvesStage([red, green, blue]));
            return;
        }

        throw Unsupported("has no lookup table ('B2A0') from the profile connection space, so it cannot be a destination.", "ICC profile without a conversion from the profile connection space");
    }

    /// <summary>
    /// Finds the lookup table of a rendering intent (ICC.1:2022 section 8.10.2): the tag of the intent when present, else
    /// the tag of the perceptual intent (AToB0 or BToA0). The ICC-absolute colorimetric intent uses the tag of the
    /// media-relative colorimetric intent. Without any of them, the profile is used through its matrix and tone curves.
    /// </summary>
    private bool TryFindLookupTable(bool deviceToConnection, IccRenderingIntent intent, out ReadOnlyMemory<byte> tag, out uint signature)
    {
        signature = (intent, deviceToConnection) switch
        {
            (IccRenderingIntent.Perceptual, true) => IccReader.TagAToB0,
            (IccRenderingIntent.Perceptual, false) => IccReader.TagBToA0,
            (IccRenderingIntent.Saturation, true) => IccReader.TagAToB2,
            (IccRenderingIntent.Saturation, false) => IccReader.TagBToA2,
            (_, true) => IccReader.TagAToB1,
            (_, false) => IccReader.TagBToA1,
        };

        var data = _data.Span;
        if (!IccReader.TryFindTag(data, signature, out var offset, out var length))
        {
            signature = deviceToConnection ? IccReader.TagAToB0 : IccReader.TagBToA0;
            if (!IccReader.TryFindTag(data, signature, out offset, out length))
            {
                tag = default;
                return false;
            }
        }

        // The type signature of the tag selects the encoding of the connection space before the tag is parsed
        if (length < 4)
            throw Invalid($"the {IccReader.FormatSignature(signature)} tag is too short to have a type.");

        tag = _data.Slice(offset, length);
        return true;
    }

    /// <summary>
    /// Appends the conversion of the normalized output of a lookup table to CIEXYZ (ICC.1:2022 section 6.3.4): CIEXYZ is
    /// encoded as u1Fixed15 (1.0 is 32768 / 65535); CIELAB as L* / 100 and (a* + 128) / 255, except in a <c>lut16Type</c>,
    /// which keeps the encoding of version 2 where 100 and 255 correspond to 65280 / 65535 (ICC.1:2001-04 annex A).
    /// </summary>
    private void AppendDecoding(IccStageList stages, uint type, uint signature)
    {
        if (!IsLabConnectionSpace)
        {
            EnsureXyzEncoding(type, signature);
            stages.Add(IccMatrixStage.CreateScale(XyzEncodingScale, XyzEncodingScale, XyzEncodingScale));
            return;
        }

        var (lightness, chroma) = GetLabEncodingScales(type);
        stages.Add(IccMatrixStage.CreateScale(lightness, chroma, chroma, 0, -128, -128));
        stages.Add(IccLabToXyzStage.Instance);
    }

    /// <summary>Appends the conversion of CIEXYZ to the normalized input of a lookup table (see <see cref="AppendDecoding"/>).</summary>
    private void AppendEncoding(IccStageList stages, uint type, uint signature) => AppendEncoding(stages, type, signature, IsLabConnectionSpace);

    /// <summary>Appends the conversion of CIEXYZ to the normalized input of a lookup table whose input is CIELAB or CIEXYZ.</summary>
    private void AppendEncoding(IccStageList stages, uint type, uint signature, bool isLab)
    {
        if (!isLab)
        {
            EnsureXyzEncoding(type, signature);
            stages.Add(IccMatrixStage.CreateScale(1 / XyzEncodingScale, 1 / XyzEncodingScale, 1 / XyzEncodingScale));
            return;
        }

        var (lightness, chroma) = GetLabEncodingScales(type);
        stages.Add(IccXyzToLabStage.Instance);
        stages.Add(IccMatrixStage.CreateScale(1 / lightness, 1 / chroma, 1 / chroma, 0, 128 / chroma, 128 / chroma));
    }

    /// <summary>The value of the largest normalized CIEXYZ component: 65535 / 32768.</summary>
    private const double XyzEncodingScale = 65535.0 / 32768;

    private static (double Lightness, double Chroma) GetLabEncodingScales(uint type)
        => type == IccReader.TypeLut16 ? (100 * 65535.0 / 65280, 255 * 65535.0 / 65280) : (100, 255);

    private void EnsureXyzEncoding(uint type, uint signature)
    {
        // There is no 8-bit encoding of CIEXYZ (ICC.1:2022 section 6.3.4.1)
        if (type == IccReader.TypeLut8)
            throw Invalid($"the {IccReader.FormatSignature(signature)} tag is a lut8Type, which cannot be used with the 'XYZ ' profile connection space.");
    }

    /// <summary>Reads the colorant tags of a matrix-based profile as the columns of the matrix converting linear RGB to CIEXYZ.</summary>
    private void ReadColorantMatrix(Span<double> matrix)
    {
        if (IsLabConnectionSpace)
            throw Invalid("a matrix-based RGB profile must use the 'XYZ ' profile connection space.");

        ReadOnlySpan<uint> signatures = [IccReader.TagRedColorant, IccReader.TagGreenColorant, IccReader.TagBlueColorant];
        for (var column = 0; column < 3; column++)
        {
            var (x, y, z) = ReadXyz(signatures[column]);
            matrix[column] = x;
            matrix[3 + column] = y;
            matrix[6 + column] = z;
        }
    }

    private (double X, double Y, double Z) ReadXyz(uint signature)
    {
        if (!IccReader.TryGetTag(_data.Span, signature, out var tag))
            throw Invalid($"the {IccReader.FormatSignature(signature)} tag is missing.");

        if (tag.Length < 20 || IccReader.ReadUInt32(tag) != IccReader.TypeXyz)
            throw Invalid($"the {IccReader.FormatSignature(signature)} tag is not an XYZType with one value.");

        return (IccReader.ReadS15Fixed16(tag[8..]), IccReader.ReadS15Fixed16(tag[12..]), IccReader.ReadS15Fixed16(tag[16..]));
    }

    /// <summary>
    /// Reads the red, green and blue tone curves. The three tags of most profiles are one curve: a tag whose bytes are
    /// those of a previous one gives the same instance, so that its tables are computed once.
    /// </summary>
    private IccCurve[] ReadRgbCurves()
    {
        var red = ReadCurve(IccReader.TagRedCurve);
        IccReader.TryGetTag(_data.Span, IccReader.TagRedCurve, out var redTag);
        var green = IccReader.TryGetTag(_data.Span, IccReader.TagGreenCurve, out var greenTag) && redTag.SequenceEqual(greenTag) ? red : ReadCurve(IccReader.TagGreenCurve);
        var blue = IccReader.TryGetTag(_data.Span, IccReader.TagBlueCurve, out var blueTag) && redTag.SequenceEqual(blueTag) ? red
            : greenTag.SequenceEqual(blueTag) ? green
            : ReadCurve(IccReader.TagBlueCurve);
        return [red, green, blue];
    }

    private IccCurve ReadCurve(uint signature)
    {
        if (!IccReader.TryGetTag(_data.Span, signature, out var tag))
            throw Invalid($"the {IccReader.FormatSignature(signature)} tag is missing.");

        if (!IccCurve.TryParse(tag, out var curve, out _))
            throw Invalid($"the {IccReader.FormatSignature(signature)} tag is not a valid curveType or parametricCurveType.");

        return curve;
    }

    private IccInverseCurve Invert(IccCurve curve, uint signature)
    {
        if (!curve.TryCreateInverse(out var inverse))
            throw Unsupported($"has a constant {IccReader.FormatSignature(signature)} curve, so it cannot be a destination.", "ICC profile with a constant tone curve");

        return inverse;
    }

    private InvalidImageContentException Invalid(string message)
        => new($"The {_role} ICC profile is invalid: {message}", ImageFormat.Unknown);

    private UnsupportedImageFeatureException Unsupported(string message, string feature) => CreateUnsupported(_role, message, feature);

    private static UnsupportedImageFeatureException CreateUnsupported(string role, string message, string feature)
        => new($"The {role} ICC profile {message}", ImageFormat.Unknown, feature);
}
