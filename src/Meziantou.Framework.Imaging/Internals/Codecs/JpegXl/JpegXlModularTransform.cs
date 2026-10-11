namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A transform of a Modular sub-bitstream (ISO/IEC 18181-1, section H.6): a reversible color transform of three channels,
/// a palette, or a squeeze (a reversible Haar-like wavelet step). Reading a transform changes the list of channels that
/// are coded (<see cref="ApplyToChannelList"/>); once the channels are decoded, the transforms are undone last to first
/// (<see cref="Undo"/>).
/// </summary>
internal sealed class JpegXlModularTransform
{
    private const int KindRct = 0;
    private const int KindPalette = 1;
    private const int KindSqueeze = 2;

    // A channel is squeezed until its sides are at most 8 samples
    private const int MaxFirstPreviewSize = 8;

    private const int SmallCube = 4;
    private const int LargeCube = 5;
    private const int LargeCubeOffset = SmallCube * SmallCube * SmallCube;

    private readonly int _kind;
    private readonly int _beginChannel;
    private readonly int _rctType;
    private readonly int _channelCount;
    private readonly int _colorCount;
    private readonly int _deltaCount;
    private readonly int _predictor;
    private List<SqueezeStep> _squeezes;

    private JpegXlModularTransform(int kind, int beginChannel, int rctType, int channelCount, int colorCount, int deltaCount, int predictor, List<SqueezeStep> squeezes)
    {
        _kind = kind;
        _beginChannel = beginChannel;
        _rctType = rctType;
        _channelCount = channelCount;
        _colorCount = colorCount;
        _deltaCount = deltaCount;
        _predictor = predictor;
        _squeezes = squeezes;
    }

    // The 72 colors of the delta palette (section H.6.4): an index below zero selects one of them, or its opposite
    private static ReadOnlySpan<short> DeltaPalette =>
    [
        0, 0, 0, 4, 4, 4, 11, 0, 0, 0, 0, -13, 0, -12, 0, -10, -10, -10,
        -18, -18, -18, -27, -27, -27, -18, -18, 0, 0, 0, -32, -32, 0, 0, -37, -37, -37,
        0, -32, -32, 24, 24, 45, 50, 50, 50, -45, -24, -24, -24, -45, -45, 0, -24, -24,
        -34, -34, 0, -24, 0, -24, -45, -45, -24, 64, 64, 64, -32, 0, -32, 0, -32, 0,
        -32, 0, 32, -24, -45, -24, 45, 24, 45, 24, -24, -45, -45, -24, 24, 80, 80, 80,
        64, 0, 0, 0, 0, -64, 0, -64, -64, -24, -24, 45, 96, 96, 96, 64, 64, 0,
        45, -24, -24, 34, -34, 0, 112, 112, 112, 24, -45, -45, 45, 45, -24, 0, -32, 32,
        24, -24, 45, 0, 96, 96, 45, -24, 24, 24, -45, -24, -24, -45, 24, 0, -64, 0,
        96, 0, 0, 128, 128, 128, 64, 0, 64, 144, 144, 144, 96, 96, 0, -36, -36, 36,
        45, -24, -45, 45, -45, -24, 0, 0, -96, 0, 128, 128, 0, 96, 0, 45, 24, -45,
        -128, 0, 0, 24, -45, 24, -45, 24, -45, 64, 0, -64, 64, -64, -64, 96, 0, 96,
        45, -45, 24, 24, 45, -45, 64, 64, -64, 128, 128, 0, 0, 0, -128, -24, 45, -45,
    ];

