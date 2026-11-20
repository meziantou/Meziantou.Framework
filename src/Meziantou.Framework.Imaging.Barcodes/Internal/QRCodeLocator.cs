using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// Locates the QR codes of a binary image from their finder patterns, samples their modules and decodes them:
/// <list type="bullet">
/// <item>standard QR codes: three finder patterns at the corners of a right isosceles triangle, and the bottom-right
/// alignment pattern for the perspective;</item>
/// <item>Micro QR codes: one finder pattern, whose orientation the corners of its outer ring give;</item>
/// <item>rMQR codes: the finder pattern and the finder sub-pattern of the opposite corner.</item>
/// </list>
/// </summary>
internal static class QRCodeLocator
{
    public static void Locate(BinaryImage image, Func<QRCodeType, bool> isIncluded, List<QRCodeImageResult> results, int maximumResults, CancellationToken cancellationToken)
    {
        var finders = PatternFinder.Find(image, PatternFinder.FinderRatios, cancellationToken);
        var used = new bool[finders.Count];
        if (isIncluded(QRCodeType.Standard))
        {
            LocateStandard(image, finders, used, results, maximumResults, cancellationToken);
        }

        if (isIncluded(QRCodeType.RMQR))
        {
            LocateSingleFinder(image, finders, used, results, maximumResults, rmqr: true, cancellationToken);
        }

        if (isIncluded(QRCodeType.MicroQR))
        {
            LocateSingleFinder(image, finders, used, results, maximumResults, rmqr: false, cancellationToken);
        }
    }

    private static void LocateStandard(BinaryImage image, List<PatternCenter> finders, bool[] used, List<QRCodeImageResult> results, int maximumResults, CancellationToken cancellationToken)
    {
        // The finder patterns of a symbol have the same module size and are 10 to 200 modules apart along the legs of the
        // triangle, about √2 times more along its hypotenuse: only these pairs, with some slack, can be in a triple
        var neighbors = new List<int>[finders.Count];
        var neighborSets = new HashSet<int>[finders.Count];
        for (var a = 0; a < finders.Count; a++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            neighbors[a] = [];
            neighborSets[a] = [];
            for (var b = a + 1; b < finders.Count; b++)
            {
                if (CanBeInSameSymbol(finders[a], finders[b]))
                {
                    neighbors[a].Add(b);
                    neighborSets[a].Add(b);
                }
            }
        }

        // The triples whose finder patterns form a right isosceles triangle
        var triples = new List<(double Score, int A, int B, int C)>();
        for (var a = 0; a < finders.Count; a++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var b in neighbors[a])
            {
                foreach (var c in neighbors[b])
                {
                    if (neighborSets[a].Contains(c) && GetTriangleScore(finders[a], finders[b], finders[c]) is { } score)
                    {
                        triples.Add((score, a, b, c));
                    }
                }
            }
        }

        // The outer corners of each finder pattern, found once
        var rawCorners = new Dictionary<PatternCenter, PointD[]?>(ReferenceEqualityComparer.Instance);
        PointD[]? GetRawCorners(PatternCenter finder)
        {
            if (!rawCorners.TryGetValue(finder, out var corners))
            {
                // The outer edge of the finder pattern: dark center, light ring, dark ring, then the light separator
                corners = PatternCorners.Find(image, finder.Point, finder.ModuleSize, transitions: 3);
                rawCorners[finder] = corners;
            }

            return corners;
        }

