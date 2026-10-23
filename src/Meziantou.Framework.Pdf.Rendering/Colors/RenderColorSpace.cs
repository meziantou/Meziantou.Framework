using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Functions;

namespace Meziantou.Framework.Pdf.Rendering.Colors;

/// <summary>A color space of a document (ISO 32000-2, 8.6), which converts its colors to sRGB.</summary>
internal abstract class RenderColorSpace
{
    private const int MaxDepth = 8;

    public static RenderColorSpace Gray { get; } = new DeviceGraySpace();

    public static RenderColorSpace Rgb { get; } = new DeviceRgbSpace();

    public static RenderColorSpace Cmyk { get; } = new DeviceCmykSpace();

    public abstract int ComponentCount { get; }

    /// <summary>Gets whether the color space paints nothing (a <c>Separation</c> named <c>None</c>).</summary>
    public virtual bool PaintsNothing => false;

    /// <summary>Gets the initial color of the color space (8.6.4, 8.6.5, 8.6.6).</summary>
    public virtual double[] GetInitialColor() => new double[ComponentCount];

    /// <summary>Gets the default decode array of the samples of an image (table 90).</summary>
    public virtual double[] GetDefaultDecode(int bitsPerComponent)
    {
        var result = new double[ComponentCount * 2];
        for (var i = 0; i < ComponentCount; i++)
        {
            result[(2 * i) + 1] = 1;
        }

        return result;
    }

    /// <summary>Converts a color to sRGB, components from 0 to 1.</summary>
    public abstract RgbColor ToRgb(ReadOnlySpan<double> components);

    /// <summary>Converts colors to 8-bit sRGB: <see cref="ComponentCount"/> components per color, three bytes per color.</summary>
    public virtual void ToRgb(ReadOnlySpan<double> components, Span<byte> rgb)
    {
        var count = ComponentCount;
        for (int i = 0, j = 0; i + count <= components.Length && j + 3 <= rgb.Length; i += count, j += 3)
        {
            var color = ToRgb(components.Slice(i, count));
            rgb[j] = ToByte(color.R);
            rgb[j + 1] = ToByte(color.G);
            rgb[j + 2] = ToByte(color.B);
        }
    }

    public static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>Reads a color space: a name (of a family or of the resources), or an array.</summary>
    public static RenderColorSpace Read(PdfObject? value, PdfDictionary? resources, RenderCache cache, int depth = 0)
    {
        if (depth > MaxDepth)
            throw new InvalidPdfContentException("The color spaces are nested too deeply");

        value = value is null ? null : PdfReference.ResolveIfReference(value);
        switch (value)
        {
            case PdfName name:
                switch (name.Value)
                {
                    case "DeviceGray" or "G":
                        return GetDefault(resources, "DefaultGray", Gray, cache, depth);
                    case "DeviceRGB" or "RGB":
                        return GetDefault(resources, "DefaultRGB", Rgb, cache, depth);
                    case "DeviceCMYK" or "CMYK":
                        return GetDefault(resources, "DefaultCMYK", Cmyk, cache, depth);
                    case "Pattern":
                        return new PatternSpace(underlying: null);
                }

                if (resources?.Get<PdfDictionary>(PdfNames.ColorSpace)?.GetResolved(name) is { } named)
                    return Read(named, resources, cache, depth + 1);

                throw new InvalidPdfContentException($"Unknown color space '{name.Value}'");

            case PdfArray { Count: > 0 } array:
                return cache.GetColorSpace(array, () => ReadArray(array, resources, cache, depth));

            default:
                throw new InvalidPdfContentException("A color space is not a name or an array");
        }
    }

    private static RenderColorSpace GetDefault(PdfDictionary? resources, string defaultName, RenderColorSpace device, RenderCache cache, int depth)
    {
        // The default color spaces of the resources replace the device color spaces (8.6.5.6)
        if (resources?.Get<PdfDictionary>(PdfNames.ColorSpace)?.GetResolved(PdfName.Create(defaultName)) is { } replacement && depth < MaxDepth)
        {
            try
            {
                var space = Read(replacement, resources: null, cache, depth + 1);
                if (space.ComponentCount == device.ComponentCount)
                    return space;
            }
            catch (InvalidPdfContentException)
            {
            }
        }

        return device;
    }

