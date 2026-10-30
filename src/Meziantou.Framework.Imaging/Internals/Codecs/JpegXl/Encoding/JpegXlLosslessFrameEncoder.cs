namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Compresses a lossless Modular frame with a learned tree (ISO/IEC 18181-1, sections G.1 and H): one tree and one entropy
/// code for the whole frame, in <c>LfGlobal</c>, which every group uses; the transforms of the frame are declared once.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// The transform: a palette of the colors (alpha included) when the frame has few enough of them, otherwise for three color
/// channels the reversible color transform whose residuals are the smallest with the gradient predictor.
/// </description></item>
/// <item><description>
/// The tree: rows of every group are sampled (at most <see cref="Settings.SamplesPerChannel"/> samples per channel), and
/// the tree of each channel is learned from them (<see cref="JpegXlTreeLearner"/>). The decisions on the channel, then
/// on the sub-bitstream when the palette is coded in the global data, lead to these trees.
/// </description></item>
/// <item><description>
/// The code: the contexts and the residuals of every group are computed, counted, and the code is built
/// (<see cref="JpegXlModularEntropyCode"/>); then they are computed again and written, group by group. Each group is coded
/// by one worker; the counts are sums and the sections are appended in order, so the bytes never depend on the workers.
/// </description></item>
/// </list>
/// The memory is bounded by the samples of the tree, the counts of the contexts and, for each worker, one group.
/// </remarks>
internal sealed class JpegXlLosslessFrameEncoder
{
    /// <summary>The smallest effort that learns a tree.</summary>
    public const int MinEffort = 3;

    private const int GroupDimension = 256;
    private const int GradientPredictor = 5;
    private const int WeightedPredictor = 6;
    private const int NoColorTransform = 0;
    private const int YCgCoTransform = 6;
    private const int SubtractGreenTransform = 10;

    private readonly int _width;
    private readonly int _height;
    private readonly int _colorChannelCount;
    private readonly int _channelCount;
    private readonly int _bitDepth;
    private readonly Settings _settings;
    private readonly int _maxDegreeOfParallelism;
    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly Action<JpegXlModularImage, int, int, int, int> _load;
    private readonly int _columns;
    private readonly int _rows;

    // The transform of the frame
    private int _colorTransform = NoColorTransform;
    private int[]? _palette;
    private Dictionary<ulong, int>? _paletteIndexes;

    // The tree
    private JpegXlMaTreeNode[] _tree = [];
    private int _leafCount;

    /// <param name="width">The width of the frame.</param>
    /// <param name="height">The height of the frame.</param>
    /// <param name="colorChannelCount">The number of color channels: 1 or 3.</param>
    /// <param name="hasAlpha">Whether an alpha channel follows the color channels.</param>
    /// <param name="bitDepth">The number of bits of a sample.</param>
    /// <param name="effort">The effort, from <see cref="MinEffort"/> to 9.</param>
    /// <param name="maxDegreeOfParallelism">The largest number of workers.</param>
    /// <param name="load">Copies the samples of a rectangle (x, y, width, height) to the channels of an image: the color channels, then the alpha channel.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between groups.</param>
    public JpegXlLosslessFrameEncoder(int width, int height, int colorChannelCount, bool hasAlpha, int bitDepth, int effort, int maxDegreeOfParallelism, Action<JpegXlModularImage, int, int, int, int> load, AllocationScope scope, CancellationToken cancellationToken)
    {
        _width = width;
        _height = height;
        _colorChannelCount = colorChannelCount;
        _channelCount = colorChannelCount + (hasAlpha ? 1 : 0);
        _bitDepth = bitDepth;
        _settings = Settings.Get(effort);
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        _load = load;
        _scope = scope;
        _cancellationToken = cancellationToken;
        _columns = (width + GroupDimension - 1) / GroupDimension;
        _rows = (height + GroupDimension - 1) / GroupDimension;
    }

    private int GroupCount => _columns * _rows;

    private bool IsSingleGroup => GroupCount == 1;

    /// <summary>The number of channels a group codes: the index channel then alpha with a palette, otherwise every channel.</summary>
    private int GroupChannelCount => _palette is null ? _channelCount : 1;

