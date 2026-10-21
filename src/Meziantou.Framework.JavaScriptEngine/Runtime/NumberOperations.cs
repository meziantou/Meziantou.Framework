namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The operations of the Number type that differ from the IEEE 754 operations of .NET (https://tc39.es/ecma262/#sec-numeric-types-number).</summary>
internal static class NumberOperations
{
    /// <summary>Number::exponentiate(base, exponent): <c>**</c> and Math.pow.</summary>
    public static double Exponentiate(double @base, double exponent)
    {
        // IEEE 754 pow returns 1 for pow(1, NaN), pow(1, \u00B1Infinity) and pow(-1, \u00B1Infinity); ECMAScript returns NaN
        if (double.IsNaN(exponent))
            return double.NaN;

        if (double.IsInfinity(exponent) && Math.Abs(@base) == 1)
            return double.NaN;

        return Math.Pow(@base, exponent);
    }
}
