using System.Collections.Concurrent;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.Pdf.Rendering.Colors;

/// <summary>
/// Converts single colors through an ICC transform, with the results of the colors already converted: a conversion has a
/// cost of its own, which the many colors of the shadings and of the paths of a page would pay each time.
/// </summary>
/// <remarks>The components are quantized to 8 bits, the precision of the image; the cache stops growing at <see cref="MaxEntries"/> colors.</remarks>
internal sealed class IccColorCache(IccColorTransform transform)
{
    private const int MaxEntries = 1 << 16;

    private readonly ConcurrentDictionary<uint, RgbColor> _colors = new();

    public int ChannelCount => transform.SourceChannelCount;

    public RgbColor ToRgb(ReadOnlySpan<double> components)
    {
        var channels = ChannelCount;
        Span<byte> source = stackalloc byte[channels];
        uint key = 0;
        for (var i = 0; i < channels; i++)
        {
            source[i] = (byte)Math.Round(Math.Clamp(i < components.Length && double.IsFinite(components[i]) ? components[i] : 0, 0, 1) * 255, MidpointRounding.AwayFromZero);
            key = (key << 8) | source[i];
        }

        var isCached = channels <= 4;
        if (isCached && _colors.TryGetValue(key, out var color))
            return color;

        Span<byte> rgb = stackalloc byte[3];
        transform.Convert(source, rgb);
        color = new RgbColor(rgb[0] / 255.0, rgb[1] / 255.0, rgb[2] / 255.0);
        if (isCached && _colors.Count < MaxEntries)
        {
            _colors.TryAdd(key, color);
        }

        return color;
    }
}
