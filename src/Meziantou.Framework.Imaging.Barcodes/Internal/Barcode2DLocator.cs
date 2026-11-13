using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// Locates the Data Matrix, PDF417 and Aztec symbols of a binary image, samples their modules and decodes them:
/// <list type="bullet">
/// <item>Data Matrix: the region of dark pixels that holds the solid sides of the finder pattern; its minimum-area
/// rectangle has two solid sides, and the alternating modules of the two other sides give the number of modules;</item>
/// <item>Aztec: the concentric squares of the bullseye, whose inner ring gives the orientation;</item>
/// <item>PDF417: the start and stop patterns of the rows.</item>
/// </list>
/// The decoder finds the orientation of a Data Matrix or Aztec symbol by itself.
/// </summary>
internal static class Barcode2DLocator
{

    private static readonly (int Height, int Width)[] DataMatrixSizes =
    [
        (10, 10), (12, 12), (14, 14), (16, 16), (18, 18), (20, 20), (22, 22), (24, 24), (26, 26), (32, 32), (36, 36), (40, 40),
        (44, 44), (48, 48), (52, 52), (64, 64), (72, 72), (80, 80), (88, 88), (96, 96), (104, 104), (120, 120), (132, 132), (144, 144),
        (8, 18), (8, 32), (12, 26), (12, 36), (16, 36), (16, 48),
    ];

    public static void Locate(BinaryImage image, Func<Barcode2DType, bool> isIncluded, List<Barcode2DImageResult> results, int maximumResults, CancellationToken cancellationToken)
    {
        if (isIncluded(Barcode2DType.Aztec) && results.Count < maximumResults)
        {
            LocateAztec(image, results, maximumResults, cancellationToken);
        }

        if (isIncluded(Barcode2DType.DataMatrix) && results.Count < maximumResults)
        {
            LocateDataMatrix(image, results, maximumResults, cancellationToken);
        }

        if (isIncluded(Barcode2DType.Pdf417) && results.Count < maximumResults)
        {
            LocatePdf417(image, results, maximumResults, cancellationToken);
        }
    }

    // ───── Data Matrix ─────

    private static void LocateDataMatrix(BinaryImage image, List<Barcode2DImageResult> results, int maximumResults, CancellationToken cancellationToken)
    {
        foreach (var hull in FindComponentHulls(image, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= maximumResults)
                return;

            if (Geometry.GetMinimumAreaRectangle(hull) is not { } corners)
                continue;

            // Two adjacent sides are solid: the finder pattern
            var center = new PointD(corners.Average(corner => corner.X), corners.Average(corner => corner.Y));
            var darkness = new double[4];
            for (var side = 0; side < 4; side++)
            {
                // The edges of a turned symbol are jagged: the darkest of a few depths into the side
                foreach (var inset in (ReadOnlySpan<double>)[1, 1.5, 2.5])
                {
                    darkness[side] = Math.Max(darkness[side], GetDarkness(image, corners[side], corners[(side + 1) % 4], center, inset));
                }
            }

            var solidCorner = -1;
            for (var side = 0; side < 4; side++)
            {
                if (darkness[side] > 0.8 && darkness[(side + 1) % 4] > 0.8 && darkness[(side + 2) % 4] < 0.75 && darkness[(side + 3) % 4] < 0.75)
                {
                    solidCorner = side;
                }
            }

            if (solidCorner < 0)
                continue;

            // The module is as thick as a solid side where no dark module is next to it; the alternating sides have a dark
            // module every other module
            var thicknesses = GetSolidThicknesses(image, corners[solidCorner], corners[(solidCorner + 1) % 4], center)
                .Concat(GetSolidThicknesses(image, corners[(solidCorner + 1) % 4], corners[(solidCorner + 2) % 4], center))
                .Where(thickness => thickness > 0)
                .Order()
                .ToList();
            var moduleSize = thicknesses.Count > 1 ? thicknesses[1] : 0;
            if (moduleSize < 1)
                continue;

            var timing1 = (solidCorner + 2) % 4;
            var timing2 = (solidCorner + 3) % 4;
            var count1 = 2 * CountDarkRuns(image, corners[timing1], corners[(timing1 + 1) % 4], center, moduleSize / 2, moduleSize);
            var count2 = 2 * CountDarkRuns(image, corners[timing2], corners[(timing2 + 1) % 4], center, moduleSize / 2, moduleSize);

            // The sides 0 and 2 have the width of the rectangle in modules, the sides 1 and 3 its height
            var (width, height) = timing1 % 2 == 0 ? (count1, count2) : (count2, count1);
            foreach (var (h, w) in GetDataMatrixSizeCandidates(height, width))
            {
                var transform = Homography.Create([new(0, 0), new(w, 0), new(w, h), new(0, h)], corners);
                if (transform is null)
                    continue;

                var modules = Sample(image, transform, h, w);
                if (Barcode2DDecoder.TryDecode(modules, Barcode2DType.DataMatrix, out var result))
                {
                    Add(results, new Barcode2DImageResult(result, OrderCorners(corners)));
                    break;
                }
            }
        }
    }

