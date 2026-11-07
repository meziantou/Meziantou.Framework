namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Combines a row of a new layer with the samples of an image (ISO/IEC 18181-1, section F.2): each channel has its own
/// mode, and the modes that weight by alpha use the alpha channel of the two layers.
/// </summary>
internal static class JpegXlBlending
{
    /// <summary>Blends a row.</summary>
    /// <param name="image">The header of the image, which tells which extra channel is an alpha channel and whether it is premultiplied.</param>
    /// <param name="target">The image whose samples are replaced with the result.</param>
    /// <param name="targetX">The first column of <paramref name="target"/>.</param>
    /// <param name="targetY">The row of <paramref name="target"/>.</param>
    /// <param name="layer">The new layer.</param>
    /// <param name="layerX">The first column of <paramref name="layer"/>.</param>
    /// <param name="layerY">The row of <paramref name="layer"/>.</param>
    /// <param name="width">The number of samples.</param>
    /// <param name="colorChannelCount">The number of color channels that are blended; the extra channels of each image follow its own color channels.</param>
    /// <param name="blendings">The blending of the color channels, then the one of each extra channel.</param>
    /// <param name="scratch">Scratch memory of <paramref name="width"/> values per channel.</param>
    public static void BlendRow(JpegXlImageHeader image, JpegXlImageBuffer target, int targetX, int targetY, JpegXlImageBuffer layer, int layerX, int layerY, int width, int colorChannelCount, ReadOnlySpan<JpegXlChannelBlending> blendings, Span<float> scratch)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(layer);
        var extraCount = image.ExtraChannels.Length;
        var targetExtra = target.ColorChannelCount;
        var layerExtra = layer.ColorChannelCount;

        // Every channel is computed from the samples of the two layers before any of them is replaced
        for (var extra = 0; extra < extraCount; extra++)
        {
            var blending = blendings[1 + extra];
            var old = target.GetRow(targetExtra + extra, targetY).Slice(targetX, width);
            var added = layer.GetRow(layerExtra + extra, layerY).Slice(layerX, width);
            var output = scratch.Slice((colorChannelCount + extra) * width, width);
            var alpha = blending.AlphaChannel;
            if (blending.UsesAlpha)
            {
                EnsureAlphaChannel(alpha, extraCount);
            }

            switch (blending.Mode)
            {
                case JpegXlChannelBlending.ModeAdd:
                    Add(old, added, output);
                    break;

                case JpegXlChannelBlending.ModeBlendAbove:
                case JpegXlChannelBlending.ModeBlendBelow:
                {
                    var oldAlpha = target.GetRow(targetExtra + alpha, targetY).Slice(targetX, width);
                    var addedAlpha = layer.GetRow(layerExtra + alpha, layerY).Slice(layerX, width);
                    var isAbove = blending.Mode == JpegXlChannelBlending.ModeBlendAbove;
                    if (alpha == extra)
                    {
                        BlendAlpha(isAbove ? oldAlpha : addedAlpha, isAbove ? addedAlpha : oldAlpha, output, blending.Clamp);
                    }
                    else if (isAbove)
                    {
                        BlendOver(old, oldAlpha, added, addedAlpha, output, image.ExtraChannels[alpha].IsAlphaAssociated, blending.Clamp);
                    }
                    else
                    {
                        BlendOver(added, addedAlpha, old, oldAlpha, output, image.ExtraChannels[alpha].IsAlphaAssociated, blending.Clamp);
                    }

                    break;
                }

                case JpegXlChannelBlending.ModeAddWeightedAbove:
                    if (alpha == extra)
                    {
                        old.CopyTo(output);
                    }
                    else
                    {
                        AddWeighted(old, added, layer.GetRow(layerExtra + alpha, layerY).Slice(layerX, width), output, blending.Clamp);
                    }

                    break;

                case JpegXlChannelBlending.ModeAddWeightedBelow:
                    if (alpha == extra)
                    {
                        added.CopyTo(output);
                    }
                    else
                    {
                        AddWeighted(added, old, target.GetRow(targetExtra + alpha, targetY).Slice(targetX, width), output, blending.Clamp);
                    }

                    break;

                case JpegXlChannelBlending.ModeMultiply:
                    Multiply(old, added, output, blending.Clamp);
                    break;

                case JpegXlChannelBlending.ModeReplace:
                    added.CopyTo(output);
                    break;

                default:
                    old.CopyTo(output);
                    break;
            }
        }

        // Without an alpha channel, a weighted addition is an addition, and blending replaces
        var color = blendings[0];
        var mode = color.Mode;
        if (image.AlphaChannelIndex < 0)
        {
            mode = mode switch
            {
                JpegXlChannelBlending.ModeAddWeightedAbove or JpegXlChannelBlending.ModeAddWeightedBelow => JpegXlChannelBlending.ModeAdd,
                JpegXlChannelBlending.ModeBlendAbove or JpegXlChannelBlending.ModeBlendBelow => JpegXlChannelBlending.ModeReplace,
                _ => mode,
            };
        }
        else if (color.UsesAlpha)
        {
            EnsureAlphaChannel(color.AlphaChannel, extraCount);
        }