    public static JpegXlModularTransform Read(JpegXlBitReader reader)
    {
        var kind = (int)reader.ReadBits(2);
        switch (kind)
        {
            case KindRct:
            {
                var beginChannel = (int)reader.ReadU32(0, 3, 8, 6, 72, 10, 1096, 13);
                var type = (int)reader.ReadU32(6, 0, 0, 2, 2, 4, 10, 6);
                if (type >= 42)
                    throw JpegXlFormat.Invalid("a color transform has a type that does not exist.");

                return new JpegXlModularTransform(kind, beginChannel, type, 3, 0, 0, 0, []);
            }

            case KindPalette:
            {
                var beginChannel = (int)reader.ReadU32(0, 3, 8, 6, 72, 10, 1096, 13);
                var channelCount = (int)reader.ReadU32(1, 0, 3, 0, 4, 0, 1, 13);
                var colorCount = (int)reader.ReadU32(0, 8, 256, 10, 1280, 12, 5376, 16);
                var deltaCount = (int)reader.ReadU32(0, 0, 1, 8, 257, 10, 1281, 16);
                var predictor = (int)reader.ReadBits(4);
                if (predictor >= JpegXlMaTree.PredictorCount)
                    throw JpegXlFormat.Invalid("a palette uses a predictor that does not exist.");

                return new JpegXlModularTransform(kind, beginChannel, 0, channelCount, colorCount, deltaCount, predictor, []);
            }

            case KindSqueeze:
            {
                var count = (int)reader.ReadU32(0, 0, 1, 4, 9, 6, 41, 8);
                var squeezes = new List<SqueezeStep>(count);
                for (var i = 0; i < count; i++)
                {
                    var horizontal = reader.ReadBool();
                    var inPlace = reader.ReadBool();
                    var beginChannel = (int)reader.ReadU32(0, 3, 8, 6, 72, 10, 1096, 13);
                    var channelCount = (int)reader.ReadU32(1, 0, 2, 0, 3, 0, 4, 4);
                    squeezes.Add(new SqueezeStep(horizontal, inPlace, beginChannel, channelCount));
                }

                return new JpegXlModularTransform(kind, 0, 0, 0, 0, 0, 0, squeezes);
            }

            default:
                throw JpegXlFormat.Invalid("a Modular transform has a kind that does not exist.");
        }
    }

    /// <summary>Changes the channels of the image to those that are coded once the transform is applied by the encoder.</summary>
    public void ApplyToChannelList(JpegXlModularImage image)
    {
        switch (_kind)
        {
            case KindRct:
                EnsureEqualChannels(image, _beginChannel, _beginChannel + 2);
                break;

            case KindPalette:
            {
                // The channels become one channel of indexes, and the palette is a new first channel
                var endChannel = _beginChannel + _channelCount - 1;
                EnsureEqualChannels(image, _beginChannel, endChannel);
                if (_beginChannel >= image.MetaChannelCount)
                {
                    image.MetaChannelCount++;
                }
                else
                {
                    if (endChannel >= image.MetaChannelCount)
                        throw Invalid();

                    image.MetaChannelCount += 2 - _channelCount;
                }

                for (var i = endChannel; i > _beginChannel; i--)
                {
                    image.Channels[i].Dispose();
                    image.Channels.RemoveAt(i);
                }

                image.InsertChannel(0, _colorCount + _deltaCount, _channelCount, -1, -1);
                break;
            }

            default:
                if (_squeezes.Count == 0)
                {
                    _squeezes = GetDefaultSqueezes(image);
                }

                foreach (var step in _squeezes)
                {
                    ApplySqueezeToChannelList(image, step);
                }

                break;
        }
    }

    /// <summary>Undoes the transform on the decoded channels.</summary>
    public void Undo(JpegXlModularImage image, AllocationScope scope)
    {
        switch (_kind)
        {
            case KindRct:
                UndoRct(image);
                break;

            case KindPalette:
                UndoPalette(image, scope);
                break;

            default:
                for (var i = _squeezes.Count - 1; i >= 0; i--)
                {
                    UndoSqueeze(image, _squeezes[i], scope);
                }

                break;
        }
    }

    /// <summary>Predicts a sample from its neighbors with one of the 14 predictors (section H.5).</summary>
    public static long Predict(int predictor, long left, long top, long topTop, long topLeft, long topRight, long leftLeft, long topRightRight, long weighted) => predictor switch
    {
        1 => left,
        2 => top,
        3 => (left + top) / 2,
        4 => Select(left, top, topLeft),
        5 => ClampedGradient((int)left, (int)top, (int)topLeft),
        6 => weighted,
        7 => topRight,
        8 => topLeft,
        9 => leftLeft,
        10 => (left + topLeft) / 2,
        11 => (topLeft + top) / 2,
        12 => (top + topRight) / 2,
        13 => ((6 * top) - (2 * topTop) + (7 * left) + leftLeft + topRightRight + (3 * topRight) + 8) / 16,
        _ => 0,
    };

