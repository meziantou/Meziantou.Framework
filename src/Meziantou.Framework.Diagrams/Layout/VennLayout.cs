using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>
/// The layout of the circles of a Venn diagram, ported from <c>layout.js</c> and <c>diagram.js</c> of venn.js 2.0.0
/// (https://github.com/upsetjs/venn.js, MIT, see THIRD-PARTY-NOTICES.md), the library Mermaid uses: a greedy initial
/// layout (or a constrained multidimensional scaling when it is better), minimized with Nelder-Mead so the overlaps of the
/// circles have the requested areas, then oriented, packed and scaled to the drawing.
/// </summary>
/// <remarks>
/// Unlike venn.js, the layout is deterministic: the random restarts of the multidimensional scaling use a fixed seed.
/// The areas with the same sets are merged into one, whose weight is their number and whose size is the mean of their
/// sizes: the loss differs by a constant, so its minimum is the same, and the number of overlaps stays bounded. A budget
/// of evaluations bounds the time of the minimization on large diagrams.
/// </remarks>
internal static class VennLayout
{
    private const double Small = 1e-10;
    private const int MaxIterations = 500;
    private const long EvaluationBudget = 5_000_000;

    /// <summary>Lays the areas out in a rectangle.</summary>
    /// <param name="data">The sets and their overlaps; a set of size 0 must have been removed, as Mermaid does.</param>
    /// <param name="width">The width of the drawing.</param>
    /// <param name="height">The height of the drawing.</param>
    /// <param name="padding">The space between the circles and the sides of the drawing.</param>
    /// <returns>The circles by identifier of set, and the centers of the labels by key of area (<see cref="GetLabelKey"/>).</returns>
    public static (VennKeyedMap<VennCircle> Circles, Dictionary<string, VennTextCentre> TextCentres) Compute(IReadOnlyList<VennLayoutArea> data, double width, double height, double padding)
    {
        var circles = new VennKeyedMap<VennCircle>();
        var textCentres = new Dictionary<string, VennTextCentre>(StringComparer.Ordinal);
        if (data.Count is 0)
            return (circles, textCentres);

        var solution = Venn(MergeAreas(data));
        if (solution.Count is 0)
            return (circles, textCentres);

        if (!IsFinite(solution))
        {
            solution = FallbackLayout(data);
        }

        solution = NormalizeSolution(solution, Math.PI / 2);
        circles = ScaleSolution(solution, width, height, padding);
        textCentres = ComputeTextCentres(circles, data);
        return (circles, textCentres);
    }

    /// <summary>Returns the key of the label of an area: its sets joined by commas, as venn.js indexes them.</summary>
    public static string GetLabelKey(IReadOnlyList<string> sets) => string.Join(',', sets);

    /// <summary>Lays the circles out so their overlaps have the requested areas (venn.js <c>venn</c>).</summary>
    internal static VennKeyedMap<VennCircle> Venn(IReadOnlyList<VennLayoutArea> sets)
    {
        var areas = AddMissingAreas(sets);
        var circles = BestInitialLayout(areas);

        // Optimize the positions from the initial layout
        var setIds = circles.Keys;
        var initial = new double[setIds.Count * 2];
        var current = new VennKeyedMap<VennCircle>();
        for (var i = 0; i < setIds.Count; i++)
        {
            var circle = circles[setIds[i]];
            initial[2 * i] = circle.X;
            initial[(2 * i) + 1] = circle.Y;
            current[setIds[i]] = new VennCircle(circle.X, circle.Y, circle.Radius);
        }

        var resolved = Resolve(current, areas);
        var cost = Math.Max(1, GetLossCost(resolved));
        var (positions, _) = VennOptimizer.NelderMead(
            values =>
            {
                for (var i = 0; i < setIds.Count; i++)
                {
                    var circle = current[setIds[i]];
                    circle.X = values[2 * i];
                    circle.Y = values[(2 * i) + 1];
                }

                return Loss(resolved);
            },
            initial,
            MaxIterations,
            maxEvaluations: Math.Max(100, EvaluationBudget / cost));

        for (var i = 0; i < setIds.Count; i++)
        {
            var circle = circles[setIds[i]];
            circle.X = positions[2 * i];
            circle.Y = positions[(2 * i) + 1];
        }

        return circles;
    }

