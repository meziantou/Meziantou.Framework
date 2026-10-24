namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>A bitmap of JBIG2 (ITU-T T.88): one bit per pixel, most significant bit first, each row on whole bytes; 1 is black.</summary>
internal sealed class Jbig2Bitmap
{
    public Jbig2Bitmap(int width, int height, Jbig2Budget budget)
    {
        if (width < 0 || height < 0)
            throw new InvalidPdfContentException("A JBIG2 bitmap has a negative size");

        Width = width;
        Height = height;
        Stride = (width + 7) / 8;
        budget.ChargeMemory((long)Stride * height);
        Data = new byte[Stride * height];
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
    public Jbig2Bitmap Extract(int x, int y, int width, int height, Jbig2Budget budget)
    {
        var result = new Jbig2Bitmap(width, height, budget);
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (GetPixel(x + column, y + row) != 0)
                {
                    result.SetPixel(column, row, 1);
                }
            }
        }

        return result;
    }

    /// <summary>Grows the height of a page whose height was unknown (7.4.8.2), keeping its rows.</summary>
    public void GrowHeight(int height, int defaultPixel, Jbig2Budget budget)
    {
        if (height <= Height)
            return;

        budget.ChargeMemory((long)Stride * (height - Height));
        var data = new byte[Stride * height];
        Data.CopyTo(data, 0);
        if (defaultPixel != 0)
        {
            data.AsSpan(Data.Length).Fill(0xFF);
        }

        Data = data;
        Height = height;
    }

    /// <summary>Combines a bitmap into this one at a position with a combination operator (6.4.5, 7.4.1.5): OR, AND, XOR, XNOR or REPLACE.</summary>
    public void Combine(Jbig2Bitmap source, int x, int y, int combinationOperator)
    {
        var startRow = Math.Max(0, -y);
        var endRow = Math.Min(source.Height, Height - y);
        var startColumn = Math.Max(0, -x);
        var endColumn = Math.Min(source.Width, Width - x);
        if (startRow >= endRow || startColumn >= endColumn)
            return;

        for (var row = startRow; row < endRow; row++)
        {
            for (var column = startColumn; column < endColumn; column++)
            {
                var value = source.GetPixel(column, row);
                var target = x + column;
                var targetRow = y + row;
                var existing = GetPixel(target, targetRow);
                var result = combinationOperator switch
                {
                    0 => existing | value,
                    1 => existing & value,
                    2 => existing ^ value,
                    3 => (existing ^ value) ^ 1,
                    _ => value,
                };
                SetPixel(target, targetRow, result);
            }
        }
    }
}