        for (var c = 0; c < colorChannelCount; c++)
        {
            var old = target.GetRow(c, targetY).Slice(targetX, width);
            var added = layer.GetRow(c, layerY).Slice(layerX, width);
            var output = scratch.Slice(c * width, width);
            switch (mode)
            {
                case JpegXlChannelBlending.ModeAdd:
                    Add(old, added, output);
                    break;

                case JpegXlChannelBlending.ModeBlendAbove:
                case JpegXlChannelBlending.ModeBlendBelow:
                {
                    var oldAlpha = target.GetRow(targetExtra + color.AlphaChannel, targetY).Slice(targetX, width);
                    var addedAlpha = layer.GetRow(layerExtra + color.AlphaChannel, layerY).Slice(layerX, width);
                    var isPremultiplied = image.ExtraChannels[color.AlphaChannel].IsAlphaAssociated;
                    if (mode == JpegXlChannelBlending.ModeBlendAbove)
                    {
                        BlendOver(old, oldAlpha, added, addedAlpha, output, isPremultiplied, color.Clamp);
                    }
                    else
                    {
                        BlendOver(added, addedAlpha, old, oldAlpha, output, isPremultiplied, color.Clamp);
                    }

                    break;
                }

                case JpegXlChannelBlending.ModeAddWeightedAbove:
                    AddWeighted(old, added, layer.GetRow(layerExtra + color.AlphaChannel, layerY).Slice(layerX, width), output, color.Clamp);
                    break;

                case JpegXlChannelBlending.ModeAddWeightedBelow:
                    AddWeighted(added, old, target.GetRow(targetExtra + color.AlphaChannel, targetY).Slice(targetX, width), output, color.Clamp);
                    break;

                case JpegXlChannelBlending.ModeMultiply:
                    Multiply(old, added, output, color.Clamp);
                    break;

                case JpegXlChannelBlending.ModeReplace:
                    added.CopyTo(output);
                    break;

                default:
                    old.CopyTo(output);
                    break;
            }
        }

        if (mode is JpegXlChannelBlending.ModeBlendAbove or JpegXlChannelBlending.ModeBlendBelow)
        {
            // Blending the colors also blends the alpha channel it uses
            var oldAlpha = target.GetRow(targetExtra + color.AlphaChannel, targetY).Slice(targetX, width);
            var addedAlpha = layer.GetRow(layerExtra + color.AlphaChannel, layerY).Slice(layerX, width);
            var output = scratch.Slice((colorChannelCount + color.AlphaChannel) * width, width);
            var isAbove = mode == JpegXlChannelBlending.ModeBlendAbove;
            BlendAlpha(isAbove ? oldAlpha : addedAlpha, isAbove ? addedAlpha : oldAlpha, output, color.Clamp);
        }

        for (var c = 0; c < colorChannelCount; c++)
        {
            scratch.Slice(c * width, width).CopyTo(target.GetRow(c, targetY).Slice(targetX, width));
        }

        for (var extra = 0; extra < extraCount; extra++)
        {
            scratch.Slice((colorChannelCount + extra) * width, width).CopyTo(target.GetRow(targetExtra + extra, targetY).Slice(targetX, width));
        }
    }

    private static void EnsureAlphaChannel(int index, int extraChannelCount)
    {
        if ((uint)index >= (uint)extraChannelCount)
            throw JpegXlFormat.Invalid("a blending uses an alpha channel that does not exist.");
    }

    private static float Clamp01(float value) => MathF.Min(1, MathF.Max(0, value));

    private static void Add(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> output)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = first[x] + second[x];
        }
    }

    /// <summary>The alpha of a layer over another one.</summary>
    private static void BlendAlpha(ReadOnlySpan<float> bottomAlpha, ReadOnlySpan<float> topAlpha, Span<float> output, bool clamp)
    {
        for (var x = 0; x < output.Length; x++)
        {
            var alpha = clamp ? Clamp01(topAlpha[x]) : topAlpha[x];
            output[x] = 1f - ((1f - alpha) * (1f - bottomAlpha[x]));
        }
    }

    /// <summary>A channel of a layer over the same channel of another one, weighted by their alpha.</summary>
    private static void BlendOver(ReadOnlySpan<float> bottom, ReadOnlySpan<float> bottomAlpha, ReadOnlySpan<float> top, ReadOnlySpan<float> topAlpha, Span<float> output, bool isPremultiplied, bool clamp)
    {
        if (isPremultiplied)
        {
            for (var x = 0; x < output.Length; x++)
            {
                var alpha = clamp ? Clamp01(topAlpha[x]) : topAlpha[x];
                output[x] = top[x] + (bottom[x] * (1f - alpha));
            }

            return;
        }

        for (var x = 0; x < output.Length; x++)
        {
            var alpha = clamp ? Clamp01(topAlpha[x]) : topAlpha[x];
            var newAlpha = 1f - ((1f - alpha) * (1f - bottomAlpha[x]));
            var inverse = newAlpha > 0 ? 1f / newAlpha : 0f;
            output[x] = ((top[x] * alpha) + (bottom[x] * bottomAlpha[x] * (1f - alpha))) * inverse;
        }
    }

    /// <summary>A channel plus the same channel of another layer multiplied by the alpha of that layer.</summary>
    private static void AddWeighted(ReadOnlySpan<float> bottom, ReadOnlySpan<float> top, ReadOnlySpan<float> topAlpha, Span<float> output, bool clamp)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = bottom[x] + (top[x] * (clamp ? Clamp01(topAlpha[x]) : topAlpha[x]));
        }
    }

    private static void Multiply(ReadOnlySpan<float> bottom, ReadOnlySpan<float> top, Span<float> output, bool clamp)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = bottom[x] * (clamp ? Clamp01(top[x]) : top[x]);
        }
    }
}