    /// <summary>The gradient predictor: <c>left + top - topLeft</c>, clamped to the range of <c>left</c> and <c>top</c>.</summary>
    public static int ClampedGradient(int left, int top, int topLeft)
    {
        var min = Math.Min(left, top);
        var max = Math.Max(left, top);
        var gradient = unchecked(left + top - topLeft);
        if (topLeft > max)
            return min;

        return topLeft < min ? max : gradient;
    }

    private static long Select(long left, long top, long topLeft)
    {
        var gradient = left + top - topLeft;
        return Math.Abs(gradient - left) < Math.Abs(gradient - top) ? left : top;
    }

    private static InvalidImageContentException Invalid() => JpegXlFormat.Invalid("a Modular transform does not apply to the channels of its image.");

    private static void EnsureEqualChannels(JpegXlModularImage image, int first, int last)
    {
        if (first < 0 || last < first || last >= image.Channels.Count)
            throw Invalid();

        if (first < image.MetaChannelCount && last >= image.MetaChannelCount)
            throw Invalid();

        var reference = image.Channels[first];
        for (var i = first + 1; i <= last; i++)
        {
            var channel = image.Channels[i];
            if (channel.Width != reference.Width || channel.Height != reference.Height || channel.HorizontalShift != reference.HorizontalShift || channel.VerticalShift != reference.VerticalShift)
                throw Invalid();
        }
    }

    private void UndoRct(JpegXlModularImage image)
    {
        EnsureEqualChannels(image, _beginChannel, _beginChannel + 2);
        if (_rctType == 0)
            return;

        var permutation = _rctType / 7;
        var type = _rctType % 7;
        var first = image.Channels[_beginChannel];
        var second = image.Channels[_beginChannel + 1];
        var third = image.Channels[_beginChannel + 2];
        if (type != 0 && !first.IsEmpty)
        {
            var a = first.Samples;
            var b = second.Samples;
            var c = third.Samples;
            if (type == 6)
            {
                // YCgCo
                for (var i = 0; i < a.Length; i++)
                {
                    var luma = a[i];
                    var orange = b[i];
                    var green = c[i];
                    var temp = unchecked(luma - (green >> 1));
                    var outGreen = unchecked(green + temp);
                    var outBlue = unchecked(temp - (orange >> 1));
                    a[i] = unchecked(outBlue + orange);
                    b[i] = outGreen;
                    c[i] = outBlue;
                }
            }
            else
            {
                var addFirstToThird = (type & 1) != 0;
                var secondMode = type >> 1;
                for (var i = 0; i < a.Length; i++)
                {
                    var x = a[i];
                    var y = b[i];
                    var z = c[i];
                    if (addFirstToThird)
                    {
                        z = unchecked(z + x);
                    }

                    if (secondMode == 1)
                    {
                        y = unchecked(y + x);
                    }
                    else if (secondMode == 2)
                    {
                        y = unchecked(y + ((x + z) >> 1));
                    }

                    b[i] = y;
                    c[i] = z;
                }
            }
        }

        // The three channels are then permuted
        if (permutation != 0)
        {
            image.Channels[_beginChannel + (permutation % 3)] = first;
            image.Channels[_beginChannel + ((permutation + 1 + (permutation / 3)) % 3)] = second;
            image.Channels[_beginChannel + ((permutation + 2 - (permutation / 3)) % 3)] = third;
        }
    }