        // The best triangles first; the textures of a photograph can give many finder-like patterns, and their triangles
        // are not all tried
        var attempts = 0;
        var maximumAttempts = 64 + (4 * finders.Count);
        foreach (var (_, a, b, c) in triples.OrderBy(triple => triple.Score))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= maximumResults)
                return;

            if (used[a] || used[b] || used[c])
                continue;

            if (++attempts > maximumAttempts)
                return;

            var (topLeft, topRight, bottomLeft) = Orient(finders[a], finders[b], finders[c]);
            if (TryDecodeStandard(image, topLeft, topRight, bottomLeft, GetRawCorners) is { } result && Add(results, result))
            {
                used[a] = used[b] = used[c] = true;
            }
        }
    }

    private static bool CanBeInSameSymbol(PatternCenter a, PatternCenter b)
    {
        if (Math.Max(a.ModuleSize, b.ModuleSize) > Math.Min(a.ModuleSize, b.ModuleSize) * 1.5)
            return false;

        var modules = a.Point.DistanceTo(b.Point) / ((a.ModuleSize + b.ModuleSize) / 2);
        return modules is >= 8 and <= 400;
    }

    // The lower the better: how far the triangle is from a right isosceles triangle, or null when it is too far
    private static double? GetTriangleScore(PatternCenter a, PatternCenter b, PatternCenter c)
    {
        var sizes = new[] { a.ModuleSize, b.ModuleSize, c.ModuleSize };
        if (sizes.Max() > sizes.Min() * 1.5)
            return null;

        var (topLeft, topRight, bottomLeft) = Orient(a, b, c);
        var leg1 = topLeft.Point.DistanceTo(topRight.Point);
        var leg2 = topLeft.Point.DistanceTo(bottomLeft.Point);
        var hypotenuse = topRight.Point.DistanceTo(bottomLeft.Point);
        var moduleSize = sizes.Average();
        if (leg1 / moduleSize < 10 || leg2 / moduleSize < 10 || leg1 / moduleSize > 200 || leg2 / moduleSize > 200)
            return null;

        var legRatio = Math.Abs(leg1 - leg2) / Math.Max(leg1, leg2);
        var hypotenuseRatio = Math.Abs(hypotenuse - Math.Sqrt((leg1 * leg1) + (leg2 * leg2))) / hypotenuse;
        if (legRatio > 0.3 || hypotenuseRatio > 0.15)
            return null;

        return legRatio + hypotenuseRatio;
    }

    // The top-left finder pattern is at the right angle, opposite the longest side; the top-right one follows it clockwise
    private static (PatternCenter TopLeft, PatternCenter TopRight, PatternCenter BottomLeft) Orient(PatternCenter a, PatternCenter b, PatternCenter c)
    {
        var ab = a.Point.DistanceTo(b.Point);
        var bc = b.Point.DistanceTo(c.Point);
        var ac = a.Point.DistanceTo(c.Point);
        var (corner, first, second) = bc >= ab && bc >= ac ? (a, b, c) : ac >= ab ? (b, a, c) : (c, a, b);

        // In the image, y goes down: the cross product is positive when the second point is clockwise from the first
        var cross = ((first.X - corner.X) * (second.Y - corner.Y)) - ((first.Y - corner.Y) * (second.X - corner.X));
        return cross > 0 ? (corner, first, second) : (corner, second, first);
    }

    private static QRCodeImageResult? TryDecodeStandard(BinaryImage image, PatternCenter topLeft, PatternCenter topRight, PatternCenter bottomLeft, Func<PatternCenter, PointD[]?> getRawCorners)
    {
        // The finder patterns are 3.5 modules from the edges: their distance gives the number of modules, then the version
        var moduleSize = (topLeft.ModuleSize + topRight.ModuleSize + bottomLeft.ModuleSize) / 3;
        var estimate = ((topLeft.Point.DistanceTo(topRight.Point) + topLeft.Point.DistanceTo(bottomLeft.Point)) / 2 / moduleSize) + 7;
        var version = (int)Math.Round((estimate - 17) / 4, MidpointRounding.AwayFromZero);
        PointD[]?[]? finderCorners = null;
        foreach (var delta in (ReadOnlySpan<int>)[0, -1, 1, -2, 2])
        {
            var candidate = version + delta;
            if (candidate is < 1 or > 40)
                continue;

            var size = 17 + (4 * candidate);
            var affine = Homography.Create([new(3.5, 3.5), new(size - 3.5, 3.5), new(3.5, size - 3.5)], [topLeft.Point, topRight.Point, bottomLeft.Point]);
            if (affine is null)
                continue;

            // The timing patterns between the finder patterns alternate: a cheap check of the version, and of the triangle
            if (GetTimingPatternScore(image, affine, size) >= 0.65)
            {
                // The bottom-right alignment pattern gives the perspective
                var transform = affine;
                if (candidate >= 2 && FindSquarePattern(image, affine, size - 6.5, size - 6.5, searchModules: 4) is { } alignment)
                {
                    transform = Homography.Create([new(3.5, 3.5), new(size - 3.5, 3.5), new(3.5, size - 3.5), new(size - 6.5, size - 6.5)], [topLeft.Point, topRight.Point, bottomLeft.Point, alignment]) ?? affine;
                }

                if (TryDecode(image, transform, size, candidate) is { } result)
                    return result;
            }

            // In a photograph taken at an angle, the centers of the finder patterns do not give the perspective: their
            // corners do, and the alignment patterns
            finderCorners ??= GetFinderCorners(topLeft, topRight, bottomLeft, getRawCorners);
            if (FitToFinderCorners(finderCorners, size) is { } fitted && GetTimingPatternScore(image, fitted, size) >= 0.65)
            {
                fitted = RefineWithAlignmentPatterns(image, fitted, finderCorners, size, candidate);
                if (TryDecode(image, fitted, size, candidate) is { } fittedResult)
                    return fittedResult;
            }
        }

        return null;
    }

    private static QRCodeImageResult? TryDecode(BinaryImage image, Homography transform, int size, int version)
    {
        var modules = Sample(image, transform, size, size);
        if (QRCodeDecoder.TryDecode(modules, out var result) && result.Type is QRCodeType.Standard && result.Version == version)
            return new QRCodeImageResult(result, GetCorners(transform, size, size));

        return null;
    }

    // The outer corners of the finder patterns, in the order (-, -), (+, -), (+, +), (-, +) along the axes of the symbol
    private static PointD[]?[] GetFinderCorners(PatternCenter topLeft, PatternCenter topRight, PatternCenter bottomLeft, Func<PatternCenter, PointD[]?> getRawCorners)
    {
        var u = Normalize(new PointD(topRight.X - topLeft.X, topRight.Y - topLeft.Y));
        var v = Normalize(new PointD(bottomLeft.X - topLeft.X, bottomLeft.Y - topLeft.Y));
        var result = new PointD[]?[3];
        PatternCenter[] finders = [topLeft, topRight, bottomLeft];
        for (var i = 0; i < 3; i++)
        {
            if (getRawCorners(finders[i]) is { } corners)
            {
                result[i] = PatternCorners.MatchToAxes(corners, finders[i].Point, u, v);
            }
        }

        return result;
    }

    private static PointD Normalize(PointD vector)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        return length < 1e-9 ? vector : new PointD(vector.X / length, vector.Y / length);
    }

    // The transform that fits the corners of the finder patterns; two of them are needed: 8 points for 8 unknowns
    private static Homography? FitToFinderCorners(PointD[]?[] finderCorners, int size)
    {
        var (source, destination) = GetFinderCornerPairs(finderCorners, size);
        return source.Count < 8 ? null : Homography.Fit([.. source], [.. destination]);
    }

    private static (List<PointD> Source, List<PointD> Destination) GetFinderCornerPairs(PointD[]?[] finderCorners, int size)
    {
        var source = new List<PointD>();
        var destination = new List<PointD>();
        ReadOnlySpan<(double Column, double Row)> origins = [(0, 0), (size - 7, 0), (0, size - 7)];
        for (var i = 0; i < 3; i++)
        {
            if (finderCorners[i] is not { } corners)
                continue;

            var (column, row) = origins[i];
            source.AddRange([new(column, row), new(column + 7, row), new(column + 7, row + 7), new(column, row + 7)]);
            destination.AddRange(corners);
        }

        return (source, destination);
    }

    // The alignment patterns, where the transform puts them; each one found corrects the transform around it
    private static Homography RefineWithAlignmentPatterns(BinaryImage image, Homography transform, PointD[]?[] finderCorners, int size, int version)
    {
        if (version < 2)
            return transform;

        var (source, destination) = GetFinderCornerPairs(finderCorners, size);
        var positions = GetAlignmentPatternPositions(version);
        var found = 0;
        foreach (var row in positions)
        {
            foreach (var column in positions)
            {
                // There are none where the finder patterns are
                if ((row == 6 && column == 6) || (row == 6 && column == size - 7) || (row == size - 7 && column == 6))
                    continue;

                if (FindSquarePattern(image, transform, column + 0.5, row + 0.5, searchModules: 2) is { } alignment)
                {
                    source.Add(new PointD(column + 0.5, row + 0.5));
                    destination.Add(alignment);
                    found++;
                }
            }
        }

        return found > 0 ? Homography.Fit([.. source], [.. destination]) ?? transform : transform;
    }

    // ISO/IEC 18004, Annex E: the rows and columns of the centers of the alignment patterns, evenly spaced from 6 to size - 7
    private static List<int> GetAlignmentPatternPositions(int version)
    {
        var count = (version / 7) + 2;
        var size = 17 + (4 * version);
        var step = version is 32 ? 26 : (((version * 4) + (count * 2) + 1) / ((count * 2) - 2)) * 2;
        var positions = new List<int> { 6 };
        for (var position = size - 7; positions.Count < count; position -= step)
        {
            positions.Insert(1, position);
        }

        return positions;
    }

    // The share of the modules of the timing patterns (row and column 6, between the finder patterns) that alternate
    private static double GetTimingPatternScore(BinaryImage image, Homography transform, int size)
    {
        var matches = 0;
        var count = 0;
        for (var i = 8; i < size - 8; i++)
        {
            var expected = i % 2 is 0;
            if (image.IsDark(transform.Transform(i + 0.5, 6.5)) == expected)
            {
                matches++;
            }

            if (image.IsDark(transform.Transform(6.5, i + 0.5)) == expected)
            {
                matches++;
            }

            count += 2;
        }

        return count is 0 ? 1 : matches / (double)count;
    }

    // Micro QR and rMQR codes: one finder pattern, the orientation from its corners
    private static void LocateSingleFinder(BinaryImage image, List<PatternCenter> finders, bool[] used, List<QRCodeImageResult> results, int maximumResults, bool rmqr, CancellationToken cancellationToken)
    {
        for (var i = 0; i < finders.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= maximumResults)
                return;

            if (used[i])
                continue;

            var finder = finders[i];
            var (angle, moduleSize) = EstimateOrientation(image, finder);
            for (var quarter = 0; quarter < 4; quarter++)
            {
                var u = new PointD(Math.Cos(angle + (quarter * Math.PI / 2)), Math.Sin(angle + (quarter * Math.PI / 2)));
                var result = rmqr ? TryDecodeRMQR(image, finder.Point, u, moduleSize) : TryDecodeMicroQR(image, finder.Point, u, moduleSize);
                if (result is not null && Add(results, result))
                {
                    used[i] = true;
                    break;
                }
            }
        }
    }

    private static QRCodeImageResult? TryDecodeMicroQR(BinaryImage image, PointD finder, PointD u, double moduleSize)
    {
        // The finder pattern is at the top left: the symbol extends along u and v from 3.5 modules before its center.
        // A mirrored symbol is the transpose, which the decoder reads.
        var v = new PointD(-u.Y, u.X);
        var transform = CreateTransform(finder, u, v, moduleSize);

        // The timing patterns of row 0 and column 0 continue the finder pattern: dark at the even modules from 8. Most of
        // the finder-like patterns of a photograph are not followed by them.
        if (CountTimingMatches(image, transform, 8, 11, row: true) + CountTimingMatches(image, transform, 8, 11, row: false) < 5)
            return null;

        // The modules of a smaller symbol are a corner of a larger one, which can decode as a valid symbol: try the largest
        // size first, among the sizes whose timing patterns run to the edge of the symbol (the quiet zone ends them)
        for (var size = 17; size >= 11; size -= 2)
        {
            var mismatches = (2 * (size - 11)) - CountTimingMatches(image, transform, 11, size, row: true) - CountTimingMatches(image, transform, 11, size, row: false);
            if (mismatches > 1)
                continue;

            var modules = Sample(image, transform, size, size);
            if (QRCodeDecoder.TryDecode(modules, out var result) && result.Type is QRCodeType.MicroQR && result.Version == (size - 9) / 2)
                return new QRCodeImageResult(result, GetCorners(transform, size, size));
        }

        return null;
    }

    private static QRCodeImageResult? TryDecodeRMQR(BinaryImage image, PointD finder, PointD u, double moduleSize)
    {
        foreach (var mirror in (ReadOnlySpan<int>)[1, -1])
        {
            var v = new PointD(-u.Y * mirror, u.X * mirror);
            var estimate = CreateTransform(finder, u, v, moduleSize);

            // The timing pattern of the top edge continues the finder pattern: dark at the even modules from 8 (the
            // narrowest symbols are 27 modules wide, their alignment pattern is further)
            if (CountTimingMatches(image, estimate, 8, 16, row: true) < 6)
                continue;

            foreach (var (height, width) in RMQRSizes)
            {
                // The finder sub-pattern is at the bottom-right corner, its center 2.5 modules from the edges
                var predicted = estimate.Transform(width - 2.5, height - 2.5);
                if (predicted.X < -moduleSize || predicted.Y < -moduleSize || predicted.X > image.Width + moduleSize || predicted.Y > image.Height + moduleSize)
                    continue;

                if (FindSquarePattern(image, estimate, width - 2.5, height - 2.5, searchModules: 2) is not { } subFinder)
                    continue;

                // The finder pattern and the finder sub-pattern give the scale and the rotation: (dx + i dy) m u = S - F
                var dx = width - 6.0;
                var dy = (height - 6.0) * mirror;
                var sx = subFinder.X - finder.X;
                var sy = subFinder.Y - finder.Y;
                var denominator = (dx * dx) + (dy * dy);
                var mu = new PointD(((sx * dx) + (sy * dy)) / denominator, ((sy * dx) - (sx * dy)) / denominator);
                var scale = Math.Sqrt((mu.X * mu.X) + (mu.Y * mu.Y));
                if (Math.Abs(scale - moduleSize) > moduleSize * 0.3)
                    continue;

                var refinedU = new PointD(mu.X / scale, mu.Y / scale);
                var refinedV = new PointD(-refinedU.Y * mirror, refinedU.X * mirror);
                var transform = CreateTransform(finder, refinedU, refinedV, scale);
                var modules = Sample(image, transform, height, width);
                if (QRCodeDecoder.TryDecode(modules, out var result) && result.Type is QRCodeType.RMQR)
                    return new QRCodeImageResult(result, GetCorners(transform, height, width));
            }
        }

        return null;
    }

    // The modules of row 0 (or column 0) from `start` to `end` (excluded) that match a timing pattern dark at the even modules
    private static int CountTimingMatches(BinaryImage image, Homography transform, int start, int end, bool row)
    {
        var matches = 0;
        for (var i = start; i < end; i++)
        {
            var point = row ? transform.Transform(i + 0.5, 0.5) : transform.Transform(0.5, i + 0.5);
            if (image.IsDark(point) == (i % 2 is 0))
            {
                matches++;
            }
        }

        return matches;
    }

    private static readonly (int Height, int Width)[] RMQRSizes =
    [
        (7, 43), (7, 59), (7, 77), (7, 99), (7, 139),
        (9, 43), (9, 59), (9, 77), (9, 99), (9, 139),
        (11, 27), (11, 43), (11, 59), (11, 77), (11, 99), (11, 139),
        (13, 27), (13, 43), (13, 59), (13, 77), (13, 99), (13, 139),
        (15, 43), (15, 59), (15, 77), (15, 99), (15, 139),
        (17, 43), (17, 59), (17, 77), (17, 99), (17, 139),
    ];

    // The module (column, row) is at the finder pattern center, 3.5 modules from the top-left corner, plus the offsets along u and v
    private static Homography CreateTransform(PointD finder, PointD u, PointD v, double moduleSize)
    {
        PointD At(double column, double row) => new(
            finder.X + ((column - 3.5) * moduleSize * u.X) + ((row - 3.5) * moduleSize * v.X),
            finder.Y + ((column - 3.5) * moduleSize * u.Y) + ((row - 3.5) * moduleSize * v.Y));

        return Homography.Create([new(0, 0), new(10, 0), new(0, 10)], [At(0, 0), At(10, 0), At(0, 10)])!;
    }

    /// <summary>
    /// Estimates the orientation of a finder pattern: its outer ring is the farthest from the center at its corners, at 45°
    /// from the sides. Returns the angle of a side and the size of the modules.
    /// </summary>
    private static (double Angle, double ModuleSize) EstimateOrientation(BinaryImage image, PatternCenter finder)
    {
        var bestAngle = 0d;
        var bestScore = double.MinValue;
        for (var degrees = 0; degrees < 90; degrees++)
        {
            var angle = degrees * Math.PI / 180;
            var score = 0d;
            for (var k = 0; k < 4; k++)
            {
                score += GetRingRadius(image, finder, angle + (Math.PI / 4) + (k * Math.PI / 2));
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        var sides = 0d;
        for (var k = 0; k < 4; k++)
        {
            sides += GetRingRadius(image, finder, bestAngle + (k * Math.PI / 2));
        }

        return (bestAngle, sides / 4 / 3.5);
    }

    // The distance from the center to the outside of the outer dark ring along a direction
    private static double GetRingRadius(BinaryImage image, PatternCenter finder, double angle)
    {
        var step = Math.Max(0.25, finder.ModuleSize / 8);
        var dx = Math.Cos(angle) * step;
        var dy = Math.Sin(angle) * step;
        var transitions = 0;
        var dark = true;
        for (var i = 1; i * step < finder.ModuleSize * 7; i++)
        {
            var isDark = image.IsDark(finder.X + (dx * i), finder.Y + (dy * i));
            if (isDark == dark)
                continue;

            dark = isDark;
            transitions++;
            if (transitions == 3)
                return i * step;
        }

        return 0;
    }

    /// <summary>
    /// Finds a 5x5 pattern of a dark ring, a light ring and a dark center module (an alignment pattern, or the finder
    /// sub-pattern of an rMQR code) around a module position, with the axes of a transform.
    /// </summary>
    private static PointD? FindSquarePattern(BinaryImage image, Homography transform, double column, double row, int searchModules)
    {
        var origin = transform.Transform(column, row);
        var right = transform.Transform(column + 1, row);
        var down = transform.Transform(column, row + 1);
        var ux = new PointD(right.X - origin.X, right.Y - origin.Y);
        var uy = new PointD(down.X - origin.X, down.Y - origin.Y);
        var best = 0;
        PointD? bestPosition = null;
        var bestDistance = double.MaxValue;
        for (var dv = -searchModules * 4; dv <= searchModules * 4; dv++)
        {
            for (var du = -searchModules * 4; du <= searchModules * 4; du++)
            {
                var center = new PointD(origin.X + (du / 4.0 * ux.X) + (dv / 4.0 * uy.X), origin.Y + (du / 4.0 * ux.Y) + (dv / 4.0 * uy.Y));
                var score = 0;
                for (var b = -2; b <= 2; b++)
                {
                    for (var a = -2; a <= 2; a++)
                    {
                        var expected = Math.Abs(a) == 2 || Math.Abs(b) == 2 || (a == 0 && b == 0);
                        if (image.IsDark(center.X + (a * ux.X) + (b * uy.X), center.Y + (a * ux.Y) + (b * uy.Y)) == expected)
                        {
                            score++;
                        }
                    }
                }

                var distance = (du * du) + (dv * dv);
                if (score > best || (score == best && distance < bestDistance))
                {
                    best = score;
                    bestPosition = center;
                    bestDistance = distance;
                }
            }
        }

        return best >= 23 ? bestPosition : null;
    }

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

    private static PointD[] GetCorners(Homography transform, int height, int width)
    {
        return [transform.Transform(0, 0), transform.Transform(width, 0), transform.Transform(width, height), transform.Transform(0, height)];
    }

    // A symbol found twice (from other finder patterns, or in the inverted image) is one result
    private static bool Add(List<QRCodeImageResult> results, QRCodeImageResult result)
    {
        var center = GetCenter(result.Corners);
        foreach (var existing in results)
        {
            if (existing.Result.Text == result.Result.Text && GetCenter(existing.Corners).DistanceTo(center) < existing.Corners[0].DistanceTo(existing.Corners[2]) / 2)
                return false;
        }

        results.Add(result);
        return true;
    }

    private static PointD GetCenter(IReadOnlyList<PointD> corners)
    {
        return new PointD(corners.Average(corner => corner.X), corners.Average(corner => corner.Y));
    }
}