    /// <summary>Compresses the frame: its sections, after <c>LfGlobal</c> starts (once the LF dequantization flag is written), and their sizes.</summary>
    /// <param name="writer">The writer of the sections.</param>
    /// <param name="sizes">Receives the size of each section.</param>
    /// <param name="total">The length of the sections already written.</param>
    /// <param name="endSection">Ends a section: pads it and records its size.</param>
    public void Encode(LsbBitWriter writer, List<int> sizes, ref int total, EndSection endSection)
    {
        ChooseTransform();
        LearnTree();

        // The global data: the palette when the frame has groups, every channel when it has one
        using var global = CreateGlobalImage();
        using var literalCode = new JpegXlModularEntropyCode(_leafCount, _settings.MaxClusters, _settings.SearchConfigs, useLz77: false, _scope);
        CountStreams(literalCode, global, tokenCosts: null);

        // With LZ77, the copies are found with the costs of the values of the code without copies, then the smaller code wins
        JpegXlModularEntropyCode? lz77Code = null;
        float[]? tokenCosts = null;
        try
        {
            if (_settings.UseLz77)
            {
                tokenCosts = literalCode.GetTokenCosts();
                lz77Code = new JpegXlModularEntropyCode(_leafCount, _settings.MaxClusters, _settings.SearchConfigs, useLz77: true, _scope);
                CountStreams(lz77Code, global, tokenCosts);
            }

            literalCode.Build();
            var code = literalCode;
            if (lz77Code is not null)
            {
                lz77Code.Build();
                if (lz77Code.EstimatedBits < literalCode.EstimatedBits)
                {
                    code = lz77Code;
                }
                else
                {
                    tokenCosts = null;
                }
            }


            // LfGlobal: the tree and its code, then the global Modular data: its header, then its samples
            writer.WriteBool(true);
            JpegXlTreeCoder.WriteTree(writer, _tree, _scope);
            code.WriteDescription(writer);
            writer.WriteBool(true);
            writer.WriteBool(true);
            WriteTransforms(writer);
            if (global is not null)
            {
                using var worker = new StreamWorker(code, tokenCosts, _settings.Lz77Chain, counting: false, _scope);
                ProcessStream(global, 0, worker);
                code.WriteStream(worker.Stream!, writer);
            }
            else if (code.UsesAns)
            {
                // No channel is coded in the global data, and the decoder of libjxl reads nothing there; jxl-oxide reads the
                // state of the ANS coder all the same. An empty stream, the initial state, is read by both
                using var empty = new JpegXlEntropyCodeBuilder.StreamBuffer(_scope);
                code.WriteStream(empty, writer);
            }

            endSection(writer, sizes, ref total);
            if (IsSingleGroup)
                return;

            // The LF groups and HfGlobal are empty
            var lfGroupCount = GetLfGroupCount();
            for (var i = 0; i <= lfGroupCount; i++)
            {
                sizes.Add(0);
            }

            WriteGroups(writer, sizes, ref total, code, tokenCosts, lfGroupCount, endSection);
        }
        finally
        {
            lz77Code?.Dispose();
        }
    }

    /// <summary>Ends a section of the frame.</summary>
    public delegate void EndSection(LsbBitWriter writer, List<int> sizes, ref int total);

    /// <summary>Counts the values of the global data and of every group, with copies when <paramref name="tokenCosts"/> is set.</summary>
    private void CountStreams(JpegXlModularEntropyCode code, JpegXlModularImage? global, float[]? tokenCosts)
    {
        if (global is not null)
        {
            using var worker = new StreamWorker(code, tokenCosts, _settings.Lz77Chain, counting: true, _scope);
            ProcessStream(global, 0, worker);
        }

        if (IsSingleGroup)
            return;

        var lfGroupCount = GetLfGroupCount();
        var workers = JpegXlGroupWorkers.GetWorkerCount(_maxDegreeOfParallelism, GroupCount, (long)_width * _height);
        JpegXlGroupWorkers.Run(
            workers,
            GroupCount,
            () => new StreamWorker(code, tokenCosts, _settings.Lz77Chain, counting: true, _scope),
            (worker, group) =>
            {
                _cancellationToken.ThrowIfCancellationRequested();
                using var image = LoadGroup(group, includeMeta: false);
                ProcessStream(image, GetStreamIndex(group, lfGroupCount), worker);
            });
    }

