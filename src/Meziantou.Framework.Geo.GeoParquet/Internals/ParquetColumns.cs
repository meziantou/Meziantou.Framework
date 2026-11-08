using Parquet;
using Parquet.Schema;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Reads the values of the columns of a row group, and the extent of a row group from the statistics of its bbox covering.</summary>
internal static class ParquetColumns
{
    private static readonly HashSet<Type> SupportedTypes =
    [
        typeof(bool), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal), typeof(DateTime), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(Guid),
        typeof(string), typeof(ReadOnlyMemory<char>), typeof(byte[]), typeof(ReadOnlyMemory<byte>),
    ];

    /// <summary>Gets a value that indicates whether the values of a column can be read: a single value of a primitive type.</summary>
    public static bool IsSupported(DataField field) => !field.IsArray && SupportedTypes.Contains(field.ClrType);

    /// <summary>Gets a value that indicates whether a column holds byte arrays, such as the WKB of geometries.</summary>
    public static bool IsBinary(DataField field) => !field.IsArray && (field.ClrType == typeof(byte[]) || field.ClrType == typeof(ReadOnlyMemory<byte>));

    public static async ValueTask<byte[]?[]> ReadBinaryAsync(ParquetRowGroupReader rowGroup, DataField field, int rowCount, CancellationToken cancellationToken)
    {
        var values = new byte[]?[rowCount];
        await rowGroup.ReadAsync(field, values.AsMemory(), repetitionLevels: null, cancellationToken).ConfigureAwait(false);
        return values;
    }

    public static async ValueTask<object?[]> ReadAsync(ParquetRowGroupReader rowGroup, DataField field, int rowCount, CancellationToken cancellationToken)
    {
        var type = field.ClrType;
        // Parquet.Net 6 describes the strings and the byte arrays as memory
        if (type == typeof(string) || type == typeof(ReadOnlyMemory<char>))
        {
            var values = new string?[rowCount];
            await rowGroup.ReadAsync(field, values.AsMemory(), repetitionLevels: null, cancellationToken).ConfigureAwait(false);
            return values;
        }

        if (IsBinary(field))
            return await ReadBinaryAsync(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false);

        return type switch
        {
            _ when type == typeof(bool) => await ReadAsync<bool>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(sbyte) => await ReadAsync<sbyte>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(byte) => await ReadAsync<byte>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(short) => await ReadAsync<short>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(ushort) => await ReadAsync<ushort>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(int) => await ReadAsync<int>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(uint) => await ReadAsync<uint>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(long) => await ReadAsync<long>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(ulong) => await ReadAsync<ulong>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(float) => await ReadAsync<float>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(double) => await ReadAsync<double>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(decimal) => await ReadAsync<decimal>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(DateTime) => await ReadAsync<DateTime>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(DateOnly) => await ReadAsync<DateOnly>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(TimeOnly) => await ReadAsync<TimeOnly>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(TimeSpan) => await ReadAsync<TimeSpan>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ when type == typeof(Guid) => await ReadAsync<Guid>(rowGroup, field, rowCount, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"The type {type} is not supported."),
        };
    }

    /// <summary>
    /// Gets a value that indicates whether a row group may have geometries in a box: <see langword="false"/> only when
    /// the statistics of the bbox covering show that every geometry is outside of it.
    /// </summary>
    public static bool MayIntersect(ParquetRowGroupReader rowGroup, (DataField XMin, DataField YMin, DataField XMax, DataField YMax) covering, GeoBounds box)
    {
        return !(GetMin(covering.XMin) > box.East || GetMax(covering.XMax) < box.West || GetMin(covering.YMin) > box.North || GetMax(covering.YMax) < box.South);

        double GetMin(DataField field) => ToDouble(GetStatistics(field)?.MinValue) ?? double.NegativeInfinity;

        double GetMax(DataField field) => ToDouble(GetStatistics(field)?.MaxValue) ?? double.PositiveInfinity;

        Parquet.Data.DataColumnStatistics? GetStatistics(DataField field)
        {
            try
            {
                return rowGroup.GetStatistics(field);
            }
            catch (ParquetException)
            {
                return null;
            }
        }

        static double? ToDouble(object? value) => value switch
        {
            double number when !double.IsNaN(number) => number,
            float number when !float.IsNaN(number) => number,
            _ => null,
        };
    }

    private static async ValueTask<object?[]> ReadAsync<T>(ParquetRowGroupReader rowGroup, DataField field, int rowCount, CancellationToken cancellationToken)
        where T : struct
    {
        var result = new object?[rowCount];
        if (field.IsNullable)
        {
            var values = new T?[rowCount];
            await rowGroup.ReadAsync(field, values.AsMemory(), repetitionLevels: null, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < rowCount; i++)
            {
                result[i] = values[i];
            }
        }
        else
        {
            var values = new T[rowCount];
            await rowGroup.ReadAsync(field, values.AsMemory(), repetitionLevels: null, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < rowCount; i++)
            {
                result[i] = values[i];
            }
        }

        return result;
    }
}
