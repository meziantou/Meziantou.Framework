namespace Meziantou.Framework.Charts;

/// <summary>
/// The circle packing of d3-hierarchy (ISC license): siblings are placed with the front-chain algorithm of Wang et al.
/// (2006), and their enclosing circle is found with Welzl's algorithm (1991) on a shuffle by a linear congruential
/// generator with a fixed seed, so the result is deterministic.
/// </summary>
internal static class CirclePacking
{
    /// <summary>Places circles tangent to each other around the origin, without overlap, then centers them on their enclosing circle.</summary>
    /// <returns>The radius of the enclosing circle.</returns>
    public static double PackSiblings(IReadOnlyList<PackCircle> circles, Func<double> random)
    {
        var n = circles.Count;
        if (n is 0)
            return 0;

        var first = circles[0];
        first.X = 0;
        first.Y = 0;
        if (n is 1)
            return first.Radius;

        var second = circles[1];
        first.X = -second.Radius;
        second.X = first.Radius;
        second.Y = 0;
        if (n is 2)
            return first.Radius + second.Radius;

        Place(second, first, circles[2]);
        var a = new ChainNode(first);
        var b = new ChainNode(second);
        var c = new ChainNode(circles[2]);
        a.Next = c.Previous = b;
        b.Next = a.Previous = c;
        c.Next = b.Previous = a;

        for (var i = 3; i < n; i++)
        {
            Place(a.Circle, b.Circle, circles[i]);
            c = new ChainNode(circles[i]);

            // Find the closest intersecting circle on the front chain, if any
            var j = b.Next!;
            var k = a.Previous!;
            var sj = b.Circle.Radius;
            var sk = a.Circle.Radius;
            var restart = false;
            do
            {
                if (sj <= sk)
                {
                    if (Intersects(j.Circle, c.Circle))
                    {
                        b = j;
                        a.Next = b;
                        b.Previous = a;
                        restart = true;
                        break;
                    }

                    sj += j.Circle.Radius;
                    j = j.Next!;
                }
                else
                {
                    if (Intersects(k.Circle, c.Circle))
                    {
                        a = k;
                        a.Next = b;
                        b.Previous = a;
                        restart = true;
                        break;
                    }

                    sk += k.Circle.Radius;
                    k = k.Previous!;
                }
            }
            while (j != k.Next);

            if (restart)
            {
                i--;
                continue;
            }

            // Insert the new circle between a and b, then find the pair of the chain closest to the origin
            c.Previous = a;
            c.Next = b;
            a.Next = c;
            b.Previous = c;
            b = c;
            var bestScore = Score(a);
            while ((c = c.Next!) != b)
            {
                var score = Score(c);
                if (score < bestScore)
                {
                    a = c;
                    bestScore = score;
                }
            }

            b = a.Next!;
        }

        var chain = new List<PackCircle> { b.Circle };
        for (var node = b.Next!; node != b; node = node.Next!)
        {
            chain.Add(node.Circle);
        }

        var enclosing = Enclose(chain, random);
        foreach (var circle in circles)
        {
            circle.X -= enclosing.X;
            circle.Y -= enclosing.Y;
        }

        return enclosing.Radius;
    }

    /// <summary>The smallest circle that encloses circles (Welzl's algorithm, with the shuffle of d3).</summary>
    public static PackCircle Enclose(IReadOnlyList<PackCircle> circles, Func<double> random)
    {
        var shuffled = circles.ToArray();
        for (var m = shuffled.Length; m > 0;)
        {
            var index = (int)(random() * m--);
            (shuffled[m], shuffled[index]) = (shuffled[index], shuffled[m]);
        }

        PackCircle? enclosing = null;
        var basis = new List<PackCircle>();
        var i = 0;
        while (i < shuffled.Length)
        {
            var p = shuffled[i];
            if (enclosing is not null && EnclosesWeak(enclosing, p))
            {
                i++;
            }
            else
            {
                // Rounding errors may leave no basis that encloses the circles, as with sizes far apart
                if (ExtendBasis(basis, p) is not { } extended)
                    return EncloseBounds(shuffled);

                basis = extended;
                enclosing = EncloseBasis(basis);
                if (!double.IsFinite(enclosing.X) || !double.IsFinite(enclosing.Y) || !double.IsFinite(enclosing.Radius))
                    return EncloseBounds(shuffled);

                i = 0;
            }
        }

        return enclosing ?? new PackCircle(0, 0, 0);
    }