    /// <summary>
    /// Computes the values of a sub-bitstream, every channel in order, and counts them or adds them to the stream of the
    /// worker, after finding the copies when the worker has a parser.
    /// </summary>
    private void ProcessStream(JpegXlModularImage image, int streamIndex, StreamWorker worker)
    {
        long length = 0;
        var distanceMultiplier = 0;
        foreach (var channel in image.Channels)
        {
            length += channel.Samples.Length;
            if (!channel.IsEmpty)
            {
                distanceMultiplier = Math.Max(distanceMultiplier, channel.Width);
            }
        }

        // The scratch memory of every channel, rented once
        var largest = 0;
        foreach (var channel in image.Channels)
        {
            largest = Math.Max(largest, channel.Samples.Length);
        }

        using var values = Av1PooledArray<ulong>.Rent(_scope, Math.Max(1, length), clear: false);
        using var work = Av1PooledArray<JpegXlMaTreeNode>.Rent(_scope, _tree.Length, clear: false);
        using var pending = Av1PooledArray<int>.Rent(_scope, _tree.Length, clear: false);
        using var residuals = Av1PooledArray<uint>.Rent(_scope, Math.Max(1, largest), clear: false);
        var offset = 0;
        for (var c = 0; c < image.Channels.Count; c++)
        {
            var samples = image.Channels[c].Samples.Length;
            if (samples == 0)
                continue;

            JpegXlTreeCoder.Collect(image, c, streamIndex, _tree, values.Span.Slice(offset, samples), work.Span, pending.Span, residuals.Span, _scope);
            offset += samples;
        }

        var span = values.Span[..offset];
        if (worker.Parser is null)
        {
            foreach (var value in span)
            {
                worker.AddValue((int)(value >> 32), (uint)value);
            }

            return;
        }

        worker.Parser.Parse(span, distanceMultiplier, ref worker.Sink);
    }

    /// <summary>Gets the stream index of a group of the first pass.</summary>
    private static int GetStreamIndex(int group, int lfGroupCount) => 1 + (3 * lfGroupCount) + JpegXlVarDctTables.QuantTableCount + group;

    private int GetLfGroupCount()
    {
        var lfDimension = GroupDimension * 8;
        return ((_width + lfDimension - 1) / lfDimension) * ((_height + lfDimension - 1) / lfDimension);
    }

    /// <summary>
    /// Chooses the transform of the frame. A palette of the colors (alpha included) when there are few of them; between 64
    /// colors and the largest palette of the effort, the palette unless a small tree learned on each estimates it much larger
    /// (<see cref="PaletteTrialMargin"/>). The color transform of three color channels is the one whose residuals with the
    /// gradient predictor are the smallest over the groups.
    /// </summary>
    private void ChooseTransform()
    {
        const int AlwaysPaletteColors = 64;
        var hasPalette = TryCreatePalette();
        if (_colorChannelCount == 3)
        {
            var palette = _palette;
            var indexes = _paletteIndexes;
            if (hasPalette && palette!.Length / _channelCount <= AlwaysPaletteColors)
                return;

            _palette = null;
            _paletteIndexes = null;
            ChooseColorTransform();
            if (hasPalette)
            {
                var transformCost = EstimateTrialCost();
                _palette = palette;
                _paletteIndexes = indexes;
                var paletteTrial = EstimateTrialCost() + EstimatePaletteCost();
                if (paletteTrial > transformCost * PaletteTrialMargin)
                {
                    _palette = null;
                    _paletteIndexes = null;
                }
                else
                {
                    _colorTransform = NoColorTransform;
                }
            }

            return;
        }

        if (hasPalette && _palette!.Length / _channelCount > AlwaysPaletteColors)
        {
            var palette = _palette;
            var indexes = _paletteIndexes;
            var paletteCost = EstimateTrialCost() + EstimatePaletteCost();
            _palette = null;
            _paletteIndexes = null;
            if (paletteCost <= EstimateTrialCost() * PaletteTrialMargin)
            {
                _palette = palette;
                _paletteIndexes = indexes;
            }
        }
    }

