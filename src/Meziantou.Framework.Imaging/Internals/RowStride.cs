namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Validation of caller strides for copied raw import (<see cref="Image.ImportPixelData{TPixel}(ReadOnlySpan{TPixel}, int, int, int, ImageConfiguration?)"/>,
/// <see cref="Image.ImportPixelBytes{TPixel}(ReadOnlySpan{byte}, int, int, int, ImageConfiguration?)"/>) and export
/// (<see cref="ImageFrame.CopyPixelBytesTo(Span{byte}, int)"/>, <see cref="ImageFrame{TPixel}.CopyPixelDataTo(Span{TPixel}, int)"/>);
/// A zero stride means tightly packed rows; any other stride must be at least the
/// visible row length, and the last row only needs the visible row length. All arithmetic is checked.
/// </summary>
internal static class RowStride
{
    /// <summary>Resolves a caller stride to the effective distance between two rows.</summary>
    /// <param name="stride">The caller stride (non-negative, validated by the caller), or 0 for tightly packed rows.</param>
    /// <param name="rowLength">The visible row length, in the same unit.</param>
    /// <param name="parameterName">The name of the stride parameter.</param>
    /// <param name="unit">The unit, for the message ("pixels" or "bytes").</param>
    /// <returns>The effective stride.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stride"/> is neither 0 nor at least <paramref name="rowLength"/>.</exception>
    public static long Resolve(int stride, long rowLength, string parameterName, string unit)
    {
        if (stride == 0)
            return rowLength;

        if (stride < rowLength)
            throw new ArgumentOutOfRangeException(parameterName, stride, string.Create(CultureInfo.InvariantCulture, $"The stride must be 0 (tightly packed rows) or at least the visible row length ({rowLength} {unit})."));

        return stride;
    }

    /// <summary>Validates that a buffer holds <paramref name="height"/> rows separated by <paramref name="stride"/>.</summary>
    /// <param name="bufferLength">The buffer length.</param>
    /// <param name="stride">The effective stride (see <see cref="Resolve"/>).</param>
    /// <param name="rowLength">The visible row length.</param>
    /// <param name="height">The number of rows (positive).</param>
    /// <param name="parameterName">The name of the buffer parameter.</param>
    /// <param name="unit">The unit, for the message.</param>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static void EnsureLength(int bufferLength, long stride, long rowLength, int height, string parameterName, string unit)
    {
        var representable = CheckedSizes.TryMultiply(stride, height - 1L, out var offset) & CheckedSizes.TryAdd(offset, rowLength, out var required);
        if (representable && bufferLength >= required)
            return;

        var requiredText = representable ? required.ToString(CultureInfo.InvariantCulture) : "more than " + long.MaxValue.ToString(CultureInfo.InvariantCulture);
        throw new ArgumentException(
            string.Create(CultureInfo.InvariantCulture, $"The buffer has {bufferLength} {unit} but {requiredText} are required for {height} rows of {rowLength} {unit} with a stride of {stride} {unit} (the last row only needs the visible row length)."),
            parameterName);
    }

    /// <summary>Gets the offset of row <paramref name="y"/> in a buffer validated by <see cref="EnsureLength"/>.</summary>
    /// <param name="y">The row index.</param>
    /// <param name="stride">The effective stride.</param>
    /// <returns>The offset, which fits in the buffer.</returns>
    public static int GetOffset(int y, long stride) => checked((int)(y * stride));
}
