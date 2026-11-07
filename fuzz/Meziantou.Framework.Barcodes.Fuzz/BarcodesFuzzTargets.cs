using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>
/// The encoders must reject invalid data with an <see cref="ArgumentException"/> only, or data that does not fit with an
/// <see cref="InvalidOperationException"/>. A symbol must have the size of a valid version, its function patterns and a
/// format information that decodes (checked by an independent BCH decoder) to its version and error correction level;
/// the human-readable text of a barcode carries the check digits recomputed here; every renderer must agree with the
/// module matrix; the payload builders must escape their fields so that a parser gets them back; the canvas drawing must
/// cover the rectangle it measures, and nothing else.
/// </summary>
public static class BarcodesFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "0123456789", "000", "99999999", "ABCDEFGHIJKLMNOPQRSTUVWXYZ", " $%*+-./:", "HELLO WORLD", "hello", "https://", "www.", ".com",
        "é", "ß", "€", "漢字", "日本", "ｱｲｳ", "😀", "\uFEFF", "\0", "\r\n", "\t", "\u0080", "ÿ",
        "WIFI:", "T:WPA;", "S:", "P:", ";;", "\\;", "\\:", "\\,", "\\\\", "BEGIN:VCARD", "MECARD:", "mailto:", "?subject=", "&body=", "smsto:", "tel:+",
        "otpauth://totp/", "bitcoin:", "BCD", "geo:", "+12", "12345",
    ];

    private static readonly (int Height, int Width)[] RMQRSizes =
    [
        (7, 43), (7, 59), (7, 77), (7, 99), (7, 139),
        (9, 43), (9, 59), (9, 77), (9, 99), (9, 139),
        (11, 27), (11, 43), (11, 59), (11, 77), (11, 99), (11, 139),
        (13, 27), (13, 43), (13, 59), (13, 77), (13, 99), (13, 139),
        (15, 43), (15, 59), (15, 77), (15, 99), (15, 139),
        (17, 43), (17, 59), (17, 77), (17, 99), (17, 139),
    ];

    private static readonly string[] QRSeedTexts =
    [
        "HELLO WORLD", "12345", "01234567", "https://www.meziantou.net/", "hello world", "漢字のテスト", "héllo wörld 😀", "A",
        "WIFI:T:WPA;S:MyNetwork;P:password123;;", "Mixed 0123456789 ÉÀ 漢字", new string('7', 300),
    ];

    private static readonly string[] BarcodeSeedTexts =
    [
        "ABC-123", "Hello 12345678", "5512345", "55123457", "590123412345", "4006381333931", "03600029145", "40156", "12345678", "1234-5678$:/.+",
    ];

    private static readonly string[] Code39Alphabet = ["0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%"];

    private static readonly double[] ModuleSizes = [1, 2, 3, 0.5, 1.5, 2.6, 4, 7.3, 0.1, 0, -1, double.NaN, double.PositiveInfinity, 1e7];

    private static readonly double[] FontSizes = [14, 8, 30, 0.5, 200, 0, -1, double.NaN, 1e6];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("barcodes-qrcode", QRCodeTarget) { Seeds = () => QRSeedTexts.SelectMany((text, index) => new[] { 0, 1, 2 }.Select(type => FuzzSeed.FromText($"qrcode-{index}-{type}", [(byte)type, (byte)index], text))), Dictionary = Tokens },
        new("barcodes-linear", LinearTarget) { Seeds = () => Enumerable.Range(0, 8).SelectMany(type => BarcodeSeedTexts.Select((text, index) => FuzzSeed.FromText($"linear-{type}-{index}", [(byte)type, (byte)index], text))), Dictionary = Tokens },
        new("barcodes-payload", PayloadTarget) { Seeds = GetPayloadSeeds, Dictionary = Tokens },
        new("barcodes-draw", DrawTarget) { Seeds = () => Enumerable.Range(0, 12).Select(kind => new FuzzSeed($"draw-{kind}", (byte[])[(byte)kind, (byte)(kind % 8), (byte)(kind * 37), (byte)(kind * 11), (byte)(kind % 3), .. FuzzText.Encode(kind < 3 ? "HELLO" : "12345678")])), Dictionary = Tokens },
    ];

    private static void QRCodeTarget(ReadOnlySpan<byte> data)
    {
        var type = (QRCodeType)(FuzzInput.ReadByte(ref data) % 3);
        var level = (ErrorCorrectionLevel)(FuzzInput.ReadByte(ref data) % 4);
        var text = FuzzInput.ReadText(data);
        if (!TryCreateQRCode(type, text, level, out var qr))
            return;

        FuzzAssert.Equal(type, qr.Type, "The QR code type");
        CheckSize(qr);
        CheckFinderPattern(qr);
        CheckFormatInformation(qr, level);

        // The encoding is deterministic
        TryCreateQRCode(type, text, level, out var other);
        FuzzAssert.True(other is not null && SameModules(qr, other), "Encoding the same data twice gives different symbols");

        // The renderers agree with the module matrix
        using var image = Image.Load<Rgba32>(qr.ToPng(new QRCodePngOptions { ModuleSize = 1, QuietZoneModules = 0 }));
        CheckPixels(image, qr.Width, qr.Height, (row, column) => qr[row, column], "ToPng");
        using var fromImaging = qr.ToImage(new QRCodeDrawingOptions { ModuleSize = 1, QuietZoneModules = 0 });
        CheckPixels(fromImaging, qr.Width, qr.Height, (row, column) => qr[row, column], "ToImage");
        FuzzAssert.True(qr.ToSvg().Length > 0, "ToSvg is empty");
        var console = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0 });
        FuzzAssert.Equal((qr.Height + 1) / 2, console.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, "ToConsoleString line count");
    }

    private static void LinearTarget(ReadOnlySpan<byte> data)
    {
        var type = (BarcodeType)(FuzzInput.ReadByte(ref data) % 8);
        var flags = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(FuzzInput.ReadUntil(ref data, 0));
        var extension = (flags & 3) switch
        {
            1 => FuzzInput.ReadText(data),
            2 => "12",
            3 => "12345",
            _ => null,
        };

        Barcode barcode;
        try
        {
            barcode = type switch
            {
                BarcodeType.Code39 => Barcode.CreateCode39(text, includeChecksum: (flags & 4) != 0),
                BarcodeType.Code93 => Barcode.CreateCode93(text),
                BarcodeType.Code128 => Barcode.CreateCode128(text),
                BarcodeType.Ean8 => Barcode.CreateEan8(text, extension),
                BarcodeType.Ean13 => Barcode.CreateEan13(text, extension),
                BarcodeType.UpcA => Barcode.CreateUpcA(text, extension),
                BarcodeType.Codabar => Barcode.CreateCodabar(text, (char)('A' + ((flags >> 2) & 3)), (char)('a' + ((flags >> 4) & 3))),
                _ => Barcode.CreateItf(text),
            };
        }
        catch (ArgumentException)
        {
            return;
        }

        FuzzAssert.Equal(type, barcode.Type, "The barcode type");
        FuzzAssert.Equal(1, barcode.Height, "A linear barcode is one module high");
        FuzzAssert.True(barcode[0, 0] && barcode[0, barcode.Width - 1], "A linear barcode starts and ends with a bar");
        CheckText(barcode, type, text, extension, (flags & 4) != 0, (char)('A' + ((flags >> 2) & 3)), (char)('A' + ((flags >> 4) & 3)));

        var png = barcode.ToPng(new BarcodePngOptions { ModuleWidth = 1, ModuleHeight = 1, QuietZoneModules = 0 });
        try
        {
            using var image = Image.Load<Rgba32>(png);
            CheckPixels(image, barcode.Width, 1, (row, column) => barcode[row, column], "ToPng");
            using var fromImaging = barcode.ToImage(new BarcodeDrawingOptions { ModuleWidth = 1, ModuleHeight = 1, QuietZoneModules = 0 });
            CheckPixels(fromImaging, barcode.Width, 1, (row, column) => barcode[row, column], "ToImage");
        }
        catch (ImageResourceLimitException)
        {
            // A barcode wider than the default image width limit of Meziantou.Framework.Imaging (32768 pixels)
        }
        FuzzAssert.True(barcode.ToSvg().Length > 0, "ToSvg is empty");
    }

    private static void PayloadTarget(ReadOnlySpan<byte> data)
    {
        var kind = FuzzInput.ReadByte(ref data);
        var options = FuzzInput.ReadByte(ref data);
        var fields = new List<string>();
        while (!data.IsEmpty && fields.Count < 8)
        {
            fields.Add(FuzzInput.ReadText(FuzzInput.ReadUntil(ref data, 0)));
        }

        string Field(int index) => index < fields.Count ? fields[index] : "";
        string? OptionalField(int index) => index < fields.Count && (options & (1 << index)) == 0 ? fields[index] : null;

        string payload;
        try
        {
            switch (kind % 6)
            {
                case 0:
                    var ssid = Field(0);
                    var password = OptionalField(1);
                    payload = QRCodePayload.Wifi(ssid, password, (WifiAuthentication)(options % 3), hidden: (options & 0x80) != 0);
                    CheckWifi(payload, ssid, password);
                    break;

                case 1:
                    var address = Field(0);
                    var subject = OptionalField(1);
                    var body = OptionalField(2);
                    payload = QRCodePayload.Email(address, subject, body);
                    CheckEmail(payload, address, subject, body);
                    break;

                case 2:
                    var number = Field(0);
                    var message = OptionalField(1);
                    payload = QRCodePayload.Sms(number, message);
                    FuzzAssert.True(payload.StartsWith("smsto:", StringComparison.Ordinal), "An SMS payload starts with smsto:");
                    var separator = payload.IndexOf(":", "smsto:".Length, StringComparison.Ordinal);
                    FuzzAssert.Equal(ReplaceLoneSurrogates(number), Uri.UnescapeDataString(separator < 0 ? payload["smsto:".Length..] : payload["smsto:".Length..separator]), "The SMS number");
                    FuzzAssert.Equal(message, separator < 0 ? null : payload[(separator + 1)..], "The SMS message");
                    break;

                case 3:
                    payload = QRCodePayload.MeCard(Field(0), OptionalField(1), OptionalField(2), OptionalField(3), OptionalField(4), OptionalField(5), OptionalField(6), OptionalField(7));
                    break;

                case 4:
                    payload = QRCodePayload.VCard(Field(0), OptionalField(1), OptionalField(2), OptionalField(3), OptionalField(4), OptionalField(5), OptionalField(6), OptionalField(7));
                    break;

                default:
                    payload = QRCodePayload.Phone(Field(0));
                    FuzzAssert.True(payload.StartsWith("tel:", StringComparison.Ordinal), "A phone payload starts with tel:");
                    FuzzAssert.Equal(ReplaceLoneSurrogates(Field(0)), Uri.UnescapeDataString(payload["tel:".Length..]), "The phone number");
                    break;
            }
        }
        catch (ArgumentException)
        {
            return;
        }

        // A payload is encoded, or is too long
        TryCreateQRCode(QRCodeType.Standard, payload, ErrorCorrectionLevel.L, out _);
    }

    private static void DrawTarget(ReadOnlySpan<byte> data)
    {
        var kind = FuzzInput.ReadByte(ref data) % 11;
        var sizeIndex = FuzzInput.ReadByte(ref data);
        var quietZone = (FuzzInput.ReadByte(ref data) % 12) - 1;
        var flags = FuzzInput.ReadByte(ref data);
        var fontByte = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(data);

        // The human-readable text of a barcode, in one of the bundled fonts
        var font = (fontByte % 3) switch
        {
            1 => BarcodeFonts.OcrA,
            2 => BarcodeFonts.OcrB,
            _ => null,
        };
        var fontSize = FontSizes[(fontByte / 3) % FontSizes.Length];

        var moduleSize = ModuleSizes[sizeIndex % ModuleSizes.Length];
        var snap = (flags & 1) == 0;
        var noLight = (flags & 2) != 0;
        var gradient = (flags & 4) != 0;
        var transformed = (flags & 8) != 0;
        var position = (flags >> 4) switch
        {
            0 or 1 or 2 or 3 => new PointD(2, 3),
            4 or 5 => new PointD(1.4, 2.6),
            6 => new PointD(-30, -20),
            7 => new PointD(1e9, 0),
            8 => new PointD(double.NaN, 0),
            _ => new PointD(-0.5, 0.5),
        };

        Brush dark = gradient ? new LinearGradientBrush(new PointD(0, 0), new PointD(64, 0), [new GradientStop(0, Colors.Black), new GradientStop(1, Colors.Blue)]) : new SolidBrush(Colors.Black);
        Brush? light = noLight ? null : new SolidBrush(Colors.White);

        int columns;
        int rows;
        Func<int, int, bool> isDark;
        Func<RectangleD> measure;
        Func<DrawingCanvas, RectangleD> draw;
        if (kind < 3)
        {
            if (!TryCreateQRCode((QRCodeType)kind, text, ErrorCorrectionLevel.L, out var qr))
                return;

            var options = new QRCodeDrawingOptions { ModuleSize = moduleSize, QuietZoneModules = quietZone, DarkBrush = dark, LightBrush = light, SnapToPixels = snap };
            (columns, rows, isDark) = (qr.Width, qr.Height, (row, column) => qr[row, column]);
            measure = () => QRCodeImageRenderer.MeasureQRCode(qr, options);
            draw = canvas => canvas.DrawQRCode(qr, position, options);
        }
        else
        {
            Barcode barcode;
            try
            {
                barcode = (BarcodeType)(kind - 3) switch
                {
                    BarcodeType.Code39 => Barcode.CreateCode39(text),
                    BarcodeType.Code93 => Barcode.CreateCode93(text),
                    BarcodeType.Code128 => Barcode.CreateCode128(text),
                    BarcodeType.Ean8 => Barcode.CreateEan8(text),
                    BarcodeType.Ean13 => Barcode.CreateEan13(text),
                    BarcodeType.UpcA => Barcode.CreateUpcA(text),
                    BarcodeType.Codabar => Barcode.CreateCodabar(text),
                    _ => Barcode.CreateItf(text),
                };
            }
            catch (ArgumentException)
            {
                return;
            }

            var options = new BarcodeDrawingOptions { ModuleWidth = moduleSize, ModuleHeight = moduleSize * 3, QuietZoneModules = quietZone, DarkBrush = dark, LightBrush = light, SnapToPixels = snap, Font = font, FontSize = fontSize };
            (columns, rows, isDark) = (barcode.Width, 1, (row, column) => barcode[row, column]);
            measure = () => BarcodeImageRenderer.MeasureBarcode(barcode, options);
            draw = canvas => canvas.DrawBarcode(barcode, position, options);
        }

        RectangleD? measured;
        try
        {
            measured = measure();
            FuzzAssert.True(measured.Value.X == 0 && measured.Value.Y == 0, "MeasureQRCode and MeasureBarcode give a rectangle at the origin");
        }
        catch (ArgumentOutOfRangeException)
        {
            measured = null;
        }

        using var image = new Image<Rgba32>(64, 48, Colors.Red);
        RectangleD? drawn = null;
        Exception? exception = null;
        image.Draw((draw, transformed), (canvas, state) =>
        {
            try
            {
                if (state.transformed)
                {
                    var result = default(RectangleD);
                    canvas.Transform(AffineTransform.CreateScale(2, 2), canvas => result = state.draw(canvas));
                    drawn = result;
                }
                else
                {
                    drawn = state.draw(canvas);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or ImageResourceLimitException)
            {
                exception = ex;
            }
        });

        if (measured is null)
        {
            FuzzAssert.True(exception is ArgumentOutOfRangeException, $"Measure rejects the options but Draw gives {FuzzAssert.Format(exception?.GetType().Name ?? "no exception")}");
            return;
        }

        if (drawn is null)
            return; // A coordinate out of the range of the canvas, or the allocation limit

        var expectedLeft = snap ? Math.Round(position.X, MidpointRounding.AwayFromZero) : position.X;
        var expectedTop = snap ? Math.Round(position.Y, MidpointRounding.AwayFromZero) : position.Y;
        FuzzAssert.Equal(new RectangleD(expectedLeft, expectedTop, measured.Value.Width, measured.Value.Height), drawn.Value, "Draw covers the measured rectangle at the position");
        if (transformed)
            return;

        // Nothing is painted outside the rectangle (a pixel the rectangle only partly covers can be blended)
        var bounds = drawn.Value;
        var pixels = new Rgba32[64 * 48];
        image.Frames[0].CopyPixelDataTo(pixels);
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var outside = x + 1 <= bounds.Left || x >= bounds.Right || y + 1 <= bounds.Top || y >= bounds.Bottom;
                if (outside && pixels[(y * 64) + x] != Colors.Red)
                    FuzzAssert.Fail($"Pixel ({x}, {y}) outside {bounds} is painted");
            }
        }

        // With whole sizes and solid brushes, each module is exactly its color (a text wider than the bars moves them)
        if (!snap || gradient || noLight || (kind >= 3 && font is not null))
            return;

        var moduleWidth = Math.Max(1, Math.Round(moduleSize, MidpointRounding.AwayFromZero));
        var moduleHeight = kind < 3 ? moduleWidth : Math.Max(1, Math.Round(moduleSize * 3, MidpointRounding.AwayFromZero));
        var verticalQuietZone = kind < 3 ? quietZone : 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = bounds.Left + ((quietZone + column + 0.5) * moduleWidth);
                var y = bounds.Top + ((verticalQuietZone + row + 0.5) * moduleHeight);
                if (x < 0 || y < 0 || x >= 64 || y >= 48)
                    continue;

                var expected = isDark(row, column) ? Colors.Black : Colors.White;
                var actual = pixels[((int)y * 64) + (int)x];
                if (actual != expected)
                    FuzzAssert.Fail($"Module ({column}, {row}) at pixel ({(int)x}, {(int)y}) is {actual}, expected {expected}");
            }
        }
    }

    private static bool TryCreateQRCode(QRCodeType type, string text, ErrorCorrectionLevel level, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out QRCode? qr)
    {
        try
        {
            qr = type switch
            {
                QRCodeType.MicroQR => QRCode.CreateMicroQR(text, level),
                QRCodeType.RMQR => QRCode.CreateRMQR(text, level),
                _ => QRCode.Create(text, level),
            };
            return true;
        }
        catch (ArgumentException)
        {
            qr = null;
            return false;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("too long", StringComparison.Ordinal))
        {
            qr = null;
            return false;
        }
    }

    private static void CheckSize(QRCode qr)
    {
        switch (qr.Type)
        {
            case QRCodeType.Standard:
                FuzzAssert.True(qr.Version is >= 1 and <= 40, $"Version {qr.Version} is not a QR code version");
                FuzzAssert.Equal((17 + (4 * qr.Version), 17 + (4 * qr.Version)), (qr.Width, qr.Height), "The size of the version");
                break;
            case QRCodeType.MicroQR:
                FuzzAssert.True(qr.Version is >= 1 and <= 4, $"Version {qr.Version} is not a Micro QR version");
                FuzzAssert.Equal((9 + (2 * qr.Version), 9 + (2 * qr.Version)), (qr.Width, qr.Height), "The size of the version");
                break;
            default:
                FuzzAssert.True(Array.IndexOf(RMQRSizes, (qr.Height, qr.Width)) >= 0, $"{qr.Width}x{qr.Height} is not an rMQR size");
                break;
        }
    }

    private static void CheckFinderPattern(QRCode qr)
    {
        for (var row = 0; row < 7; row++)
        {
            for (var column = 0; column < 7; column++)
            {
                var ring = Math.Max(Math.Abs(row - 3), Math.Abs(column - 3));
                FuzzAssert.Equal(ring != 2, qr[row, column], $"Finder pattern module ({column}, {row})");
            }
        }
    }

    // The format information is checked by decoding it here, with the BCH(15,5) code of ISO/IEC 18004 (generator 0x537)
    private static void CheckFormatInformation(QRCode qr, ErrorCorrectionLevel requested)
    {
        if (qr.Type is QRCodeType.RMQR)
            return;

        int bits;
        if (qr.Type is QRCodeType.Standard)
        {
            // Row 8 from column 0 (skipping the timing column 6), then column 8 up from row 7 (skipping the timing row 6)
            bits = 0;
            foreach (var column in new[] { 0, 1, 2, 3, 4, 5, 7, 8 })
            {
                bits = (bits << 1) | (qr[8, column] ? 1 : 0);
            }

            foreach (var row in new[] { 7, 5, 4, 3, 2, 1, 0 })
            {
                bits = (bits << 1) | (qr[row, 8] ? 1 : 0);
            }

            var value = DecodeFormat(bits ^ 0x5412);
            FuzzAssert.True(value >= 0, $"The format information {bits:X4} is not a BCH codeword");
            var level = (value >> 3) switch { 1 => ErrorCorrectionLevel.L, 0 => ErrorCorrectionLevel.M, 3 => ErrorCorrectionLevel.Q, _ => ErrorCorrectionLevel.H };
            FuzzAssert.Equal(qr.ErrorCorrectionLevel, level, "The error correction level of the format information");
            FuzzAssert.True(qr.ErrorCorrectionLevel >= requested, "The error correction level is lower than requested");
            return;
        }

        // Micro QR: row 8 from column 1 to 8, then column 8 up from row 7 to row 1
        bits = 0;
        for (var column = 1; column <= 8; column++)
        {
            bits = (bits << 1) | (qr[8, column] ? 1 : 0);
        }

        for (var row = 7; row >= 1; row--)
        {
            bits = (bits << 1) | (qr[row, 8] ? 1 : 0);
        }

        var symbol = DecodeFormat(bits ^ 0x4445);
        FuzzAssert.True(symbol >= 0, $"The Micro QR format information {bits:X4} is not a BCH codeword");
        var (version, symbolLevel) = (symbol >> 2) switch
        {
            0 => (1, (ErrorCorrectionLevel?)null),
            1 => (2, ErrorCorrectionLevel.L),
            2 => (2, ErrorCorrectionLevel.M),
            3 => (3, ErrorCorrectionLevel.L),
            4 => (3, ErrorCorrectionLevel.M),
            5 => (4, ErrorCorrectionLevel.L),
            6 => (4, ErrorCorrectionLevel.M),
            _ => (4, ErrorCorrectionLevel.Q),
        };
        FuzzAssert.Equal(qr.Version, version, "The version of the Micro QR format information");
        if (symbolLevel is not null)
        {
            FuzzAssert.Equal(qr.ErrorCorrectionLevel, symbolLevel.Value, "The error correction level of the Micro QR format information");
        }
    }

    // Returns the 5 data bits of a BCH(15,5) codeword, or -1 when the value is not a codeword
    private static int DecodeFormat(int codeword)
    {
        var data = codeword >> 10;
        var remainder = data << 10;
        for (var bit = 14; bit >= 10; bit--)
        {
            if ((remainder & (1 << bit)) != 0)
            {
                remainder ^= 0x537 << (bit - 10);
            }
        }

        return ((data << 10) | remainder) == codeword ? data : -1;
    }

    private static void CheckText(Barcode barcode, BarcodeType type, string data, string? extension, bool code39Checksum, char codabarStart, char codabarStop)
    {
        switch (type)
        {
            case BarcodeType.Ean8 or BarcodeType.Ean13 or BarcodeType.UpcA:
                var length = type switch { BarcodeType.Ean8 => 8, BarcodeType.Ean13 => 13, _ => 12 };
                var expectedText = data.Length == length ? data : data + GetCheckDigit(data).ToString(CultureInfo.InvariantCulture);
                FuzzAssert.Equal(GetCheckDigit(expectedText[..^1]), expectedText[^1] - '0', "The check digit");
                if (!string.IsNullOrEmpty(extension))
                {
                    expectedText += " " + extension;
                }

                FuzzAssert.Equal(expectedText, barcode.Text, "The human-readable text");
                var addOnWidth = extension?.Length switch
                {
                    2 => (type is BarcodeType.UpcA ? 9 : 7) + 20,
                    5 => (type is BarcodeType.UpcA ? 9 : 7) + 47,
                    _ => 0,
                };
                FuzzAssert.Equal((type is BarcodeType.Ean8 ? 67 : 95) + addOnWidth, barcode.Width, "The width of the symbol");
                break;

            case BarcodeType.Code39:
                var checksum = code39Checksum ? Code39Alphabet[0][data.Sum(c => Code39Alphabet[0].IndexOf(c, StringComparison.Ordinal)) % 43].ToString() : "";
                FuzzAssert.Equal(data + checksum, barcode.Text, "The human-readable text");
                break;

            case BarcodeType.Codabar:
                FuzzAssert.Equal(codabarStart + data + codabarStop, barcode.Text, "The human-readable text");
                break;

            case BarcodeType.Code93:
                FuzzAssert.Equal(data, barcode.Text, "The human-readable text");
                FuzzAssert.Equal(0, (barcode.Width - 1) % 9, "Code 93 symbols are 9 modules wide, plus the termination bar");
                break;

            case BarcodeType.Code128:
                FuzzAssert.Equal(data, barcode.Text, "The human-readable text");
                FuzzAssert.Equal(0, (barcode.Width - 13) % 11, "Code 128 symbols are 11 modules wide, the stop pattern 13");
                break;

            default:
                FuzzAssert.Equal(data, barcode.Text, "The human-readable text");
                break;
        }
    }

    // GS1 check digit: weights 3 and 1 alternate from the rightmost digit
    private static int GetCheckDigit(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var weight = i % 2 == 0 ? 3 : 1;
            sum += (digits[digits.Length - 1 - i] - '0') * weight;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static void CheckWifi(string payload, string ssid, string? password)
    {
        FuzzAssert.True(payload.StartsWith("WIFI:", StringComparison.Ordinal) && payload.EndsWith(";;", StringComparison.Ordinal), "A WiFi payload is WIFI:...;;");

        // Fields are separated by ';', and '\' escapes the next character
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = new System.Text.StringBuilder();
        string? key = null;
        for (var i = "WIFI:".Length; i < payload.Length; i++)
        {
            var c = payload[i];
            if (c == '\\' && i + 1 < payload.Length)
            {
                current.Append(payload[++i]);
            }
            else if (c == ':' && key is null)
            {
                key = current.ToString();
                current.Clear();
            }
            else if (c == ';')
            {
                if (key is not null)
                {
                    FuzzAssert.True(fields.TryAdd(key, current.ToString()), $"The field {key} is repeated");
                }

                key = null;
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        FuzzAssert.Equal(ssid, fields.GetValueOrDefault("S"), "The SSID");
        FuzzAssert.Equal(password, fields.GetValueOrDefault("P"), "The password");
    }

    private static void CheckEmail(string payload, string address, string? subject, string? body)
    {
        FuzzAssert.True(payload.StartsWith("mailto:", StringComparison.Ordinal), "An email payload starts with mailto:");
        var query = payload.IndexOf('?', StringComparison.Ordinal);
        var addressPart = query < 0 ? payload["mailto:".Length..] : payload["mailto:".Length..query];
        FuzzAssert.Equal(ReplaceLoneSurrogates(address), Uri.UnescapeDataString(addressPart), "The address");

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (query >= 0)
        {
            foreach (var parameter in payload[(query + 1)..].Split('&'))
            {
                var equal = parameter.IndexOf('=', StringComparison.Ordinal);
                FuzzAssert.True(equal > 0, $"The parameter {parameter} has no name");
                FuzzAssert.True(parameters.TryAdd(parameter[..equal], Uri.UnescapeDataString(parameter[(equal + 1)..])), $"The parameter {parameter[..equal]} is repeated");
            }
        }

        FuzzAssert.Equal(subject is null ? null : ReplaceLoneSurrogates(subject), parameters.GetValueOrDefault("subject"), "The subject");
        FuzzAssert.Equal(body is null ? null : ReplaceLoneSurrogates(body), parameters.GetValueOrDefault("body"), "The body");
    }

    // Percent-encoding goes through UTF-8, where a lone surrogate becomes U+FFFD, as it does in the byte mode of a QR code
    private static string ReplaceLoneSurrogates(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(value, i, 2);
                i++;
            }
            else
            {
                builder.Append(char.IsSurrogate(value[i]) ? '\uFFFD' : value[i]);
            }
        }

        return builder.ToString();
    }

    private static bool SameModules(QRCode a, QRCode b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return false;

        for (var row = 0; row < a.Height; row++)
        {
            for (var column = 0; column < a.Width; column++)
            {
                if (a[row, column] != b[row, column])
                    return false;
            }
        }

        return true;
    }

    private static void CheckPixels(Image<Rgba32> image, int width, int height, Func<int, int, bool> isDark, string renderer)
    {
        FuzzAssert.Equal((width, height), (image.Width, image.Height), $"The size of the {renderer} image");
        var pixels = new Rgba32[width * height];
        image.Frames[0].CopyPixelDataTo(pixels);
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var expected = isDark(row, column) ? Colors.Black : Colors.White;
                if (pixels[(row * width) + column] != expected)
                    FuzzAssert.Fail($"{renderer} pixel ({column}, {row}) does not match the module");
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetPayloadSeeds()
    {
        string[][] fields =
        [
            ["MyNetwork", "password123"],
            ["Net;work", "pa:ss\\word,\""],
            ["test@example.com", "Hello", "Body & more"],
            ["a@b.com?bcc=c@d.com", "Hi"],
            ["+1234567890", "Hello!"],
            ["Doe", "John", "+1234567890", "john@example.com", "Contoso", "Engineer", "https://example.com", "1 Main St"],
        ];
        for (var kind = 0; kind < 6; kind++)
        {
            foreach (var (values, index) in fields.Select((values, index) => (values, index)))
            {
                yield return new FuzzSeed($"payload-{kind}-{index}", (byte[])[(byte)kind, 0, .. FuzzText.Encode(string.Join('\0', values))]);
            }
        }
    }
}
