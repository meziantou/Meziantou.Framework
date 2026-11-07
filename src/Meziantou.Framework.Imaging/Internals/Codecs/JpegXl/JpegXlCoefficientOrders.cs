namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The orders in which the coefficients of the transforms are coded (ISO/IEC 18181-1, section I.3): the natural order of
/// each of the 13 shapes of coefficient blocks, and, for each pass, the permutations of it that <c>HfPass</c> stores for
/// some shapes and channels.
/// </summary>
/// <remarks>
/// An order gives, for each rank, the position of the coefficient in its block, whose rows are the smaller side of the
/// transform. The lowest frequencies (one per block of 8x8 samples) come first, in raster order; they are not coded with
/// the others, which follow along the diagonals of the square of the larger side, in alternating directions, keeping the
/// rows that exist in the block.
/// </remarks>
internal sealed class JpegXlCoefficientOrders : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly Av1PooledArray<int>?[] _natural = new Av1PooledArray<int>?[JpegXlVarDctTables.OrderCount];
    private readonly Av1PooledArray<int>?[] _custom;

    public JpegXlCoefficientOrders(int passCount, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _custom = new Av1PooledArray<int>?[passCount * JpegXlVarDctTables.OrderCount * 3];
    }

    /// <summary>Gets the number of coefficients of the blocks of an order.</summary>
    public static int GetSize(int order) => JpegXlVarDctTables.OrderRows[order] * JpegXlVarDctTables.OrderColumns[order] * 64;

    /// <summary>Computes the natural order of a shape of coefficient blocks.</summary>
    /// <param name="rowBlocks">The number of rows of the block, in blocks of 8 coefficients.</param>
    /// <param name="columnBlocks">The number of columns of the block, in blocks of 8 coefficients; at least <paramref name="rowBlocks"/>.</param>
    /// <param name="order">Receives the position (<c>row * columns + column</c>) of the coefficient of each rank.</param>
    public static void ComputeNaturalOrder(int rowBlocks, int columnBlocks, Span<int> order)
    {
        var columns = columnBlocks * 8;
        var ratio = columnBlocks / rowBlocks;
        var shift = System.Numerics.BitOperations.Log2((uint)ratio);
        var next = rowBlocks * columnBlocks;

        // The diagonals of the square of the larger side, from the lowest frequency: those that start on the first row or
        // column, then those that end on the last ones
        for (var diagonal = 0; diagonal < columns; diagonal++)
        {
            for (var i = 0; i <= diagonal; i++)
            {
                var (x, y) = (diagonal & 1) == 0 ? (i, diagonal - i) : (diagonal - i, i);
                if ((y & (ratio - 1)) != 0)
                    continue;

                y >>= shift;
                var rank = x < columnBlocks && y < rowBlocks ? (y * columnBlocks) + x : next++;
                order[rank] = (y * columns) + x;
            }
        }

        for (var diagonal = columns - 2; diagonal >= 0; diagonal--)
        {
            for (var i = 0; i <= diagonal; i++)
            {
                var x = columns - 1 - (diagonal - i);
                var y = columns - 1 - i;
                if ((diagonal & 1) != 0)
                {
                    (x, y) = (y, x);
                }

                if ((y & (ratio - 1)) != 0)
                    continue;

                order[next++] = ((y >> shift) * columns) + x;
            }
        }
    }

    /// <summary>Reads the orders of a pass (the start of <c>HfPass</c>).</summary>
    public void ReadPass(JpegXlBitReader reader, int pass)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var usedOrders = reader.ReadU32(0x5F, 0, 0x13, 0, 0, 0, 0, 13);
        if (usedOrders == 0)
            return;

        using var code = JpegXlEntropyCode.Read(reader, JpegXlPermutation.ContextCount, _scope);
        using var decoder = new JpegXlEntropyDecoder(code, _scope);
        decoder.Begin(reader);
        for (var order = 0; order < JpegXlVarDctTables.OrderCount; order++)
        {
            if ((usedOrders & (1u << order)) == 0)
                continue;

            var natural = GetNaturalOrder(order);
            for (var channel = 0; channel < 3; channel++)
            {
                var index = (((pass * JpegXlVarDctTables.OrderCount) + order) * 3) + channel;
                _custom[index]?.Dispose();
                _custom[index] = null;
                _custom[index] = ReadOrder(reader, decoder, natural);
            }

            reader.EnsureNotOverrun();
        }

        decoder.EnsureFinalState();
    }

    /// <summary>Reads a permutation of the natural order: the lowest frequencies keep their place.</summary>
    private Av1PooledArray<int> ReadOrder(JpegXlBitReader reader, JpegXlEntropyDecoder decoder, ReadOnlySpan<int> natural)
    {
        var result = Av1PooledArray<int>.Rent(_scope, natural.Length, clear: false);
        try
        {
            var permutation = result.Span[..natural.Length];
            JpegXlPermutation.Read(reader, decoder, permutation, natural.Length / 64, _scope);
            for (var i = 0; i < permutation.Length; i++)
            {
                permutation[i] = natural[permutation[i]];
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>Gets the order of the coefficients of a channel (X, Y, B) for a pass.</summary>
    public ReadOnlySpan<int> GetOrder(int pass, int order, int channel)
    {
        var custom = _custom[(((pass * JpegXlVarDctTables.OrderCount) + order) * 3) + channel];
        return custom is null ? GetNaturalOrder(order) : custom.Span[..GetSize(order)];
    }

    public void Dispose()
    {
        for (var i = 0; i < _natural.Length; i++)
        {
            _natural[i]?.Dispose();
            _natural[i] = null;
        }

        for (var i = 0; i < _custom.Length; i++)
        {
            _custom[i]?.Dispose();
            _custom[i] = null;
        }
    }

    private ReadOnlySpan<int> GetNaturalOrder(int order)
    {
        var size = GetSize(order);
        var natural = _natural[order];
        if (natural is null)
        {
            Av1PooledArray<int>? owned = null;
            try
            {
                owned = Av1PooledArray<int>.Rent(_scope, size, clear: false);
                ComputeNaturalOrder(JpegXlVarDctTables.OrderRows[order], JpegXlVarDctTables.OrderColumns[order], owned.Span[..size]);
                _natural[order] = natural = owned;
                owned = null;
            }
            finally
            {
                owned?.Dispose();
            }
        }

        return natural.Span[..size];
    }
}
