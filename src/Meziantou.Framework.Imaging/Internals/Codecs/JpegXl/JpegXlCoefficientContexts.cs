using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The contexts of the coefficients of a VarDCT group (ISO/IEC 18181-1, section C.8.3): the decoder reads the number of
/// non-zero coefficients of a block and its coefficients with them, and the encoder writes them with the same ones.
/// </summary>
internal static class JpegXlCoefficientContexts
{
    /// <summary>The side of a group of a VarDCT frame, in blocks: the stride of the non-zero counts of a channel.</summary>
    public const int GroupBlocks = 32;

    /// <summary>Gets the index, in the block context map, of the contexts of a channel (X, Y, B) for an order, a range of the quantization factor and a range of the LF values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBlockContextIndex(int channel, int order, int quantContextCount, int quantIndex, int lfContextCount, int lfIndex)
    {
        // The map is ordered Y, X, B
        var channelIndex = channel < 2 ? channel ^ 1 : 2;
        return (((((channelIndex * JpegXlVarDctTables.OrderCount) + order) * quantContextCount) + quantIndex) * lfContextCount) + lfIndex;
    }

    /// <summary>Predicts the number of non-zero coefficients of a block from the blocks above and on the left, in the group.</summary>
    /// <param name="channelNonZeros">The stored counts of the blocks of the channel, in rows of <see cref="GroupBlocks"/> values.</param>
    /// <param name="x">The column of the block in the group, in blocks of the channel.</param>
    /// <param name="y">The row of the block in the group, in blocks of the channel.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PredictNonZeros(ReadOnlySpan<byte> channelNonZeros, int x, int y)
    {
        var index = (y * GroupBlocks) + x;
        if (x == 0)
            return y == 0 ? 32 : channelNonZeros[index - GroupBlocks];

        if (y == 0)
            return channelNonZeros[index - 1];

        return (channelNonZeros[index - GroupBlocks] + channelNonZeros[index - 1] + 1) >> 1;
    }

    /// <summary>Gets the context of the number of non-zero coefficients of a block, in the contexts of a preset.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetNonZeroContext(int predicted, int blockContextCount, int blockContext)
        => ((predicted < 8 ? predicted : 4 + (predicted / 2)) * blockContextCount) + blockContext;

    /// <summary>Gets the count that a transform stores for each of its blocks, which the next blocks are predicted from.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetStoredNonZeros(int nonZeroCount, int blockCount, int logBlockCount)
        => (byte)((nonZeroCount + blockCount - 1) >> logBlockCount);

    /// <summary>Gets the first context of the coefficients of a block context, in the contexts of a preset.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCoefficientContextOffset(int blockContextCount, int blockContext)
        => (JpegXlVarDctTables.NonZeroContextCount * blockContextCount) + (JpegXlVarDctTables.CoefficientContextCount * blockContext);

    /// <summary>Gets the value of "the previous coefficient is not zero" before the first coefficient of a block.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetInitialPrevious(int nonZeroCount, int size) => nonZeroCount > size / 16 ? 0 : 1;

    /// <summary>Gets the context of a coefficient, relative to <see cref="GetCoefficientContextOffset"/>.</summary>
    /// <param name="remainingNonZeros">The number of non-zero coefficients that are not read yet, this one included.</param>
    /// <param name="rank">The rank of the coefficient in the order of its block.</param>
    /// <param name="blockCount">The number of blocks of the transform.</param>
    /// <param name="logBlockCount">The base-2 logarithm of <paramref name="blockCount"/>.</param>
    /// <param name="previous">1 when the previous coefficient is not zero, 0 otherwise.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCoefficientContext(int remainingNonZeros, int rank, int blockCount, int logBlockCount, int previous)
    {
        var remaining = (remainingNonZeros + blockCount - 1) >> logBlockCount;
        return ((JpegXlVarDctTables.CoefficientNonZeroContext[remaining] + JpegXlVarDctTables.CoefficientFrequencyContext[rank >> logBlockCount]) * 2) + previous;
    }
}