    /// <summary>Returns the distance at which two circles overlap on a given area.</summary>
    internal static double DistanceFromIntersectArea(double r1, double r2, double overlap)
    {
        // Completely overlapped circles
        if (Math.Min(r1, r2) * Math.Min(r1, r2) * Math.PI <= overlap + Small)
            return Math.Abs(r1 - r2);

        return VennOptimizer.Bisect(distance => VennGeometry.CircleOverlap(r1, r2, distance) - overlap, 0, r1 + r2);
    }

    /// <summary>
    /// Merges the areas with the same sets, and keeps the last size of a set declared twice at the place of its first
    /// declaration, as the circles of venn.js, which are indexed by set.
    /// </summary>
    private static List<VennLayoutArea> MergeAreas(IReadOnlyList<VennLayoutArea> data)
    {
        var result = new List<VennLayoutArea>();
        var groups = new Dictionary<string, (int Index, double SizeSum, int Count)>(StringComparer.Ordinal);
        foreach (var area in data)
        {
            var key = GetMergeKey(area.Sets);
            if (area.Sets.Count is 1)
            {
                if (groups.TryGetValue(key, out var set))
                {
                    result[set.Index] = area;
                }
                else
                {
                    groups[key] = (result.Count, 0, 1);
                    result.Add(area);
                }

                continue;
            }

            if (groups.TryGetValue(key, out var group))
            {
                group = (group.Index, group.SizeSum + area.Size, group.Count + 1);
                groups[key] = group;
                result[group.Index] = new VennLayoutArea(area.Sets, group.SizeSum / group.Count, group.Count);
            }
            else
            {
                groups[key] = (result.Count, area.Size, 1);
                result.Add(area);
            }
        }

        return result;
    }

    /// <summary>Returns a key that identifies the sets of an area: each identifier prefixed by its length, so no two lists of identifiers share a key.</summary>
    private static string GetMergeKey(IReadOnlyList<string> sets) => string.Concat(sets.Select(set => set.Length.ToString(CultureInfo.InvariantCulture) + ":" + set));

    /// <summary>Adds the missing overlaps of two sets with a size of 0, so the sets without an overlap are laid out apart.</summary>
    private static List<VennLayoutArea> AddMissingAreas(IReadOnlyList<VennLayoutArea> areas)
    {
        var result = new List<VennLayoutArea>(areas);
        var ids = new List<string>();
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in result)
        {
            if (area.Sets.Count is 1)
            {
                ids.Add(area.Sets[0]);
            }
            else if (area.Sets.Count is 2)
            {
                pairs.Add(area.Sets[0] + ";" + area.Sets[1]);
                pairs.Add(area.Sets[1] + ";" + area.Sets[0]);
            }
        }