    private static RenderColorSpace ReadArray(PdfArray array, PdfDictionary? resources, RenderCache cache, int depth)
    {
        var family = array.GetResolved(0) is PdfName name ? name.Value : "";
        switch (family)
        {
            case "DeviceGray" or "G":
                return Gray;
            case "DeviceRGB" or "RGB":
                return Rgb;
            case "DeviceCMYK" or "CMYK":
                return Cmyk;
            case "CalGray":
                // The CIE-based gray is drawn as the device gray, as viewers do
                return Gray;
            case "CalRGB":
                return new CalRgbSpace(array.Count > 1 ? array.GetResolved(1) as PdfDictionary : null);
            case "Lab":
                return new LabSpace(array.Count > 1 ? array.GetResolved(1) as PdfDictionary : null);
            case "ICCBased":
                return ReadIccBased(array.Count > 1 ? array.GetResolved(1) as PdfStream : null, resources, cache, depth);
            case "Indexed" or "I":
                {
                    if (array.Count < 4)
                        throw new InvalidPdfContentException("An indexed color space is incomplete");

                    var baseSpace = Read(array.GetResolved(1), resources, cache, depth + 1);
                    var high = (int)Math.Clamp(array.GetNumber(2), 0, 255);
                    var lookup = array.GetResolved(3) switch
                    {
                        PdfString text => text.Bytes.ToArray(),
                        PdfStream stream => stream.GetDecodedData(),
                        _ => throw new InvalidPdfContentException("The lookup table of an indexed color space is invalid"),
                    };
                    return new IndexedSpace(baseSpace, high, lookup);
                }

            case "Separation":
                {
                    if (array.Count < 4)
                        throw new InvalidPdfContentException("A separation color space is incomplete");

                    var colorant = array.GetResolved(1) is PdfName colorantName ? colorantName.Value : "";
                    var alternate = Read(array.GetResolved(2), resources, cache, depth + 1);
                    return new DeviceNSpace(1, alternate, RenderFunction.Read(array.GetResolved(3)), paintsNothing: colorant == "None");
                }

            case "DeviceN":
                {
                    if (array.Count < 4 || array.GetResolved(1) is not PdfArray names)
                        throw new InvalidPdfContentException("A DeviceN color space is incomplete");

                    var alternate = Read(array.GetResolved(2), resources, cache, depth + 1);
                    var paintsNothing = names.Count > 0 && names.All(item => PdfReference.ResolveIfReference(item) is PdfName { Value: "None" });
                    return new DeviceNSpace(Math.Max(1, names.Count), alternate, RenderFunction.Read(array.GetResolved(3)), paintsNothing);
                }

            case "Pattern":
                return new PatternSpace(array.Count > 1 ? Read(array.GetResolved(1), resources, cache, depth + 1) : null);
            default:
                throw new UnsupportedPdfFeatureException($"The color space '{family}' is not supported");
        }
    }

