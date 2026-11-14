using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Content-aware resizing by seam carving (Avidan and Shamir, "Seam Carving for Content-Aware Image Resizing", 2007), with
/// the forward energy of Rubinstein, Shamir and Avidan ("Improved Seam Carving for Video Retargeting", 2008).
/// </summary>
/// <remarks>
/// <para>
/// A vertical seam is a path of one pixel per row, each one at most one column away from the one above. Removing the seam
/// of least cost narrows the image by one pixel; the forward energy of a seam is the sum of the absolute differences
/// between the pixels its removal makes neighbors, the pixels beyond the edges being the edge pixels repeated. The pixels are described by integer features: for every frame, its Rec.
/// 709 luma premultiplied by alpha and its alpha, at the storage precision (<see cref="ToneSamples"/>). The differences of
/// all the frames are added, so that one set of seams is removed from every frame and an animation keeps its alignment.
/// </para>
/// <para>
/// The cost of a seam is compared first by the number of protected pixels minus the number of pixels to remove that it
/// crosses (<see cref="SeamCarvingOptions.ProtectedRegion"/>, <see cref="SeamCarvingOptions.RemovedRegion"/>), then by
/// its energy: integers, so the comparisons are exact and the result does not depend on the platform. Among the paths of
/// equal cost, the one that goes straight wins over the one from the left, which wins over the one from the right, and
/// the seam that ends in the leftmost column wins.
/// </para>
/// <para>
/// The width changes first, with vertical seams, then the height, with horizontal seams computed on the planes transposed
/// once. To enlarge, the seams that would be removed are found on a copy, and each one is duplicated: a pixel blended from
/// the seam pixel and its next neighbor (premultiplied by alpha) is inserted after it, or before it at the end of a line. An enlargement inserts at most half
/// of the current size at a time, so that the same seam is not duplicated again and again.
/// </para>
/// <para>
/// The plan is a list of steps, each one a map from the pixels of its output to one or two pixels of its input along the
/// same line: the steps are computed once, from the features, and applied to every frame.
/// </para>
/// </remarks>
internal sealed class SeamCarver : IDisposable
{
    private readonly List<Step> _steps = [];
    private readonly AllocationScope _scope;
    private PooledBuffer? _first;
    private PooledBuffer? _second;
    private long _largestPixelCount;

    private SeamCarver(AllocationScope scope, Size sourceSize, Size targetSize)
    {
        _scope = scope;
        SourceSize = sourceSize;
        TargetSize = targetSize;
    }

    public Size SourceSize { get; }

    public Size TargetSize { get; }

    /// <summary>
    /// Plans the seams of a resize from the features of the frames: <paramref name="features"/> holds
    /// <paramref name="channels"/> integers per pixel, in row order.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static SeamCarver Create(AllocationScope scope, Size sourceSize, Size targetSize, ReadOnlySpan<int> features, int channels, PixelMask? protectedRegion, PixelMask? removedRegion, CancellationToken cancellationToken)
    {
        var carver = new SeamCarver(scope, sourceSize, targetSize);
        try
        {
            carver.Plan(features, channels, protectedRegion, removedRegion, cancellationToken);
            carver._first = ResampleWeights.Rent<RgbaFloat>(scope, carver._largestPixelCount);
            carver._second = ResampleWeights.Rent<RgbaFloat>(scope, carver._largestPixelCount);
        }
        catch
        {
            carver.Dispose();
            throw;
        }

        return carver;
    }

    /// <summary>Applies the steps to one frame.</summary>
    public void Apply(PixelRowSource<RgbaFloat> source, PixelAccessor<RgbaFloat> destination, CancellationToken cancellationToken)
    {
        var current = unsafe(MemoryMarshal.Cast<byte, RgbaFloat>(_first!.Span));
        var next = unsafe(MemoryMarshal.Cast<byte, RgbaFloat>(_second!.Span));
        var width = SourceSize.Width;
        var height = SourceSize.Height;
        for (var y = 0; y < height; y++)
        {
            source.GetRow(y).CopyTo(current.Slice(y * width, width));
        }

        foreach (var step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            step.Apply(current, next, width);
            if (step.Horizontal)
            {
                height = step.OutputLength;
            }
            else
            {
                width = step.OutputLength;
            }

            var swap = current;
            current = next;
            next = swap;
        }

        for (var y = 0; y < height; y++)
        {
            current.Slice(y * width, width).CopyTo(destination.GetRowSpan(y));
        }
    }

