namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>
/// The numerical optimizers the layout of venn.js uses, ported from fmin (https://github.com/benfred/fmin, BSD-3-Clause,
/// see THIRD-PARTY-NOTICES.md), as bundled by venn.js 2.0.0: bisection, the Nelder-Mead downhill simplex and the
/// conjugate gradient with a Wolfe line search. A maximum number of evaluations bounds the time of the Nelder-Mead
/// method; it is high enough to never stop the small problems.
/// </summary>
internal static class VennOptimizer
{
    /// <summary>Finds a zero of a function between two points where its values have opposite signs.</summary>
    public static double Bisect(Func<double, double> f, double a, double b, int maxIterations = 100, double tolerance = 1e-10)
    {
        var fA = f(a);
        var fB = f(b);
        var delta = b - a;

        // fmin throws here; the callers never pass such points, but a value is safer than an exception
        if (fA * fB > 0)
            return b;

        if (fA == 0)
            return a;

        if (fB == 0)
            return b;

        for (var i = 0; i < maxIterations; i++)
        {
            delta /= 2;
            var mid = a + delta;
            var fMid = f(mid);
            if (fMid * fA >= 0)
            {
                a = mid;
            }

            if (Math.Abs(delta) < tolerance || fMid == 0)
                return mid;
        }

        return a + delta;
    }

    /// <summary>Minimizes a function with the downhill simplex method of Nelder and Mead.</summary>
    /// <returns>The best point and the value of the function there.</returns>
    public static (double[] X, double Fx) NelderMead(Func<double[], double> f, double[] x0, int maxIterations = 0, double minErrorDelta = 1e-6, double minTolerance = 1e-5, long maxEvaluations = long.MaxValue)
    {
        const double NonZeroDelta = 1.05;
        const double ZeroDelta = 0.001;
        const double Rho = 1;
        const double Chi = 2;
        const double Psi = -0.5;
        const double Sigma = 0.5;

        if (maxIterations <= 0)
        {
            maxIterations = x0.Length * 200;
        }

        var evaluations = 0L;
        double Evaluate(double[] point)
        {
            evaluations++;
            return f(point);
        }

        var n = x0.Length;
        var simplex = new List<SimplexPoint>(n + 1) { new((double[])x0.Clone(), 0) };
        simplex[0].Fx = Evaluate(simplex[0].X);
        for (var i = 0; i < n; i++)
        {
            var point = (double[])x0.Clone();

            // JavaScript: point[i] ? point[i] * nonZeroDelta : zeroDelta, where 0 and NaN are false
            point[i] = point[i] != 0 && !double.IsNaN(point[i]) ? point[i] * NonZeroDelta : ZeroDelta;
            simplex.Add(new SimplexPoint(point, Evaluate(point)));
        }

        var centroid = (double[])x0.Clone();
        var reflected = (double[])x0.Clone();
        var contracted = (double[])x0.Clone();
        var expanded = (double[])x0.Clone();
        for (var iteration = 0; iteration < maxIterations && evaluations < maxEvaluations; iteration++)
        {
            VennSort.StableSort(simplex, static (a, b) => a.Fx - b.Fx);

            var maxDiff = 0.0;
            for (var i = 0; i < n; i++)
            {
                maxDiff = Math.Max(maxDiff, Math.Abs(simplex[0].X[i] - simplex[1].X[i]));
            }

            if (Math.Abs(simplex[0].Fx - simplex[n].Fx) < minErrorDelta && maxDiff < minTolerance)
                break;

            // The centroid of all the points but the worst
            for (var i = 0; i < n; i++)
            {
                centroid[i] = 0;
                for (var j = 0; j < n; j++)
                {
                    centroid[i] += simplex[j].X[i];
                }

                centroid[i] /= n;
            }

            // Reflect the worst point past the centroid
            var worst = simplex[n];
            WeightedSum(reflected, 1 + Rho, centroid, -Rho, worst.X);
            var reflectedFx = Evaluate(reflected);

            if (reflectedFx < simplex[0].Fx)
            {
                // The best point so far: try to go further
                WeightedSum(expanded, 1 + Chi, centroid, -Chi, worst.X);
                var expandedFx = Evaluate(expanded);
                if (expandedFx < reflectedFx)
                {
                    UpdateSimplex(expanded, expandedFx);
                }
                else
                {
                    UpdateSimplex(reflected, reflectedFx);
                }
            }
            else if (reflectedFx >= simplex[n - 1].Fx)
            {
                // Worse than the second worst point: contract
                var shouldReduce = false;
                if (reflectedFx > worst.Fx)
                {
                    WeightedSum(contracted, 1 + Psi, centroid, -Psi, worst.X);
                    var contractedFx = Evaluate(contracted);
                    if (contractedFx < worst.Fx)
                    {
                        UpdateSimplex(contracted, contractedFx);
                    }
                    else
                    {
                        shouldReduce = true;
                    }
                }
                else
                {
                    WeightedSum(contracted, 1 - (Psi * Rho), centroid, Psi * Rho, worst.X);
                    var contractedFx = Evaluate(contracted);
                    if (contractedFx < reflectedFx)
                    {
                        UpdateSimplex(contracted, contractedFx);
                    }
                    else
                    {
                        shouldReduce = true;
                    }
                }

                if (shouldReduce)
                {
                    for (var i = 1; i < simplex.Count; i++)
                    {
                        WeightedSum(simplex[i].X, 1 - Sigma, simplex[0].X, Sigma, simplex[i].X);
                        simplex[i].Fx = Evaluate(simplex[i].X);
                    }
                }
            }
            else
            {
                UpdateSimplex(reflected, reflectedFx);
            }
        }

        VennSort.StableSort(simplex, static (a, b) => a.Fx - b.Fx);
        return (simplex[0].X, simplex[0].Fx);

        void UpdateSimplex(double[] value, double fx)
        {
            Array.Copy(value, simplex[n].X, n);
            simplex[n].Fx = fx;
        }
    }

