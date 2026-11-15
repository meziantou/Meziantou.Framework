using System.Buffers.Binary;
using System.IO.Compression;

#pragma warning disable CA1819 // Properties should not return arrays

namespace Meziantou.Framework.Imaging.TestHarness.Dng;

/// <summary>
/// An independent renderer of DNG files, written from the DNG specification 1.7.1 for the tests, sharing no code with the
/// library: a reader of the TIFF directories and of the uncompressed or Deflate-compressed samples, the mapping of
/// chapter 5, the opcodes the library implements, the bilinear demosaicing and the color transform of chapter 6 to linear
/// BT.709 (D65), with the choices the library documents (DngPipeline, DngOpcodeList and DngColorProfile of the library).
/// </summary>
/// <remarks>
/// Each stage is held in single precision and computed in double precision, in the order of the documented definitions,
/// so that a correct decoder gives the same values. The samples of other compressions (lossless JPEG, JPEG XL) are given
/// by the caller (stage 1 from the DNG SDK, or the samples the file was made from).
/// </remarks>
public static class ReferenceDng
{
    private const double D50x = 0.3457;
    private const double D50y = 0.3585;

    /// <summary>Renders a DNG file.</summary>
    /// <param name="data">The file.</param>
    /// <param name="storedSamples">
    /// The samples of the raw image in the order of the image (stage 1 before opcode list 1, the fields of an interleaved
    /// file put back in order), or <see langword="null"/> to decode them from the file.
    /// </param>
    public static DngRendering Render(byte[] data, double[]? storedSamples = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var file = new TiffFile(data);
        var ifd0 = file.ReadDirectory(file.FirstDirectory);
        var raw = FindRaw(file, ifd0) ?? throw new InvalidDataException("No raw IFD.");
        var width = (int)raw.Number(256);
        var height = (int)raw.Number(257);
        var spp = (int)(raw.TryNumber(277) ?? 1);
        var bits = (int)raw.Numbers(258)[0];
        var isFloat = raw.TryNumber(339) == 3;
        var isCfa = raw.Number(262) == 32803;
        var stage1 = storedSamples ?? DecodeSamples(file, raw, width, height, spp, bits, isFloat);
        if (stage1.Length != width * height * spp)
            throw new InvalidDataException("The number of stored samples does not match the raw image.");

        // Stage 1 values: integers divided by 65,535 (list 1), or the floats
        var s1 = new float[stage1.Length];
        for (var i = 0; i < s1.Length; i++)
        {
            s1[i] = isFloat ? (float)stage1[i] : (float)(stage1[i] / 65535.0);
        }

        if (storedSamples is null)
        {
            Deinterleave(raw, s1, width, height, spp);
        }

        ApplyOpcodes(raw.TryBytes(51008), s1, width, height, spp, isFloat ? Values.StoredFloats : Values.StoredIntegers);

        // Stage 2
        (int Top, int Left, int Bottom, int Right) area = raw.TryNumbers(50829) is { } a ? ((int)a[0], (int)a[1], (int)a[2], (int)a[3]) : (0, 0, height, width);
        var aw = area.Right - area.Left;
        var ah = area.Bottom - area.Top;
        var table = raw.TryNumbers(50712);
        var repeat = raw.TryNumbers(50713) ?? [1, 1];
        var (rr, rc) = ((int)repeat[0], (int)repeat[1]);
        var levels = raw.TryNumbers(50714) ?? new double[rr * rc * spp];
        var deltaH = raw.TryNumbers(50715);
        var deltaV = raw.TryNumbers(50716);
        var maxStored = isFloat ? 1.0 : (1L << bits) - 1;
        if (table is not null)
        {
            maxStored = table.Max();
        }

        var white = raw.TryNumbers(50717) ?? [.. Enumerable.Repeat(maxStored, spp)];
        var s2 = new float[aw * ah * spp];
        for (var y = 0; y < ah; y++)
        {
            for (var x = 0; x < aw; x++)
            {
                for (var s = 0; s < spp; s++)
                {
                    double value = s1[((((area.Top + y) * width) + area.Left + x) * spp) + s];
                    if (!isFloat)
                    {
                        var stored = (int)Math.Round(value * 65535.0, MidpointRounding.AwayFromZero);
                        value = table is null ? stored : table[Math.Min(stored, table.Length - 1)];
                    }

                    var black = levels[((((y % rr) * rc) + (x % rc)) * spp) + s] + (deltaH?[x] ?? 0) + (deltaV?[y] ?? 0);
                    var maxBlack = double.NegativeInfinity;
                    for (var i = s; i < levels.Length; i += spp)
                    {
                        maxBlack = Math.Max(maxBlack, levels[i]);
                    }

                    maxBlack = maxBlack + (deltaH is { Length: > 0 } ? deltaH.Max() : 0) + (deltaV is { Length: > 0 } ? deltaV.Max() : 0);
                    var scale = 1 / (white[s] - maxBlack);
                    s2[(((y * aw) + x) * spp) + s] = (float)Math.Clamp((value - black) * scale, 0, 1);
                }
            }
        }

        ApplyOpcodes(raw.TryBytes(51009), s2, aw, ah, spp, Values.Linear);

        // Stage 3
        int planes;
        float[] s3;
        if (isCfa)
        {
            var dims = raw.Numbers(33421);
            var (pr, pc) = ((int)dims[0], (int)dims[1]);
            var patternColors = raw.Bytes(33422);
            var planeColors = raw.TryBytes(50710) ?? [0, 1, 2];
            var pattern = patternColors.Select(color => Array.IndexOf(planeColors, color)).ToArray();
            planes = planeColors.Length;
            s3 = Demosaic(s2, aw, ah, pattern, pr, pc, planes);
        }
        else
        {
            planes = spp;
            s3 = s2;
        }

        ApplyOpcodes(raw.TryBytes(51022), s3, aw, ah, planes, Values.Linear);

        // Crop and color
        var origin = raw.TryNumbers(50719) ?? [0, 0];
        var size = raw.TryNumbers(50720) ?? [aw - Math.Round(origin[0], MidpointRounding.AwayFromZero), ah - Math.Round(origin[1], MidpointRounding.AwayFromZero)];
        var cropX = (int)Math.Round(origin[0], MidpointRounding.AwayFromZero);
        var cropY = (int)Math.Round(origin[1], MidpointRounding.AwayFromZero);
        var cropW = (int)Math.Round(size[0], MidpointRounding.AwayFromZero);
        var cropH = (int)Math.Round(size[1], MidpointRounding.AwayFromZero);
        var matrix = GetCameraToOutput(ifd0, planes);
        var rgba = new float[cropW * cropH * 4];
        for (var y = 0; y < cropH; y++)
        {
            for (var x = 0; x < cropW; x++)
            {
                var pixel = ((((cropY + y) * aw) + cropX + x) * planes);
                for (var c = 0; c < 3; c++)
                {
                    var sum = 0.0;
                    for (var p = 0; p < planes; p++)
                    {
                        sum += matrix[(c * planes) + p] * s3[pixel + p];
                    }

                    rgba[(((y * cropW) + x) * 4) + c] = (float)sum;
                }

                rgba[(((y * cropW) + x) * 4) + 3] = 1;
            }
        }

        return new DngRendering(cropW, cropH, rgba, aw, ah, spp, s2);
    }