    /// <summary>Gets where a pixel of the source lands in the result.</summary>
    public bool TryMapPoint(Point source, out Point destination)
    {
        var x = source.X;
        var y = source.Y;
        foreach (var step in _steps)
        {
            if (step.Horizontal)
            {
                if (!step.TryMapPosition(line: x, position: y, out y))
                {
                    destination = default;
                    return false;
                }
            }
            else if (!step.TryMapPosition(line: y, position: x, out x))
            {
                destination = default;
                return false;
            }
        }

        destination = new Point(x, y);
        return true;
    }

    public void Dispose()
    {
        foreach (var step in _steps)
        {
            step.Dispose();
        }

        _steps.Clear();
        _first?.Dispose();
        _first = null;
        _second?.Dispose();
        _second = null;
    }

    private void Plan(ReadOnlySpan<int> features, int channels, PixelMask? protectedRegion, PixelMask? removedRegion, CancellationToken cancellationToken)
    {
        _largestPixelCount = (long)SourceSize.Width * SourceSize.Height;
        if (TargetSize.Width != SourceSize.Width)
        {
            using var grid = Grid.Create(_scope, features, channels, protectedRegion, removedRegion, SourceSize, transpose: false);
            Resize(grid, TargetSize.Width, horizontal: false, cancellationToken);
            if (TargetSize.Height != SourceSize.Height)
            {
                using var transposed = grid.Transpose(_scope);
                Resize(transposed, TargetSize.Height, horizontal: true, cancellationToken);
            }
        }
        else if (TargetSize.Height != SourceSize.Height)
        {
            using var grid = Grid.Create(_scope, features, channels, protectedRegion, removedRegion, SourceSize, transpose: true);
            Resize(grid, TargetSize.Height, horizontal: true, cancellationToken);
        }
    }

    /// <summary>Removes or inserts seams in the grid until its lines have <paramref name="target"/> positions, recording the steps.</summary>
    private void Resize(Grid grid, int target, bool horizontal, CancellationToken cancellationToken)
    {
        if (target < grid.Length)
        {
            _steps.Add(grid.RemoveSeams(_scope, grid.Length - target, horizontal, cancellationToken));
            return;
        }

        while (grid.Length < target)
        {
            var count = Math.Min(target - grid.Length, Math.Max(1, grid.Length / 2));
            var step = grid.InsertSeams(_scope, count, horizontal, cancellationToken);
            _steps.Add(step);
            _largestPixelCount = Math.Max(_largestPixelCount, (long)step.OutputLength * step.LineCount);
        }
    }

    /// <summary>
    /// The features of the pixels and their mask scores, as lines of positions: the rows for vertical seams, the columns for
    /// horizontal ones. The lines keep their capacity when seams are removed.
    /// </summary>
    private sealed class Grid : IDisposable
    {
        private PooledBuffer? _features;
        private PooledBuffer? _scores;

        private Grid(PooledBuffer features, PooledBuffer scores, int lineCount, int length, int capacity, int channels)
        {
            _features = features;
            _scores = scores;
            LineCount = lineCount;
            Length = length;
            Capacity = capacity;
            Channels = channels;
        }

        public int LineCount { get; }

        public int Length { get; private set; }

        public int Capacity { get; private set; }

        public int Channels { get; }

        private Span<int> Features => unsafe(MemoryMarshal.Cast<byte, int>(_features!.Span))[..(LineCount * Capacity * Channels)];

        private Span<sbyte> Scores => unsafe(MemoryMarshal.Cast<byte, sbyte>(_scores!.Span))[..(LineCount * Capacity)];