    private static IEnumerable<(int Height, int Width)> GetDataMatrixSizeCandidates(int height, int width)
    {
        // The counts can be one or two modules off on a blurred edge: the closest sizes first
        return DataMatrixSizes
            .Select(size => (Size: size, Distance: Math.Abs(size.Height - height) + Math.Abs(size.Width - width)))
            .Where(candidate => candidate.Distance <= 4)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Size);
    }

    // The share of dark pixels along a side, just inside the rectangle
    private static double GetDarkness(BinaryImage image, PointD from, PointD to, PointD center, double inset)
    {
        var length = from.DistanceTo(to);
        var samples = Math.Max(8, (int)length);
        var dark = 0;
        for (var i = 0; i < samples; i++)
        {
            var point = MoveToward(Geometry.Lerp(from, to, (i + 0.5) / samples), center, inset);
            if (image.IsDark(point.X, point.Y))
            {
                dark++;
            }
        }

        return dark / (double)samples;
    }

    // The widths of the dark band along a solid side, at a few places: the thickness of the finder pattern, or more where
    // dark modules are next to it
    private static List<double> GetSolidThicknesses(BinaryImage image, PointD from, PointD to, PointD center)
    {
        var thicknesses = new List<double>();
        for (var i = 1; i < 10; i++)
        {
            // From the first dark pixel, which a jagged edge can push a pixel or two inside
            var start = Geometry.Lerp(from, to, i / 10.0);
            var distance = start.DistanceTo(center);
            var offset = 0.25;
            while (offset < 3 && !image.IsDark(MoveToward(start, center, offset)))
            {
                offset += 0.25;
            }

            var end = offset;
            while (end < distance && image.IsDark(MoveToward(start, center, end)))
            {
                end += 0.25;
            }

            thicknesses.Add(end - offset);
        }

        return thicknesses;
    }

    // The dark runs along a side, a run counting once it is a third of a module long
    private static int CountDarkRuns(BinaryImage image, PointD from, PointD to, PointD center, double inset, double moduleSize)
    {
        var length = from.DistanceTo(to);
        const double Step = 0.5;
        var runs = 0;
        var runLength = 0d;
        var counted = false;
        for (var position = 0d; position <= length; position += Step)
        {
            if (image.IsDark(MoveToward(Geometry.Lerp(from, to, position / length), center, inset)))
            {
                runLength += Step;
                if (!counted && runLength >= moduleSize / 3)
                {
                    runs++;
                    counted = true;
                }
            }
            else
            {
                runLength = 0;
                counted = false;
            }
        }

        return runs;
    }

    private static PointD MoveToward(PointD point, PointD target, double distance)
    {
        var length = point.DistanceTo(target);
        if (length < 1e-9)
            return point;

        return new PointD(point.X + ((target.X - point.X) / length * distance), point.Y + ((target.Y - point.Y) / length * distance));
    }

    // The convex hull of each region of connected dark pixels (8-connectivity) large enough to be a symbol
    private static IEnumerable<List<PointD>> FindComponentHulls(BinaryImage image, CancellationToken cancellationToken)
    {
        var labels = new int[image.Width * image.Height];
        var stack = new Stack<int>();
        var label = 0;
        for (var start = 0; start < labels.Length; start++)
        {
            if (labels[start] is not 0 || !image.IsDark(start % image.Width, start / image.Width))
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            label++;
            labels[start] = label;
            stack.Push(start);
            var extremes = new Dictionary<int, (int Min, int Max)>();
            var count = 0;
            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var x = index % image.Width;
                var y = index / image.Width;
                count++;
                extremes[y] = extremes.TryGetValue(y, out var extreme) ? (Math.Min(extreme.Min, x), Math.Max(extreme.Max, x)) : (x, x);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if ((uint)nx >= (uint)image.Width || (uint)ny >= (uint)image.Height)
                            continue;

                        var neighbor = (ny * image.Width) + nx;
                        if (labels[neighbor] is 0 && image.IsDark(nx, ny))
                        {
                            labels[neighbor] = label;
                            stack.Push(neighbor);
                        }
                    }
                }
            }

            if (count < 30 || extremes.Count < 6)
                continue;

            // The hull of the pixels: the corners of the first and last pixel of each row
            var points = new List<PointD>(extremes.Count * 4);
            foreach (var (y, (min, max)) in extremes)
            {
                points.Add(new PointD(min, y));
                points.Add(new PointD(min, y + 1));
                points.Add(new PointD(max + 1, y));
                points.Add(new PointD(max + 1, y + 1));
            }

            yield return Geometry.GetConvexHull(points);
        }
    }

    // ───── Aztec ─────

    private static void LocateAztec(BinaryImage image, List<Barcode2DImageResult> results, int maximumResults, CancellationToken cancellationToken)
    {
        foreach (var (center, bullseyeModuleSize, isFullRange) in FindBullseyes(image, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= maximumResults)
                return;

            // A row through the center crosses the rings of a turned symbol 1 / cos(angle) modules wide
            var angle = EstimateBullseyeAngle(image, center, bullseyeModuleSize);
            var tilt = Math.IEEERemainder(angle, Math.PI / 2);
            var moduleSize = bullseyeModuleSize * Math.Cos(tilt);
            var u = new PointD(Math.Cos(angle), Math.Sin(angle));
            var v = new PointD(-u.Y, u.X);

            // The rings of a compact bullseye can continue in the mode message as if it were a full-range one: both are tried
            foreach (var compact in isFullRange ? (ReadOnlySpan<bool>)[true, false] : [true])
            {
                if (TryDecodeAztec(image, center, u, v, moduleSize, compact) is { } result)
                {
                    Add(results, result);
                    break;
                }
            }
        }
    }

    private static Barcode2DImageResult? TryDecodeAztec(BinaryImage image, PointD center, PointD u, PointD v, double moduleSize, bool compact)
    {
        // The orientation marks around the bullseye: 3, 2, 1 and 0 dark modules at the corners of the mode message ring
        if (!HasOrientationMarks(image, center, u, v, moduleSize, compact ? 5 : 7))
            return null;

        for (var layers = 1; layers <= (compact ? 4 : 32); layers++)
        {
            var size = GetAztecSize(compact, layers);
            if (size * moduleSize > 2 * Math.Max(image.Width, image.Height))
                break;

            // The module size is measured on a few pixels: the error grows towards the edges of a large symbol
            foreach (var scale in (ReadOnlySpan<double>)[1, 0.99, 1.01, 0.98, 1.02])
            {
                var module = moduleSize * scale;
                var origin = new PointD(center.X - (size / 2.0 * module * (u.X + v.X)), center.Y - (size / 2.0 * module * (u.Y + v.Y)));
                var transform = Homography.Create(
                    [new(0, 0), new(size, 0), new(0, size)],
                    [origin, new(origin.X + (size * module * u.X), origin.Y + (size * module * u.Y)), new(origin.X + (size * module * v.X), origin.Y + (size * module * v.Y))]);
                if (transform is null)
                    continue;

                var modules = Sample(image, transform, size, size);
                if (Barcode2DDecoder.TryDecode(modules, Barcode2DType.Aztec, out var result))
                    return new Barcode2DImageResult(result, OrderCorners([transform.Transform(0, 0), transform.Transform(size, 0), transform.Transform(size, size), transform.Transform(0, size)]));

                if (layers > 2 && scale is 1 && size * moduleSize * 0.01 < 0.25)
                    break;
            }
        }

        return null;
    }

    private static bool HasOrientationMarks(BinaryImage image, PointD center, PointD u, PointD v, double moduleSize, int radius)
    {
        bool IsDark(int column, int row) => image.IsDark(center.X + (((column * u.X) + (row * v.X)) * moduleSize), center.Y + (((column * u.Y) + (row * v.Y)) * moduleSize));

        // The corner module and its two neighbors on the ring, at each corner
        Span<int> counts = stackalloc int[4];
        ReadOnlySpan<(int X, int Y)> corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)];
        for (var k = 0; k < 4; k++)
        {
            var (sx, sy) = corners[k];
            counts[k] = (IsDark(sx * radius, sy * radius) ? 1 : 0) + (IsDark(sx * (radius - 1), sy * radius) ? 1 : 0) + (IsDark(sx * radius, sy * (radius - 1)) ? 1 : 0);
        }

        counts.Sort();
        var errors = 0;
        for (var k = 0; k < 4; k++)
        {
            errors += Math.Abs(counts[k] - k);
        }

        return errors <= 1;
    }

    // ISO/IEC 24778: compact symbols are 11 + 4 layers modules wide; full-range ones have a reference grid line every 16 modules
    private static int GetAztecSize(bool compact, int layers)
    {
        if (compact)
            return 11 + (4 * layers);

        var baseSize = 14 + (4 * layers);
        return baseSize + 1 + (2 * (((baseSize / 2) - 1) / 15));
    }

    // The bullseye: a row and a column through its center cross 9 (compact) or 13 (full-range) runs of one module, alternately dark and light
    private static List<(PointD Center, double ModuleSize, bool IsFullRange)> FindBullseyes(BinaryImage image, CancellationToken cancellationToken)
    {
        var found = new List<(PointD Center, double ModuleSize, bool IsFullRange, int Count)>();
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runs = image.GetRowRuns(y);
            var positions = new int[runs.Length + 1];
            for (var i = 0; i < runs.Length; i++)
            {
                positions[i + 1] = positions[i] + runs[i];
            }

            // A dark center run with 3 runs of the same width on each side, then the outer dark ring, which can merge with the
            // dark modules of the mode message around it
            for (var c = 5; c + 4 < runs.Length; c += 2)
            {
                if (!IsBullseye(runs, c, 3))
                    continue;

                var isFullRange = IsBullseye(runs, c, 5);
                var centerX = positions[c] + (runs[c] / 2.0);
                var moduleSize = (positions[c + 4] - positions[c - 3]) / 7.0;
                var column = image.GetColumnRuns((int)centerX);
                var row = FindRun(column, y);
                if (!ScanLineIsDark(row) || !IsBullseye(column, row, 3))
                    continue;

                var columnPositions = 0;
                for (var i = 0; i < row; i++)
                {
                    columnPositions += column[i];
                }

                var centerY = columnPositions + (column[row] / 2.0);
                var verticalModule = column.Skip(row - 3).Take(7).Sum() / 7.0;
                if (Math.Abs(verticalModule - moduleSize) > moduleSize * 0.4)
                    continue;

                var center = new PointD(centerX, centerY);
                var index = found.FindIndex(item => item.Center.DistanceTo(center) < item.ModuleSize * 2);
                if (index < 0)
                {
                    found.Add((center, (moduleSize + verticalModule) / 2, isFullRange, 1));
                }
                else
                {
                    var item = found[index];
                    found[index] = (new PointD(((item.Center.X * item.Count) + center.X) / (item.Count + 1), ((item.Center.Y * item.Count) + center.Y) / (item.Count + 1)), item.ModuleSize, item.IsFullRange || isFullRange, item.Count + 1);
                }
            }
        }

        return [.. found.Select(item => (item.Center, item.ModuleSize, item.IsFullRange))];

        static bool ScanLineIsDark(int run) => (run & 1) is 1;
    }

    // The center run and the rings around it: `rings` runs on each side of the same width, then a dark run at least as wide
    private static bool IsBullseye(int[] runs, int center, int rings)
    {
        if (center - rings - 1 < 1 || center + rings + 1 >= runs.Length || !AreEqualRuns(runs, center - rings, (2 * rings) + 1))
            return false;

        var mean = (double)runs.Skip(center - rings).Take((2 * rings) + 1).Sum() / ((2 * rings) + 1);
        return runs[center - rings - 1] >= mean * 0.6 && runs[center + rings + 1] >= mean * 0.6;
    }

    private static int FindRun(int[] runs, int position)
    {
        var total = 0;
        for (var i = 0; i < runs.Length; i++)
        {
            total += runs[i];
            if (position < total)
                return i;
        }

        return -1;
    }

    private static bool AreEqualRuns(int[] runs, int start, int count)
    {
        if (start < 1 || start + count > runs.Length)
            return false;

        var total = 0;
        for (var i = start; i < start + count; i++)
        {
            total += runs[i];
        }

        var mean = total / (double)count;
        if (mean < 1)
            return false;

        for (var i = start; i < start + count; i++)
        {
            if (Math.Abs(runs[i] - mean) > Math.Max(1, mean * 0.5))
                return false;
        }

        return true;
    }

    // The inner dark ring (2 modules from the center) is a square: it is farthest from the center at its corners
    private static double EstimateBullseyeAngle(BinaryImage image, PointD center, double moduleSize)
    {
        var bestAngle = 0d;
        var bestScore = double.MinValue;
        for (var tenths = 0; tenths < 900; tenths += 5)
        {
            var angle = tenths / 10.0 * Math.PI / 180;
            var score = 0d;
            for (var k = 0; k < 4; k++)
            {
                score += GetInnerRingRadius(image, center, moduleSize, angle + (Math.PI / 4) + (k * Math.PI / 2));
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        return bestAngle;
    }

    private static double GetInnerRingRadius(BinaryImage image, PointD center, double moduleSize, double angle)
    {
        var step = Math.Max(0.2, moduleSize / 10);
        var dx = Math.Cos(angle) * step;
        var dy = Math.Sin(angle) * step;
        var transitions = 0;
        var dark = true;
        for (var i = 1; i * step < moduleSize * 5; i++)
        {
            var isDark = image.IsDark(center.X + (dx * i), center.Y + (dy * i));
            if (isDark == dark)
                continue;

            dark = isDark;
            transitions++;
            if (transitions == 3)
                return i * step;
        }

        return 0;
    }

    // ───── PDF417 ─────

    private static readonly int[] Pdf417Start = [8, 1, 1, 1, 1, 1, 1, 3];
    private static readonly int[] Pdf417Stop = [7, 1, 1, 3, 1, 1, 1, 2, 1];
    private static readonly int[] Pdf417StopReversed = [1, 2, 1, 1, 1, 3, 1, 1, 7];
    private static readonly int[] Pdf417StartReversed = [1, 1, 1, 1, 1, 1, 8];

    private static void LocatePdf417(BinaryImage image, List<Barcode2DImageResult> results, int maximumResults, CancellationToken cancellationToken)
    {
        // The rows of the symbol are rows of the image, or its columns for a turned symbol
        foreach (var vertical in (ReadOnlySpan<bool>)[false, true])
        {
            var lines = vertical ? image.Width : image.Height;
            var rows = new List<(int Line, double Start, double End, int Columns, bool[] Modules)>();
            for (var line = 0; line < lines; line++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var runs = vertical ? image.GetColumnRuns(line) : image.GetRowRuns(line);
                foreach (var row in FindPdf417Rows(image, runs, line, vertical))
                {
                    rows.Add(row);
                }
            }

            // The rows of a symbol have the same columns and start at the same place
            while (rows.Count > 0 && results.Count < maximumResults)
            {
                var first = rows[0];
                var group = rows.Where(row => row.Columns == first.Columns && Math.Abs(row.Start - first.Start) < (first.End - first.Start) * 0.1 && Math.Abs(row.End - first.End) < (first.End - first.Start) * 0.1).ToList();
                rows.RemoveAll(group.Contains);
                if (group.Count < 3)
                    continue;

                var width = (17 * (first.Columns + 4)) + 1;
                var modules = new bool[group.Count, width];
                for (var i = 0; i < group.Count; i++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        modules[i, x] = group[i].Modules[x];
                    }
                }

                if (Barcode2DDecoder.TryDecode(modules, Barcode2DType.Pdf417, out var result))
                {
                    var firstLine = group.Min(row => row.Line);
                    var lastLine = group.Max(row => row.Line) + 1;
                    var start = group.Min(row => row.Start);
                    var end = group.Max(row => row.End);
                    PointD At(double along, double across) => vertical ? new PointD(across, along) : new PointD(along, across);
                    Add(results, new Barcode2DImageResult(result, OrderCorners([At(start, firstLine), At(end, firstLine), At(end, lastLine), At(start, lastLine)])));
                }
            }
        }
    }

    // The rows of a line: a start pattern, then a stop pattern, in either direction, sampled as modules
    private static IEnumerable<(int Line, double Start, double End, int Columns, bool[] Modules)> FindPdf417Rows(BinaryImage image, int[] runs, int line, bool vertical)
    {
        var positions = new double[runs.Length + 1];
        for (var i = 0; i < runs.Length; i++)
        {
            positions[i + 1] = positions[i] + runs[i];
        }

        for (var i = 1; i + 8 <= runs.Length; i += 2)
        {
            foreach (var reversed in new[] { false, true })
            {
                // Read from the left: the start pattern, then the stop pattern. Read from the right: the stop pattern reversed
                // first, then the start pattern reversed, from its first dark run (its 3 light modules come first)
                var first = reversed ? Pdf417StopReversed : Pdf417Start;
                var last = reversed ? Pdf417StartReversed : Pdf417Stop;
                if (i + first.Length > runs.Length || !MatchesPattern(runs, i, first, out var moduleSize))
                    continue;

                // The stop pattern of the same row, starting with a dark run: 17 modules per codeword, the module size of the start pattern
                var firstStop = i + first.Length + ((i + first.Length) % 2 == 0 ? 1 : 0);
                for (var j = firstStop; j + last.Length <= runs.Length; j += 2)
                {
                    if (!MatchesPattern(runs, j, last, out var lastModuleSize) || Math.Abs(lastModuleSize - moduleSize) > moduleSize * 0.4)
                        continue;

                    var start = positions[i];
                    var end = positions[j + last.Length];
                    var width = (end - start) / ((moduleSize + lastModuleSize) / 2);
                    var columns = (int)Math.Round((width - 1) / 17, MidpointRounding.AwayFromZero) - 4;
                    if (columns is < 1 or > 30)
                        continue;

                    var modules = (17 * (columns + 4)) + 1;
                    var module = (end - start) / modules;
                    var row = new bool[modules];
                    for (var k = 0; k < modules; k++)
                    {
                        var along = start + ((k + 0.5) * module);
                        row[k] = vertical ? image.IsDark(line + 0.5, along) : image.IsDark(along, line + 0.5);
                    }

                    if (reversed)
                    {
                        Array.Reverse(row);
                    }

                    yield return (line, start, end, columns, row);
                    break;
                }
            }
        }
    }

    private static bool MatchesPattern(int[] runs, int start, int[] pattern, out double moduleSize)
    {
        var total = 0;
        var modules = 0;
        for (var k = 0; k < pattern.Length; k++)
        {
            total += runs[start + k];
            modules += pattern[k];
        }

        moduleSize = total / (double)modules;
        if (moduleSize < 1)
            return false;

        for (var k = 0; k < pattern.Length; k++)
        {
            if (Math.Abs(runs[start + k] - (pattern[k] * moduleSize)) > Math.Max(1, moduleSize * 0.6))
                return false;
        }

        return true;
    }

    // ───── Shared ─────

    private static bool[,] Sample(BinaryImage image, Homography transform, int height, int width)
    {
        var modules = new bool[height, width];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var point = transform.Transform(column + 0.5, row + 0.5);
                modules[row, column] = image.IsDark(point.X, point.Y);
            }
        }

        return modules;
    }

    // Clockwise in the image, from the corner nearest to its top-left
    private static PointD[] OrderCorners(PointD[] corners)
    {
        var center = new PointD(corners.Average(corner => corner.X), corners.Average(corner => corner.Y));
        var ordered = corners.OrderBy(corner => Math.Atan2(corner.Y - center.Y, corner.X - center.X)).ToArray();
        var first = Array.IndexOf(ordered, ordered.MinBy(corner => corner.X + corner.Y));
        return [.. ordered[first..], .. ordered[..first]];
    }

    private static void Add(List<Barcode2DImageResult> results, Barcode2DImageResult result)
    {
        var center = new PointD(result.Corners.Average(corner => corner.X), result.Corners.Average(corner => corner.Y));
        foreach (var existing in results)
        {
            var existingCenter = new PointD(existing.Corners.Average(corner => corner.X), existing.Corners.Average(corner => corner.Y));
            if (existing.Result.Type == result.Result.Type && existing.Result.Text == result.Result.Text && existingCenter.DistanceTo(center) < existing.Corners[0].DistanceTo(existing.Corners[2]) / 2)
                return;
        }

        results.Add(result);
    }
}