    /// <summary>Gets the 3 x planes matrix (row-major) from the camera color space to linear BT.709 RGB (D65), with the baseline exposure.</summary>
    public static double[] GetCameraToOutput(TiffFile.Directory ifd0, int planes)
    {
        ArgumentNullException.ThrowIfNull(ifd0);
        var exposure = (ifd0.TryNumber(50730) ?? 0) + (ifd0.TryNumber(51109) ?? 0);
        var gain = Math.Pow(2, exposure);
        var cm1 = ifd0.TryNumbers(50721);
        var result = new double[3 * planes];
        if (cm1 is null)
        {
            for (var c = 0; c < 3; c++)
            {
                result[c * planes] = gain;
            }

            return result;
        }

        var cm2 = ifd0.TryNumber(50779) is not null ? ifd0.TryNumbers(50722) : null;
        var fm1 = ifd0.TryNumbers(50964);
        var fm2 = ifd0.TryNumbers(50965);
        var cc1 = ifd0.TryNumbers(50723);
        var cc2 = ifd0.TryNumbers(50724);
        var useForward = fm1 is not null && (cm2 is null || fm2 is not null);
        var signaturesMatch = string.Equals(ifd0.TryString(50931), ifd0.TryString(50932), StringComparison.Ordinal);
        var t1 = Temperature((int)(ifd0.TryNumber(50778) ?? 0));
        var t2 = cm2 is null ? null : Temperature((int)ifd0.Number(50779));
        var ab = ifd0.TryNumbers(50727) ?? [.. Enumerable.Repeat(1.0, planes)];
        var abMatrix = Matrix.Diagonal(ab);

        double Weight((double X, double Y) white)
        {
            if (cm2 is null || t1 is not { } a || t2 is not { } b || a == b)
                return 1;

            var t = Cct(white.X, white.Y);
            var (low, high) = a < b ? (a, b) : (b, a);
            var weightOfLow = t <= low ? 1 : t >= high ? 0 : ((1 / t) - (1 / high)) / ((1 / low) - (1 / high));
            return a < b ? weightOfLow : 1 - weightOfLow;
        }

        Matrix Pick(double[] one, double[]? two, int rows, int columns, double w)
        {
            var m1 = new Matrix(rows, columns, one);
            if (two is null || w >= 1)
                return m1;

            var m2 = new Matrix(rows, columns, two);
            if (w <= 0)
                return m2;

            var r = new Matrix(rows, columns);
            for (var i = 0; i < rows; i++)
            {
                for (var j = 0; j < columns; j++)
                {
                    r[i, j] = (w * m1[i, j]) + ((1 - w) * m2[i, j]);
                }
            }

            return r;
        }

        Matrix Calibration(double w) => signaturesMatch ? Pick(cc1 ?? Matrix.Identity(planes).Values, cc1 is null ? null : cc2, planes, planes, cc2 is null ? 1 : w) : Matrix.Identity(planes);

        Matrix XyzToCamera(double w) => abMatrix.Multiply(Calibration(w)).Multiply(Pick(cm1, cm2, planes, 3, w));

        double[] neutral;
        (double X, double Y) whiteXy;
        if (ifd0.TryNumbers(50728) is { } asShot)
        {
            var max = asShot.Max();
            neutral = [.. asShot.Select(value => value / max)];
            whiteXy = (D50x, D50y);
            for (var i = 0; i < 30; i++)
            {
                var xyz = XyzToCamera(Weight(whiteXy)).PseudoInverse().Multiply(Matrix.Column(neutral));
                var sum = xyz[0, 0] + xyz[1, 0] + xyz[2, 0];
                var next = (xyz[0, 0] / sum, xyz[1, 0] / sum);
                var converged = Math.Abs(next.Item1 - whiteXy.X) + Math.Abs(next.Item2 - whiteXy.Y) < 1e-12;
                whiteXy = next;
                if (converged)
                    break;
            }
        }
        else
        {
            whiteXy = ifd0.TryNumbers(50729) is { } xy ? (xy[0], xy[1]) : (D50x, D50y);
            var n = XyzToCamera(Weight(whiteXy)).Multiply(Matrix.Column(Xyz(whiteXy.X, whiteXy.Y)));
            var max = Enumerable.Range(0, planes).Max(i => n[i, 0]);
            neutral = [.. Enumerable.Range(0, planes).Select(i => n[i, 0] / max)];
        }

        var weight = Weight(whiteXy);
        Matrix cameraToXyz;
        if (useForward)
        {
            var inverse = abMatrix.Multiply(Calibration(weight)).Inverse();
            var reference = inverse.Multiply(Matrix.Column(neutral));
            var d = Matrix.Diagonal([.. Enumerable.Range(0, planes).Select(i => 1 / reference[i, 0])]);
            cameraToXyz = Pick(fm1!, cm2 is null ? null : fm2, 3, planes, weight).Multiply(d).Multiply(inverse);
        }
        else
        {
            var m = Bradford(Xyz(whiteXy.X, whiteXy.Y), Xyz(D50x, D50y)).Multiply(XyzToCamera(weight).PseudoInverse());
            var y = m.Multiply(Matrix.Column(neutral))[1, 0];
            cameraToXyz = m.Scale(1 / y);
        }

        var d65 = Xyz(0.3127, 0.3290);
        var primaries = new Matrix(3, 3, [0.64 / 0.33, 0.30 / 0.60, 0.15 / 0.06, 1, 1, 1, (1 - 0.64 - 0.33) / 0.33, (1 - 0.30 - 0.60) / 0.60, (1 - 0.15 - 0.06) / 0.06]);
        var s = primaries.Inverse().Multiply(Matrix.Column(d65));
        var rgbToXyz = primaries.Multiply(Matrix.Diagonal([s[0, 0], s[1, 0], s[2, 0]]));
        var output = rgbToXyz.Inverse().Multiply(Bradford(Xyz(D50x, D50y), d65)).Multiply(cameraToXyz).Scale(gain);
        for (var c = 0; c < 3; c++)
        {
            for (var p = 0; p < planes; p++)
            {
                result[(c * planes) + p] = output[c, p];
            }
        }

        return result;
    }

