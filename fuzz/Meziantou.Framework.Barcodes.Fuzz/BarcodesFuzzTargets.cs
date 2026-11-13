using System.Globalization;
using System.Text;
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

    // The tokens of UPC-E numbers, GTINs and GS1 element strings: application identifiers, separators and the
    // characters of the general-purpose modes of GS1 DataBar Expanded
    private static readonly string[] Gs1DataBarTokens =
    [
        "0", "1", "9", "00000", "0123456", "04252614", "0950110153000", "09501101530003", "2001234567890", "1999999999999",
        "01\0", "10\0", "17\0", "21\0", "90\0", "3103\0", "3202\0", "3203\0", "3922\0", "3932\0", "11\0", "13\0", "15\0", "17\0", "8200\0",
        "\0", "ABCXYZ", "abcxyz", "*,-./", "!\"%&'()+:;<=>?_", "991231", "250101", "001750",
    ];

    // The UPC-E numbers and GTINs of the unit tests, then element strings: application identifier and data, separated by NUL
    private static readonly string[] Gs1DataBarSeedTexts =
    [
        "0425261", "04252614", "425261", "04210000526", "0123456", "1234567", "03600000029", "0180432",
        "0950110153001", "09501101530003", "2001234567890", "1501234567890", "0658200098646", "0000001979845", "9999999999999",
        "01\098898765432106\03202\0012345\015\0991231", "01\090012345678908\03103\0001750", "01\090012345678908\03922\0795",
        "01\090012345678908\03932\00401234\021\0XY", "01\009501101530003\017\0250101\010\0ABC123", "10\012", "21\0abc-DEF/12345%xyz",
        "8200\0http://example.com/abc", "90\00123456789ABCDEFGHIJ0123456789",
    ];

    private static readonly string[] Barcode2DSeedTexts =
    [
        "Hello", "123456", "0123456789012345678901234567890123456789", "HELLO WORLD", "hello world", "Abc 123 xyz", "https://www.meziantou.net/",
        "*ABC>123*", "\r*>\r", "@ABC^_", "café", "Ελληνικά 漢字 😀", "\u0001\u007f\u0080\u00ff", new string('a', 200), new string('7', 400),
        "01095212345432131725010110ABC123", "10ABC\u001D21XY",
    ];

    // Characters that switch the encodation modes of Data Matrix: C40 and Text (uppercase, lowercase), X12 (*>\r), EDIFACT
    // (@^_), Base 256; of PDF417: the text sub-modes, numeric runs of 13 digits; of Aztec: the punctuation pairs
    private static readonly string[] Barcode2DTokens = ["*", ">", "\r", "@", "^", "_", "ABC", "abc", "a1b2", "\u001D", "\u00e9", "\u0080", "\u00ff", "0000", ". ", ", ", ": ", "\t", "\n", ";", "{", "|", "1234567890123"];

    private static readonly string[] Code39Alphabet = ["0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%"];

    private static readonly double[] ModuleSizes = [1, 2, 3, 0.5, 1.5, 2.6, 4, 7.3, 0.1, 0, -1, double.NaN, double.PositiveInfinity, 1e7];

    private static readonly double[] FontSizes = [14, 8, 30, 0.5, 200, 0, -1, double.NaN, 1e6];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("barcodes-qrcode", QRCodeTarget) { Seeds = () => QRSeedTexts.SelectMany((text, index) => new[] { 0, 1, 2 }.Select(type => FuzzSeed.FromText($"qrcode-{index}-{type}", [(byte)type, (byte)index], text))), Dictionary = Tokens },
        new("barcodes-linear", LinearTarget) { Seeds = () => Enumerable.Range(0, 8).SelectMany(type => BarcodeSeedTexts.Select((text, index) => FuzzSeed.FromText($"linear-{type}-{index}", [(byte)type, (byte)index], text))), Dictionary = Tokens },
        new("barcodes-gs1-databar", Gs1DataBarTarget) { Seeds = GetGs1DataBarSeeds, Dictionary = Gs1DataBarTokens },
        new("barcodes-payload", PayloadTarget) { Seeds = GetPayloadSeeds, Dictionary = Tokens },
        new("barcodes-2d", Barcode2DTarget) { Seeds = () => Enumerable.Range(0, 3).SelectMany(type => Barcode2DSeedTexts.SelectMany((text, index) => new[] { 0, 0x5A, 0xA5 }.Select(options => FuzzSeed.FromText($"2d-{type}-{index}-{options}", [(byte)type, (byte)options, (byte)index], text)))), Dictionary = [.. Tokens, .. Barcode2DTokens] },
        new("barcodes-draw", DrawTarget) { Seeds = () => Enumerable.Range(0, 12).Select(kind => new FuzzSeed($"draw-{kind}", (byte[])[(byte)kind, (byte)(kind % 8), (byte)(kind * 37), (byte)(kind * 11), (byte)(kind % 3), .. FuzzText.Encode(kind < 3 ? "HELLO" : "12345678")])), Dictionary = Tokens },
        .. Gs1FuzzTargets.All,
        .. QRSegmentFuzzTargets.All,
        .. QRDecodeFuzzTargets.All,
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

        // The decoder reads the text back, also with a damaged module, which the error correction fixes (M1 only detects errors)
        var expectedText = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));
        var decoded = QRCodeDecoder.Decode(qr);
        FuzzAssert.Equal(expectedText, decoded.Text, "The decoded text");
        FuzzAssert.Equal((qr.Type, qr.Version, qr.ErrorCorrectionLevel, 0), (decoded.Type, decoded.Version, decoded.ErrorCorrectionLevel, decoded.CorrectedErrorCount), "The decoded symbol parameters");
        var damaged = QRDecodeFuzzTargets.GetModules(qr);
        var hash = 17u;
        foreach (var c in text)
        {
            hash = (hash * 31) + c;
        }

        var damagedRow = (int)(hash % (uint)qr.Height);
        var damagedColumn = (int)((hash / 7919) % (uint)qr.Width);
        damaged[damagedRow, damagedColumn] = !damaged[damagedRow, damagedColumn];
        if (QRCodeDecoder.TryDecode(damaged, out var repaired) || !(qr.Type is QRCodeType.MicroQR && qr.Version is 1))
        {
            FuzzAssert.True(repaired is not null && repaired.Text == expectedText, $"A damaged module at ({damagedRow}, {damagedColumn}) gives '{repaired?.Text}'");
        }

        // The encoding is deterministic
        TryCreateQRCode(type, text, level, out var other);
        FuzzAssert.True(other is not null && SameModules(qr, other), "Encoding the same data twice gives different symbols");

        // The segments of different modes never take more room than the whole text in one byte segment
        QRCode? byteSegment = null;
        try
        {
            QRCodeSegment[] segments = [QRCodeSegment.CreateByte(text)];
            byteSegment = type switch
            {
                QRCodeType.MicroQR => QRCode.CreateMicroQR(segments, level),
                QRCodeType.RMQR => QRCode.CreateRMQR(segments, level),
                _ => QRCode.Create(segments, level),
            };
        }
        catch (InvalidOperationException)
        {
            // The byte segment does not fit
        }

        FuzzAssert.True(byteSegment is null || qr.Width * qr.Height <= byteSegment.Width * byteSegment.Height, $"The symbol of the text ({qr.Width}x{qr.Height}) is larger than the one of a single byte segment ({byteSegment?.Width}x{byteSegment?.Height})");

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

    // UPC-E and GS1 DataBar: the texts carry the check digits recomputed here, the sizes are the ones of the
    // specification, a stacked symbol has the modules of the single-row symbol, and the renderers agree with the matrix
    private static void Gs1DataBarTarget(ReadOnlySpan<byte> data)
    {
        var type = (BarcodeType)((int)BarcodeType.UpcE + (FuzzInput.ReadByte(ref data) % 7));
        var segmentsPerRow = 2 * (1 + (FuzzInput.ReadByte(ref data) % 10));
        var text = FuzzInput.ReadText(data);
        var elements = new List<Gs1Element>();
        if (type is BarcodeType.Gs1DataBarExpanded or BarcodeType.Gs1DataBarExpandedStacked)
        {
            var fields = text.Split('\0');
            for (var i = 0; i + 1 < fields.Length; i += 2)
            {
                try
                {
                    elements.Add(new Gs1Element(fields[i], fields[i + 1]));
                }
                catch (ArgumentException)
                {
                    return;
                }
            }
        }

        Barcode barcode;
        try
        {
            barcode = type switch
            {
                BarcodeType.UpcE => Barcode.CreateUpcE(text),
                BarcodeType.Gs1DataBar => Barcode.CreateGs1DataBar(text),
                BarcodeType.Gs1DataBarStacked => Barcode.CreateGs1DataBarStacked(text),
                BarcodeType.Gs1DataBarStackedOmnidirectional => Barcode.CreateGs1DataBarStackedOmnidirectional(text),
                BarcodeType.Gs1DataBarLimited => Barcode.CreateGs1DataBarLimited(text),
                BarcodeType.Gs1DataBarExpanded => Barcode.CreateGs1DataBarExpanded([.. elements]),
                _ => Barcode.CreateGs1DataBarExpandedStacked(segmentsPerRow, [.. elements]),
            };
        }
        catch (ArgumentException)
        {
            return;
        }

        FuzzAssert.Equal(type, barcode.Type, "The barcode type");
        FuzzAssert.Equal(type is BarcodeType.Gs1DataBarStacked or BarcodeType.Gs1DataBarStackedOmnidirectional or BarcodeType.Gs1DataBarExpandedStacked, barcode.IsStacked, "IsStacked");
        switch (type)
        {
            case BarcodeType.UpcE:
                FuzzAssert.Equal(8, barcode.Text.Length, "The length of a UPC-E number");
                var upcA = ExpandUpcE(barcode.Text);
                FuzzAssert.Equal(GetCheckDigit(upcA[..^1]), upcA[^1] - '0', "The check digit of the UPC-A number of the UPC-E number");
                var expectedPrefix = text.Length switch
                {
                    6 => "0" + text,
                    7 or 8 => text,
                    _ => null,
                };
                FuzzAssert.True(expectedPrefix is null ? upcA.StartsWith(text, StringComparison.Ordinal) : barcode.Text.StartsWith(expectedPrefix, StringComparison.Ordinal), "The UPC-E number stands for the data");
                FuzzAssert.Equal((51, 1), (barcode.Width, barcode.Height), "The size of a UPC-E symbol");
                FuzzAssert.True(Barcode.CreateUpcE(upcA).Text == barcode.Text, "The UPC-A number gives the same UPC-E number");
                break;

            case BarcodeType.Gs1DataBarExpanded or BarcodeType.Gs1DataBarExpandedStacked:
                FuzzAssert.Equal(string.Concat(elements.Select(element => element.ToString())), barcode.Text, "The human-readable text");
                var singleRow = Barcode.CreateGs1DataBarExpanded([.. elements]);
                FuzzAssert.Equal(1, singleRow.Height, "GS1 DataBar Expanded is one row");
                var characters = Enumerable.Range(3, 20).FirstOrDefault(count => 4 + (17 * count) + (15 * ((count + 1) / 2)) == singleRow.Width);
                FuzzAssert.True(characters is >= 4 and <= 22, "The number of symbol characters");
                if (type is BarcodeType.Gs1DataBarExpandedStacked)
                {
                    // The last row has at least 2 segments: the data is padded with a character when it would have 1
                    var stackedCharacters = characters % segmentsPerRow == 1 ? characters + 1 : characters;
                    var rows = (stackedCharacters + segmentsPerRow - 1) / segmentsPerRow;
                    FuzzAssert.Equal((34 * rows) + (3 * (rows - 1)), barcode.Height, "The height of a GS1 DataBar Expanded Stacked symbol");
                    if (rows == 1 && stackedCharacters == characters)
                    {
                        FuzzAssert.True(SameModules(singleRow, barcode, row: 0), "A single-row GS1 DataBar Expanded Stacked symbol is GS1 DataBar Expanded");
                    }
                }

                break;

            default:
                var gtin = text.Length == 13 ? text + GetCheckDigit(text).ToString(CultureInfo.InvariantCulture) : text;
                FuzzAssert.Equal(GetCheckDigit(gtin[..^1]), gtin[^1] - '0', "The check digit of the GTIN");
                FuzzAssert.Equal("(01)" + gtin, barcode.Text, "The human-readable text");
                var expectedSize = type switch
                {
                    BarcodeType.Gs1DataBar => (96, 1),
                    BarcodeType.Gs1DataBarStacked => (50, 13),
                    BarcodeType.Gs1DataBarStackedOmnidirectional => (50, 69),
                    _ => (74, 1),
                };
                FuzzAssert.Equal(expectedSize, (barcode.Width, barcode.Height), "The size of the symbol");
                if (type is BarcodeType.Gs1DataBarStacked or BarcodeType.Gs1DataBarStackedOmnidirectional)
                {
                    // The top row is the left half of the single-row symbol and a guard, the bottom row a guard and its right half
                    var omnidirectional = Barcode.CreateGs1DataBar(text);
                    for (var column = 0; column < 48; column++)
                    {
                        FuzzAssert.Equal(omnidirectional[0, column], barcode[0, column], "The top row of a stacked symbol");
                        FuzzAssert.Equal(omnidirectional[0, 48 + column], barcode[barcode.Height - 1, 2 + column], "The bottom row of a stacked symbol");
                    }
                }

                break;
        }

        // A stacked symbol is drawn with rows as high as a module is wide
        try
        {
            using var image = Image.Load<Rgba32>(barcode.ToPng(new BarcodePngOptions { ModuleWidth = 1, ModuleHeight = 1, QuietZoneModules = 0 }));
            CheckPixels(image, barcode.Width, barcode.Height, (row, column) => barcode[row, column], "ToPng");
            using var fromImaging = barcode.ToImage(new BarcodeDrawingOptions { ModuleWidth = 1, ModuleHeight = 1, QuietZoneModules = 0 });
            CheckPixels(fromImaging, barcode.Width, barcode.Height, (row, column) => barcode[row, column], "ToImage");
        }
        catch (ImageResourceLimitException)
        {
            // Wider than the default image width limit of Meziantou.Framework.Imaging
        }

        var svg = barcode.ToSvg(new BarcodeSvgOptions { ModuleWidth = 1, ModuleHeight = 1, QuietZoneModules = 0 });
        FuzzAssert.True(svg.Contains(FormattableString.Invariant($"viewBox=\"0 0 {barcode.Width} {barcode.Height}\""), StringComparison.Ordinal), "The size of the SVG");
    }

    // The 12 digits of the UPC-A number of the 8 digits of a UPC-E number (GS1 General Specifications, section 5.2.2.4.2)
    private static string ExpandUpcE(string upcE) => upcE[6] switch
    {
        '0' or '1' or '2' => upcE[..3] + upcE[6] + "0000" + upcE[3..6],
        '3' => upcE[..4] + "00000" + upcE[4..6],
        '4' => upcE[..5] + "00000" + upcE[5],
        _ => upcE[..6] + "0000" + upcE[6],
    } + upcE[7];

    private static bool SameModules(Barcode expected, Barcode actual, int row)
    {
        if (expected.Width != actual.Width)
            return false;

        for (var column = 0; column < expected.Width; column++)
        {
            if (expected[0, column] != actual[row, column])
                return false;
        }

        return true;
    }

    private static IEnumerable<FuzzSeed> GetGs1DataBarSeeds()
    {
        for (var type = 0; type < 7; type++)
        {
            for (var index = 0; index < Gs1DataBarSeedTexts.Length; index++)
            {
                yield return FuzzSeed.FromText($"gs1-databar-{type}-{index}", [(byte)type, (byte)index], Gs1DataBarSeedTexts[index]);
            }
        }
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

    private static void Barcode2DTarget(ReadOnlySpan<byte> data)
    {
        var type = (Barcode2DType)(FuzzInput.ReadByte(ref data) % 3);
        var flags = FuzzInput.ReadByte(ref data);
        var sizeByte = FuzzInput.ReadByte(ref data);
        var binary = (flags & 0x80) != 0;
        var text = FuzzInput.ReadText(data);
        var bytes = data.ToArray();

        Barcode2D? Create()
        {
            try
            {
                return type switch
                {
                    Barcode2DType.DataMatrix when (flags & 0x40) != 0 => Barcode2D.CreateGs1DataMatrix([.. CreateGs1Elements(text)], CreateDataMatrixOptions(flags, sizeByte)),
                    Barcode2DType.DataMatrix => binary ? Barcode2D.CreateDataMatrix(bytes, CreateDataMatrixOptions(flags, sizeByte)) : Barcode2D.CreateDataMatrix(text, CreateDataMatrixOptions(flags, sizeByte)),
                    Barcode2DType.Pdf417 => binary ? Barcode2D.CreatePdf417(bytes, CreatePdf417Options(flags, sizeByte)) : Barcode2D.CreatePdf417(text, CreatePdf417Options(flags, sizeByte)),
                    _ => binary ? Barcode2D.CreateAztec(bytes, CreateAztecOptions(flags, sizeByte)) : Barcode2D.CreateAztec(text, CreateAztecOptions(flags, sizeByte)),
                };
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                // The data does not fit
                return null;
            }
        }

        var barcode = Create();
        if (barcode is null)
            return;

        FuzzAssert.Equal(type, barcode.Type, "The barcode type");
        switch (type)
        {
            case Barcode2DType.DataMatrix:
                CheckDataMatrix(barcode, CreateDataMatrixOptions(flags, sizeByte));
                CheckDataMatrixContent(barcode, (flags & 0x40) != 0 ? CreateGs1Elements(text) : null, binary ? bytes : null, text);
                break;
            case Barcode2DType.Pdf417:
                CheckPdf417(barcode, CreatePdf417Options(flags, sizeByte));
                break;
            default:
                CheckAztec(barcode, CreateAztecOptions(flags, sizeByte));
                break;
        }

        // The encoding is deterministic
        var other = Create();
        FuzzAssert.True(other is not null && SameModules(barcode, other), "Encoding the same data twice gives different symbols");

        // The renderers agree with the module matrix
        using var image = Image.Load<Rgba32>(barcode.ToPng(new Barcode2DPngOptions { ModuleSize = 1, QuietZoneModules = 0 }));
        CheckPixels(image, barcode.Width, barcode.Height, (row, column) => barcode[row, column], "ToPng");
        using var fromImaging = barcode.ToImage(new Barcode2DDrawingOptions { ModuleSize = 1, QuietZoneModules = 0 });
        CheckPixels(fromImaging, barcode.Width, barcode.Height, (row, column) => barcode[row, column], "ToImage");
        FuzzAssert.True(barcode.ToSvg().StartsWith("<svg ", StringComparison.Ordinal), "ToSvg does not start with <svg");
    }

    // The text as element strings: an application identifier of 2 digits, its data, and GS between the elements
    private static List<Gs1Element> CreateGs1Elements(string text)
    {
        var elements = new List<Gs1Element>();
        foreach (var part in text.Split('\u001D'))
        {
            if (part.Length < 3)
                continue;

            elements.Add(new Gs1Element(part[..2], part[2..]));
        }

        return elements;
    }

    // The heights and widths of the Data Matrix sizes, and invalid ones
    private static readonly int[] DataMatrixDimensions = [8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 32, 36, 40, 44, 48, 52, 64, 72, 80, 88, 96, 104, 120, 132, 144, 26, 32, 36, 48, 9, 0, -1];

    private static DataMatrixOptions? CreateDataMatrixOptions(byte flags, byte sizeByte)
    {
        if ((flags & 0x0F) == 0)
            return null;

        var options = new DataMatrixOptions { Shape = (DataMatrixShape)((flags & 0x0F) % 3) };
        try
        {
            if ((flags & 0x10) != 0)
            {
                options.Height = DataMatrixDimensions[sizeByte % DataMatrixDimensions.Length];
            }

            if ((flags & 0x20) != 0)
            {
                options.Width = DataMatrixDimensions[(sizeByte / DataMatrixDimensions.Length) % DataMatrixDimensions.Length];
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Not a height or width of a Data Matrix size: the options keep the other constraints
        }

        return options;
    }

    // The bits 0x01, 0x02 and 0x04 of the flags set the error correction level, the columns and the row height, from the
    // size byte; some values are out of range and must be rejected
    private static Pdf417Options? CreatePdf417Options(byte flags, byte sizeByte)
    {
        if ((flags & 0x0F) == 0)
            return null;

        var options = new Pdf417Options();
        if ((flags & 0x01) != 0)
        {
            var level = (sizeByte % 10) - 1;
            SetOption(level is >= 0 and <= 8, () => options.SecurityLevel = level, nameof(Pdf417Options.SecurityLevel));
        }

        if ((flags & 0x02) != 0)
        {
            var columns = sizeByte % 32;
            SetOption(columns is >= 1 and <= 30, () => options.Columns = columns, nameof(Pdf417Options.Columns));
        }

        if ((flags & 0x04) != 0)
        {
            var rowHeight = sizeByte % 22;
            SetOption(rowHeight is >= 1 and <= 20, () => options.RowHeight = rowHeight, nameof(Pdf417Options.RowHeight));
        }

        return options;
    }

    // An invalid value throws an ArgumentOutOfRangeException, which the caller catches
    private static void SetOption(bool valid, Action set, string name)
    {
        set();
        FuzzAssert.True(valid, $"The invalid value of {name} is accepted");
    }

    // Bits 0-1 of the flags: the shape (any, any, compact, full-range); bits 2-3: what the size byte sets (nothing, the error
    // correction percentage, the number of layers, both). The values out of range throw ArgumentOutOfRangeException, which
    // the caller handles as invalid options.
    private static AztecOptions? CreateAztecOptions(byte flags, byte sizeByte)
    {
        if ((flags & 0x0F) == 0)
            return null;

        var options = new AztecOptions
        {
            Compact = (flags & 0x03) switch
            {
                2 => true,
                3 => false,
                _ => null,
            },
        };

        switch ((flags >> 2) & 0x03)
        {
            case 1:
                SetAztecOption(options, sizeByte, sizeByte is < 5 or > 95, (options, value) => options.ErrorCorrectionPercent = value);
                break;

            case 2:
                SetAztecOption(options, sizeByte % 34, sizeByte % 34 is 0 or 33, (options, value) => options.Layers = value);
                break;

            case 3:
                options.ErrorCorrectionPercent = 5 + (sizeByte % 16 * 6);
                options.Layers = 1 + (sizeByte >> 3);
                break;
        }

        return options;
    }

    private static void SetAztecOption(AztecOptions options, int value, bool outOfRange, Action<AztecOptions, int> setter)
    {
        try
        {
            setter(options, value);
        }
        catch (ArgumentOutOfRangeException)
        {
            FuzzAssert.True(outOfRange, $"The option value {value} is valid but throws");
            throw;
        }

        FuzzAssert.False(outOfRange, $"The option value {value} is out of range but does not throw");
    }

    private static readonly (int Height, int Width, int RegionHeight, int RegionWidth)[] DataMatrixSizes =
    [
        (10, 10, 8, 8), (12, 12, 10, 10), (14, 14, 12, 12), (16, 16, 14, 14), (18, 18, 16, 16), (20, 20, 18, 18), (22, 22, 20, 20),
        (24, 24, 22, 22), (26, 26, 24, 24), (32, 32, 14, 14), (36, 36, 16, 16), (40, 40, 18, 18), (44, 44, 20, 20), (48, 48, 22, 22),
        (52, 52, 24, 24), (64, 64, 14, 14), (72, 72, 16, 16), (80, 80, 18, 18), (88, 88, 20, 20), (96, 96, 22, 22), (104, 104, 24, 24),
        (120, 120, 18, 18), (132, 132, 20, 20), (144, 144, 22, 22), (8, 18, 6, 16), (8, 32, 6, 14), (12, 26, 10, 24), (12, 36, 10, 16),
        (16, 36, 14, 16), (16, 48, 14, 22),
    ];

    // The structure of a Data Matrix symbol: its size, and the finder pattern of each data region (a solid left side and
    // bottom side, alternating modules on the top side and the right side)
    private static void CheckDataMatrix(Barcode2D barcode, DataMatrixOptions? options)
    {
        var index = Array.FindIndex(DataMatrixSizes, size => size.Height == barcode.Height && size.Width == barcode.Width);
        FuzzAssert.True(index >= 0, $"{barcode.Height} x {barcode.Width} is not a Data Matrix size");
        var (height, width, regionHeight, regionWidth) = DataMatrixSizes[index];
        if (options?.Height is { } expectedHeight)
        {
            FuzzAssert.Equal(expectedHeight, height, "The height of the symbol");
        }

        if (options?.Width is { } expectedWidth)
        {
            FuzzAssert.Equal(expectedWidth, width, "The width of the symbol");
        }

        if (options is not null && options.Height is null && options.Width is null)
        {
            FuzzAssert.True(options.Shape switch { DataMatrixShape.Square => height == width, DataMatrixShape.Rectangle => height != width, _ => true }, $"A {height} x {width} symbol does not have the shape {options.Shape}");
        }

        for (var top = 0; top < height; top += regionHeight + 2)
        {
            for (var left = 0; left < width; left += regionWidth + 2)
            {
                var bottom = top + regionHeight + 1;
                var right = left + regionWidth + 1;
                for (var column = left; column <= right; column++)
                {
                    FuzzAssert.True(barcode[bottom, column], $"The module ({column}, {bottom}) of the bottom side of a region is light");
                    FuzzAssert.Equal((column - left) % 2 == 0, barcode[top, column], $"The module ({column}, {top}) of the top side of a region");
                }

                for (var row = top; row <= bottom; row++)
                {
                    FuzzAssert.True(barcode[row, left], $"The module ({left}, {row}) of the left side of a region is light");
                    FuzzAssert.Equal((row - top) % 2 == 1, barcode[row, right], $"The module ({right}, {row}) of the right side of a region");
                }
            }
        }
    }

    // The symbol decodes to the data: the bytes, the ASCII text, or the UTF-8 text after the ECI designator of UTF-8; the
    // element strings of GS1 data after a FNC1, with FNC1 separators between some of them
    private static void CheckDataMatrixContent(Barcode2D barcode, List<Gs1Element>? gs1Elements, byte[]? bytes, string text)
    {
        var (message, eci) = DataMatrixDecoder.Decode(barcode);
        if (gs1Elements is not null)
        {
            FuzzAssert.True(eci is null, "GS1 data has an ECI designator");
            FuzzAssert.True(message.Count > 0 && message[0] == DataMatrixDecoder.Fnc1, "GS1 data does not start with FNC1");
            FuzzAssert.True(message[^1] != DataMatrixDecoder.Fnc1, "GS1 data ends with FNC1");
            FuzzAssert.True(message.Count(value => value == DataMatrixDecoder.Fnc1) <= gs1Elements.Count, "GS1 data has more FNC1 separators than elements");
            var elementStrings = string.Concat(gs1Elements.Select(element => element.ApplicationIdentifier + element.Value));
            FuzzAssert.Equal(elementStrings, string.Concat(message.Where(value => value != DataMatrixDecoder.Fnc1).Select(value => (char)value)), "The element strings");
            return;
        }

        FuzzAssert.False(message.Contains(DataMatrixDecoder.Fnc1), "The data has a FNC1");
        var (expected, expectedEci) = bytes is not null ? (bytes, (int?)null) : Ascii.IsValid(text) ? (Encoding.ASCII.GetBytes(text), null) : (Encoding.UTF8.GetBytes(text), 26);
        FuzzAssert.Equal(expectedEci, eci, "The ECI assignment number");
        FuzzAssert.True(message.SequenceEqual(expected.Select(value => (int)value)), $"The symbol decodes to {Convert.ToHexString([.. message.Select(value => (byte)value)])} instead of {Convert.ToHexString(expected)}");
    }

    // The structure of a PDF417 symbol: its size, start and stop patterns, rows, and the clusters of the codewords
    private static void CheckPdf417(Barcode2D barcode, Pdf417Options? options)
    {
        const string StartPattern = "11111111010101000";
        const string StopPattern = "111111101000101001";

        FuzzAssert.Equal(1, barcode.Width % 17, "The width is 17 modules per column, plus the final bar");
        var columns = ((barcode.Width - 1) / 17) - 4;
        FuzzAssert.True(columns is >= 1 and <= 30, $"{columns} data columns");
        if (options?.Columns is { } expectedColumns)
        {
            FuzzAssert.Equal(expectedColumns, columns, "The number of columns of the options");
        }

        var rowHeight = options?.RowHeight ?? 3;
        FuzzAssert.Equal(0, barcode.Height % rowHeight, "The height is a multiple of the row height");
        var rows = barcode.Height / rowHeight;
        FuzzAssert.True(rows is >= 3 and <= 90, $"{rows} rows");
        FuzzAssert.True(rows * columns <= 928, $"{rows} rows of {columns} columns hold more than 928 codewords");

        Span<int> widths = stackalloc int[8];
        for (var row = 0; row < barcode.Height; row++)
        {
            var symbolRow = row / rowHeight;
            for (var column = 0; column < barcode.Width; column++)
            {
                if (barcode[row, column] != barcode[symbolRow * rowHeight, column])
                    FuzzAssert.Fail($"The row {row} differs from the first row of its group at column {column}");
            }

            for (var i = 0; i < StartPattern.Length; i++)
            {
                FuzzAssert.Equal(StartPattern[i] == '1', barcode[row, i], $"The start pattern of the row {row}");
            }

            for (var i = 0; i < StopPattern.Length; i++)
            {
                FuzzAssert.Equal(StopPattern[i] == '1', barcode[row, barcode.Width - StopPattern.Length + i], $"The stop pattern of the row {row}");
            }

            // The row indicators and the data codewords: 4 bars and 4 spaces of 1 to 6 modules, in the cluster of the row
            for (var codeword = 0; codeword < columns + 2; codeword++)
            {
                var start = 17 * (codeword + 1);
                widths.Clear();
                var count = 0;
                for (var i = 0; i < 17; i++)
                {
                    if (i > 0 && barcode[row, start + i] != barcode[row, start + i - 1])
                    {
                        count++;
                        if (count >= 8)
                            break;
                    }

                    widths[count]++;
                }

                FuzzAssert.True(count == 7 && barcode[row, start] && !barcode[row, start + 16], $"The codeword {codeword} of the row {row} does not have 4 bars and 4 spaces");
                foreach (var width in widths)
                {
                    FuzzAssert.True(width is >= 1 and <= 6, $"The codeword {codeword} of the row {row} has an element of {width} modules");
                }

                var cluster = (widths[0] - widths[2] + widths[4] - widths[6] + 9) % 9;
                FuzzAssert.Equal(3 * (symbolRow % 3), cluster, $"The cluster of the codeword {codeword} of the row {row}");
            }
        }
    }

    // The structure of an Aztec symbol: its size, finder pattern and orientation marks, the reference grid of a full-range
    // symbol, and a mode message whose Reed-Solomon check words are valid and that gives the number of layers of the size
    // and a number of data codewords that leaves the requested error correction
    private static void CheckAztec(Barcode2D barcode, AztecOptions? options)
    {
        var size = barcode.Width;
        FuzzAssert.Equal(size, barcode.Height, "An Aztec symbol is square");
        var center = size / 2;

        // A full-range finder has 4 dark rings (distances 0, 2, 4, 6) and a light ring at distance 5; the ring at distance 5
        // of a compact symbol has the orientation marks
        var compact = !(IsRing(barcode, center, 6, dark: true) && IsRing(barcode, center, 5, dark: false));
        var radius = compact ? 5 : 7;
        for (var distance = 0; distance < radius; distance++)
        {
            FuzzAssert.True(IsRing(barcode, center, distance, dark: distance % 2 == 0), $"The ring {distance} of the finder pattern");
        }

        bool Module(int row, int column) => barcode[center + row, center + column];
        FuzzAssert.True(Module(-radius, -radius) && Module(-radius, -radius + 1) && Module(-radius + 1, -radius), "The top left orientation mark");
        FuzzAssert.True(Module(-radius, radius) && Module(-radius + 1, radius) && !Module(-radius, radius - 1), "The top right orientation mark");
        FuzzAssert.True(Module(radius - 1, radius) && !Module(radius, radius) && !Module(radius, radius - 1), "The bottom right orientation mark");
        FuzzAssert.True(!Module(radius, -radius) && !Module(radius - 1, -radius) && !Module(radius, -radius + 1), "The bottom left orientation mark");

        int layers;
        if (compact)
        {
            FuzzAssert.True(size is 15 or 19 or 23 or 27, $"A compact Aztec symbol of {size} modules");
            layers = (size - 11) / 4;
            FuzzAssert.True(options?.Compact is not false, "A compact symbol when full-range is requested");
        }
        else
        {
            layers = 0;
            for (var i = 1; i <= 32; i++)
            {
                var baseSize = 14 + (4 * i);
                if (baseSize + 1 + (2 * (((baseSize / 2) - 1) / 15)) == size)
                {
                    layers = i;
                }
            }

            FuzzAssert.True(layers > 0, $"A full-range Aztec symbol of {size} modules");
            FuzzAssert.True(options?.Compact is not true, "A full-range symbol when compact is requested");
            FuzzAssert.True(layers >= 4 || options?.Compact is false || options?.Layers is not null, "A full-range symbol of 1 to 3 layers holds less than the compact symbol of the same size");

            // The reference grid: every 16th row and column from the center alternates dark and light modules
            for (var offset = 0; offset <= center; offset += 16)
            {
                foreach (var line in new[] { center - offset, center + offset })
                {
                    for (var i = 0; i < size; i++)
                    {
                        var expected = (i - center) % 2 == 0;
                        FuzzAssert.True(barcode[line, i] == expected && barcode[i, line] == expected, $"The reference grid line {line - center}");
                    }
                }
            }
        }

        if (options?.Layers is { } requestedLayers)
        {
            FuzzAssert.Equal(requestedLayers, layers, "The number of layers");
        }

        // The mode message, clockwise from the top left corner of the finder, skipping the reference grid
        var modeMessage = new int[compact ? 7 : 10];
        var bitIndex = 0;
        void AddBit(bool bit)
        {
            modeMessage[bitIndex / 4] = (modeMessage[bitIndex / 4] << 1) | (bit ? 1 : 0);
            bitIndex++;
        }

        var sideLength = compact ? 7 : 10;
        var positions = new int[sideLength];
        for (var i = 0; i < sideLength; i++)
        {
            positions[i] = compact ? i - 3 : i - 5 + (i / 5);
        }

        for (var i = 0; i < sideLength; i++)
        {
            AddBit(Module(-radius, positions[i]));
        }

        for (var i = 0; i < sideLength; i++)
        {
            AddBit(Module(positions[i], radius));
        }

        for (var i = sideLength - 1; i >= 0; i--)
        {
            AddBit(Module(radius, positions[i]));
        }

        for (var i = sideLength - 1; i >= 0; i--)
        {
            AddBit(Module(positions[i], -radius));
        }

        FuzzAssert.True(HasValidGf16Syndromes(modeMessage, compact ? 5 : 6), "The check words of the mode message");
        var info = compact ? (modeMessage[0] << 4) | modeMessage[1] : (modeMessage[0] << 12) | (modeMessage[1] << 8) | (modeMessage[2] << 4) | modeMessage[3];
        var messageLayers = (compact ? info >> 6 : info >> 11) + 1;
        var dataWords = (compact ? info & 0x3F : info & 0x7FF) + 1;
        FuzzAssert.Equal(layers, messageLayers, "The number of layers in the mode message");

        var wordSize = layers switch
        {
            <= 2 => 6,
            <= 8 => 8,
            <= 22 => 10,
            _ => 12,
        };
        var totalWords = ((compact ? 88 : 112) + (16 * layers)) * layers / wordSize;
        var percent = options?.ErrorCorrectionPercent ?? 23;
        FuzzAssert.True(dataWords + ((totalWords * percent) + 99) / 100 + 3 <= totalWords, $"{dataWords} data codewords of {totalWords} leave {percent}% and 3 codewords of error correction");
    }

    private static bool IsRing(Barcode2D barcode, int center, int distance, bool dark)
    {
        for (var i = -distance; i <= distance; i++)
        {
            if (barcode[center - distance, center + i] != dark || barcode[center + distance, center + i] != dark || barcode[center + i, center - distance] != dark || barcode[center + i, center + distance] != dark)
                return false;
        }

        return true;
    }

    // The values of the codeword (highest degree first) at α^1 to α^checkWords in GF(16), x^4 + x + 1, are 0
    private static bool HasValidGf16Syndromes(int[] codeword, int checkWords)
    {
        var exp = new int[30];
        var value = 1;
        for (var i = 0; i < exp.Length; i++)
        {
            exp[i] = value;
            value <<= 1;
            if (value >= 16)
            {
                value ^= 0x13;
            }
        }

        var log = new int[16];
        for (var i = 0; i < 15; i++)
        {
            log[exp[i]] = i;
        }

        for (var root = 1; root <= checkWords; root++)
        {
            var syndrome = 0;
            foreach (var coefficient in codeword)
            {
                syndrome = (syndrome is 0 ? 0 : exp[(log[syndrome] + root) % 15]) ^ coefficient;
            }

            if (syndrome is not 0)
                return false;
        }

        return true;
    }

    private static bool SameModules(Barcode2D a, Barcode2D b)
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

    internal static void CheckSize(QRCode qr)
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

    internal static void CheckFinderPattern(QRCode qr)
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
    internal static void CheckFormatInformation(QRCode qr, ErrorCorrectionLevel requested)
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
    internal static string ReplaceLoneSurrogates(string value)
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

    internal static bool SameModules(QRCode a, QRCode b)
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
