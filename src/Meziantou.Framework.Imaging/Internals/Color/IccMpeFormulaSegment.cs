namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A formula curve segment (ICC.2:2023 Table 111; ICC.1:2022 Table 60 for the types 0 to 2).</summary>
internal sealed class IccMpeFormulaSegment : IccMpeSegment
{
    private readonly int _function;
    private readonly double[] _p;

    public IccMpeFormulaSegment(int function, double[] parameters)
    {
        _function = function;
        _p = parameters;
    }

    public override double Evaluate(double value)
    {
        var p = _p;
        return _function switch
        {
            0 => Math.Pow((p[1] * value) + p[2], p[0]) + p[3],                                       // (a X + b)^g + c
            1 => (p[1] * Math.Log10((p[2] * Math.Pow(value, p[0])) + p[3])) + p[4],                  // a log10(b X^g + c) + d
            2 => (p[0] * Math.Pow(p[1], (p[2] * value) + p[3])) + p[4],                              // a b^(c X + d) + e
            3 => (p[1] * Math.Pow((p[2] * value) + p[3], p[0])) + p[4],                              // a (b X + c)^g + d
            4 => (p[1] * Math.Log((p[4] * Math.Pow(value, p[0])) - p[2])) + p[3],                    // a ln(d X^g - b) + c
            5 => (p[5] * Math.Exp(((p[4] * Math.Pow(value, p[0])) - p[3]) / p[1])) + p[2],           // e exp((d X^g - c) / a) + b
            6 => p[5] * Math.Pow(Math.Max((p[6] * Math.Pow(value, p[1])) - p[2], 0) / (p[3] - (p[4] * Math.Pow(value, p[1]))), p[0]), // d (max(e X^g - a, 0) / (b - c X^g))^w
            _ => p[5] * Math.Pow((p[2] + (p[3] * Math.Pow(value, p[1]))) / (1 + (p[4] * Math.Pow(value, p[1]))), p[0]), // d ((a + b X^g) / (1 + c X^g))^w
        };
    }
}
