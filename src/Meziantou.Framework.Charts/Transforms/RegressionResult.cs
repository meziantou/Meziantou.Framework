using System.Globalization;

namespace Meziantou.Framework.Charts;

/// <summary>A polynomial fitted by least squares: y = c0 + c1 x + c2 x² + ...</summary>
public sealed class RegressionResult
{
    internal RegressionResult(double[] coefficients, double rSquared)
    {
        Coefficients = coefficients;
        RSquared = rSquared;
    }

    /// <summary>Gets the coefficients, from the constant term to the term of the highest degree.</summary>
    public IReadOnlyList<double> Coefficients { get; }

    /// <summary>Gets the degree of the polynomial.</summary>
    public int Degree => Coefficients.Count - 1;

    /// <summary>Gets the coefficient of determination: the share of the variance of y that the fit explains, from 0 to 1.</summary>
    public double RSquared { get; }

    /// <summary>Evaluates the polynomial.</summary>
    /// <param name="x">The x value.</param>
    /// <returns>The fitted y value.</returns>
    public double Evaluate(double x)
    {
        // Horner's method
        var result = 0.0;
        for (var i = Coefficients.Count - 1; i >= 0; i--)
        {
            result = (result * x) + Coefficients[i];
        }

        return result;
    }

    /// <summary>Returns the equation, such as <c>y = 1.5x + 2 (R² = 0.98)</c>.</summary>
    /// <returns>The equation.</returns>
    public override string ToString()
    {
        // The terms that are rounding noise next to the largest coefficient are left out
        var largest = Coefficients.Max(Math.Abs);
        var terms = new List<string>();
        for (var i = Coefficients.Count - 1; i >= 0; i--)
        {
            var coefficient = NumberFormats.RoundSignificant(Coefficients[i], 4);
            if (Math.Abs(coefficient) <= largest * 1e-9)
                continue;

            var magnitude = NumberFormats.Default(Math.Abs(coefficient));
            var power = i switch { 0 => string.Empty, 1 => "x", _ => "x^" + i.ToString(CultureInfo.InvariantCulture) };
            var term = i > 0 && magnitude is "1" ? power : magnitude + power;
            terms.Add(terms.Count is 0 ? (coefficient < 0 ? "-" + term : term) : (coefficient < 0 ? "- " : "+ ") + term);
        }

        var equation = terms.Count is 0 ? "0" : string.Join(' ', terms);
        return "y = " + equation + " (R² = " + NumberFormats.RoundSignificant(RSquared, 4).ToString(CultureInfo.InvariantCulture) + ")";
    }
}
