using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Overflow-checked arithmetic for dimensions, strides, pixel counts, capacities and byte sizes.
/// Every helper reports overflow through its return value instead of wrapping around, so callers can turn it into a
/// resource-limit failure before anything is allocated.
/// </summary>
internal static class CheckedSizes
{
    /// <summary>Gets the maximum length of one contiguous managed buffer (one row must fit in one buffer).</summary>
    public static int MaxBufferLength => Array.MaxLength;

    /// <summary>Multiplies two non-negative values.</summary>
    /// <returns><see langword="false"/> if the product overflows <see cref="long"/>.</returns>
    public static bool TryMultiply(long left, long right, out long result)
    {
        Debug.Assert(left >= 0 && right >= 0);
        if (left != 0 && right > long.MaxValue / left)
        {
            result = long.MaxValue;
            return false;
        }

        result = left * right;
        return true;
    }

    /// <summary>Adds two non-negative values.</summary>
    /// <returns><see langword="false"/> if the sum overflows <see cref="long"/>.</returns>
    public static bool TryAdd(long left, long right, out long result)
    {
        Debug.Assert(left >= 0 && right >= 0);
        if (right > long.MaxValue - left)
        {
            result = long.MaxValue;
            return false;
        }

        result = left + right;
        return true;
    }

    /// <summary>Rounds a non-negative value up to a multiple of a positive alignment.</summary>
    /// <returns><see langword="false"/> if the result overflows <see cref="long"/>.</returns>
    public static bool TryAlignUp(long value, int alignment, out long result)
    {
        Debug.Assert(value >= 0 && alignment > 0);
        var remainder = value % alignment;
        if (remainder == 0)
        {
            result = value;
            return true;
        }

        return TryAdd(value, alignment - remainder, out result);
    }

    /// <summary>Computes the number of visible bytes of one row: <c>width * bytesPerPixel</c>. Never overflows for <see cref="int"/> inputs.</summary>
    public static long GetRowLength(int width, int bytesPerPixel)
    {
        Debug.Assert(width >= 0 && bytesPerPixel > 0);
        return (long)width * bytesPerPixel;
    }

    /// <summary>Computes the number of pixels of a canvas: <c>width * height</c>. Never overflows for <see cref="int"/> inputs.</summary>
    public static long GetPixelCount(int width, int height)
    {
        Debug.Assert(width >= 0 && height >= 0);
        return (long)width * height;
    }

    /// <summary>Computes the visible bytes of a canvas: <c>width * height * bytesPerPixel</c>.</summary>
    /// <returns><see langword="false"/> if the result overflows <see cref="long"/>.</returns>
    public static bool TryGetVisibleByteCount(int width, int height, int bytesPerPixel, out long result)
        => TryMultiply(GetRowLength(width, bytesPerPixel), height, out result);

    /// <summary>Creates the exception reported when a live-allocation size cannot even be represented.</summary>
    public static ImageResourceLimitException CreateOverflowException(ImageResourceLimits limits)
        => new(ImageResourceLimitKind.LiveAllocationBytes, limits.MaxLiveAllocationBytes, requested: null);
}