    /// <summary>A circle that encloses circles, centered on their bounding box: larger than the smallest one, but always computable.</summary>
    internal static PackCircle EncloseBounds(IReadOnlyList<PackCircle> circles)
    {
        var x0 = circles.Min(circle => circle.X - circle.Radius);
        var x1 = circles.Max(circle => circle.X + circle.Radius);
        var y0 = circles.Min(circle => circle.Y - circle.Radius);
        var y1 = circles.Max(circle => circle.Y + circle.Radius);
        var x = (x0 / 2) + (x1 / 2);
        var y = (y0 / 2) + (y1 / 2);
        var radius = circles.Max(circle => Math.Sqrt(((circle.X - x) * (circle.X - x)) + ((circle.Y - y) * (circle.Y - y))) + circle.Radius);
        return new PackCircle(x, y, radius);
    }

    /// <summary>The linear congruential generator of d3, seeded with 1: values in [0, 1).</summary>
    public static Func<double> CreateRandom()
    {
        const ulong A = 1664525;
        const ulong C = 1013904223;
        const ulong M = 4294967296;
        ulong state = 1;
        return () =>
        {
            state = ((A * state) + C) % M;
            return state / (double)M;
        };
    }

    /// <summary>Places c tangent to a and b, on the counterclockwise side of the vector from a to b.</summary>
    internal static void Place(PackCircle b, PackCircle a, PackCircle c)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var d2 = (dx * dx) + (dy * dy);
        if (d2 > 0)
        {
            var a2 = a.Radius + c.Radius;
            a2 *= a2;
            var b2 = b.Radius + c.Radius;
            b2 *= b2;
            if (a2 > b2)
            {
                var x = (d2 + b2 - a2) / (2 * d2);
                var y = Math.Sqrt(Math.Max(0, (b2 / d2) - (x * x)));
                c.X = b.X - (x * dx) - (y * dy);
                c.Y = b.Y - (x * dy) + (y * dx);
            }
            else
            {
                var x = (d2 + a2 - b2) / (2 * d2);
                var y = Math.Sqrt(Math.Max(0, (a2 / d2) - (x * x)));
                c.X = a.X + (x * dx) - (y * dy);
                c.Y = a.Y + (x * dy) + (y * dx);
            }

            if (!double.IsFinite(c.X) || !double.IsFinite(c.Y))
            {
                PlaceStable(b, a, c, d2, a2, b2);
            }
        }
        else
        {
            c.X = a.X + c.Radius;
            c.Y = a.Y;
        }
    }

    /// <summary>
    /// The same placement, without dividing by the squared distance between a and b, which overflows when the circles
    /// are much larger than that distance: the distances along and across the line from a to b are computed directly.
    /// </summary>
    private static void PlaceStable(PackCircle b, PackCircle a, PackCircle c, double d2, double a2, double b2)
    {
        var d = Math.Sqrt(d2);
        var ux = (b.X - a.X) / d;
        var uy = (b.Y - a.Y) / d;
        if (a2 > b2)
        {
            var along = (d2 + b2 - a2) / (2 * d);
            var across = Math.Sqrt(Math.Max(0, b2 - (along * along)));
            c.X = b.X - (along * ux) - (across * uy);
            c.Y = b.Y - (along * uy) + (across * ux);
        }
        else
        {
            var along = (d2 + a2 - b2) / (2 * d);
            var across = Math.Sqrt(Math.Max(0, a2 - (along * along)));
            c.X = a.X + (along * ux) - (across * uy);
            c.Y = a.Y + (along * uy) + (across * ux);
        }
    }

    private static bool Intersects(PackCircle a, PackCircle b)
    {
        // d3's tolerance is 1e-6: for huge circles it is below the precision of their coordinates, so tangent circles
        // would intersect and the front chain would restart forever. Make it relative to the radii when they are large.
        var sum = a.Radius + b.Radius;
        var dr = sum - Math.Max(1e-6, sum * 1e-12);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return dr > 0 && dr * dr > (dx * dx) + (dy * dy);
    }

    private static double Score(ChainNode node)
    {
        var a = node.Circle;
        var b = node.Next!.Circle;
        var ab = a.Radius + b.Radius;
        var dx = ((a.X * b.Radius) + (b.X * a.Radius)) / ab;
        var dy = ((a.Y * b.Radius) + (b.Y * a.Radius)) / ab;
        return (dx * dx) + (dy * dy);
    }

    private static List<PackCircle>? ExtendBasis(List<PackCircle> basis, PackCircle p)
    {
        if (EnclosesWeakAll(p, basis))
            return [p];

        // If p is outside of the circle of a single element of the basis and p...
        foreach (var b in basis)
        {
            if (EnclosesNot(p, b) && EnclosesWeakAll(EncloseBasis2(b, p), basis))
                return [b, p];
        }

        for (var i = 0; i < basis.Count - 1; i++)
        {
            for (var j = i + 1; j < basis.Count; j++)
            {
                if (EnclosesNot(EncloseBasis2(basis[i], basis[j]), p)
                    && EnclosesNot(EncloseBasis2(basis[i], p), basis[j])
                    && EnclosesNot(EncloseBasis2(basis[j], p), basis[i])
                    && EnclosesWeakAll(EncloseBasis3(basis[i], basis[j], p), basis))
                {
                    return [basis[i], basis[j], p];
                }
            }
        }

        return null;
    }

    private static bool EnclosesNot(PackCircle a, PackCircle b)
    {
        var dr = a.Radius - b.Radius;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return dr < 0 || dr * dr < (dx * dx) + (dy * dy);
    }

    private static bool EnclosesWeak(PackCircle a, PackCircle b)
    {
        var dr = a.Radius - b.Radius + (Math.Max(Math.Max(a.Radius, b.Radius), 1) * 1e-9);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return dr > 0 && dr * dr > (dx * dx) + (dy * dy);
    }

    private static bool EnclosesWeakAll(PackCircle a, List<PackCircle> basis) => basis.TrueForAll(b => EnclosesWeak(a, b));

    private static PackCircle EncloseBasis(List<PackCircle> basis)
    {
        return basis.Count switch
        {
            1 => new PackCircle(basis[0].X, basis[0].Y, basis[0].Radius),
            2 => EncloseBasis2(basis[0], basis[1]),
            _ => EncloseBasis3(basis[0], basis[1], basis[2]),
        };
    }

    internal static PackCircle EncloseBasis2(PackCircle a, PackCircle b)
    {
        var x21 = b.X - a.X;
        var y21 = b.Y - a.Y;
        var r21 = b.Radius - a.Radius;
        var l = Math.Sqrt((x21 * x21) + (y21 * y21));
        return new PackCircle((a.X + b.X + (x21 / l * r21)) / 2, (a.Y + b.Y + (y21 / l * r21)) / 2, (l + a.Radius + b.Radius) / 2);
    }

    internal static PackCircle EncloseBasis3(PackCircle a, PackCircle b, PackCircle c)
    {
        double x1 = a.X, y1 = a.Y, r1 = a.Radius;
        double x2 = b.X, y2 = b.Y, r2 = b.Radius;
        double x3 = c.X, y3 = c.Y, r3 = c.Radius;
        var a2 = x1 - x2;
        var a3 = x1 - x3;
        var b2 = y1 - y2;
        var b3 = y1 - y3;
        var c2 = r2 - r1;
        var c3 = r3 - r1;
        var d1 = (x1 * x1) + (y1 * y1) - (r1 * r1);
        var d2 = d1 - (x2 * x2) - (y2 * y2) + (r2 * r2);
        var d3 = d1 - (x3 * x3) - (y3 * y3) + (r3 * r3);
        var ab = (a3 * b2) - (a2 * b3);
        var xa = (((b2 * d3) - (b3 * d2)) / (ab * 2)) - x1;
        var xb = ((b3 * c2) - (b2 * c3)) / ab;
        var ya = (((a3 * d2) - (a2 * d3)) / (ab * 2)) - y1;
        var yb = ((a2 * c3) - (a3 * c2)) / ab;
        var qa = (xb * xb) + (yb * yb) - 1;
        var qb = 2 * (r1 + (xa * xb) + (ya * yb));
        var qc = (xa * xa) + (ya * ya) - (r1 * r1);
        var r = -(Math.Abs(qa) > 1e-6 ? (qb + Math.Sqrt((qb * qb) - (4 * qa * qc))) / (2 * qa) : qc / qb);
        return new PackCircle(x1 + xa + (xb * r), y1 + ya + (yb * r), r);
    }

    private sealed class ChainNode(PackCircle circle)
    {
        public PackCircle Circle { get; } = circle;

        public ChainNode? Next { get; set; }

        public ChainNode? Previous { get; set; }
    }
}