    private void UndoPalette(JpegXlModularImage image, AllocationScope scope)
    {
        if (image.MetaChannelCount < 1)
            throw Invalid();

        var palette = image.Channels[0];
        var count = palette.Height;
        var indexChannelPosition = _beginChannel + 1;
        if (indexChannelPosition >= image.Channels.Count || count < 1)
            throw Invalid();

        var indexChannel = image.Channels[indexChannelPosition];
        var width = indexChannel.Width;
        var height = indexChannel.Height;
        for (var i = 1; i < count; i++)
        {
            image.InsertChannel(indexChannelPosition + 1, width, height, indexChannel.HorizontalShift, indexChannel.VerticalShift);
            image.Channels[indexChannelPosition + 1].Allocate(scope);
        }

        var paletteSamples = palette.Samples;
        var paletteWidth = palette.Width;
        var bitDepth = Math.Min(image.BitDepth, 24);
        if (width != 0 && height != 0)
        {
            if (_deltaCount == 0 && _predictor == 0)
            {
                if (count == 1)
                {
                    var samples = indexChannel.Samples;
                    for (var i = 0; i < samples.Length; i++)
                    {
                        var index = Math.Clamp(samples[i], 0, Math.Max(0, paletteWidth - 1));
                        samples[i] = GetPaletteValue(paletteSamples, index, 0, paletteWidth, bitDepth);
                    }
                }
                else
                {
                    // The last channels first: the indexes are in the first one
                    var indexes = indexChannel.Samples;
                    for (var c = count - 1; c >= 0; c--)
                    {
                        var samples = image.Channels[indexChannelPosition + c].Samples;
                        for (var i = 0; i < samples.Length; i++)
                        {
                            samples[i] = GetPaletteValue(paletteSamples, indexes[i], c, paletteWidth, bitDepth);
                        }
                    }
                }
            }
            else
            {
                // Delta palette: the first entries are differences from a prediction of the color being rebuilt
                using var indexes = new JpegXlModularChannel(width, height, 0, 0);
                indexes.TakeFrom(indexChannel);
                indexChannel.Resize(width, height);
                indexChannel.HorizontalShift = indexes.HorizontalShift;
                indexChannel.VerticalShift = indexes.VerticalShift;
                indexChannel.Allocate(scope);
                for (var c = 0; c < count; c++)
                {
                    var channel = image.Channels[indexChannelPosition + c];
                    using var weighted = _predictor == 6 ? new JpegXlWeightedPredictor(image.WeightedPredictor, width, scope) : null;
                    for (var y = 0; y < height; y++)
                    {
                        var row = channel.GetRow(y);
                        var above = y > 0 ? channel.GetRow(y - 1) : default;
                        var twoAbove = y > 1 ? channel.GetRow(y - 2) : default;
                        var indexRow = indexes.GetRow(y);
                        for (var x = 0; x < width; x++)
                        {
                            var index = indexRow[x];
                            long value = GetPaletteValue(paletteSamples, index, c, paletteWidth, bitDepth);
                            if (index < _deltaCount || weighted is not null)
                            {
                                long left = x > 0 ? row[x - 1] : y > 0 ? above[x] : 0;
                                long top = y > 0 ? above[x] : left;
                                long topLeft = x > 0 && y > 0 ? above[x - 1] : left;
                                long topRight = x + 1 < width && y > 0 ? above[x + 1] : top;
                                long leftLeft = x > 1 ? row[x - 2] : left;
                                long topTop = y > 1 ? twoAbove[x] : top;
                                long topRightRight = x + 2 < width && y > 0 ? above[x + 2] : topRight;
                                var weightedPrediction = weighted?.Predict(x, y, top, left, topRight, topLeft, topTop, out _) ?? 0;
                                if (index < _deltaCount)
                                {
                                    value += Predict(_predictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, weightedPrediction);
                                }
                            }

                            row[x] = (int)value;
                            weighted?.Update(row[x], x, y);
                        }
                    }
                }
            }
        }

        if (indexChannelPosition >= image.MetaChannelCount)
        {
            image.MetaChannelCount--;
        }
        else
        {
            image.MetaChannelCount -= 2 - count;
            if (image.MetaChannelCount < 0)
                throw Invalid();
        }

        palette.Dispose();
        image.Channels.RemoveAt(0);
    }

    /// <summary>
    /// Gets a component of the color of an index: an entry of the palette; beyond the palette, one of the implicit colors
    /// (a 4x4x4 cube then a 5x5x5 cube); below zero, a difference of the delta palette.
    /// </summary>
    private static int GetPaletteValue(ReadOnlySpan<int> palette, int index, int component, int paletteWidth, int bitDepth)
    {
        if (index < 0)
        {
            if (component >= 3)
                return 0;

            index = -(index + 1);
            index %= 1 + (2 * ((DeltaPalette.Length / 3) - 1));
            var value = DeltaPalette[(((index + 1) >> 1) * 3) + component] * ((index & 1) != 0 ? 1 : -1);
            if (bitDepth > 8)
            {
                value *= 1 << (bitDepth - 8);
            }

            return value;
        }

        if (index >= paletteWidth && index < paletteWidth + LargeCubeOffset)
        {
            if (component >= 3)
                return 0;

            index -= paletteWidth;
            index >>= component * 2;
            return (int)((((ulong)(index % SmallCube) * ((1UL << bitDepth) - 1)) >> 2) + (1UL << Math.Max(0, bitDepth - 3)));
        }

        if (index >= paletteWidth + LargeCubeOffset)
        {
            if (component >= 3)
                return 0;

            index -= paletteWidth + LargeCubeOffset;
            if (component == 1)
            {
                index /= LargeCube;
            }
            else if (component == 2)
            {
                index /= LargeCube * LargeCube;
            }

            return (int)(((ulong)(index % LargeCube) * ((1UL << bitDepth) - 1)) >> 2);
        }

        return palette[(component * paletteWidth) + index];
    }

