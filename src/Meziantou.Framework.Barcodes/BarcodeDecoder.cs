using Meziantou.Framework.Barcodes.Internal.Linear;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Reads linear barcodes from their modules or from scan lines: Code 39, Code 93, Code 128, GS1-128, EAN-8, EAN-13,
/// UPC-A, UPC-E (with their add-ons), Codabar, ITF, and GS1 DataBar (Omnidirectional, Stacked, Stacked Omnidirectional,
/// Limited, Expanded and Expanded Stacked).
/// </summary>
/// <remarks>
/// The decoder works on the widths of the bars and spaces, not on pixels. To read the barcodes of an image, use the
/// <c>ReadBarcodes</c> methods of the <c>Meziantou.Framework.Imaging.Barcodes</c> package, which scan its pixels.
/// </remarks>
public static class BarcodeDecoder
{
    // The quiet zone added around the modules of a barcode
    private const int QuietZoneModules = 10;

    /// <summary>Reads a barcode.</summary>
    /// <param name="barcode">The barcode.</param>
    /// <returns>The barcode that was read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/BarCode128Tests.cs" ref:region="BarcodeDecoder_Decode" ref:dedent="true">
    /// var barcode = Barcode.CreateCode128("SKU-123456");
    ///
    /// var result = BarcodeDecoder.Decode(barcode);
    ///
    /// Assert.Equal((BarcodeType.Code128, "SKU-123456"), (result.Type, result.Text));
    /// </code>
    /// </example>
    public static BarcodeDecodeResult Decode(Barcode barcode)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        return Decode(barcode.Modules);
    }

    /// <summary>Reads a barcode from its modules.</summary>
    /// <param name="modules">
    /// The modules, <see langword="true"/> for dark, indexed by row then column, without quiet zone: one row for a linear
    /// barcode, the rows of a stacked GS1 DataBar symbol (separators included) for a stacked one.
    /// </param>
    /// <returns>The barcode that was read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The modules are not a barcode that the decoder can read.</exception>
    public static BarcodeDecodeResult Decode(bool[,] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (TryDecode(modules, out var result))
            return result;

        throw new FormatException("The modules are not a barcode that the decoder can read: no symbol has valid characters, check characters and quiet zones.");
    }

    /// <summary>Tries to read a barcode from its modules.</summary>
    /// <param name="modules">The modules, <see langword="true"/> for dark, indexed by row then column, without quiet zone.</param>
    /// <param name="result">When this method returns <see langword="true"/>, the barcode that was read.</param>
    /// <returns><see langword="true"/> if a barcode could be read; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/> is <see langword="null"/>.</exception>
    public static bool TryDecode(bool[,] modules, [NotNullWhen(true)] out BarcodeDecodeResult? result)
    {
        ArgumentNullException.ThrowIfNull(modules);

        // Each row is a scan line, with a quiet zone on both sides
        var lines = new List<int[]>(modules.GetLength(0));
        for (var row = 0; row < modules.GetLength(0); row++)
        {
            var runs = new List<int> { QuietZoneModules };
            var isDark = false;
            for (var column = 0; column < modules.GetLength(1); column++)
            {
                if (modules[row, column] != isDark)
                {
                    runs.Add(0);
                    isDark = !isDark;
                }

                runs[^1]++;
            }

            if (isDark)
            {
                runs.Add(QuietZoneModules);
            }
            else
            {
                runs[^1] += QuietZoneModules;
            }

            lines.Add([.. runs]);
        }

        // The symbol that spans the most rows and modules: a symbology without check character can be read inside another one
        result = LinearDecoder.Decode(lines).OrderByDescending(result => result.LastScanLine - result.FirstScanLine).ThenByDescending(result => result.End - result.Start).FirstOrDefault();
        return result is not null;
    }

    /// <summary>Reads the linear barcodes that scan lines cross.</summary>
    /// <param name="scanLines">
    /// The scan lines: each is the widths of its alternating light and dark runs, in any unit (pixels), starting with a
    /// light run (which can be 0 wide). The lines of a stacked GS1 DataBar symbol are in order from the top.
    /// </param>
    /// <returns>The barcodes found, each once even when several lines cross it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scanLines"/> is <see langword="null"/> or contains <see langword="null"/>.</exception>
    /// <remarks>
    /// A barcode can be read from either end, and must have its quiet zones. A symbology without check character, such as
    /// ITF or Codabar, can be misread from a damaged line: require that several lines agree
    /// (<see cref="BarcodeDecodeResult.FirstScanLine"/> and <see cref="BarcodeDecodeResult.LastScanLine"/>).
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/BarCode128Tests.cs" ref:region="BarcodeDecoder_DecodeScanLines" ref:dedent="true">
    /// // A scan line: the widths of its light and dark runs, starting with a light run, here 3 pixels per module
    /// var barcode = Barcode.CreateEan13("590123412345");
    /// var runs = new List&lt;int&gt; { 30 };
    /// for (var column = 0; column &lt; barcode.Width; column++)
    /// {
    ///     if (barcode[0, column] != (runs.Count % 2 == 0))
    ///     {
    ///         runs.Add(0);
    ///     }
    ///
    ///     runs[^1] += 3;
    /// }
    ///
    /// // The quiet zone after the last bar
    /// runs.Add(30);
    ///
    /// var results = BarcodeDecoder.DecodeScanLines([runs.ToArray()]);
    ///
    /// var result = Assert.Single(results);
    /// Assert.Equal((BarcodeType.Ean13, "5901234123457"), (result.Type, result.Text));
    /// Assert.Equal((30, 30 + (95 * 3)), (result.Start, result.End));
    /// </code>
    /// </example>
    public static IReadOnlyList<BarcodeDecodeResult> DecodeScanLines(IReadOnlyList<int[]> scanLines)
    {
        ArgumentNullException.ThrowIfNull(scanLines);
        foreach (var line in scanLines)
        {
            if (line is null)
                throw new ArgumentNullException(nameof(scanLines), "The scan lines cannot contain null.");
        }

        return LinearDecoder.Decode(scanLines);
    }
}