    /// <summary>
    /// How much larger than the color transform the trial of the palette may be: the trial underestimates the palette, whose
    /// indexes need a larger tree and are copied by LZ77 more often than the colors (on screenshots and graphics, a palette
    /// estimated 16 % larger wrote 0 to 28 % smaller files; on a photograph and smooth gradients it was 2.4 and 10 times
    /// larger).
    /// </summary>
    private const double PaletteTrialMargin = 1.25;

    /// <summary>The estimated size of the channels of the groups with a small tree learned on few samples.</summary>
    private double EstimateTrialCost()
    {
        const int TrialSamples = 32 * 1024;
        const int TrialLeaves = 32;
        LearnChannelTrees(Math.Min(TrialSamples, _settings.SamplesPerChannel), Math.Min(TrialLeaves, _settings.MaxLeavesPerChannel), out var cost);
        return cost;
    }

    /// <summary>Creates the palette of the colors of the frame, sorted by luma then by value, when they are few enough.</summary>
    private bool TryCreatePalette()
    {
        var limit = _settings.MaxPaletteColors;
        var pixelCount = (long)_width * _height;
        if (limit <= 0)
            return false;

        var colors = new HashSet<ulong>();
        limit = (int)Math.Min(limit, Math.Max(1, pixelCount / 8));
        for (var group = 0; group < GroupCount && colors.Count <= limit; group++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            using var image = LoadRaw(group);
            var length = image.Channels[0].Samples.Length;
            for (var i = 0; i < length && colors.Count <= limit; i++)
            {
                colors.Add(GetColor(image, i));
            }
        }

        if (colors.Count > limit)
            return false;

        // Neighbors in the palette are similar colors
        var sorted = colors.ToArray();
        Array.Sort(sorted, (a, b) =>
        {
            var comparison = GetLuma(a).CompareTo(GetLuma(b));
            return comparison != 0 ? comparison : a.CompareTo(b);
        });

        var count = sorted.Length;
        _palette = new int[count * _channelCount];
        _paletteIndexes = new Dictionary<ulong, int>(count);
        for (var i = 0; i < count; i++)
        {
            _paletteIndexes[sorted[i]] = i;
            for (var c = 0; c < _channelCount; c++)
            {
                _palette[(c * count) + i] = (int)((sorted[i] >> (16 * c)) & 0xFFFF);
            }
        }

        return true;
    }

    /// <summary>The color transform of three color channels whose residuals with the gradient predictor are the smallest over the groups (one in four when there are many).</summary>
    private void ChooseColorTransform()
    {
        ReadOnlySpan<int> candidates = [YCgCoTransform, SubtractGreenTransform, NoColorTransform];
        Span<double> costs = stackalloc double[candidates.Length];
        costs.Clear();
        var groupStep = GroupCount > 16 ? 4 : 1;
        for (var group = 0; group < GroupCount; group += groupStep)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            using var image = LoadRaw(group);
            var length = image.Channels[0].Samples.Length;
            using var original = Av1PooledArray<int>.Rent(_scope, (long)length * 3, clear: false);
            using var residuals = Av1PooledArray<uint>.Rent(_scope, length, clear: false);
            for (var c = 0; c < 3; c++)
            {
                image.Channels[c].Samples.CopyTo(original.Span.Slice(length * c, length));
            }

            for (var i = 0; i < candidates.Length; i++)
            {
                for (var c = 0; c < 3; c++)
                {
                    original.Span.Slice(length * c, length).CopyTo(image.Channels[c].Samples);
                }

                JpegXlModularEncoder.ApplyColorTransform(candidates[i], image.Channels[0].Samples, image.Channels[1].Samples, image.Channels[2].Samples);
                for (var c = 0; c < 3; c++)
                {
                    costs[i] += JpegXlModularEncoder.EstimateCost(image.Channels[c], GradientPredictor, residuals.Span, _scope);
                }
            }
        }

        var best = 0;
        for (var i = 1; i < candidates.Length; i++)
        {
            if (costs[i] < costs[best])
            {
                best = i;
            }
        }