    private static RenderColorSpace ReadIccBased(PdfStream? stream, PdfDictionary? resources, RenderCache cache, int depth)
    {
        if (stream is null)
            throw new InvalidPdfContentException("An ICC-based color space has no profile");

        var count = (int)stream.Dictionary.GetInteger(PdfNames.N, 0);
        RenderColorSpace? alternate = null;
        if (stream.Dictionary.GetResolved(PdfNames.Alternate) is { } alternateValue)
        {
            try
            {
                alternate = Read(alternateValue, resources, cache, depth + 1);
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
            {
            }
        }

        try
        {
            var profile = new IccProfile(new MetadataBlob(stream.GetDecodedData()));
            var transform = IccColorTransform.Create(profile, IccProfile.Srgb);
            if (count == 0 || transform.SourceChannelCount == count)
                return new IccSpace(transform);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnsupportedImageFeatureException or InvalidPdfContentException or ArgumentException)
        {
        }

        // A profile that cannot be read: the alternate color space, or the device color space of its number of components
        if (alternate is not null && (count == 0 || alternate.ComponentCount == count))
            return alternate;

        return count switch
        {
            1 => Gray,
            4 => Cmyk,
            _ => Rgb,
        };
    }

    private sealed class DeviceGraySpace : RenderColorSpace
    {
        public override int ComponentCount => 1;

        public override RgbColor ToRgb(ReadOnlySpan<double> components)
        {
            var gray = components.Length > 0 ? components[0] : 0;
            return new RgbColor(gray, gray, gray);
        }
    }

    private sealed class DeviceRgbSpace : RenderColorSpace
    {
        public override int ComponentCount => 3;

        public override RgbColor ToRgb(ReadOnlySpan<double> components) => components.Length >= 3 ? new RgbColor(components[0], components[1], components[2]) : default;
    }

    /// <summary>DeviceCMYK, converted with the CMYK profile Imaging has (CGATS 001, close to the conversion of print viewers).</summary>
    private sealed class DeviceCmykSpace : RenderColorSpace
    {
        private static readonly Lazy<IccColorTransform> Transform = new(() => IccColorTransform.Create(IccProfile.Cgats001CompatibleCmyk, IccProfile.Srgb));
        private static readonly Lazy<IccColorCache> Colors = new(() => new IccColorCache(Transform.Value));

        public override int ComponentCount => 4;

        public override double[] GetInitialColor() => [0, 0, 0, 1];

        public override RgbColor ToRgb(ReadOnlySpan<double> components)
        {
            if (components.Length < 4)
                return default;

            return Colors.Value.ToRgb(components);
        }

        public override void ToRgb(ReadOnlySpan<double> components, Span<byte> rgb)
        {
            var count = Math.Min(components.Length / 4, rgb.Length / 3);
            var cmyk = new byte[count * 4];
            for (var i = 0; i < cmyk.Length; i++)
            {
                cmyk[i] = ToByte(components[i]);
            }

            Transform.Value.Convert(cmyk, rgb[..(count * 3)]);
        }
    }

    private sealed class IccSpace(IccColorTransform transform) : RenderColorSpace
    {
        private readonly IccColorCache _colors = new(transform);

        public override int ComponentCount => transform.SourceChannelCount;

        public override double[] GetInitialColor() => ComponentCount == 4 ? [0, 0, 0, 1] : new double[ComponentCount];

        public override RgbColor ToRgb(ReadOnlySpan<double> components) => _colors.ToRgb(components);

        public override void ToRgb(ReadOnlySpan<double> components, Span<byte> rgb)
        {
            var channels = ComponentCount;
            var count = Math.Min(components.Length / channels, rgb.Length / 3);
            var source = new byte[count * channels];
            for (var i = 0; i < source.Length; i++)
            {
                source[i] = ToByte(components[i]);
            }

            transform.Convert(source, rgb[..(count * 3)]);
        }
    }

    private sealed class IndexedSpace(RenderColorSpace baseSpace, int high, byte[] lookup) : RenderColorSpace
    {
        public override int ComponentCount => 1;

        public override double[] GetDefaultDecode(int bitsPerComponent) => [0, (1 << bitsPerComponent) - 1];

        public override RgbColor ToRgb(ReadOnlySpan<double> components)
        {
            var index = (int)Math.Clamp(Math.Round(components.Length > 0 ? components[0] : 0, MidpointRounding.AwayFromZero), 0, high);
            var count = baseSpace.ComponentCount;
            Span<double> values = stackalloc double[count];
            for (var i = 0; i < count; i++)
            {
                var position = (index * count) + i;
                values[i] = (position < lookup.Length ? lookup[position] : 0) / 255.0;
            }

            return baseSpace.ToRgb(values);
        }
    }

    /// <summary>Separation and DeviceN: the tint transform converts the colorants to the alternate color space (8.6.6.4, 8.6.6.5).</summary>
    private sealed class DeviceNSpace(int count, RenderColorSpace alternate, RenderFunction tintTransform, bool paintsNothing) : RenderColorSpace
    {
        public override int ComponentCount => count;

        public override bool PaintsNothing => paintsNothing;

        public override double[] GetInitialColor()
        {
            var result = new double[count];
            result.AsSpan().Fill(1);
            return result;
        }

        public override RgbColor ToRgb(ReadOnlySpan<double> components)
        {
            Span<double> values = stackalloc double[Math.Max(alternate.ComponentCount, tintTransform.OutputCount)];
            tintTransform.Evaluate(components, values);
            return alternate.ToRgb(values[..alternate.ComponentCount]);
        }
    }

    internal sealed class PatternSpace(RenderColorSpace? underlying) : RenderColorSpace
    {
        public RenderColorSpace? Underlying { get; } = underlying;

        public override int ComponentCount => Underlying?.ComponentCount ?? 0;

        public override RgbColor ToRgb(ReadOnlySpan<double> components) => Underlying?.ToRgb(components) ?? default;
    }
}