    private static TiffFile.Directory? FindRaw(TiffFile file, TiffFile.Directory directory)
    {
        if ((directory.TryNumber(254) ?? 0) == 0)
            return directory;

        foreach (var offset in directory.TryNumbers(330) ?? [])
        {
            if (FindRaw(file, file.ReadDirectory((uint)offset)) is { } raw)
                return raw;
        }

        return null;
    }

    private static double[] DecodeSamples(TiffFile file, TiffFile.Directory raw, int width, int height, int spp, int bits, bool isFloat)
    {
        var compression = (int)(raw.TryNumber(259) ?? 1);
        if (compression is not (1 or 8))
            throw new NotSupportedException("The reference decodes uncompressed and Deflate samples; give the others.");

        var predictor = (int)(raw.TryNumber(317) ?? 1);
        var tiled = raw.TryNumbers(324) is not null;
        var (bw, bh) = tiled ? ((int)raw.Number(322), (int)raw.Number(323)) : (width, (int)(raw.TryNumber(278) ?? height));
        var offsets = raw.Numbers(tiled ? (ushort)324 : (ushort)273);
        var counts = raw.Numbers(tiled ? (ushort)325 : (ushort)279);
        var across = (width + bw - 1) / bw;
        var rowBytes = isFloat ? bw * spp * (bits / 8) : ((bw * spp * bits) + 7) / 8;
        var result = new double[width * height * spp];
        for (var block = 0; block < offsets.Length; block++)
        {
            var bx = block % across;
            var by = block / across;
            var rows = tiled ? bh : Math.Min(bh, height - (by * bh));
            var stored = file.Data.AsSpan((int)offsets[block], (int)counts[block]).ToArray();
            if (compression == 8)
            {
                using var input = new MemoryStream(stored);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                stored = output.ToArray();
            }

            for (var y = 0; y < rows; y++)
            {
                var row = stored.AsSpan(y * rowBytes, rowBytes).ToArray();
                UndoPredictor(row, predictor, spp, bits, file.IsBigEndian, bw);
                for (var x = 0; x < bw; x++)
                {
                    var ix = (bx * bw) + x;
                    var iy = (by * bh) + y;
                    if (ix >= width || iy >= height)
                        continue;

                    for (var s = 0; s < spp; s++)
                    {
                        result[(((iy * width) + ix) * spp) + s] = ReadSample(row, (x * spp) + s, bits, isFloat, file.IsBigEndian);
                    }
                }
            }
        }

        return result;
    }

