using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Applies a gain map to a base image (ISO 21496-1, section 5, and the Adobe "Gain map" specification, which Ultra HDR
/// follows): <c>alternate = (base + baseOffset) * exp2(lerp(min, max, pow(g, 1 / gamma)) * weight) - alternateOffset</c>
/// per channel, in linear light.
/// </summary>
internal static class GainMapRenderer
{
    // The CICP primaries that an ICC profile of a base image is compared with: sRGB (BT.709), Display P3, DCI-P3, BT.2020
    private static ReadOnlySpan<byte> RecognizedPrimaries => [1, 12, 11, 9];

    private const byte LinearTransfer = 8;

    public static Image<RgbaFloat> Render(Image baseImage, Image gainMap, GainMapMetadata metadata, double weight, byte? alternatePrimaries, CancellationToken cancellationToken)
    {
        var baseFrame = baseImage.Frames[0];
        var gainFrame = gainMap.Frames[0];
        var linearizer = BaseLinearizer.Create(baseImage.Metadata, PixelFormats.IsGrayscale(baseImage.PixelFormat));

        // The gain applies in the color space of the base image, or in the one of the alternate rendition
        var primaries = linearizer.Primaries;
        double[]? toAlternate = null;
        if (!metadata.UseBaseColorSpace)
        {
            if (alternatePrimaries is not { } alternate)
                throw new UnsupportedImageFeatureException("The gain map applies in the color space of the alternate rendition, which is not known.", ImageFormat.Unknown, "Gain map in the color space of the alternate rendition");

            if (alternate != primaries)
            {
                toAlternate = GetConversionMatrix(primaries, alternate);
                primaries = alternate;
            }
        }

        var channels = metadata.Channels;
        Span<double> minimums = stackalloc double[3];
        Span<double> ranges = stackalloc double[3];
        Span<double> inverseGammas = stackalloc double[3];
        Span<double> baseOffsets = stackalloc double[3];
        Span<double> alternateOffsets = stackalloc double[3];
        for (var c = 0; c < 3; c++)
        {
            var channel = channels[channels.Count is 3 ? c : 0];
            minimums[c] = channel.GainMapMin;
            ranges[c] = channel.GainMapMax - channel.GainMapMin;
            inverseGammas[c] = 1 / channel.Gamma;
            baseOffsets[c] = channel.BaseOffset;
            alternateOffsets[c] = channel.AlternateOffset;
        }

        var width = baseImage.Width;
        var height = baseImage.Height;
        var gainWidth = gainMap.Width;
        var gainHeight = gainMap.Height;
        var result = new Image<RgbaFloat>(width, height, default, baseImage.Configuration);
        try
        {
            var baseStorage = baseFrame.GetStorage();
            var gainStorage = gainFrame.GetStorage();
            var resultStorage = result.Frames[0].GetStorage();
            using var baseLease = baseStorage.AcquireLease();
            using var gainLease = gainStorage.AcquireLease();
            using var resultLease = resultStorage.AcquireLease();

            var baseRow = new Rgba64[width];
            var gainRows = new[] { new Rgba64[gainWidth], new Rgba64[gainWidth] };
            var gainRowIndexes = new[] { -1, -1 };

            // The samples of the gain map cover the base image: their centers are aligned, and a sample is interpolated
            // bilinearly between the four nearest ones (clamped to the edges)
            var columnScale = (double)gainWidth / width;
            var rowScale = (double)gainHeight / height;
            var left = new int[width];
            var right = new int[width];
            var columnWeights = new double[width];
            for (var x = 0; x < width; x++)
            {
                GetNeighbors((x + 0.5) * columnScale - 0.5, gainWidth, out left[x], out right[x], out columnWeights[x]);
            }

            Span<double> linear = stackalloc double[3];
            Span<double> converted = stackalloc double[3];
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GetNeighbors((y + 0.5) * rowScale - 0.5, gainHeight, out var top, out var bottom, out var rowWeight);
                var topRow = GetGainRow(gainFrame, gainStorage, gainRows, gainRowIndexes, top);
                var bottomRow = GetGainRow(gainFrame, gainStorage, gainRows, gainRowIndexes, bottom);
                baseFrame.ReadConvertedRgba64(baseStorage, y, 0, baseRow);
                var output = resultLease.GetRow<RgbaFloat>(y);
                for (var x = 0; x < width; x++)
                {
                    var pixel = baseRow[x];
                    linearizer.Linearize(pixel, linear);
                    if (toAlternate is not null)
                    {
                        Multiply(toAlternate, linear, converted);
                        converted.CopyTo(linear);
                    }

                    var columnWeight = columnWeights[x];
                    var topLeft = topRow[left[x]];
                    var topRight = topRow[right[x]];
                    var bottomLeft = bottomRow[left[x]];
                    var bottomRight = bottomRow[right[x]];
                    for (var c = 0; c < 3; c++)
                    {
                        var sample = Interpolate(GetChannel(topLeft, c), GetChannel(topRight, c), GetChannel(bottomLeft, c), GetChannel(bottomRight, c), columnWeight, rowWeight);
                        var recovery = inverseGammas[c] is 1 ? sample : Math.Pow(sample, inverseGammas[c]);
                        var gain = minimums[c] + (ranges[c] * recovery);
                        linear[c] = ((linear[c] + baseOffsets[c]) * double.Exp2(gain * weight)) - alternateOffsets[c];
                    }

                    output[x] = new RgbaFloat((float)linear[0], (float)linear[1], (float)linear[2], pixel.A / 65535f);
                }
            }

            var resultMetadata = result.Metadata;
            var source = baseImage.Metadata;
            resultMetadata.SourceFormat = source.SourceFormat;
            resultMetadata.Orientation = source.Orientation;
            resultMetadata.ExifProfile = source.ExifProfile;
            resultMetadata.XmpProfile = source.XmpProfile;
            resultMetadata.Resolution = source.Resolution;
            resultMetadata.PixelAspectRatio = source.PixelAspectRatio;
            if (primaries is 1)
            {
                resultMetadata.TransferFunction = ColorTransferFunction.Linear;
            }
            else
            {
                resultMetadata.Cicp = new CicpColorDescription(primaries, LinearTransfer, matrixCoefficients: 0, fullRange: true);
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    // The gain map samples are its coded values in [0, 1]: a grayscale gain map gives its value to the three channels
    private static double GetChannel(Rgba64 sample, int channel) => channel switch
    {
        0 => sample.R / 65535.0,
        1 => sample.G / 65535.0,
        _ => sample.B / 65535.0,
    };

    private static Rgba64[] GetGainRow(ImageFrame frame, PixelStorage storage, Rgba64[][] rows, int[] indexes, int y)
    {
        if (indexes[0] == y)
            return rows[0];

        if (indexes[1] == y)
            return rows[1];

        // Rows are read top to bottom: the older of the two cached rows is replaced
        var slot = indexes[0] < indexes[1] ? 0 : 1;
        frame.ReadConvertedRgba64(storage, y, 0, rows[slot]);
        indexes[slot] = y;
        return rows[slot];
    }

    private static void GetNeighbors(double position, int count, out int first, out int second, out double weight)
    {
        if (position <= 0)
        {
            first = second = 0;
            weight = 0;
            return;
        }

        if (position >= count - 1)
        {
            first = second = count - 1;
            weight = 0;
            return;
        }

        first = (int)position;
        second = first + 1;
        weight = position - first;
    }

    private static double Interpolate(double topLeft, double topRight, double bottomLeft, double bottomRight, double columnWeight, double rowWeight)
    {
        var top = topLeft + ((topRight - topLeft) * columnWeight);
        var bottom = bottomLeft + ((bottomRight - bottomLeft) * columnWeight);
        return top + ((bottom - top) * rowWeight);
    }

    private static void Multiply(double[] matrix, ReadOnlySpan<double> vector, Span<double> result)
    {
        for (var row = 0; row < 3; row++)
        {
            result[row] = (matrix[row * 3] * vector[0]) + (matrix[(row * 3) + 1] * vector[1]) + (matrix[(row * 3) + 2] * vector[2]);
        }
    }

    // From the linear samples of a set of primaries to the linear samples of another one, through CIEXYZ of the profile
    // connection space (both matrices adapt their white to D50)
    private static double[] GetConversionMatrix(byte source, byte destination)
    {
        var from = GetToConnectionSpace(source);
        var to = ColorValueMatrix.FromRows(GetToConnectionSpace(destination)).Invert();
        var product = to.Multiply(ColorValueMatrix.FromRows(from));
        var result = new double[9];
        product.CopyTo(result);
        return result;
    }

    private static double[] GetToConnectionSpace(byte primaries)
    {
        var matrix = new double[9];
        if (!new CicpColorSpace(primaries, LinearTransfer, IsGray: false).TryGetToConnectionSpace(matrix))
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The color primaries {primaries} do not define a color space."), ImageFormat.Unknown, "CICP color primaries without a color space");

        return matrix;
    }

    /// <summary>Converts the samples of the base image to linear light, and tells their primaries.</summary>
    private sealed class BaseLinearizer
    {
        private readonly double[]? _table;
        private readonly IccPipeline? _pipeline;
        private readonly bool _isGray;

        private BaseLinearizer(byte primaries, double[]? table, IccPipeline? pipeline, bool isGray)
        {
            Primaries = primaries;
            _table = table;
            _pipeline = pipeline;
            _isGray = isGray;
        }

        public byte Primaries { get; }

        public static BaseLinearizer Create(ImageMetadata metadata, bool isGray)
        {
            if (metadata.Cicp is { } cicp)
            {
                var primaries = cicp.ColorPrimaries is 2 ? (byte)1 : cicp.ColorPrimaries;
                var transfer = cicp.TransferCharacteristics is 2 ? (byte)13 : cicp.TransferCharacteristics;
                if (!CicpTransfer.IsSupported(transfer) || CicpTransfer.IsHdr(transfer))
                    throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The gain map cannot be applied to a base image of the transfer characteristics {cicp.TransferCharacteristics}."), metadata.SourceFormat, "Gain map on a high dynamic range base image");

                if (!CicpPrimaries.IsSupported(primaries))
                    throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The gain map cannot be applied to a base image of the color primaries {cicp.ColorPrimaries}."), metadata.SourceFormat, "Gain map on a base image of reserved primaries");

                return new BaseLinearizer(primaries, CicpTransfer.GetDecode16(transfer).ToArray(), pipeline: null, isGray);
            }

            if (metadata.IccProfile is { } profile)
                return CreateFromIccProfile(profile, metadata.SourceFormat, isGray);

            var code = metadata.TransferFunction is ColorTransferFunction.Linear ? LinearTransfer : (byte)13;
            return new BaseLinearizer(1, CicpTransfer.GetDecode16(code).ToArray(), pipeline: null, isGray);
        }

        public void Linearize(Rgba64 pixel, Span<double> linear)
        {
            if (_table is { } table)
            {
                linear[0] = table[pixel.R];
                linear[1] = table[pixel.G];
                linear[2] = table[pixel.B];
                return;
            }

            if (_isGray)
            {
                linear[0] = pixel.R / 65535.0;
                _pipeline!.Apply(linear[..1]);
                linear[1] = linear[2] = linear[0];
                return;
            }

            linear[0] = pixel.R / 65535.0;
            linear[1] = pixel.G / 65535.0;
            linear[2] = pixel.B / 65535.0;
            _pipeline!.Apply(linear);
        }

        private static BaseLinearizer CreateFromIccProfile(IccProfile profile, ImageFormat format, bool isGray)
        {
            if (isGray)
            {
                if (profile.ColorSpace != IccProfileColorSpace.Gray)
                    throw new UnsupportedImageFeatureException("The gain map cannot be applied to a grayscale base image whose ICC profile is not a grayscale one.", format, "Gain map on a base image of this ICC profile");

                // A gray profile gives the lightness of its white: the result is gray in the sRGB primaries
                return new BaseLinearizer(1, table: null, IccPipeline.Create(profile, new CicpColorSpace(1, LinearTransfer, IsGray: true), IccRenderingIntent.RelativeColorimetric, blackPointCompensation: false), isGray: true);
            }

            if (profile.ColorSpace != IccProfileColorSpace.Rgb)
                throw new UnsupportedImageFeatureException($"The gain map cannot be applied to a base image whose ICC profile describes {profile.ColorSpace} colors.", format, "Gain map on a base image of this ICC profile");

            foreach (var primaries in RecognizedPrimaries)
            {
                var target = new CicpColorSpace(primaries, LinearTransfer, IsGray: false);
                var toLinear = IccPipeline.Create(profile, target, IccRenderingIntent.RelativeColorimetric, blackPointCompensation: false);
                var fromLinear = IccPipeline.TryCreateFromConnectionSpace(profile, IccRenderingIntent.RelativeColorimetric) is null
                    ? null
                    : IccPipeline.Create(target, profile, IccRenderingIntent.RelativeColorimetric, blackPointCompensation: false);
                if (HasSamePrimaries(toLinear) && (fromLinear is null || HasSamePrimaries(fromLinear)))
                    return new BaseLinearizer(primaries, table: null, toLinear, isGray: false);
            }

            throw new UnsupportedImageFeatureException("The gain map cannot be applied to a base image whose ICC profile does not have the primaries of sRGB, Display P3, DCI-P3 or BT.2020.", format, "Gain map on a base image of this ICC profile");
        }

        // The primaries and the white match when each of them converts to itself. Both directions are checked, since a
        // conversion clips the colors that are out of the gamut of its destination
        private static bool HasSamePrimaries(IccPipeline pipeline)
        {
            const double Tolerance = 0.01;
            Span<double> values = stackalloc double[3];
            for (var color = 0; color < 4; color++)
            {
                for (var c = 0; c < 3; c++)
                {
                    values[c] = color is 3 || color == c ? 1 : 0;
                }

                pipeline.Apply(values);
                for (var c = 0; c < 3; c++)
                {
                    var expected = color is 3 || color == c ? 1 : 0;
                    if (Math.Abs(values[c] - expected) > Tolerance)
                        return false;
                }
            }

            return true;
        }
    }
}
