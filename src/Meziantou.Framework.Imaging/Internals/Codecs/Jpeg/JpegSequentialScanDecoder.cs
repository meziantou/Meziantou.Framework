using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the blocks of one sequential Huffman scan (ITU-T T.81 section F.2): DC prediction, AC run-lengths,
/// dequantization and the inverse DCT; the samples are written into the component planes. When the frame is reconstructed
/// by several workers (<see cref="JpegReconstruction"/>), the quantized coefficients of each block are stored in the
/// coefficient ring of its component instead, and transformed once their MCU row is decoded.
/// </summary>
/// <remarks>
/// Validation (all <see cref="InvalidImageContentException"/>), besides the scan structure of <see cref="JpegScanDecoder"/>:
/// invalid Huffman codes, DC categories above 11 and AC magnitudes above 10 (15 and 14 for 12-bit samples), coefficient
/// runs past the 63rd AC coefficient and DC values outside 16 bits.
/// </remarks>
internal sealed class JpegSequentialScanDecoder : JpegScanDecoder
{
    private readonly double[] _block = new double[64];
    private readonly bool _deferTransforms;

    /// <param name="components">The scan components with their tables, in scan order.</param>
    /// <param name="reader">The entropy reader (reset by this scan).</param>
    /// <param name="mcusPerLine">For interleaved scans, the MCUs per MCU row of the frame.</param>
    /// <param name="mcuRows">For interleaved scans, the MCU rows of the frame.</param>
    /// <param name="restartInterval">The restart interval in MCUs, or 0.</param>
    /// <param name="mcuRowDecoded">Called after each completed MCU row (block row for a non-interleaved scan) with its index.</param>
    /// <param name="cancellationToken">Checked after each MCU row.</param>
    /// <param name="deferTransforms">Whether the quantized coefficients are stored in the coefficient rings of the components instead of being transformed.</param>
    public JpegSequentialScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, Action<int>? mcuRowDecoded, CancellationToken cancellationToken, bool deferTransforms = false)
        : base(components, reader, mcusPerLine, mcuRows, restartInterval, mcuRowDecoded, cancellationToken)
    {
        _deferTransforms = deferTransforms;
    }

    protected override void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        if (_deferTransforms)
        {
            DecodeCoefficients(scanComponent, blockRow, blockColumn);
            return;
        }

        // The destination is computed first: one value instead of two to keep alive through the loop
        var offset = scanComponent.Component.GetBlockOffset(blockRow, blockColumn);
        var reader = Reader.GetCursor();

        // DC: difference from the prediction (T.81 F.2.2.1)
        var category = reader.DecodeHuffman(scanComponent.DcTable!);
        if (category > MaxDcCategory)
            throw CreateDcCategoryException();

        var dc = scanComponent.Predictor + (category == 0 ? 0 : reader.ReceiveExtend(category));
        if (dc is < short.MinValue or > short.MaxValue)
            throw Invalid("The JPEG DC coefficient is out of range.");

        scanComponent.Predictor = dc;

        // AC: run-length/size symbols in zig-zag order (T.81 F.2.2.2). Each entry of the table gives the natural index of
        // a zig-zag position and its quantization value
        ref var block = ref unsafe(MemoryMarshal.GetArrayDataReference(_block));
        var zigZagQuantization = scanComponent.ZigZagQuantization;
        var acTable = scanComponent.AcTable!;
        var fastAc = acTable.FastAc;

        // The natural indexes of the non-zero AC coefficients ORed together, with bit 6 set when there is one
        var naturalIndexes = 0;
        for (var k = 1; k < 64;)
        {
            // Short codes with small magnitudes: symbol and magnitude in one lookup
            var fast = reader.TryDecodeFastAc(fastAc);
            if (fast != 0)
            {
                k += (fast >> 4) & 0x0F;
                if (k > 63)
                    throw CreateRunException();

                // The natural index is at most 63 and the block has 64 coefficients
                var fastEntry = zigZagQuantization[k];
                var fastNatural = fastEntry & 63;
                unsafe { Unsafe.Add(ref block, fastNatural) = (fast >> 8) * (double)(fastEntry >> 6); }
                naturalIndexes |= fastNatural | 64;
                k++;
                continue;
            }

            var symbol = reader.DecodeHuffman(acTable);
            var run = symbol >> 4;
            var size = symbol & 0x0F;
            if (size == 0)
            {
                if (run != 15)
                    break; // EOB

                k += 16; // ZRL
                continue;
            }

            k += run;
            if (k > 63)
                throw CreateRunException();

            if (size > MaxAcCategory)
                throw CreateAcCategoryException();

            var entry = zigZagQuantization[k];
            var natural = entry & 63;
            unsafe { Unsafe.Add(ref block, natural) = reader.ReceiveExtend(size) * (double)(entry >> 6); }
            naturalIndexes |= natural | 64;
            k++;
        }

        reader.Commit();

        // The DC coefficient is read back from the predictor: one value less to keep alive in the loop
        TransformBlock(scanComponent, offset, _block, scanComponent.Predictor, naturalIndexes != 0, JpegIdct.IsSparse(naturalIndexes));
    }

    /// <summary>
    /// Decodes the next block like <see cref="DecodeBlock"/>, but stores its quantized coefficients (natural order) in the
    /// coefficient ring of the component: the transform of <see cref="JpegReconstruction"/> gives the samples of
    /// <see cref="JpegScanDecoder.TransformBlock(ScanComponent, int, double[], int, bool, bool)"/>.
    /// </summary>
    private void DecodeCoefficients(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        var component = scanComponent.Component;
        var coefficients = component.GetCoefficients(blockRow % component.CoefficientBlockRows, blockColumn);
        coefficients.Clear();
        ref var block = ref unsafe(MemoryMarshal.GetReference(coefficients));
        var reader = Reader.GetCursor();
        var category = reader.DecodeHuffman(scanComponent.DcTable!);
        if (category > MaxDcCategory)
            throw CreateDcCategoryException();

        var dc = scanComponent.Predictor + (category == 0 ? 0 : reader.ReceiveExtend(category));
        if (dc is < short.MinValue or > short.MaxValue)
            throw Invalid("The JPEG DC coefficient is out of range.");

        scanComponent.Predictor = dc;
        block = (short)dc;
        var zigZagQuantization = scanComponent.ZigZagQuantization;
        var acTable = scanComponent.AcTable!;
        var fastAc = acTable.FastAc;
        for (var k = 1; k < 64;)
        {
            var fast = reader.TryDecodeFastAc(fastAc);
            if (fast != 0)
            {
                k += (fast >> 4) & 0x0F;
                if (k > 63)
                    throw CreateRunException();

                // The natural index is at most 63 and the block has 64 coefficients; a magnitude of at most 14 bits fits
                unsafe { Unsafe.Add(ref block, zigZagQuantization[k] & 63) = (short)(fast >> 8); }
                k++;
                continue;
            }

            var symbol = reader.DecodeHuffman(acTable);
            var run = symbol >> 4;
            var size = symbol & 0x0F;
            if (size == 0)
            {
                if (run != 15)
                    break; // EOB

                k += 16; // ZRL
                continue;
            }

            k += run;
            if (k > 63)
                throw CreateRunException();

            if (size > MaxAcCategory)
                throw CreateAcCategoryException();

            unsafe { Unsafe.Add(ref block, zigZagQuantization[k] & 63) = (short)reader.ReceiveExtend(size); }
            k++;
        }

        reader.Commit();
    }

    // The exceptions are created out of line, which keeps their message formatting out of the frame of DecodeBlock
    private InvalidImageContentException CreateDcCategoryException() => Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG DC coefficient category is greater than {MaxDcCategory}."));

    private InvalidImageContentException CreateAcCategoryException() => Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG AC coefficient magnitude category is greater than {MaxAcCategory}."));

    private static InvalidImageContentException CreateRunException() => Invalid("The JPEG AC coefficients run past the end of the block.");
}
