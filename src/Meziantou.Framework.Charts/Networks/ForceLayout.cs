namespace Meziantou.Framework.Charts;

/// <summary>
/// The force-directed layout of d3-force (ISC license), run for a fixed number of ticks so it is deterministic: the nodes
/// start on d3's phyllotaxis spiral, and the jiggle that separates coincident nodes comes from d3's seeded LCG. The forces
/// are, in this order, the link force (springs of a given length), the many-body force (a repulsion between every pair of
/// nodes, summed exactly instead of with d3's Barnes–Hut approximation) and the centering force.
/// </summary>
internal sealed class ForceLayout
{
    private const double InitialRadius = 10;
    private static readonly double InitialAngle = Math.PI * (3 - Math.Sqrt(5));

    private readonly int _count;
    private readonly IReadOnlyList<(int Source, int Target)> _links;
    private readonly double[] _linkStrengths;
    private readonly double[] _linkBiases;
    private readonly Func<double> _random = CirclePacking.CreateRandom();

    public ForceLayout(int count, IReadOnlyList<(int Source, int Target)> links)
    {
        _count = count;
        _links = links;
        X = new double[count];
        Y = new double[count];
        VX = new double[count];
        VY = new double[count];
        for (var i = 0; i < count; i++)
        {
            var radius = InitialRadius * Math.Sqrt(0.5 + i);
            var angle = i * InitialAngle;
            X[i] = radius * Math.Cos(angle);
            Y[i] = radius * Math.Sin(angle);
        }

        // d3: the strength of a link is 1 / the degree of its less connected end; the bias moves the less connected end more
        var degrees = new int[count];
        foreach (var (source, target) in links)
        {
            degrees[source]++;
            degrees[target]++;
        }

        _linkStrengths = new double[links.Count];
        _linkBiases = new double[links.Count];
        for (var i = 0; i < links.Count; i++)
        {
            var (source, target) = links[i];
            _linkStrengths[i] = 1.0 / Math.Min(degrees[source], degrees[target]);
            _linkBiases[i] = degrees[source] / (double)(degrees[source] + degrees[target]);
        }
    }

    public double[] X { get; }

    public double[] Y { get; }

    public double[] VX { get; }

    public double[] VY { get; }

    public double Alpha { get; private set; } = 1;

    /// <summary>Gets or sets the length of the links. The default is 30, as d3.</summary>
    public double LinkDistance { get; set; } = 30;

    /// <summary>Gets or sets the strength of the many-body force: negative values repel. The default is -30, as d3.</summary>
    public double ManyBodyStrength { get; set; } = -30;

    /// <summary>The number of ticks d3 runs until alpha goes under 0.001: 300.</summary>
    public static int DefaultTicks => (int)Math.Ceiling(Math.Log(AlphaMin) / Math.Log(1 - AlphaDecay));

    private static double AlphaMin => 0.001;

    private static double AlphaDecay => 1 - Math.Pow(AlphaMin, 1.0 / 300);

    private const double VelocityDecay = 0.6;

    public void Run(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            Tick();
        }
    }

    public void Tick()
    {
        Alpha += (0 - Alpha) * AlphaDecay;
        ApplyLinks();
        ApplyManyBody();
        for (var i = 0; i < _count; i++)
        {
            VX[i] *= VelocityDecay;
            X[i] += VX[i];
            VY[i] *= VelocityDecay;
            Y[i] += VY[i];
        }

        ApplyCenter();
    }

    private double Jiggle() => (_random() - 0.5) * 1e-6;

    private void ApplyLinks()
    {
        for (var i = 0; i < _links.Count; i++)
        {
            var (source, target) = _links[i];
            var x = X[target] + VX[target] - X[source] - VX[source];
            if (x is 0)
            {
                x = Jiggle();
            }

            var y = Y[target] + VY[target] - Y[source] - VY[source];
            if (y is 0)
            {
                y = Jiggle();
            }

            var length = Math.Sqrt((x * x) + (y * y));
            length = (length - LinkDistance) / length * Alpha * _linkStrengths[i];
            x *= length;
            y *= length;
            var bias = _linkBiases[i];
            VX[target] -= x * bias;
            VY[target] -= y * bias;
            VX[source] += x * (1 - bias);
            VY[source] += y * (1 - bias);
        }
    }

    private void ApplyManyBody()
    {
        for (var i = 0; i < _count; i++)
        {
            for (var j = 0; j < _count; j++)
            {
                if (i == j)
                    continue;

                var x = X[j] - X[i];
                var y = Y[j] - Y[i];
                var l = (x * x) + (y * y);
                if (x is 0)
                {
                    x = Jiggle();
                    l += x * x;
                }

                if (y is 0)
                {
                    y = Jiggle();
                    l += y * y;
                }

                // d3's distanceMin² is 1: closer nodes do not push harder than nodes at a distance of 1
                if (l < 1)
                {
                    l = Math.Sqrt(l);
                }

                var w = ManyBodyStrength * Alpha / l;
                VX[i] += x * w;
                VY[i] += y * w;
            }
        }
    }

    private void ApplyCenter()
    {
        if (_count is 0)
            return;

        var sx = 0.0;
        var sy = 0.0;
        for (var i = 0; i < _count; i++)
        {
            sx += X[i];
            sy += Y[i];
        }

        sx /= _count;
        sy /= _count;
        for (var i = 0; i < _count; i++)
        {
            X[i] -= sx;
            Y[i] -= sy;
        }
    }
}