    /// <summary>The squeezes of a transform that lists none (section H.6.2.3): the chroma channels first when they have the size of the first channel, then every channel, alternately in each direction, until it fits 8x8.</summary>
    private static List<SqueezeStep> GetDefaultSqueezes(JpegXlModularImage image)
    {
        var steps = new List<SqueezeStep>();
        var first = image.MetaChannelCount;
        var count = image.Channels.Count - first;
        if (count <= 0)
            throw Invalid();

        var width = image.Channels[first].Width;
        var height = image.Channels[first].Height;
        if (count > 2 && image.Channels[first + 1].Width == width && image.Channels[first + 1].Height == height)
        {
            steps.Add(new SqueezeStep(Horizontal: true, InPlace: false, first + 1, 2));
            steps.Add(new SqueezeStep(Horizontal: false, InPlace: false, first + 1, 2));
        }

        if (width <= height && height > MaxFirstPreviewSize)
        {
            steps.Add(new SqueezeStep(Horizontal: false, InPlace: true, first, count));
            height = (height + 1) / 2;
        }

        while (width > MaxFirstPreviewSize || height > MaxFirstPreviewSize)
        {
            if (width > MaxFirstPreviewSize)
            {
                steps.Add(new SqueezeStep(Horizontal: true, InPlace: true, first, count));
                width = (width + 1) / 2;
            }

            if (height > MaxFirstPreviewSize)
            {
                steps.Add(new SqueezeStep(Horizontal: false, InPlace: true, first, count));
                height = (height + 1) / 2;
            }
        }

        return steps;
    }

    private static void ApplySqueezeToChannelList(JpegXlModularImage image, SqueezeStep step)
    {
        var begin = step.BeginChannel;
        var end = step.BeginChannel + step.ChannelCount - 1;
        if (begin >= image.Channels.Count || end >= image.Channels.Count)
            throw Invalid();

        if (begin < image.MetaChannelCount)
        {
            if (end >= image.MetaChannelCount || !step.InPlace)
                throw Invalid();

            image.MetaChannelCount += step.ChannelCount;
        }

        // Each channel keeps the averages; the residuals are new channels, right after the squeezed ones or at the end
        var offset = step.InPlace ? end + 1 : image.Channels.Count;
        for (var c = begin; c <= end; c++)
        {
            var channel = image.Channels[c];
            if (channel.HorizontalShift > 30 || channel.VerticalShift > 30 || channel.IsEmpty)
                throw Invalid();

            var width = channel.Width;
            var height = channel.Height;
            if (step.Horizontal)
            {
                channel.Resize((width + 1) / 2, height);
                if (channel.HorizontalShift >= 0)
                {
                    channel.HorizontalShift++;
                }

                width -= (width + 1) / 2;
            }
            else
            {
                channel.Resize(width, (height + 1) / 2);
                if (channel.VerticalShift >= 0)
                {
                    channel.VerticalShift++;
                }

                height -= (height + 1) / 2;
            }

            image.InsertChannel(offset + (c - begin), width, height, channel.HorizontalShift, channel.VerticalShift);
        }
    }

    private static void UndoSqueeze(JpegXlModularImage image, SqueezeStep step, AllocationScope scope)
    {
        var begin = step.BeginChannel;
        var end = step.BeginChannel + step.ChannelCount - 1;
        if (begin >= image.Channels.Count || end >= image.Channels.Count)
            throw Invalid();

        var offset = step.InPlace ? end + 1 : image.Channels.Count + begin - end - 1;
        if (offset < 0 || offset + (end - begin) >= image.Channels.Count)
            throw Invalid();

        if (begin < image.MetaChannelCount)
        {
            if (image.MetaChannelCount <= step.ChannelCount)
                throw Invalid();

            image.MetaChannelCount -= step.ChannelCount;
        }

        for (var c = begin; c <= end; c++)
        {
            var average = image.Channels[c];
            var residual = image.Channels[offset + c - begin];
            if (average.Width < residual.Width || average.Height < residual.Height)
                throw Invalid();

            if (step.Horizontal)
            {
                UndoHorizontalSqueeze(average, residual, scope);
            }
            else
            {
                UndoVerticalSqueeze(average, residual, scope);
            }
        }

        for (var i = end - begin; i >= 0; i--)
        {
            image.Channels[offset + i].Dispose();
            image.Channels.RemoveAt(offset + i);
        }
    }

