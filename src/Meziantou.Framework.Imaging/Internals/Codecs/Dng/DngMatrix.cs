namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A small dense matrix of doubles, row-major, for the color transforms of DNG (at most 4x4).</summary>
internal sealed class DngMatrix
{
    private readonly double[] _values;

    public DngMatrix(int rows, int columns, double[]? values = null)
    {
        Rows = rows;
        Columns = columns;
        _values = values ?? new double[rows * columns];
        if (_values.Length != rows * columns)
            throw new ArgumentException("The number of values does not match the dimensions.", nameof(values));
    }

    public int Rows { get; }

    public int Columns { get; }

    public double this[int row, int column]
    {
        get => _values[(row * Columns) + column];
        set => _values[(row * Columns) + column] = value;
    }

    public static DngMatrix Identity(int size)
    {
        var result = new DngMatrix(size, size);
        for (var i = 0; i < size; i++)
        {
            result[i, i] = 1;
        }

        return result;
    }

    public static DngMatrix Diagonal(ReadOnlySpan<double> values)
    {
        var result = new DngMatrix(values.Length, values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            result[i, i] = values[i];
        }

        return result;
    }

    public static DngMatrix Column(ReadOnlySpan<double> values) => new(values.Length, 1, values.ToArray());

    public DngMatrix Multiply(DngMatrix right)
    {
        if (Columns != right.Rows)
            throw new ArgumentException("The dimensions of the matrices do not match.", nameof(right));

        var result = new DngMatrix(Rows, right.Columns);
        for (var i = 0; i < Rows; i++)
        {
            for (var j = 0; j < right.Columns; j++)
            {
                var sum = 0.0;
                for (var k = 0; k < Columns; k++)
                {
                    sum += this[i, k] * right[k, j];
                }

                result[i, j] = sum;
            }
        }

        return result;
    }

    public DngMatrix Scale(double factor)
    {
        var result = new DngMatrix(Rows, Columns);
        for (var i = 0; i < _values.Length; i++)
        {
            result._values[i] = _values[i] * factor;
        }

        return result;
    }

    /// <summary>Gets <c>weight * this + (1 - weight) * other</c>.</summary>
    public DngMatrix Interpolate(DngMatrix other, double weight)
    {
        var result = new DngMatrix(Rows, Columns);
        for (var i = 0; i < _values.Length; i++)
        {
            result._values[i] = (weight * _values[i]) + ((1 - weight) * other._values[i]);
        }

        return result;
    }

    public DngMatrix Transpose()
    {
        var result = new DngMatrix(Columns, Rows);
        for (var i = 0; i < Rows; i++)
        {
            for (var j = 0; j < Columns; j++)
            {
                result[j, i] = this[i, j];
            }
        }

        return result;
    }

    /// <summary>Inverts a square matrix by Gauss-Jordan elimination with partial pivoting.</summary>
    /// <returns>The inverse, or <see langword="null"/> when the matrix is singular.</returns>
    public DngMatrix? TryInvert()
    {
        if (Rows != Columns)
            throw new InvalidOperationException("Only a square matrix is inverted.");

        var size = Rows;
        var width = 2 * size;
        var work = new double[size * width];
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < size; j++)
            {
                work[(i * width) + j] = this[i, j];
            }

            work[(i * width) + size + i] = 1;
        }

        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < size; row++)
            {
                if (Math.Abs(work[(row * width) + column]) > Math.Abs(work[(pivot * width) + column]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(work[(pivot * width) + column]) < 1e-12)
                return null;

            if (pivot != column)
            {
                for (var j = 0; j < 2 * size; j++)
                {
                    (work[(pivot * width) + j], work[(column * width) + j]) = (work[(column * width) + j], work[(pivot * width) + j]);
                }
            }

            var divisor = work[(column * width) + column];
            for (var j = 0; j < 2 * size; j++)
            {
                work[(column * width) + j] /= divisor;
            }

            for (var row = 0; row < size; row++)
            {
                if (row == column)
                    continue;

                var factor = work[(row * width) + column];
                if (factor == 0)
                    continue;

                for (var j = 0; j < 2 * size; j++)
                {
                    work[(row * width) + j] -= factor * work[(column * width) + j];
                }
            }
        }

        var result = new DngMatrix(size, size);
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < size; j++)
            {
                result[i, j] = work[(i * width) + size + j];
            }
        }

        return result;
    }

    /// <summary>Gets the inverse of a square matrix, or the pseudo-inverse <c>(AᵀA)⁻¹Aᵀ</c> of a matrix of more rows than columns.</summary>
    /// <returns>The (pseudo-)inverse, or <see langword="null"/> when it does not exist.</returns>
    public DngMatrix? TryPseudoInvert()
    {
        if (Rows == Columns)
            return TryInvert();

        var transpose = Transpose();
        return transpose.Multiply(this).TryInvert()?.Multiply(transpose);
    }

    public void CopyTo(Span<double> destination) => _values.CopyTo(destination);
}