        public static Grid Create(AllocationScope scope, ReadOnlySpan<int> features, int channels, PixelMask? protectedRegion, PixelMask? removedRegion, Size size, bool transpose)
        {
            var lineCount = transpose ? size.Width : size.Height;
            var length = transpose ? size.Height : size.Width;
            var grid = Allocate(scope, lineCount, length, length, channels);
            try
            {
                var destination = grid.Features;
                var scores = grid.Scores;
                for (var y = 0; y < size.Height; y++)
                {
                    for (var x = 0; x < size.Width; x++)
                    {
                        var pixel = (y * size.Width) + x;
                        var cell = transpose ? (x * length) + y : pixel;
                        features.Slice(pixel * channels, channels).CopyTo(destination.Slice(cell * channels, channels));
                        var score = 0;
                        if (protectedRegion is not null && protectedRegion.Get(pixel))
                        {
                            score++;
                        }

                        if (removedRegion is not null && removedRegion.Get(pixel))
                        {
                            score--;
                        }

                        scores[cell] = (sbyte)score;
                    }
                }
            }
            catch
            {
                grid.Dispose();
                throw;
            }

            return grid;
        }

        /// <summary>Gets a grid whose lines are the positions of this one (the columns of the rows).</summary>
        public Grid Transpose(AllocationScope scope)
        {
            var grid = Allocate(scope, Length, LineCount, LineCount, Channels);
            var source = Features;
            var sourceScores = Scores;
            var destination = grid.Features;
            var scores = grid.Scores;
            for (var line = 0; line < LineCount; line++)
            {
                for (var position = 0; position < Length; position++)
                {
                    var from = (line * Capacity) + position;
                    var to = (position * LineCount) + line;
                    source.Slice(from * Channels, Channels).CopyTo(destination.Slice(to * Channels, Channels));
                    scores[to] = sourceScores[from];
                }
            }

            return grid;
        }

        /// <summary>Removes <paramref name="count"/> seams, one at a time, and returns the map from the remaining pixels to the pixels of the grid before.</summary>
        public Step RemoveSeams(AllocationScope scope, int count, bool horizontal, CancellationToken cancellationToken)
        {
            var step = Step.Create(scope, horizontal, LineCount, Length, Length - count);
            try
            {
                using var origins = ResampleWeights.Rent<int>(scope, (long)LineCount * Capacity);
                using var seam = ResampleWeights.Rent<int>(scope, LineCount);
                using var dynamic = DynamicProgram.Create(scope, LineCount, Length);
                var origin = unsafe(MemoryMarshal.Cast<byte, int>(origins.Span))[..(LineCount * Capacity)];
                for (var line = 0; line < LineCount; line++)
                {
                    for (var position = 0; position < Length; position++)
                    {
                        origin[(line * Capacity) + position] = position;
                    }
                }

                var path = unsafe(MemoryMarshal.Cast<byte, int>(seam.Span))[..LineCount];
                for (var i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FindSeam(dynamic, path);
                    RemoveSeam(path, origin);
                }

                var map = step.Map;
                for (var line = 0; line < LineCount; line++)
                {
                    for (var position = 0; position < Length; position++)
                    {
                        var source = origin[(line * Capacity) + position];
                        var index = ((line * step.OutputLength) + position) * 2;
                        map[index] = source;
                        map[index + 1] = source;
                    }
                }
            }
            catch
            {
                step.Dispose();
                throw;
            }

            return step;
        }