    private static void UndoHorizontalSqueeze(JpegXlModularChannel average, JpegXlModularChannel residual, AllocationScope scope)
    {
        if (average.Width != (average.Width + residual.Width + 1) / 2 || average.Height != residual.Height)
            throw Invalid();

        if (residual.Width == 0)
        {
            average.HorizontalShift--;
            return;
        }

        using var output = new JpegXlModularChannel(average.Width + residual.Width, average.Height, average.HorizontalShift - 1, average.VerticalShift);
        output.Allocate(scope);
        for (var y = 0; y < average.Height; y++)
        {
            var averages = average.GetRow(y);
            var residuals = residual.GetRow(y);
            var row = output.GetRow(y);
            for (var x = 0; x < residuals.Length; x++)
            {
                long current = averages[x];
                long next = x + 1 < averages.Length ? averages[x + 1] : current;
                long left = x > 0 ? row[(x << 1) - 1] : current;
                var difference = residuals[x] + GetSmoothTendency(left, current, next);
                var first = current + (difference / 2);
                row[x << 1] = (int)first;
                row[(x << 1) + 1] = (int)(first - difference);
            }

            if ((row.Length & 1) != 0)
            {
                row[^1] = averages[^1];
            }
        }

        average.TakeFrom(output);
    }

    private static void UndoVerticalSqueeze(JpegXlModularChannel average, JpegXlModularChannel residual, AllocationScope scope)
    {
        if (average.Height != (average.Height + residual.Height + 1) / 2 || average.Width != residual.Width)
            throw Invalid();

        if (residual.Height == 0)
        {
            average.VerticalShift--;
            return;
        }

        using var output = new JpegXlModularChannel(average.Width, average.Height + residual.Height, average.HorizontalShift, average.VerticalShift - 1);
        output.Allocate(scope);
        for (var y = 0; y < residual.Height; y++)
        {
            var averages = average.GetRow(y);
            var nextAverages = y + 1 < average.Height ? average.GetRow(y + 1) : averages;
            var residuals = residual.GetRow(y);
            var first = output.GetRow(y << 1);
            var second = output.GetRow((y << 1) + 1);
            var previous = y > 0 ? output.GetRow((y << 1) - 1) : averages;
            for (var x = 0; x < averages.Length; x++)
            {
                long current = averages[x];
                var difference = residuals[x] + GetSmoothTendency(previous[x], current, nextAverages[x]);
                var value = current + (difference / 2);
                first[x] = (int)value;
                second[x] = (int)(value - difference);
            }
        }

        if ((output.Height & 1) != 0)
        {
            average.GetRow(average.Height - 1).CopyTo(output.GetRow(output.Height - 1));
        }

        average.TakeFrom(output);
    }

    /// <summary>The expected difference of two samples from their average and the neighboring ones, when they are monotonic (section H.6.2.2).</summary>
    private static long GetSmoothTendency(long before, long average, long next)
    {
        long difference = 0;
        if (before >= average && average >= next)
        {
            difference = ((4 * before) - (3 * next) - average + 6) / 12;
            if (difference - (difference & 1) > 2 * (before - average))
            {
                difference = (2 * (before - average)) + 1;
            }

            if (difference + (difference & 1) > 2 * (average - next))
            {
                difference = 2 * (average - next);
            }
        }
        else if (before <= average && average <= next)
        {
            difference = ((4 * before) - (3 * next) - average - 6) / 12;
            if (difference + (difference & 1) < 2 * (before - average))
            {
                difference = (2 * (before - average)) - 1;
            }

            if (difference - (difference & 1) < 2 * (average - next))
            {
                difference = 2 * (average - next);
            }
        }

        return difference;
    }

    private readonly record struct SqueezeStep(bool Horizontal, bool InPlace, int BeginChannel, int ChannelCount);
}