    /// <summary>Minimizes a function with the nonlinear conjugate gradient method (Polak-Ribiere).</summary>
    /// <param name="f">The function: it returns the value at a point, and writes the gradient into its second argument.</param>
    /// <param name="initial">The starting point.</param>
    /// <param name="maxIterations">The maximum number of iterations, or 0 for 20 times the number of dimensions.</param>
    /// <returns>The best point and the value of the function there.</returns>
    public static (double[] X, double Fx) ConjugateGradient(Func<double[], double[], double> f, double[] initial, int maxIterations = 0)
    {
        var current = new GradientPoint((double[])initial.Clone(), (double[])initial.Clone());
        var next = new GradientPoint((double[])initial.Clone(), (double[])initial.Clone());
        var yk = (double[])initial.Clone();
        var a = 1.0;
        if (maxIterations <= 0)
        {
            maxIterations = initial.Length * 20;
        }

        current.Fx = f(current.X, current.FxPrime);
        var pk = (double[])current.FxPrime.Clone();
        Scale(pk, current.FxPrime, -1);
        for (var i = 0; i < maxIterations; i++)
        {
            a = WolfeLineSearch(f, pk, current, next, a);
            if (a == 0 || double.IsNaN(a))
            {
                // No point satisfies the Wolfe conditions: reset the direction
                Scale(pk, current.FxPrime, -1);
            }
            else
            {
                WeightedSum(yk, 1, next.FxPrime, -1, current.FxPrime);
                var deltaK = Dot(current.FxPrime, current.FxPrime);
                var betaK = Math.Max(0, Dot(yk, next.FxPrime) / deltaK);
                WeightedSum(pk, betaK, pk, -1, next.FxPrime);
                (current, next) = (next, current);
            }

            if (Norm2(current.FxPrime) <= 1e-5)
                break;
        }

        return (current.X, current.Fx);
    }

    /// <summary>Searches along a direction for a step that satisfies the Wolfe conditions (Nocedal and Wright, p. 59-60).</summary>
    /// <returns>The step, or 0 when none is found.</returns>
    private static double WolfeLineSearch(Func<double[], double[], double> f, double[] pk, GradientPoint current, GradientPoint next, double a)
    {
        const double C1 = 1e-6;
        const double C2 = 0.1;

        var phi0 = current.Fx;
        var phiPrime0 = Dot(current.FxPrime, pk);
        double phi;
        var phiOld = phi0;
        double phiPrime;
        var a0 = 0.0;
        if (a == 0 || double.IsNaN(a))
        {
            a = 1;
        }

        for (var iteration = 0; iteration < 10; iteration++)
        {
            WeightedSum(next.X, 1, current.X, a, pk);
            phi = next.Fx = f(next.X, next.FxPrime);
            phiPrime = Dot(next.FxPrime, pk);
            if (phi > phi0 + (C1 * a * phiPrime0) || (iteration != 0 && phi >= phiOld))
                return Zoom(a0, a, phiOld);

            if (Math.Abs(phiPrime) <= -C2 * phiPrime0)
                return a;

            if (phiPrime >= 0)
                return Zoom(a, a0, phi);

            phiOld = phi;
            a0 = a;
            a *= 2;
        }

        return a;

        double Zoom(double aLow, double aHigh, double phiLow)
        {
            for (var iteration = 0; iteration < 16; iteration++)
            {
                a = (aLow + aHigh) / 2;
                WeightedSum(next.X, 1, current.X, a, pk);
                phi = next.Fx = f(next.X, next.FxPrime);
                phiPrime = Dot(next.FxPrime, pk);
                if (phi > phi0 + (C1 * a * phiPrime0) || phi >= phiLow)
                {
                    aHigh = a;
                }
                else
                {
                    if (Math.Abs(phiPrime) <= -C2 * phiPrime0)
                        return a;

                    if (phiPrime * (aHigh - aLow) >= 0)
                    {
                        aHigh = aLow;
                    }

                    aLow = a;
                    phiLow = phi;
                }
            }

            return 0;
        }
    }

    public static double Dot(double[] a, double[] b)
    {
        var result = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            result += a[i] * b[i];
        }

        return result;
    }

    public static double Norm2(double[] a) => Math.Sqrt(Dot(a, a));

    private static void Scale(double[] result, double[] value, double c)
    {
        for (var i = 0; i < value.Length; i++)
        {
            result[i] = value[i] * c;
        }
    }

    private static void WeightedSum(double[] result, double w1, double[] v1, double w2, double[] v2)
    {
        for (var j = 0; j < result.Length; j++)
        {
            result[j] = (w1 * v1[j]) + (w2 * v2[j]);
        }
    }

    private sealed class SimplexPoint(double[] x, double fx)
    {
        public double[] X { get; } = x;

        public double Fx { get; set; } = fx;
    }

    private sealed class GradientPoint(double[] x, double[] fxPrime)
    {
        public double[] X { get; } = x;

        public double[] FxPrime { get; } = fxPrime;

        public double Fx { get; set; }
    }
}