        /// <summary>
        /// Finds <paramref name="count"/> seams on a copy of the grid, then duplicates each of them in the grid, and returns
        /// the map from the pixels of the larger grid to the pixels of the grid before.
        /// </summary>
        public Step InsertSeams(AllocationScope scope, int count, bool horizontal, CancellationToken cancellationToken)
        {
            var length = Length;
            var step = Step.Create(scope, horizontal, LineCount, length, length + count);
            try
            {
                using (var copy = Copy(scope))
                using (var origins = ResampleWeights.Rent<int>(scope, (long)LineCount * copy.Capacity))
                using (var marks = ResampleWeights.Rent<byte>(scope, (long)LineCount * length))
                using (var seam = ResampleWeights.Rent<int>(scope, LineCount))
                using (var dynamic = DynamicProgram.Create(scope, LineCount, length))
                {
                    var origin = unsafe(MemoryMarshal.Cast<byte, int>(origins.Span))[..(LineCount * copy.Capacity)];
                    var marked = marks.Span[..(LineCount * length)];
                    for (var line = 0; line < LineCount; line++)
                    {
                        for (var position = 0; position < length; position++)
                        {
                            origin[(line * copy.Capacity) + position] = position;
                        }
                    }

                    var path = unsafe(MemoryMarshal.Cast<byte, int>(seam.Span))[..LineCount];
                    for (var i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        copy.FindSeam(dynamic, path);
                        for (var line = 0; line < LineCount; line++)
                        {
                            marked[(line * length) + origin[(line * copy.Capacity) + path[line]]] = 1;
                        }

                        copy.RemoveSeam(path, origin);
                    }

                    // Each marked pixel is followed by a pixel blended from it and its next neighbor; at the end of a line, the
                    // blend of the pixel and the previous one comes before it, so that the edge pixel stays at the edge
                    var map = step.Map;
                    for (var line = 0; line < LineCount; line++)
                    {
                        var output = line * step.OutputLength * 2;
                        for (var position = 0; position < length; position++)
                        {
                            var isMarked = marked[(line * length) + position] != 0;
                            if (isMarked && position + 1 == length && position > 0)
                            {
                                map[output++] = position - 1;
                                map[output++] = position;
                                isMarked = false;
                            }

                            map[output++] = position;
                            map[output++] = position;
                            if (isMarked)
                            {
                                map[output++] = position;
                                map[output++] = position + 1 < length ? position + 1 : position;
                            }
                        }
                    }
                }

                Grow(scope, step);
            }
            catch
            {
                step.Dispose();
                throw;
            }

            return step;
        }

        public void Dispose()
        {
            _features?.Dispose();
            _features = null;
            _scores?.Dispose();
            _scores = null;
        }

        private static Grid Allocate(AllocationScope scope, int lineCount, int length, int capacity, int channels)
        {
            var cells = (long)lineCount * capacity;
            var features = ResampleWeights.Rent<int>(scope, cells * channels);
            try
            {
                return new Grid(features, ResampleWeights.Rent<sbyte>(scope, cells), lineCount, length, capacity, channels);
            }
            catch
            {
                features.Dispose();
                throw;
            }
        }

        private Grid Copy(AllocationScope scope)
        {
            var copy = Allocate(scope, LineCount, Length, Capacity, Channels);
            Features.CopyTo(copy.Features);
            Scores.CopyTo(copy.Scores);
            return copy;
        }

        /// <summary>Replaces the grid by the result of an insertion step: blended features (rounded up) and the score of the seam pixel.</summary>
        private void Grow(AllocationScope scope, Step step)
        {
            var length = step.OutputLength;
            var features = ResampleWeights.Rent<int>(scope, (long)LineCount * length * Channels);
            PooledBuffer scoresBuffer;
            try
            {
                scoresBuffer = ResampleWeights.Rent<sbyte>(scope, (long)LineCount * length);
            }
            catch
            {
                features.Dispose();
                throw;
            }

            var source = Features;
            var sourceScores = Scores;
            var destination = unsafe(MemoryMarshal.Cast<byte, int>(features.Span))[..(LineCount * length * Channels)];
            var scores = unsafe(MemoryMarshal.Cast<byte, sbyte>(scoresBuffer.Span))[..(LineCount * length)];
            var map = step.Map;
            for (var line = 0; line < LineCount; line++)
            {
                for (var position = 0; position < length; position++)
                {
                    var index = (line * length) + position;
                    var first = (line * Capacity) + map[2 * index];
                    var second = (line * Capacity) + map[(2 * index) + 1];
                    for (var c = 0; c < Channels; c++)
                    {
                        destination[(index * Channels) + c] = (int)(((long)source[(first * Channels) + c] + source[(second * Channels) + c] + 1) >> 1);
                    }

                    scores[index] = sourceScores[first];
                }
            }

            Dispose();
            _features = features;
            _scores = scoresBuffer;
            Length = length;
            Capacity = length;
        }

        /// <summary>Gets the sum over the channels of the absolute differences of the features of two cells.</summary>
        private long Difference(ReadOnlySpan<int> features, int first, int second)
        {
            var a = features.Slice(first * Channels, Channels);
            var b = features.Slice(second * Channels, Channels);
            var sum = 0L;
            for (var c = 0; c < a.Length; c++)
            {
                sum += Math.Abs((long)a[c] - b[c]);
            }

            return sum;
        }

