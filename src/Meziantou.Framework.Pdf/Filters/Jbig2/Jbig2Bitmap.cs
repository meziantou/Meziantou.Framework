using System.Buffers.Binary;

namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>A bitmap of JBIG2 (ITU-T T.88): one bit per pixel, most significant bit first, each row on whole bytes; 1 is black.</summary>
internal sealed class Jbig2Bitmap
{
    public Jbig2Bitmap(int width, int height, Jbig2Budget budget)
    {
        if (width < 0 || height < 0)
            throw new InvalidPdfContentException("A JBIG2 bitmap has a negative size");

        // In 64 bits: the size of a bitmap of int.MaxValue pixels per row does not fit in an int
        var stride = ((long)width + 7) / 8;
        var size = stride * height;
        budget.ChargeMemory(size);
        if (size > Array.MaxLength)
            throw new PdfResourceLimitException("A JBIG2 bitmap is larger than the maximum size of an array");

        Width = width;
        Height = height;
        Stride = (int)stride;
        Data = new byte[size];
    }

    public int Width { get; }

    public int Height { get; private set; }

    public int Stride { get; }

    public byte[] Data { get; private set; }

    /// <summary>Gets a pixel; the pixels outside the bitmap are 0 (6.2.5.2).</summary>
    public int GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return 0;