        VennSort.StableSort(ids, static (a, b) => string.CompareOrdinal(a, b));
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                if (!pairs.Contains(ids[i] + ";" + ids[j]))
                {
                    result.Add(new VennLayoutArea([ids[i], ids[j]], 0));
                }
            }
        }

        return result;
    }

    /// <summary>Takes the greedy layout, or the constrained multidimensional scaling when it is better on more than 7 areas.</summary>
    private static VennKeyedMap<VennCircle> BestInitialLayout(List<VennLayoutArea> areas)
    {
        var initial = GreedyLayout(areas);

        // The greedy layout is good enough for 2 or 3 circles, and aligns them on the axes
        if (areas.Count >= 8)
        {
            var constrained = ConstrainedMdsLayout(areas);
            var constrainedLoss = Loss(Resolve(constrained, areas));
            var greedyLoss = Loss(Resolve(initial, areas));
            if (constrainedLoss + 1e-8 < greedyLoss)
            {
                initial = constrained;
            }
        }

        return initial;
    }

    /// <summary>Lays the sets out with a multidimensional scaling of the distances their overlaps require, from 10 random starts.</summary>
    private static VennKeyedMap<VennCircle> ConstrainedMdsLayout(List<VennLayoutArea> areas)
    {
        const int Restarts = 10;

        var sets = new List<VennLayoutArea>();
        var setIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var area in areas)
        {
            if (area.Sets.Count is 1)
            {
                setIds[area.Sets[0]] = sets.Count;
                sets.Add(area);
            }
        }

        var (distances, constraints) = GetDistanceMatrices(areas, sets, setIds);

        // Keep the distances bounded
        var rowNorms = new double[distances.Length];
        for (var i = 0; i < distances.Length; i++)
        {
            rowNorms[i] = VennOptimizer.Norm2(distances[i]);
        }

        var norm = VennOptimizer.Norm2(rowNorms) / distances.Length;
        foreach (var row in distances)
        {
            for (var j = 0; j < row.Length; j++)
            {
                row[j] /= norm;
            }
        }

        var random = new VennRandom(0x5eed);
        double[]? best = null;
        var bestFx = 0.0;
        for (var i = 0; i < Restarts; i++)
        {
            var initial = new double[distances.Length * 2];
            for (var j = 0; j < initial.Length; j++)
            {
                initial[j] = random.NextDouble();
            }

            var (x, fx) = VennOptimizer.ConjugateGradient((x, fxPrime) => ConstrainedMdsGradient(x, fxPrime, distances, constraints), initial, MaxIterations);
            if (best is null || fx < bestFx)
            {
                best = x;
                bestFx = fx;
            }
        }

        var circles = new VennKeyedMap<VennCircle>();
        for (var i = 0; i < sets.Count; i++)
        {
            var set = sets[i];
            circles[set.Sets[0]] = new VennCircle(best![2 * i] * norm, best[(2 * i) + 1] * norm, Math.Sqrt(set.Size / Math.PI));
        }

        return circles;
    }

    /// <summary>Returns the distances the overlaps of two sets require, and whether one set is inside the other (1) or apart (-1).</summary>
    private static (double[][] Distances, double[][] Constraints) GetDistanceMatrices(List<VennLayoutArea> areas, List<VennLayoutArea> sets, Dictionary<string, int> setIds)
    {
        var distances = new double[sets.Count][];
        var constraints = new double[sets.Count][];
        for (var i = 0; i < sets.Count; i++)
        {
            distances[i] = new double[sets.Count];
            constraints[i] = new double[sets.Count];
        }

        foreach (var current in areas)
        {
            if (current.Sets.Count is not 2)
                continue;

            var left = setIds[current.Sets[0]];
            var right = setIds[current.Sets[1]];
            var r1 = Math.Sqrt(sets[left].Size / Math.PI);
            var r2 = Math.Sqrt(sets[right].Size / Math.PI);
            var distance = DistanceFromIntersectArea(r1, r2, current.Size);
            distances[left][right] = distances[right][left] = distance;

            var constraint = 0;
            if (current.Size + 1e-10 >= Math.Min(sets[left].Size, sets[right].Size))
            {
                constraint = 1;
            }
            else if (current.Size <= 1e-10)
            {
                constraint = -1;
            }

            constraints[left][right] = constraints[right][left] = constraint;
        }

        return (distances, constraints);
    }

    /// <summary>Computes the loss and the gradient of the constrained multidimensional scaling.</summary>
    private static double ConstrainedMdsGradient(double[] x, double[] fxPrime, double[][] distances, double[][] constraints)
    {
        Array.Clear(fxPrime);
        var loss = 0.0;
        for (var i = 0; i < distances.Length; i++)
        {
            var xi = x[2 * i];
            var yi = x[(2 * i) + 1];
            for (var j = i + 1; j < distances.Length; j++)
            {
                var xj = x[2 * j];
                var yj = x[(2 * j) + 1];
                var dij = distances[i][j];
                var constraint = constraints[i][j];

                var squaredDistance = ((xj - xi) * (xj - xi)) + ((yj - yi) * (yj - yi));
                var distance = Math.Sqrt(squaredDistance);
                var delta = squaredDistance - (dij * dij);
                if ((constraint > 0 && distance <= dij) || (constraint < 0 && distance >= dij))
                    continue;

                loss += 2 * delta * delta;
                fxPrime[2 * i] += 4 * delta * (xi - xj);
                fxPrime[(2 * i) + 1] += 4 * delta * (yi - yj);
                fxPrime[2 * j] += 4 * delta * (xj - xi);
                fxPrime[(2 * j) + 1] += 4 * delta * (yj - yi);
            }
        }

        return loss;
    }

    /// <summary>
    /// Lays the sets out greedily, from the most overlapped to the least, each at the position, among the ones where its
    /// overlaps with the sets already placed have the right areas, that has the smallest loss.
    /// </summary>
    private static VennKeyedMap<VennCircle> GreedyLayout(List<VennLayoutArea> areas)
    {
        var circles = new VennKeyedMap<VennCircle>();
        var setOverlaps = new VennKeyedMap<List<(string Set, double Size, double Weight)>>();
        foreach (var area in areas)
        {
            if (area.Sets.Count is 1)
            {
                var set = area.Sets[0];
                circles[set] = new VennCircle(1e10, 1e10, Math.Sqrt(area.Size / Math.PI)) { Size = area.Size };
                setOverlaps[set] = [];
            }
        }

        var pairs = areas.Where(area => area.Sets.Count is 2).ToList();
        foreach (var current in pairs)
        {
            var weight = current.Weight;
            var left = current.Sets[0];
            var right = current.Sets[1];

            // The completely overlapped circles must not be placed early
            if (current.Size + Small >= Math.Min(circles[left].Size, circles[right].Size))
            {
                weight = 0;
            }

            setOverlaps[left].Add((right, current.Size, weight));
            setOverlaps[right].Add((left, current.Size, weight));
        }

        var mostOverlapped = new List<(string Set, double Size)>();
        foreach (var set in setOverlaps.Keys)
        {
            var size = 0.0;
            foreach (var overlap in setOverlaps[set])
            {
                size += overlap.Size * overlap.Weight;
            }

            mostOverlapped.Add((set, size));
        }

        VennSort.StableSort(mostOverlapped, static (a, b) => b.Size - a.Size);
        if (mostOverlapped.Count is 0)
            return circles;

        var positioned = new HashSet<string>(StringComparer.Ordinal);
        var resolvedPairs = Resolve(circles, pairs);
        PositionSet(0, 0, mostOverlapped[0].Set);
        for (var i = 1; i < mostOverlapped.Count; i++)
        {
            var setIndex = mostOverlapped[i].Set;
            var set = circles[setIndex];
            var overlap = setOverlaps[setIndex].Where(element => positioned.Contains(element.Set)).ToList();
            VennSort.StableSort(overlap, static (a, b) => b.Size - a.Size);

            // venn.js throws here, which addMissingAreas prevents
            if (overlap.Count is 0)
            {
                PositionSet(0, 0, setIndex);
                continue;
            }

            var points = new List<(double X, double Y)>();
            for (var j = 0; j < overlap.Count; j++)
            {
                // The distance to the set already placed, sampled at 90 degrees, which looks best
                var p1 = circles[overlap[j].Set];
                var d1 = DistanceFromIntersectArea(set.Radius, p1.Radius, overlap[j].Size);
                points.Add((p1.X + d1, p1.Y));
                points.Add((p1.X - d1, p1.Y));
                points.Add((p1.X, p1.Y + d1));
                points.Add((p1.X, p1.Y - d1));

                // The positions at the right distance of two sets already placed
                for (var k = j + 1; k < overlap.Count; k++)
                {
                    var p2 = circles[overlap[k].Set];
                    var d2 = DistanceFromIntersectArea(set.Radius, p2.Radius, overlap[k].Size);
                    points.AddRange(VennGeometry.CircleCircleIntersection(p1.X, p1.Y, d1, p2.X, p2.Y, d2));
                }
            }

            var bestLoss = 1e50;
            var bestPoint = points[0];
            foreach (var point in points)
            {
                set.X = point.X;
                set.Y = point.Y;
                var localLoss = Loss(resolvedPairs);
                if (localLoss < bestLoss)
                {
                    bestLoss = localLoss;
                    bestPoint = point;
                }
            }

            PositionSet(bestPoint.X, bestPoint.Y, setIndex);
        }

        return circles;

        void PositionSet(double x, double y, string set)
        {
            circles[set].X = x;
            circles[set].Y = y;
            positioned.Add(set);
        }
    }

    /// <summary>Returns the sum of the squared differences between the areas of the overlaps and their requested sizes.</summary>
    private static double Loss(List<VennResolvedArea> overlaps)
    {
        var output = 0.0;
        foreach (var area in overlaps)
        {
            if (area.Circles.Length is 1)
                continue;

            double overlap;
            if (area.Circles.Length is 2)
            {
                var left = area.Circles[0];
                var right = area.Circles[1];
                overlap = VennGeometry.CircleOverlap(left.Radius, right.Radius, VennGeometry.Distance(left, right));
            }
            else
            {
                overlap = VennGeometry.IntersectionArea(area.Circles);
            }

            output += area.Weight * (overlap - area.Size) * (overlap - area.Size);
        }

        return output;
    }

    /// <summary>Returns the cost of an evaluation of the loss, to bound the number of evaluations of the minimization.</summary>
    private static long GetLossCost(List<VennResolvedArea> areas)
    {
        var cost = 0L;
        foreach (var area in areas)
        {
            long count = area.Circles.Length;
            cost += count switch
            {
                1 => 0,
                2 => 1,
                _ => (count * count) + (count * count * count / 4),
            };
        }

        return cost;
    }

    private static List<VennResolvedArea> Resolve(VennKeyedMap<VennCircle> circles, IEnumerable<VennLayoutArea> areas)
    {
        var result = new List<VennResolvedArea>();
        foreach (var area in areas)
        {
            var resolved = new VennCircle[area.Sets.Count];
            for (var i = 0; i < resolved.Length; i++)
            {
                resolved[i] = circles[area.Sets[i]];
            }

            result.Add(new VennResolvedArea(resolved, area.Size, area.Weight));
        }

        return result;
    }

    /// <summary>Orients the clusters of overlapping circles, the second largest circle of each at <paramref name="orientation"/> from the largest, and packs them.</summary>
    internal static VennKeyedMap<VennCircle> NormalizeSolution(VennKeyedMap<VennCircle> solution, double orientation)
    {
        var circles = new List<VennCircle>();
        foreach (var key in solution.Keys)
        {
            var circle = solution[key].Clone();
            circle.SetId = key;
            circles.Add(circle);
        }

        var clusters = DisjointCluster(circles);
        foreach (var cluster in clusters)
        {
            OrientateCircles(cluster.Circles, orientation);
            cluster.Bounds = GetBoundingBox(cluster.Circles);
            cluster.Size = (cluster.Bounds.XMax - cluster.Bounds.XMin) * (cluster.Bounds.YMax - cluster.Bounds.YMin);
        }

        VennSort.StableSort(clusters, static (a, b) => b.Size - a.Size);

        // The largest cluster at (0, 0), then the others around it in a grid
        circles = clusters[0].Circles;
        var returnBounds = clusters[0].Bounds;
        var spacing = (returnBounds.XMax - returnBounds.XMin) / 50;
        var index = 1;
        while (index < clusters.Count)
        {
            AddCluster(index, right: true, bottom: false);
            AddCluster(index + 1, right: false, bottom: true);
            AddCluster(index + 2, right: true, bottom: true);
            index += 3;
            returnBounds = GetBoundingBox(circles);
        }

        var result = new VennKeyedMap<VennCircle>();
        foreach (var circle in circles)
        {
            result[circle.SetId] = circle;
        }

        return result;

        void AddCluster(int clusterIndex, bool right, bool bottom)
        {
            if (clusterIndex >= clusters.Count)
                return;

            var cluster = clusters[clusterIndex];
            var bounds = cluster.Bounds;
            double xOffset;
            double yOffset;
            if (right)
            {
                xOffset = returnBounds.XMax - bounds.XMin + spacing;
            }
            else
            {
                xOffset = returnBounds.XMax - bounds.XMax;
                var centreing = ((bounds.XMax - bounds.XMin) / 2) - ((returnBounds.XMax - returnBounds.XMin) / 2);
                if (centreing < 0)
                {
                    xOffset += centreing;
                }
            }

            if (bottom)
            {
                yOffset = returnBounds.YMax - bounds.YMin + spacing;
            }
            else
            {
                yOffset = returnBounds.YMax - bounds.YMax;
                var centreing = ((bounds.YMax - bounds.YMin) / 2) - ((returnBounds.YMax - returnBounds.YMin) / 2);
                if (centreing < 0)
                {
                    yOffset += centreing;
                }
            }

            foreach (var circle in cluster.Circles)
            {
                circle.X += xOffset;
                circle.Y += yOffset;
                circles.Add(circle);
            }
        }
    }

    /// <summary>Rotates the circles so the second largest is at <paramref name="orientation"/> from the largest, and mirrors them so the third is below.</summary>
    private static void OrientateCircles(List<VennCircle> circles, double orientation)
    {
        VennSort.StableSort(circles, static (a, b) => b.Radius - a.Radius);

        // The largest circle at (0, 0)
        if (circles.Count > 0)
        {
            var largestX = circles[0].X;
            var largestY = circles[0].Y;
            foreach (var circle in circles)
            {
                circle.X -= largestX;
                circle.Y -= largestY;
            }
        }

        if (circles.Count is 2)
        {
            // A circle inside the other is moved to its side (https://github.com/benfred/venn.js/issues/120)
            var distance = VennGeometry.Distance(circles[0], circles[1]);
            if (distance < Math.Abs(circles[1].Radius - circles[0].Radius))
            {
                circles[1].X = circles[0].X + circles[0].Radius - circles[1].Radius - 1e-10;
                circles[1].Y = circles[0].Y;
            }
        }

        if (circles.Count > 1)
        {
            var rotation = VennMath.Atan2(circles[1].X, circles[1].Y) - orientation;
            var c = VennMath.Cos(rotation);
            var s = VennMath.Sin(rotation);
            foreach (var circle in circles)
            {
                var x = circle.X;
                var y = circle.Y;
                circle.X = (c * x) - (s * y);
                circle.Y = (s * x) + (c * y);
            }
        }

        if (circles.Count > 2)
        {
            var angle = VennMath.Atan2(circles[2].X, circles[2].Y) - orientation;
            while (angle < 0)
            {
                angle += 2 * Math.PI;
            }

            while (angle > 2 * Math.PI)
            {
                angle -= 2 * Math.PI;
            }

            if (angle > Math.PI)
            {
                var slope = circles[1].Y / (1e-10 + circles[1].X);
                foreach (var circle in circles)
                {
                    var d = (circle.X + (slope * circle.Y)) / (1 + (slope * slope));
                    circle.X = (2 * d) - circle.X;
                    circle.Y = (2 * d * slope) - circle.Y;
                }
            }
        }
    }

    /// <summary>Groups the circles that overlap, directly or through other circles.</summary>
    private static List<VennCluster> DisjointCluster(List<VennCircle> circles)
    {
        foreach (var circle in circles)
        {
            circle.Parent = circle;
        }

        for (var i = 0; i < circles.Count; i++)
        {
            for (var j = i + 1; j < circles.Count; j++)
            {
                var maxDistance = circles[i].Radius + circles[j].Radius;
                if (VennGeometry.Distance(circles[i], circles[j]) + 1e-10 < maxDistance)
                {
                    Find(circles[j]).Parent = Find(circles[i]);
                }
            }
        }

        var clusters = new List<VennCluster>();
        var clusterIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var circle in circles)
        {
            var setId = Find(circle).SetId;
            if (!clusterIndices.TryGetValue(setId, out var index))
            {
                index = clusters.Count;
                clusterIndices[setId] = index;
                clusters.Add(new VennCluster());
            }

            clusters[index].Circles.Add(circle);
        }

        foreach (var circle in circles)
        {
            circle.Parent = null;
        }

        return clusters;

        static VennCircle Find(VennCircle circle)
        {
            var root = circle;
            while (root.Parent != root)
            {
                root = root.Parent!;
            }

            // Path compression
            while (circle.Parent != root)
            {
                var next = circle.Parent!;
                circle.Parent = root;
                circle = next;
            }

            return root;
        }
    }

    private static VennBounds GetBoundingBox(List<VennCircle> circles)
    {
        var bounds = new VennBounds(double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity);
        foreach (var circle in circles)
        {
            bounds = new VennBounds(
                Math.Min(bounds.XMin, circle.X - circle.Radius),
                Math.Max(bounds.XMax, circle.X + circle.Radius),
                Math.Min(bounds.YMin, circle.Y - circle.Radius),
                Math.Max(bounds.YMax, circle.Y + circle.Radius));
        }

        return bounds;
    }

    /// <summary>Scales and centers the circles to fit in a rectangle, with a padding.</summary>
    internal static VennKeyedMap<VennCircle> ScaleSolution(VennKeyedMap<VennCircle> solution, double width, double height, double padding)
    {
        var circles = new List<VennCircle>();
        foreach (var key in solution.Keys)
        {
            var circle = solution[key];
            circle.SetId = key;
            circles.Add(circle);
        }

        // Unlike venn.js, a drawing smaller than its padding does not give negative radii
        width = Math.Max(0, width - (2 * padding));
        height = Math.Max(0, height - (2 * padding));
        var bounds = GetBoundingBox(circles);
        if (bounds.XMax == bounds.XMin || bounds.YMax == bounds.YMin)
            return solution;

        var xScaling = width / (bounds.XMax - bounds.XMin);
        var yScaling = height / (bounds.YMax - bounds.YMin);
        var scaling = Math.Min(yScaling, xScaling);

        // Center the diagram
        var xOffset = (width - ((bounds.XMax - bounds.XMin) * scaling)) / 2;
        var yOffset = (height - ((bounds.YMax - bounds.YMin) * scaling)) / 2;
        var result = new VennKeyedMap<VennCircle>();
        foreach (var circle in circles)
        {
            result[circle.SetId] = new VennCircle(
                padding + xOffset + ((circle.X - bounds.XMin) * scaling),
                padding + yOffset + ((circle.Y - bounds.YMin) * scaling),
                scaling * circle.Radius)
            {
                SetId = circle.SetId,
            };
        }

        return result;
    }

    /// <summary>Computes the centers of the labels: the points of each area farthest from the outlines of the circles.</summary>
    private static Dictionary<string, VennTextCentre> ComputeTextCentres(VennKeyedMap<VennCircle> circles, IReadOnlyList<VennLayoutArea> areas)
    {
        var overlapped = GetOverlappingCircles(circles);

        // venn.js keeps the last center of the areas with the same key: compute only that one
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < areas.Count; i++)
        {
            last[GetLabelKey(areas[i].Sets)] = i;
        }

        var result = new Dictionary<string, VennTextCentre>(StringComparer.Ordinal);
        foreach (var (key, index) in last)
        {
            var area = areas[index].Sets;
            var areaIds = new HashSet<string>(StringComparer.Ordinal);
            var exclude = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in area)
            {
                areaIds.Add(set);

                // The circles that contain this area do not count for its center
                if (overlapped.TryGetValue(set, out var overlaps))
                {
                    exclude.UnionWith(overlaps);
                }
            }

            var interior = new List<VennCircle>();
            var exterior = new List<VennCircle>();
            foreach (var setId in circles.Keys)
            {
                if (areaIds.Contains(setId))
                {
                    interior.Add(circles[setId]);
                }
                else if (!exclude.Contains(setId))
                {
                    exterior.Add(circles[setId]);
                }
            }

            result[key] = interior.Count is 0 ? new VennTextCentre(0, -1000, Disjoint: true) : ComputeTextCentre(interior, exterior);
        }

        return result;
    }

    /// <summary>Returns the point that maximizes its distance to the outlines of the interior circles and of the exterior ones.</summary>
    private static VennTextCentre ComputeTextCentre(List<VennCircle> interior, List<VennCircle> exterior)
    {
        // An initial estimate: the sampled point with the largest margin
        var points = new List<(double X, double Y)>();
        foreach (var c in interior)
        {
            points.Add((c.X, c.Y));
            points.Add((c.X + (c.Radius / 2), c.Y));
            points.Add((c.X - (c.Radius / 2), c.Y));
            points.Add((c.X, c.Y + (c.Radius / 2)));
            points.Add((c.X, c.Y - (c.Radius / 2)));
        }

        var initial = points[0];
        var margin = CircleMargin(points[0].X, points[0].Y, interior, exterior);
        for (var i = 1; i < points.Count; i++)
        {
            var m = CircleMargin(points[i].X, points[i].Y, interior, exterior);
            if (m >= margin)
            {
                initial = points[i];
                margin = m;
            }
        }

        // Maximize the margin numerically (fmin reads minErrorDelta as the tolerance on the positions too)
        var (solution, _) = VennOptimizer.NelderMead(p => -1 * CircleMargin(p[0], p[1], interior, exterior), [initial.X, initial.Y], maxIterations: 500, minErrorDelta: 1e-10, minTolerance: 1e-10);
        var x = solution[0];
        var y = solution[1];

        // Check the solution, and fall back when the circles overlap completely
        var valid = true;
        foreach (var circle in interior)
        {
            if (VennGeometry.Distance(x, y, circle.X, circle.Y) > circle.Radius)
            {
                valid = false;
                break;
            }
        }

        foreach (var circle in exterior)
        {
            if (VennGeometry.Distance(x, y, circle.X, circle.Y) < circle.Radius)
            {
                valid = false;
                break;
            }
        }

        if (valid)
            return new VennTextCentre(x, y, Disjoint: false);

        if (interior.Count is 1)
            return new VennTextCentre(interior[0].X, interior[0].Y, Disjoint: false);

        VennGeometry.IntersectionArea(interior, out var arcs);
        if (arcs.Count is 0)
            return new VennTextCentre(0, -1000, Disjoint: true);

        if (arcs.Count is 1)
            return new VennTextCentre(arcs[0].Circle.X, arcs[0].Circle.Y, Disjoint: false);

        if (exterior.Count > 0)
            return ComputeTextCentre(interior, []);

        // The mean of the points of the intersection
        var centerX = 0.0;
        var centerY = 0.0;
        foreach (var arc in arcs)
        {
            centerX += arc.X1;
            centerY += arc.Y1;
        }

        return new VennTextCentre(centerX / arcs.Count, centerY / arcs.Count, Disjoint: false);
    }

    private static double CircleMargin(double x, double y, List<VennCircle> interior, List<VennCircle> exterior)
    {
        var margin = interior[0].Radius - VennGeometry.Distance(interior[0].X, interior[0].Y, x, y);
        for (var i = 1; i < interior.Count; i++)
        {
            var m = interior[i].Radius - VennGeometry.Distance(interior[i].X, interior[i].Y, x, y);
            if (m <= margin)
            {
                margin = m;
            }
        }

        foreach (var circle in exterior)
        {
            var m = VennGeometry.Distance(circle.X, circle.Y, x, y) - circle.Radius;
            if (m <= margin)
            {
                margin = m;
            }
        }

        return margin;
    }

    /// <summary>Returns, for each circle, the circles that contain it completely.</summary>
    private static Dictionary<string, List<string>> GetOverlappingCircles(VennKeyedMap<VennCircle> circles)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var ids = circles.Keys;
        foreach (var id in ids)
        {
            result[id] = [];
        }

        for (var i = 0; i < ids.Count; i++)
        {
            var a = circles[ids[i]];
            for (var j = i + 1; j < ids.Count; j++)
            {
                var b = circles[ids[j]];
                var d = VennGeometry.Distance(a, b);
                if (d + b.Radius <= a.Radius + 1e-10)
                {
                    result[ids[j]].Add(ids[i]);
                }
                else if (d + a.Radius <= b.Radius + 1e-10)
                {
                    result[ids[i]].Add(ids[j]);
                }
            }
        }

        return result;
    }

    private static bool IsFinite(VennKeyedMap<VennCircle> circles)
    {
        foreach (var key in circles.Keys)
        {
            var circle = circles[key];
            if (!double.IsFinite(circle.X) || !double.IsFinite(circle.Y) || !double.IsFinite(circle.Radius))
                return false;
        }

        return true;
    }

    /// <summary>Places the sets side by side, when the optimization does not give finite positions.</summary>
    private static VennKeyedMap<VennCircle> FallbackLayout(IReadOnlyList<VennLayoutArea> data)
    {
        var circles = new VennKeyedMap<VennCircle>();
        var x = 0.0;
        foreach (var area in data)
        {
            if (area.Sets.Count is not 1)
                continue;

            var radius = Math.Sqrt(area.Size / Math.PI);
            if (!double.IsFinite(radius))
            {
                radius = 1;
            }

            if (!circles.ContainsKey(area.Sets[0]))
            {
                circles[area.Sets[0]] = new VennCircle(x + radius, 0, radius);
                x += 2 * radius;
            }
        }

        return circles;
    }

    private sealed record VennResolvedArea(VennCircle[] Circles, double Size, double Weight);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct VennBounds(double XMin, double XMax, double YMin, double YMax);

    private sealed class VennCluster
    {
        public List<VennCircle> Circles { get; } = [];

        public VennBounds Bounds { get; set; }

        public double Size { get; set; }
    }
}