        /// <summary>Finds the seam of least cost: <paramref name="path"/> receives its position in every line.</summary>
        private void FindSeam(DynamicProgram dynamic, Span<int> path)
        {
            var features = Features;
            var scores = Scores;
            var length = Length;
            var costs = dynamic.Costs;
            var counts = dynamic.Counts;
            var moves = dynamic.Moves;
            for (var line = 0; line < LineCount; line++)
            {
                var row = line * Capacity;
                var previous = (line - 1) * Capacity;
                for (var position = 0; position < length; position++)
                {
                    var cell = row + position;

                    // The pixels the removal makes neighbors: left and right, and the pixel above with either of them. The
                    // edge pixels are repeated beyond the line, so that removing an edge pixel costs its difference with
                    // its neighbor
                    var leftCell = position > 0 ? cell - 1 : cell;
                    var rightCell = position + 1 < length ? cell + 1 : cell;
                    var up = Difference(features, leftCell, rightCell);
                    var score = (int)scores[cell];
                    var index = (line * length) + position;
                    if (line == 0)
                    {
                        costs[index] = up;
                        counts[index] = score;
                        moves[index] = 0;
                        continue;
                    }

                    var above = ((line - 1) * length) + position;
                    var bestCount = counts[above];
                    var bestCost = costs[above] + up;
                    var move = (sbyte)0;
                    if (position > 0)
                    {
                        var left = Difference(features, previous + position, leftCell);
                        Consider(counts[above - 1], costs[above - 1] + up + left, -1, ref bestCount, ref bestCost, ref move);
                    }

                    if (position + 1 < length)
                    {
                        var right = Difference(features, previous + position, rightCell);
                        Consider(counts[above + 1], costs[above + 1] + up + right, 1, ref bestCount, ref bestCost, ref move);
                    }

                    costs[index] = bestCost;
                    counts[index] = bestCount + score;
                    moves[index] = move;
                }
            }

            // The seam that ends with the least cost, the leftmost among equal ones, then back up through the moves
            var last = (LineCount - 1) * length;
            var end = 0;
            for (var position = 1; position < length; position++)
            {
                if (counts[last + position] < counts[last + end] || (counts[last + position] == counts[last + end] && costs[last + position] < costs[last + end]))
                {
                    end = position;
                }
            }

            for (var line = LineCount - 1; line >= 0; line--)
            {
                path[line] = end;
                end += moves[(line * length) + end];
            }
        }

        private static void Consider(int count, long cost, sbyte direction, ref int bestCount, ref long bestCost, ref sbyte move)
        {
            if (count < bestCount || (count == bestCount && cost < bestCost))
            {
                bestCount = count;
                bestCost = cost;
                move = direction;
            }
        }

        /// <summary>Removes the cells of a seam from every line, with their origins.</summary>
        private void RemoveSeam(ReadOnlySpan<int> path, Span<int> origin)
        {
            var features = Features;
            var scores = Scores;
            var length = Length;
            for (var line = 0; line < LineCount; line++)
            {
                var row = line * Capacity;
                var position = path[line];
                var tail = length - position - 1;
                features.Slice((row + position + 1) * Channels, tail * Channels).CopyTo(features.Slice((row + position) * Channels));
                scores.Slice(row + position + 1, tail).CopyTo(scores.Slice(row + position));
                origin.Slice(row + position + 1, tail).CopyTo(origin.Slice(row + position));
            }

            Length = length - 1;
        }
    }

    /// <summary>The tables of the search of a seam: the cost and the mask count of the best path to every cell, and its last move.</summary>
    private sealed class DynamicProgram : IDisposable
    {
        private readonly int _cells;
        private PooledBuffer? _costs;
        private PooledBuffer? _counts;
        private PooledBuffer? _moves;

        private DynamicProgram(int cells)
        {
            _cells = cells;
        }

        public Span<long> Costs => unsafe(MemoryMarshal.Cast<byte, long>(_costs!.Span))[.._cells];

        public Span<int> Counts => unsafe(MemoryMarshal.Cast<byte, int>(_counts!.Span))[.._cells];

        public Span<sbyte> Moves => unsafe(MemoryMarshal.Cast<byte, sbyte>(_moves!.Span))[.._cells];

