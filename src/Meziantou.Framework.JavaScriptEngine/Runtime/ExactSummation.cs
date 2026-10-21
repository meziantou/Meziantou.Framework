using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The exact sum of finite numbers (a superaccumulator), rounded once at the end: the summation of Math.sumPrecise. Each
/// number is m \u00D7 2^e with e \u2265 -1074, so the sum is an integer multiple of 2^-1074 kept in 32-bit limbs.
/// </summary>
internal sealed class ExactSummation
{
    // The limb i has the weight 2^(32i - 1074): 2098 bits for the numbers, plus room for 2^53 additions
    private const int LimbCount = 70;
    private const int AdditionsBeforeNormalization = 1 << 30;

    private readonly long[] _limbs = new long[LimbCount];
    private int _pendingAdditions;

    /// <summary>Adds a finite number.</summary>
    public void Add(double value)
    {
        if (value == 0)
            return;

        NumberFormatting.Decompose(value, out var significand, out var exponent);
        var offset = exponent + 1074;
        var index = offset >> 5;
        var shifted = (UInt128)significand << (offset & 31);
        var low = (long)(ulong)(shifted & uint.MaxValue);
        var middle = (long)(ulong)((shifted >> 32) & uint.MaxValue);
        var high = (long)(ulong)(shifted >> 64);
        if (value < 0)
        {
            _limbs[index] -= low;
            _limbs[index + 1] -= middle;
            _limbs[index + 2] -= high;
        }
        else
        {
            _limbs[index] += low;
            _limbs[index + 1] += middle;
            _limbs[index + 2] += high;
        }

        if (++_pendingAdditions >= AdditionsBeforeNormalization)
        {
            Normalize();
        }
    }

    /// <summary>Propagates the carries: every limb but the last one is between 0 and 2^32 - 1.</summary>
    private void Normalize()
    {
        for (var i = 0; i < LimbCount - 1; i++)
        {
            var carry = _limbs[i] >> 32;
            _limbs[i] -= carry << 32;
            _limbs[i + 1] += carry;
        }

        _pendingAdditions = 0;
    }

    /// <summary>Gets the number closest to the sum (round half to even); \u00B1Infinity when it is too large.</summary>
    public double GetResult()
    {
        Normalize();
        var sum = BigInteger.Zero;
        for (var i = LimbCount - 1; i >= 0; i--)
        {
            sum = (sum << 32) + _limbs[i];
        }

        return BigIntOperations.ToDouble(sum, -1074);
    }
}