        return (Data[(y * Stride) + (x >> 3)] >> (7 - (x & 7))) & 1;
    }

    public void SetPixel(int x, int y, int value)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;

        var index = (y * Stride) + (x >> 3);
        var mask = (byte)(0x80 >> (x & 7));
        if (value != 0)
        {
            Data[index] |= mask;
        }
        else
        {
            Data[index] &= (byte)~mask;
        }
    }

    public void Fill(int value) => Data.AsSpan().Fill(value != 0 ? (byte)0xFF : (byte)0);

    public void CopyRow(int source, int destination) => Data.AsSpan(source * Stride, Stride).CopyTo(Data.AsSpan(destination * Stride, Stride));

    public Span<byte> GetRow(int y) => Data.AsSpan(y * Stride, Stride);

    /// <summary>Gets a part of the bitmap; the pixels outside it are 0.</summary>
    /// <remarks>The part is the bitmap placed with REPLACE in a bitmap of 0 (<see cref="Combine"/>).</remarks>
    public Jbig2Bitmap Extract(int x, int y, int width, int height, Jbig2Budget budget)
    {
        var result = new Jbig2Bitmap(width, height, budget);
        result.Combine(this, (int)Math.Clamp(-(long)x, int.MinValue, int.MaxValue), (int)Math.Clamp(-(long)y, int.MinValue, int.MaxValue), combinationOperator: 4);
        return result;
    }

    /// <summary>Grows the height of a page whose height was unknown (7.4.8.2), keeping its rows.</summary>
    public void GrowHeight(int height, int defaultPixel, Jbig2Budget budget)
    {
        if (height <= Height)
            return;

        var size = (long)Stride * height;
        budget.ChargeMemory((long)Stride * (height - Height));
        if (size > Array.MaxLength)
            throw new PdfResourceLimitException("A JBIG2 bitmap is larger than the maximum size of an array");

        var data = new byte[size];
        Data.CopyTo(data, 0);
        if (defaultPixel != 0)
        {
            data.AsSpan(Data.Length).Fill(0xFF);
        }

        Data = data;
        Height = height;
    }

    /// <summary>Combines a bitmap into this one at a position with a combination operator (6.4.5, 7.4.1.5): OR, AND, XOR, XNOR or REPLACE.</summary>
    /// <remarks>
    /// The rows are combined eight bytes of this bitmap at a time: the pixels of the source under them are read at the bit
    /// offset of the position, and masks keep the pixels outside the source in the first and the last byte. The bits
    /// after the last pixel of a row are never read from the source, nor changed in this bitmap.
    /// </remarks>
    public void Combine(Jbig2Bitmap source, int x, int y, int combinationOperator)
    {
        switch (combinationOperator)
        {
            case 0:
                Combine<Or>(source, x, y);
                break;
            case 1:
                Combine<And>(source, x, y);
                break;
            case 2:
                Combine<Xor>(source, x, y);
                break;
            case 3:
                Combine<Xnor>(source, x, y);
                break;
            default:
                Combine<Replace>(source, x, y);
                break;
        }
    }

    private void Combine<TOperator>(Jbig2Bitmap source, int x, int y)
        where TOperator : struct, ICombinationOperator
    {
        var startRow = Math.Max(0, -(long)y);
        var endRow = Math.Min(source.Height, Height - (long)y);
        var startColumn = Math.Max(0, -(long)x);
        var endColumn = Math.Min(source.Width, Width - (long)x);
        if (startRow >= endRow || startColumn >= endColumn)
            return;

        // The pixels of this bitmap that the source covers, and the bytes that hold them
        var first = (int)(x + startColumn);
        var last = (int)(x + endColumn - 1);
        var firstByte = first >> 3;
        var lastByte = last >> 3;
        var firstMask = (byte)(0xFF >> (first & 7));
        var lastMask = (byte)(0xFF << (7 - (last & 7)));
        if (firstByte == lastByte)
        {
            firstMask &= lastMask;
        }

        // The byte i of a row of this bitmap is the eight pixels of the source from the column 8 * i - x: the byte
        // i + offset of the source shifted left by shift bits, with the high bits of the next byte
        var offset = (int)((-(long)x) >> 3);
        var shift = (int)(-(long)x & 7);
        for (var row = (int)startRow; row < endRow; row++)
        {
            ReadOnlySpan<byte> sourceRow = source.GetRow(row);
            var targetRow = GetRow(y + row);
            CombineByte<TOperator>(sourceRow, targetRow, firstByte, x, firstMask);
            if (firstByte == lastByte)
                continue;

            // The bytes between the first and the last one are whole: eight at a time while the source has the bytes
            var i = firstByte + 1;
            var sourceEnd = sourceRow.Length - (shift == 0 ? 8 : 9);
            for (; i + 8 <= lastByte && i + offset <= sourceEnd; i += 8)
            {
                var index = i + offset;
                var value = BinaryPrimitives.ReadUInt64BigEndian(sourceRow[index..]);
                if (shift != 0)
                {
                    value = (value << shift) | ((ulong)sourceRow[index + 8] >> (8 - shift));
                }

                var target = targetRow[i..];
                BinaryPrimitives.WriteUInt64BigEndian(target, TOperator.Apply(BinaryPrimitives.ReadUInt64BigEndian(target), value));
            }

            for (; i < lastByte; i++)
            {
                CombineByte<TOperator>(sourceRow, targetRow, i, x, 0xFF);
            }

            CombineByte<TOperator>(sourceRow, targetRow, lastByte, x, lastMask);
        }
    }

    private static void CombineByte<TOperator>(ReadOnlySpan<byte> sourceRow, Span<byte> targetRow, int index, int x, byte mask)
        where TOperator : struct, ICombinationOperator
    {
        var existing = targetRow[index];
        var combined = (byte)TOperator.Apply(existing, ReadByte(sourceRow, (index * 8L) - x));
        targetRow[index] = (byte)((existing & ~mask) | (combined & mask));
    }

    /// <summary>Reads eight pixels of a row from a column, which may be negative or past the row; the pixels outside the bytes of the row are 0.</summary>
    private static byte ReadByte(ReadOnlySpan<byte> row, long column)
    {
        var index = column >> 3;
        var shift = (int)(column & 7);
        var high = (ulong)index < (ulong)row.Length ? row[(int)index] : 0;
        if (shift == 0)
            return (byte)high;

        var low = (ulong)(index + 1) < (ulong)row.Length ? row[(int)(index + 1)] : 0;
        return (byte)((high << shift) | (low >> (8 - shift)));
    }

    /// <summary>A combination operator, applied to the bits of this bitmap and of the source.</summary>
    private interface ICombinationOperator
    {
        static abstract ulong Apply(ulong existing, ulong value);
    }

    private readonly struct Or : ICombinationOperator
    {
        public static ulong Apply(ulong existing, ulong value) => existing | value;
    }

    private readonly struct And : ICombinationOperator
    {
        public static ulong Apply(ulong existing, ulong value) => existing & value;
    }

    private readonly struct Xor : ICombinationOperator
    {
        public static ulong Apply(ulong existing, ulong value) => existing ^ value;
    }

    private readonly struct Xnor : ICombinationOperator
    {
        public static ulong Apply(ulong existing, ulong value) => ~(existing ^ value);
    }

    private readonly struct Replace : ICombinationOperator
    {
        public static ulong Apply(ulong existing, ulong value) => value;
    }
}