        public static DynamicProgram Create(AllocationScope scope, int lineCount, int length)
        {
            var cells = (long)lineCount * length;
            var program = new DynamicProgram((int)cells);
            try
            {
                program._costs = ResampleWeights.Rent<long>(scope, cells);
                program._counts = ResampleWeights.Rent<int>(scope, cells);
                program._moves = ResampleWeights.Rent<sbyte>(scope, cells);
            }
            catch
            {
                program.Dispose();
                throw;
            }

            return program;
        }

        public void Dispose()
        {
            _costs?.Dispose();
            _costs = null;
            _counts?.Dispose();
            _counts = null;
            _moves?.Dispose();
            _moves = null;
        }
    }

    /// <summary>One step of the plan: for every pixel of its output, two positions of the same line of its input whose pixels it blends (twice the same for a copy).</summary>
    private sealed class Step : IDisposable
    {
        private PooledBuffer? _map;

        private Step(bool horizontal, int lineCount, int inputLength, int outputLength)
        {
            Horizontal = horizontal;
            LineCount = lineCount;
            InputLength = inputLength;
            OutputLength = outputLength;
        }

        /// <summary>Gets whether the lines are the columns of the image (horizontal seams).</summary>
        public bool Horizontal { get; }

        public int LineCount { get; }

        public int InputLength { get; }

        public int OutputLength { get; }

        public Span<int> Map => unsafe(MemoryMarshal.Cast<byte, int>(_map!.Span))[..(2 * LineCount * OutputLength)];

        public static Step Create(AllocationScope scope, bool horizontal, int lineCount, int inputLength, int outputLength)
        {
            var step = new Step(horizontal, lineCount, inputLength, outputLength);
            step._map = ResampleWeights.Rent<int>(scope, 2L * lineCount * outputLength);
            return step;
        }

        /// <summary>Gets the output position of the pixel copied from an input position, the first one when it is copied twice.</summary>
        public bool TryMapPosition(int line, int position, out int result)
        {
            var map = Map.Slice(2 * line * OutputLength, 2 * OutputLength);
            for (var i = 0; i < OutputLength; i++)
            {
                if (map[2 * i] == position && map[(2 * i) + 1] == position)
                {
                    result = i;
                    return true;
                }
            }

            result = 0;
            return false;
        }

        /// <summary>Applies the step to the pixels of a frame <paramref name="width"/> pixels wide, in row order.</summary>
        public void Apply(ReadOnlySpan<RgbaFloat> source, Span<RgbaFloat> destination, int width)
        {
            var map = Map;
            var outputWidth = Horizontal ? width : OutputLength;
            for (var line = 0; line < LineCount; line++)
            {
                for (var i = 0; i < OutputLength; i++)
                {
                    var first = map[2 * ((line * OutputLength) + i)];
                    var second = map[(2 * ((line * OutputLength) + i)) + 1];
                    int from;
                    int other;
                    int to;
                    if (Horizontal)
                    {
                        from = (first * width) + line;
                        other = (second * width) + line;
                        to = (i * outputWidth) + line;
                    }
                    else
                    {
                        from = (line * width) + first;
                        other = (line * width) + second;
                        to = (line * outputWidth) + i;
                    }

                    destination[to] = first == second ? source[from] : Blend(source[from], source[other]);
                }
            }
        }

        public void Dispose()
        {
            _map?.Dispose();
            _map = null;
        }

        /// <summary>Gets the mean of two pixels, premultiplied by alpha.</summary>
        private static RgbaFloat Blend(RgbaFloat a, RgbaFloat b)
        {
            var alpha = ((double)a.A + b.A) / 2;
            if (alpha == 0)
                return new RgbaFloat((float)(((double)a.R + b.R) / 2), (float)(((double)a.G + b.G) / 2), (float)(((double)a.B + b.B) / 2), 0);

            return new RgbaFloat(
                (float)((((double)a.R * a.A) + ((double)b.R * b.A)) / 2 / alpha),
                (float)((((double)a.G * a.A) + ((double)b.G * b.A)) / 2 / alpha),
                (float)((((double)a.B * a.A) + ((double)b.B * b.A)) / 2 / alpha),
                (float)alpha);
        }
    }
}