    private static void UndoPredictor(byte[] row, int predictor, int spp, int bits, bool bigEndian, int width)
    {
        switch (predictor)
        {
            case 2 or 34892 or 34893:
                {
                    var distance = (predictor == 2 ? 1 : predictor == 34892 ? 2 : 4) * spp;
                    if (bits == 8)
                    {
                        for (var i = distance; i < row.Length; i++)
                        {
                            row[i] = (byte)(row[i] + row[i - distance]);
                        }
                    }
                    else
                    {
                        for (var i = distance; i < row.Length / 2; i++)
                        {
                            var value = (ushort)(Read16(row, i * 2, bigEndian) + Read16(row, (i - distance) * 2, bigEndian));
                            if (bigEndian)
                            {
                                BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(i * 2), value);
                            }
                            else
                            {
                                BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(i * 2), value);
                            }
                        }
                    }

                    break;
                }

            case 3 or 34894 or 34895:
                {
                    var distance = (predictor == 3 ? 1 : predictor == 34894 ? 2 : 4) * spp;
                    for (var i = distance; i < row.Length; i++)
                    {
                        row[i] = (byte)(row[i] + row[i - distance]);
                    }

                    var bytesPerSample = bits / 8;
                    var count = width * spp;
                    var planes = row.ToArray();
                    for (var i = 0; i < count; i++)
                    {
                        for (var b = 0; b < bytesPerSample; b++)
                        {
                            row[(i * bytesPerSample) + (bigEndian ? b : bytesPerSample - 1 - b)] = planes[(b * count) + i];
                        }
                    }

                    break;
                }
        }
    }

    private static int Read16(byte[] data, int offset, bool bigEndian) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));

    private static double ReadSample(byte[] row, int index, int bits, bool isFloat, bool bigEndian)
    {
        if (isFloat)
        {
            return bits == 16
                ? (double)BitConverter.UInt16BitsToHalf((ushort)Read16(row, index * 2, bigEndian))
                : bigEndian ? BinaryPrimitives.ReadSingleBigEndian(row.AsSpan(index * 4)) : BinaryPrimitives.ReadSingleLittleEndian(row.AsSpan(index * 4));
        }

        if (bits == 8)
            return row[index];

        if (bits == 16)
            return Read16(row, index * 2, bigEndian);

        var value = 0;
        for (var bit = 0; bit < bits; bit++)
        {
            var position = (index * bits) + bit;
            value = (value << 1) | ((row[position / 8] >> (7 - (position % 8))) & 1);
        }

        return value;
    }

    private static void Deinterleave(TiffFile.Directory raw, float[] samples, int width, int height, int spp)
    {
        var rowFields = (int)(raw.TryNumber(50975) ?? 1);
        var columnFields = (int)(raw.TryNumber(52547) ?? 1);
        if (rowFields == 1 && columnFields == 1)
            return;

        static int[] Order(int length, int fields)
        {
            var order = new List<int>();
            for (var f = 0; f < fields; f++)
            {
                for (var line = f; line < length; line += fields)
                {
                    order.Add(line);
                }
            }

            return [.. order];
        }

        var rows = Order(height, rowFields);
        var columns = Order(width, columnFields);
        var copy = samples.ToArray();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var s = 0; s < spp; s++)
                {
                    samples[(((rows[y] * width) + columns[x]) * spp) + s] = copy[(((y * width) + x) * spp) + s];
                }
            }
        }
    }

    private static float[] Demosaic(float[] cfa, int width, int height, int[] pattern, int rows, int columns, int planes)
    {
        var output = new float[width * height * planes];
        int Plane(int x, int y) => pattern[((((y % rows) + rows) % rows) * columns) + (((x % columns) + columns) % columns)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var own = Plane(x, y);
                for (var p = 0; p < planes; p++)
                {
                    if (p == own)
                    {
                        output[(((y * width) + x) * planes) + p] = cfa[(y * width) + x];
                        continue;
                    }

                    for (var radius = 1; ; radius++)
                    {
                        var sum = 0.0;
                        var count = 0;
                        for (var wy = Math.Max(0, y - radius); wy <= Math.Min(height - 1, y + radius); wy++)
                        {
                            for (var wx = Math.Max(0, x - radius); wx <= Math.Min(width - 1, x + radius); wx++)
                            {
                                if (Plane(wx, wy) == p)
                                {
                                    sum += cfa[(wy * width) + wx];
                                    count++;
                                }
                            }
                        }

                        if (count > 0)
                        {
                            output[(((y * width) + x) * planes) + p] = (float)(sum / count);
                            break;
                        }
                    }
                }
            }
        }

        return output;
    }

    private enum Values
    {
        StoredIntegers,
        StoredFloats,
        Linear,
    }

    private static void ApplyOpcodes(byte[]? list, float[] image, int width, int height, int planes, Values values)
    {
        if (list is null)
            return;

        var count = BinaryPrimitives.ReadUInt32BigEndian(list);
        var position = 4;
        for (var i = 0; i < count; i++)
        {
            var id = BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(position));
            var flags = BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(position + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(position + 12));
            var p = new Params(list.AsSpan(position + 16, length).ToArray());
            position += 16 + length;
            if (!ApplyOpcode(id, p, image, width, height, planes, values))
            {
                if ((flags & 1) == 0)
                    throw new NotSupportedException("A required opcode the reference does not implement.");

                continue;
            }

            if (values == Values.StoredFloats)
                continue;

            for (var j = 0; j < image.Length; j++)
            {
                var value = Math.Clamp(image[j], 0f, 1f);
                image[j] = values == Values.StoredIntegers ? (float)(Math.Round(value * 65535.0, MidpointRounding.AwayFromZero) / 65535.0) : value;
            }
        }
    }

    private static bool ApplyOpcode(uint id, Params p, float[] image, int width, int height, int planes, Values values)
    {
        float Get(int x, int y, int plane) => image[(((y * width) + x) * planes) + plane];
        void Set(int x, int y, int plane, float value) => image[(((y * width) + x) * planes) + plane] = value;

        (double Cx, double Cy, double M) Center(double nx, double ny)
        {
            var cx = nx * width;
            var cy = ny * height;
            var mx = Math.Max(Math.Abs(cx), Math.Abs(width - cx));
            var my = Math.Max(Math.Abs(cy), Math.Abs(height - cy));
            var m = Math.Sqrt((mx * mx) + (my * my));
            return (cx, cy, m > 0 ? m : 1);
        }

        void ForArea()
        {
            var (top, left, bottom, right, plane, count, rowPitch, columnPitch) = (p.Int(), p.Int(), p.Int(), p.Int(), p.Int(), p.Int(), p.Int(), p.Int());
            p.Area = (top, left, rowPitch, columnPitch, plane);
            var mapper = p.ReadMapper();
            for (var y = Math.Max(top, 0); y < Math.Min(bottom, height); y++)
            {
                if ((y - top) % rowPitch != 0)
                    continue;

                for (var x = Math.Max(left, 0); x < Math.Min(right, width); x++)
                {
                    if ((x - left) % columnPitch != 0)
                        continue;

                    for (var pl = plane; pl < Math.Min(plane + count, planes); pl++)
                    {
                        Set(x, y, pl, (float)mapper(x, y, pl, Get(x, y, pl)));
                    }
                }
            }
        }

        switch (id)
        {
            case 1:
                {
                    var n = p.Int();
                    var sets = Enumerable.Range(0, n).Select(_ => new[] { p.Double(), p.Double(), p.Double(), p.Double(), p.Double(), p.Double() }).ToArray();
                    var (cx, cy, m) = Center(p.Double(), p.Double());
                    var source = image.ToArray();
                    double Sample(double sx, double sy, int plane)
                    {
                        sx = Math.Clamp(sx, 0, width - 1);
                        sy = Math.Clamp(sy, 0, height - 1);
                        var x0 = (int)Math.Floor(sx);
                        var y0 = (int)Math.Floor(sy);
                        var x1 = Math.Min(x0 + 1, width - 1);
                        var y1 = Math.Min(y0 + 1, height - 1);
                        var fx = sx - x0;
                        var fy = sy - y0;
                        double S(int x, int y) => source[(((y * width) + x) * planes) + plane];
                        var top = (S(x0, y0) * (1 - fx)) + (S(x1, y0) * fx);
                        var bottom = (S(x0, y1) * (1 - fx)) + (S(x1, y1) * fx);
                        return (top * (1 - fy)) + (bottom * fy);
                    }

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var dx = (x + 0.5 - cx) / m;
                            var dy = (y + 0.5 - cy) / m;
                            var r2 = (dx * dx) + (dy * dy);
                            for (var plane = 0; plane < planes; plane++)
                            {
                                var k = sets[n == 1 ? 0 : plane];
                                var f = k[0] + (k[1] * r2) + (k[2] * r2 * r2) + (k[3] * r2 * r2 * r2);
                                var tx = (k[4] * 2 * dx * dy) + (k[5] * (r2 + (2 * dx * dx)));
                                var ty = (k[5] * 2 * dx * dy) + (k[4] * (r2 + (2 * dy * dy)));
                                Set(x, y, plane, (float)Sample(cx + (m * ((f * dx) + tx)) - 0.5, cy + (m * ((f * dy) + ty)) - 0.5, plane));
                            }
                        }
                    }

                    return true;
                }

            case 3:
                {
                    double[] k = [p.Double(), p.Double(), p.Double(), p.Double(), p.Double()];
                    var (cx, cy, m) = Center(p.Double(), p.Double());
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var r2 = (((x + 0.5 - cx) * (x + 0.5 - cx)) + ((y + 0.5 - cy) * (y + 0.5 - cy))) / (m * m);
                            var gain = 1 + (r2 * (k[0] + (r2 * (k[1] + (r2 * (k[2] + (r2 * (k[3] + (r2 * k[4])))))))));
                            for (var plane = 0; plane < planes; plane++)
                            {
                                Set(x, y, plane, (float)(gain * Get(x, y, plane)));
                            }
                        }
                    }

                    return true;
                }

            case 4 or 5:
                {
                    var bad = new HashSet<(int X, int Y)>();
                    int phase;
                    if (id == 4)
                    {
                        var constant = p.UInt();
                        phase = p.Int();
                        var marked = values == Values.StoredFloats ? constant : (float)(constant / 65535.0);
                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                if (Get(x, y, 0) == marked)
                                {
                                    bad.Add((x, y));
                                }
                            }
                        }
                    }
                    else
                    {
                        phase = p.Int();
                        var points = p.Int();
                        var rectangles = p.Int();
                        for (var i = 0; i < points; i++)
                        {
                            var (row, column) = (p.Int(), p.Int());
                            if (row < height && column < width)
                            {
                                bad.Add((column, row));
                            }
                        }

                        for (var i = 0; i < rectangles; i++)
                        {
                            var (top, left, bottom, right) = (p.Int(), p.Int(), p.Int(), p.Int());
                            for (var y = top; y < Math.Min(bottom, height); y++)
                            {
                                for (var x = left; x < Math.Min(right, width); x++)
                                {
                                    bad.Add((x, y));
                                }
                            }
                        }
                    }

                    var repaired = new List<(int, int, float)>();
                    foreach (var (x, y) in bad.OrderBy(point => point.X).ThenBy(point => point.Y))
                    {
                        var sum = 0.0;
                        var count = 0;
                        var neighbors = new List<(int, int)> { (x - 2, y), (x + 2, y), (x, y - 2), (x, y + 2) };
                        if (((x + y) % 2 == 0) == (phase is 1 or 2))
                        {
                            neighbors.AddRange([(x - 1, y - 1), (x + 1, y - 1), (x - 1, y + 1), (x + 1, y + 1)]);
                        }

                        foreach (var (nx, ny) in neighbors)
                        {
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height || bad.Contains((nx, ny)))
                                continue;

                            sum += Get(nx, ny, 0);
                            count++;
                        }

                        if (count > 0)
                        {
                            repaired.Add((x, y, (float)(sum / count)));
                        }
                    }

                    foreach (var (x, y, value) in repaired)
                    {
                        Set(x, y, 0, value);
                    }

                    return true;
                }

            case 7 or 8 or 9 or 10 or 11 or 12 or 13:
                p.Kind = id;
                p.Size = (width, height);
                ForArea();
                return true;

            default:
                return false;
        }
    }

    private static double? Temperature(int lightSource) => lightSource switch
    {
        1 or 4 or 9 or 20 => 5503,
        2 or 14 => 4200,
        3 or 17 => 2856,
        10 or 21 => 6504,
        11 or 22 => 7504,
        12 => 6400,
        13 => 5000,
        15 => 3450,
        16 => 2925,
        18 => 4874,
        19 => 6774,
        23 => 5003,
        24 => 3200,
        _ => null,
    };

    // Robertson (1968), from Wyszecki and Stiles, table 1(3.11): reciprocal megakelvins, u, v, slope
    private static readonly double[][] Robertson =
    [
        [0, 0.18006, 0.26352, -0.24341], [10, 0.18066, 0.26589, -0.25479], [20, 0.18133, 0.26846, -0.26876], [30, 0.18208, 0.27119, -0.28539],
        [40, 0.18293, 0.27407, -0.30470], [50, 0.18388, 0.27709, -0.32675], [60, 0.18494, 0.28021, -0.35156], [70, 0.18611, 0.28342, -0.37915],
        [80, 0.18740, 0.28668, -0.40955], [90, 0.18880, 0.28997, -0.44278], [100, 0.19032, 0.29326, -0.47888], [125, 0.19462, 0.30141, -0.58204],
        [150, 0.19962, 0.30921, -0.70471], [175, 0.20525, 0.31647, -0.84901], [200, 0.21142, 0.32312, -1.0182], [225, 0.21807, 0.32909, -1.2168],
        [250, 0.22511, 0.33439, -1.4512], [275, 0.23247, 0.33904, -1.7298], [300, 0.24010, 0.34308, -2.0637], [325, 0.24792, 0.34655, -2.4681],
        [350, 0.25591, 0.34951, -2.9641], [375, 0.26400, 0.35200, -3.5814], [400, 0.27218, 0.35407, -4.3633], [425, 0.28039, 0.35577, -5.3762],
        [450, 0.28863, 0.35714, -6.7262], [475, 0.29685, 0.35823, -8.5955], [500, 0.30505, 0.35907, -11.324], [525, 0.31320, 0.35968, -15.628],
        [550, 0.32129, 0.36011, -23.325], [575, 0.32931, 0.36038, -40.770], [600, 0.33724, 0.36051, -116.45],
    ];

    /// <summary>Gets the correlated color temperature of a chromaticity (Robertson).</summary>
    public static double Cct(double x, double y)
    {
        var u = 4 * x / ((-2 * x) + (12 * y) + 3);
        var v = 6 * y / ((-2 * x) + (12 * y) + 3);
        var distances = Robertson.Select(line => ((v - line[2]) - (line[3] * (u - line[1]))) / Math.Sqrt(1 + (line[3] * line[3]))).ToArray();
        for (var i = 1; i < distances.Length; i++)
        {
            if (distances[i - 1] * distances[i] <= 0)
            {
                var f = distances[i - 1] == distances[i] ? 0 : distances[i - 1] / (distances[i - 1] - distances[i]);
                var mired = Robertson[i - 1][0] + (f * (Robertson[i][0] - Robertson[i - 1][0]));
                return mired <= 1 ? 1e6 : 1e6 / mired;
            }
        }

        return Math.Abs(distances[0]) < Math.Abs(distances[^1]) ? 1e6 : 1e6 / 600;
    }

    private static double[] Xyz(double x, double y) => [x / y, 1, (1 - x - y) / y];

    private static Matrix Bradford(double[] source, double[] destination)
    {
        var bradford = new Matrix(3, 3, [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296]);
        var s = bradford.Multiply(Matrix.Column(source));
        var d = bradford.Multiply(Matrix.Column(destination));
        return bradford.Inverse().Multiply(Matrix.Diagonal([d[0, 0] / s[0, 0], d[1, 0] / s[1, 0], d[2, 0] / s[2, 0]])).Multiply(bradford);
    }

    /// <summary>The parameters of an opcode, read big-endian, and the area operations.</summary>
    private sealed class Params(byte[] data)
    {
        private int _position;

        public uint Kind { get; set; }

        public (int Top, int Left, int RowPitch, int ColumnPitch, int Plane) Area { get; set; }

        public (int Width, int Height) Size { get; set; }

        public int Int() => (int)UInt();

        public uint UInt()
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(_position));
            _position += 4;
            return value;
        }

        public double Double()
        {
            var value = BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(_position));
            _position += 8;
            return value;
        }

        public float Float()
        {
            var value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(_position));
            _position += 4;
            return value;
        }

        public ushort Short()
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(_position));
            _position += 2;
            return value;
        }

        /// <summary>Reads the parameters after the area and gives the mapping of a sample.</summary>
        public Func<int, int, int, double, double> ReadMapper()
        {
            switch (Kind)
            {
                case 7:
                    {
                        var size = Int();
                        var table = Enumerable.Range(0, size).Select(_ => Short()).ToArray();
                        return (_, _, _, value) => table[(int)Math.Clamp(Math.Round(value * 65535.0, MidpointRounding.AwayFromZero), 0, table.Length - 1)] / 65535.0;
                    }

                case 8:
                    {
                        var degree = Int();
                        var coefficients = Enumerable.Range(0, degree + 1).Select(_ => Double()).ToArray();
                        return (_, _, _, value) =>
                        {
                            var result = 0.0;
                            for (var i = coefficients.Length - 1; i >= 0; i--)
                            {
                                result = (result * value) + coefficients[i];
                            }

                            return result;
                        };
                    }

                case 9:
                    {
                        var (pointsV, pointsH) = (Int(), Int());
                        var (spacingV, spacingH, originV, originH) = (Double(), Double(), Double(), Double());
                        var mapPlanes = Int();
                        var gains = Enumerable.Range(0, pointsV * pointsH * mapPlanes).Select(_ => Float()).ToArray();
                        var (width, height) = Size;
                        var firstPlane = Area.Plane;
                        return (x, y, plane, value) =>
                        {
                            var mapPlane = Math.Min(plane - firstPlane, mapPlanes - 1);
                            var row = Math.Clamp((((y + 0.5) / height) - originV) / spacingV, 0, pointsV - 1);
                            var column = Math.Clamp((((x + 0.5) / width) - originH) / spacingH, 0, pointsH - 1);
                            var row0 = (int)Math.Floor(row);
                            var column0 = (int)Math.Floor(column);
                            var row1 = Math.Min(row0 + 1, pointsV - 1);
                            var column1 = Math.Min(column0 + 1, pointsH - 1);
                            var fy = row - row0;
                            var fx = column - column0;
                            double G(int r, int c) => gains[(((r * pointsH) + c) * mapPlanes) + mapPlane];
                            var top = (G(row0, column0) * (1 - fx)) + (G(row0, column1) * fx);
                            var bottom = (G(row1, column0) * (1 - fx)) + (G(row1, column1) * fx);
                            return value * ((top * (1 - fy)) + (bottom * fy));
                        };
                    }

                default:
                    {
                        var count = Int();
                        var lines = Enumerable.Range(0, count).Select(_ => Float()).ToArray();
                        var isRow = Kind is 10 or 12;
                        var isScale = Kind is 12 or 13;
                        var (top, left, rowPitch, columnPitch, _) = Area;
                        return (x, y, _, value) =>
                        {
                            var line = isRow ? (y - top) / rowPitch : (x - left) / columnPitch;
                            return isScale ? value * lines[line] : value + lines[line];
                        };
                    }
            }
        }
    }

    /// <summary>A small dense matrix.</summary>
    private sealed class Matrix(int rows, int columns, double[]? values = null)
    {
        public double[] Values { get; } = values ?? new double[rows * columns];

        public int Rows { get; } = rows;

        public int Columns { get; } = columns;

        public double this[int i, int j]
        {
            get => Values[(i * Columns) + j];
            set => Values[(i * Columns) + j] = value;
        }

        public static Matrix Identity(int size) => Diagonal([.. Enumerable.Repeat(1.0, size)]);

        public static Matrix Diagonal(double[] values)
        {
            var m = new Matrix(values.Length, values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                m[i, i] = values[i];
            }

            return m;
        }

        public static Matrix Column(double[] values) => new(values.Length, 1, [.. values]);

        public Matrix Multiply(Matrix other)
        {
            var r = new Matrix(Rows, other.Columns);
            for (var i = 0; i < Rows; i++)
            {
                for (var j = 0; j < other.Columns; j++)
                {
                    var sum = 0.0;
                    for (var k = 0; k < Columns; k++)
                    {
                        sum += this[i, k] * other[k, j];
                    }

                    r[i, j] = sum;
                }
            }

            return r;
        }

        public Matrix Scale(double factor) => new(Rows, Columns, [.. Values.Select(value => value * factor)]);

        public Matrix Transpose()
        {
            var r = new Matrix(Columns, Rows);
            for (var i = 0; i < Rows; i++)
            {
                for (var j = 0; j < Columns; j++)
                {
                    r[j, i] = this[i, j];
                }
            }

            return r;
        }

        public Matrix PseudoInverse() => Rows == Columns ? Inverse() : Transpose().Multiply(this).Inverse().Multiply(Transpose());

        /// <summary>Inverts by Gauss-Jordan elimination with partial pivoting.</summary>
        public Matrix Inverse()
        {
            var n = Rows;
            var a = new double[n][];
            for (var i = 0; i < n; i++)
            {
                a[i] = new double[2 * n];
                for (var j = 0; j < n; j++)
                {
                    a[i][j] = this[i, j];
                }

                a[i][n + i] = 1;
            }

            for (var c = 0; c < n; c++)
            {
                var pivot = c;
                for (var r = c + 1; r < n; r++)
                {
                    if (Math.Abs(a[r][c]) > Math.Abs(a[pivot][c]))
                    {
                        pivot = r;
                    }
                }

                (a[pivot], a[c]) = (a[c], a[pivot]);
                var d = a[c][c];
                for (var j = 0; j < 2 * n; j++)
                {
                    a[c][j] /= d;
                }

                for (var r = 0; r < n; r++)
                {
                    if (r == c || a[r][c] == 0)
                        continue;

                    var f = a[r][c];
                    for (var j = 0; j < 2 * n; j++)
                    {
                        a[r][j] -= f * a[c][j];
                    }
                }
            }

            var result = new Matrix(n, n);
            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                {
                    result[i, j] = a[i][n + j];
                }
            }

            return result;
        }
    }
}