        _colorTransform = candidates[best];
    }

    private ulong GetColor(JpegXlModularImage image, int index)
    {
        ulong color = 0;
        for (var c = 0; c < _channelCount; c++)
        {
            color |= (ulong)(uint)image.Channels[c].Samples[index] << (16 * c);
        }

        return color;
    }

    private long GetLuma(ulong color)
    {
        if (_colorChannelCount == 1)
            return (long)(color & 0xFFFF);

        // 2 R + 5 G + B: close to the luma, in integers
        return (2 * (long)(color & 0xFFFF)) + (5 * (long)((color >> 16) & 0xFFFF)) + (long)((color >> 32) & 0xFFFF);
    }

    /// <summary>Learns the tree of each channel from the sampled rows of the groups, and assembles the tree of the frame.</summary>
    private void LearnTree()
    {
        var channelTrees = LearnChannelTrees(_settings.SamplesPerChannel, _settings.MaxLeavesPerChannel, out _);

        // The channels of the groups, on a chain of decisions on the channel; the palette before them
        JpegXlTreeLearner.TreeNode root;
        if (_palette is null)
        {
            root = Chain(channelTrees, 0);
        }
        else
        {
            var paletteLeaf = JpegXlTreeLearner.TreeNode.CreateLeaf(ChoosePalettePredictor());
            if (IsSingleGroup)
            {
                root = Chain([paletteLeaf, .. channelTrees], 0);
            }
            else
            {
                // The sub-bitstream 0 is the global data, which only holds the palette
                const int StreamProperty = 1;
                root = JpegXlTreeLearner.TreeNode.CreateDecision(StreamProperty, 0, Chain(channelTrees, 0), paletteLeaf);
            }
        }

        _tree = JpegXlTreeCoder.Flatten(root, out _leafCount);

        static JpegXlTreeLearner.TreeNode Chain(JpegXlTreeLearner.TreeNode[] trees, int index)
        {
            const int ChannelProperty = 0;
            if (index == trees.Length - 1)
                return trees[index];

            return JpegXlTreeLearner.TreeNode.CreateDecision(ChannelProperty, index, Chain(trees, index + 1), trees[index]);
        }
    }

    /// <summary>Learns the tree of each channel of the groups from at most <paramref name="samplesPerChannel"/> samples of rows of every group.</summary>
    /// <param name="samplesPerChannel">The largest number of samples of a channel.</param>
    /// <param name="maxLeaves">The largest number of leaves of the tree of a channel.</param>
    /// <param name="cost">Receives the estimated size in bits of the channels.</param>
    private JpegXlTreeLearner.TreeNode[] LearnChannelTrees(int samplesPerChannel, int maxLeaves, out double cost)
    {
        // The indexes of a palette are not smooth: the weighted predictor and its property do not suit them
        var groupChannels = GroupChannelCount;
        var properties = _settings.Properties;
        var predictors = _settings.Predictors;
        if (_palette is not null)
        {
            properties = [.. properties.Where(property => property != JpegXlMaTree.WeightedProperty)];
            predictors = [.. predictors.Where(predictor => predictor != WeightedPredictor)];
        }

        // The rows of each group whose index is the phase of the group modulo the step: the number of samples is known
        // before any is taken, and they are rented at once
        var pixelCount = (long)_width * _height;
        var rowStep = (int)Math.Max(1, (pixelCount + samplesPerChannel - 1) / samplesPerChannel);
        long capacity = 0;
        for (var group = 0; group < GroupCount; group++)
        {
            var width = Math.Min(GroupDimension, _width - (group % _columns * GroupDimension));
            var height = Math.Min(GroupDimension, _height - (group / _columns * GroupDimension));
            var phase = (group * 7) % rowStep;
            if (height > phase)
            {
                capacity += (long)(((height - 1 - phase) / rowStep) + 1) * width;
            }
        }

        var sets = new JpegXlTreeLearner.SampleSet?[groupChannels];
        try
        {
            for (var c = 0; c < groupChannels; c++)
            {
                sets[c] = new JpegXlTreeLearner.SampleSet(properties, predictors, (int)capacity, _scope);
            }

            var lfGroupCount = GetLfGroupCount();
            for (var group = 0; group < GroupCount; group++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                using var image = LoadGroup(group, includeMeta: IsSingleGroup);
                var first = image.Channels.Count - groupChannels;
                var streamIndex = IsSingleGroup ? 0 : GetStreamIndex(group, lfGroupCount);
                for (var c = 0; c < groupChannels; c++)
                {
                    JpegXlTreeCoder.Sample(image, first + c, streamIndex, sets[c]!, rowStep, (group * 7) % rowStep, _scope);
                }
            }

            var learner = new JpegXlTreeLearner.LearnerSettings(maxLeaves, _settings.MaxBuckets, _settings.SplitCost, _settings.SplitPredictors);
            var trees = new JpegXlTreeLearner.TreeNode[groupChannels];
            cost = 0;
            for (var c = 0; c < groupChannels; c++)
            {
                var set = sets[c]!;
                set.Scale = set.Count == 0 ? 1 : (double)pixelCount / set.Count;
                trees[c] = JpegXlTreeLearner.Learn(set, learner, _scope, out var channelCost, _cancellationToken);
                cost += channelCost;

                // The samples of a channel are released once its tree is learned
                set.Dispose();
            }

            return trees;
        }
        finally
        {
            foreach (var set in sets)
            {
                set?.Dispose();
            }
        }
    }

    /// <summary>The estimated size in bits of the palette: its components with the predictor that suits them best.</summary>
    private double EstimatePaletteCost()
    {
        using var image = CreatePaletteImage();
        var channel = image.Channels[0];
        using var residuals = Av1PooledArray<uint>.Rent(_scope, channel.Samples.Length, clear: false);
        var best = double.MaxValue;
        foreach (var predictor in (ReadOnlySpan<int>)[0, 1, 2, GradientPredictor])
        {
            best = Math.Min(best, JpegXlModularEncoder.EstimateCost(channel, predictor, residuals.Span, _scope));
        }

        return best;
    }

    /// <summary>The predictor of the palette, whose rows are the components of the colors sorted by luma.</summary>
    private int ChoosePalettePredictor()
    {
        using var image = CreatePaletteImage();
        var channel = image.Channels[0];
        using var residuals = Av1PooledArray<uint>.Rent(_scope, channel.Samples.Length, clear: false);
        var best = 0;
        var bestCost = double.MaxValue;
        foreach (var predictor in (ReadOnlySpan<int>)[0, 1, 2, GradientPredictor])
        {
            var cost = JpegXlModularEncoder.EstimateCost(channel, predictor, residuals.Span, _scope);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = predictor;
            }
        }

        return best;
    }

    private JpegXlModularImage CreatePaletteImage()
    {
        var count = _palette!.Length / _channelCount;
        var image = new JpegXlModularImage(_bitDepth);
        try
        {
            image.AddChannel(count, _channelCount, -1, -1);
            image.Channels[0].Allocate(_scope);
            _palette.CopyTo(image.Channels[0].Samples);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>The image of the global data: the palette for a frame of several groups, every channel for a frame of one group.</summary>
    private JpegXlModularImage? CreateGlobalImage()
    {
        if (IsSingleGroup)
            return LoadGroup(0, includeMeta: true);

        return _palette is null ? null : CreatePaletteImage();
    }

    private void WriteGroups(LsbBitWriter writer, List<int> sizes, ref int total, JpegXlModularEntropyCode code, float[]? tokenCosts, int lfGroupCount, EndSection endSection)
    {
        var workers = JpegXlGroupWorkers.GetWorkerCount(_maxDegreeOfParallelism, GroupCount, (long)_width * _height);
        if (workers == 1)
        {
            using var worker = new StreamWorker(code, tokenCosts, _settings.Lz77Chain, counting: false, _scope);
            for (var group = 0; group < GroupCount; group++)
            {
                WriteGroup(writer, group, lfGroupCount, code, worker);
                endSection(writer, sizes, ref total);
            }

            return;
        }

        var sections = new LsbBitWriter?[GroupCount];
        try
        {
            JpegXlGroupWorkers.Run(
                workers,
                GroupCount,
                () => new StreamWorker(code, tokenCosts, _settings.Lz77Chain, counting: false, _scope),
                (worker, group) =>
                {
                    var section = sections[group] = new LsbBitWriter(_scope);
                    WriteGroup(section, group, lfGroupCount, code, worker);
                    section.ZeroPadToByte();
                });

            foreach (var section in sections)
            {
                writer.WriteAlignedBytes(section!.WrittenSpan);
                endSection(writer, sizes, ref total);
            }
        }
        finally
        {
            foreach (var section in sections)
            {
                section?.Dispose();
            }
        }
    }

    /// <summary>Writes a group: a sub-bitstream that uses the global tree and has no transform, then its samples.</summary>
    private void WriteGroup(LsbBitWriter writer, int group, int lfGroupCount, JpegXlModularEntropyCode code, StreamWorker worker)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        using var image = LoadGroup(group, includeMeta: false);
        writer.WriteBool(true);
        writer.WriteBool(true);
        writer.WriteU32(0, 0, 0, 1, 0, 2, 4, 18, 8);
        ProcessStream(image, GetStreamIndex(group, lfGroupCount), worker);
        code.WriteStream(worker.Stream!, writer);
    }

    /// <summary>Writes the transforms of the global data: the palette, the color transform, or none.</summary>
    private void WriteTransforms(LsbBitWriter writer)
    {
        if (_palette is not null)
        {
            writer.WriteU32(1, 0, 0, 1, 0, 2, 4, 18, 8);
            writer.WriteBits(1, 2);
            writer.WriteU32(0, 0, 3, 8, 6, 72, 10, 1096, 13);
            writer.WriteU32((uint)_channelCount, 1, 0, 3, 0, 4, 0, 1, 13);
            writer.WriteU32((uint)(_palette.Length / _channelCount), 0, 8, 256, 10, 1280, 12, 5376, 16);
            writer.WriteU32(0, 0, 0, 1, 8, 257, 10, 1281, 16);
            writer.WriteBits(0, 4);
            return;
        }

        if (_colorTransform == NoColorTransform)
        {
            writer.WriteU32(0, 0, 0, 1, 0, 2, 4, 18, 8);
            return;
        }

        writer.WriteU32(1, 0, 0, 1, 0, 2, 4, 18, 8);
        writer.WriteBits(0, 2);
        writer.WriteU32(0, 0, 3, 8, 6, 72, 10, 1096, 13);
        writer.WriteU32((uint)_colorTransform, 6, 0, 0, 2, 2, 4, 10, 6);
    }

    /// <summary>Loads the samples of a group, without transform.</summary>
    private JpegXlModularImage LoadRaw(int group)
    {
        var x0 = group % _columns * GroupDimension;
        var y0 = group / _columns * GroupDimension;
        var width = Math.Min(GroupDimension, _width - x0);
        var height = Math.Min(GroupDimension, _height - y0);
        var image = new JpegXlModularImage(_bitDepth);
        try
        {
            for (var i = 0; i < _channelCount; i++)
            {
                image.AddChannel(width, height, 0, 0);
                image.Channels[i].Allocate(_scope);
            }

            _load(image, x0, y0, width, height);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Loads the channels a group codes: the transformed samples, preceded, when <paramref name="includeMeta"/> is set, by
    /// the palette (the channels of the global data of a frame of one group).
    /// </summary>
    private JpegXlModularImage LoadGroup(int group, bool includeMeta)
    {
        var image = LoadRaw(group);
        try
        {
            if (_palette is not null)
            {
                // The colors become indexes; the alpha channel is in the palette
                var indexes = image.Channels[0].Samples;
                for (var i = 0; i < indexes.Length; i++)
                {
                    indexes[i] = _paletteIndexes![GetColor(image, i)];
                }

                for (var c = _channelCount - 1; c >= 1; c--)
                {
                    image.Channels[c].Dispose();
                    image.Channels.RemoveAt(c);
                }

                if (includeMeta)
                {
                    var count = _palette.Length / _channelCount;
                    image.InsertChannel(0, count, _channelCount, -1, -1);
                    image.MetaChannelCount = 1;
                    image.Channels[0].Allocate(_scope);
                    _palette.CopyTo(image.Channels[0].Samples);
                }
            }
            else if (_colorTransform != NoColorTransform)
            {
                JpegXlModularEncoder.ApplyColorTransform(_colorTransform, image.Channels[0].Samples, image.Channels[1].Samples, image.Channels[2].Samples);
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The state of a worker: the counts of the streams it codes, added to the code when the worker ends, or the stream it
    /// writes; and its parser of copies.
    /// </summary>
    private sealed class StreamWorker : IDisposable
    {
        private readonly JpegXlModularEntropyCode _code;
        private readonly JpegXlModularEntropyCode.Counter? _counter;

        public StreamWorker(JpegXlModularEntropyCode code, float[]? tokenCosts, int maxChain, bool counting, AllocationScope scope)
        {
            _code = code;
            if (counting)
            {
                _counter = code.CreateCounter();
            }
            else
            {
                Stream = new JpegXlEntropyCodeBuilder.StreamBuffer(scope);
            }

            if (tokenCosts is not null)
            {
                Parser = new JpegXlLz77Parser(tokenCosts, maxChain);
            }

            Sink = new ParserSink(code, _counter, Stream);
        }

        public JpegXlEntropyCodeBuilder.StreamBuffer? Stream { get; }

        public JpegXlLz77Parser? Parser { get; }

        public ParserSink Sink;

        public void AddValue(int context, uint value)
        {
            if (_counter is not null)
            {
                _counter.Add(context, value);
            }
            else
            {
                _code.Add(Stream!, context, value);
            }
        }

        public void Dispose()
        {
            if (_counter is not null)
            {
                lock (_code)
                {
                    _code.AddCounts(_counter);
                }

                _counter.Dispose();
            }

            Stream?.Dispose();
        }
    }

    /// <summary>Receives what the parser finds: counts it, or adds it to a stream.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct ParserSink(JpegXlModularEntropyCode code, JpegXlModularEntropyCode.Counter? counter, JpegXlEntropyCodeBuilder.StreamBuffer? stream) : JpegXlLz77Parser.IStreamSink
    {
        public void AddValue(int context, uint value)
        {
            if (counter is not null)
            {
                counter.Add(context, value);
            }
            else
            {
                code.Add(stream!, context, value);
            }
        }

        public void AddCopy(int context, int length, uint distance)
        {
            if (counter is not null)
            {
                counter.AddCopy(context, length, distance);
            }
            else
            {
                code.AddCopy(stream!, context, length, distance);
            }
        }
    }

    /// <summary>What an effort searches.</summary>
    internal sealed record Settings(int SamplesPerChannel, int[] Properties, int[] Predictors, int MaxLeavesPerChannel, int MaxBuckets, double SplitCost, int MaxClusters, bool SearchConfigs, int Lz77Chain, int MaxPaletteColors, int SplitPredictors = 1)
    {
        /// <summary>Gets a value indicating whether copies of previous values are searched.</summary>
        public bool UseLz77 => Lz77Chain > 0;

        public static Settings Get(int effort) => effort switch
        {
            <= 3 => new(16 * 1024, [6, 7, 9, 10, 11, 12], [GradientPredictor, 0, 1, 2], 32, 16, 48, 32, false, 0, 1024),
            4 => new(32 * 1024, [4, 5, 6, 7, 9, 10, 11, 12, 16, 17, 18, 19], [GradientPredictor, 0, 1, 2, 4], 64, 32, 40, 64, true, 0, 2048),
            5 => new(64 * 1024, [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 18, 19], [GradientPredictor, 0, 1, 2, 4, 13], 128, 32, 40, 128, true, 8, 4096),
            6 => new(96 * 1024, [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 18, 19], [GradientPredictor, 0, 1, 2, 4, 13], 192, 48, 36, 256, true, 16, 4096),
            7 => new(128 * 1024, [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19], [GradientPredictor, 0, 1, 2, 4, 13, 6], 160, 48, 32, 256, true, 32, 4096, 2),
            8 => new(256 * 1024, [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23], [GradientPredictor, 0, 1, 2, 4, 13, 6], 192, 64, 32, 256, true, 64, 8192, 2),
            _ => new(512 * 1024, [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23], [GradientPredictor, 0, 1, 2, 3, 4, 13, 6], 256, 64, 28, 256, true, 128, 8192, 3),
        };
    }
}
