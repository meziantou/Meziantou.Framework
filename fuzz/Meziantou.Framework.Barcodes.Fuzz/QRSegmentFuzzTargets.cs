using System.Text;
using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>
/// The segments that the caller chooses must be encoded as they are, in the smallest version that holds them, and the
/// structured append sequences must split the data across at most 16 symbols of one version that give the data back in
/// order: the data codewords of each symbol are read back by <see cref="QRDataDecoder"/>, independently of the encoder.
/// </summary>
internal static class QRSegmentFuzzTargets
{
    private static readonly Encoding ShiftJis = GetShiftJis();

    // A field is a selector byte (numeric, alphanumeric, UTF-8 text, raw bytes, Kanji, ECI) and its content, up to a NUL byte
    private static readonly (string Name, byte Selector)[] Selectors = [("N", 0), ("A", 1), ("B", 2), ("R", 3), ("K", 4), ("E", 5)];

    private static readonly string[][] SegmentSeeds =
    [
        ["A:HELLO ", "N:0123456789", "B:/item", "K:漢字"],
        ["E:26", "B:Café ", "N:20261110"],
        ["E:3", "R:é", "E:26", "B:€"],
        ["N:12345"],
        ["A:AB", "N:1234"],
        ["B:ab", "K:漢", "N:123456"],
        ["B:item ", "N:12345678901234567890", "A: OK"],
        ["A:PART LIST ", "N:" + new string('7', 120), "E:26", "B: end ✓"],
        ["N:" + new string('1', 41)],
        ["E:999999", "R:\u0001\u00FF"],
    ];

    private static readonly string[] StructuredAppendSeedTexts =
    [
        "HELLO", "Lorem ipsum dolor sit amet 0123456789 Lorem ipsum dolor sit amet 0123456789 Lorem ipsum dolor sit amet",
        "漢字のテスト 12345678901234567890 ABCDEF 漢字のテスト 漢字のテスト", "Café crème brûlée 😀 à la française, s'il vous plaît",
        "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog.", new string('7', 400), "😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("barcodes-qrcode-segments", SegmentsTarget) { Seeds = GetSegmentSeeds, Dictionary = ["\0\u0000", "\0\u0001", "\0\u0002", "\0\u0003", "\0\u0004", "\0\u0005", "0123456789", "ABC $%*+-./:", "abc", "漢字", "é", "€", "26", "3"] },
        new("barcodes-qrcode-structured-append", StructuredAppendTarget) { Seeds = GetStructuredAppendSeeds, Dictionary = ["0123456789", "HELLO WORLD", "hello", "漢字", "é", "😀", "\uFEFF", " "] },
    ];

    private static IEnumerable<FuzzSeed> GetSegmentSeeds()
    {
        for (var type = 0; type < 3; type++)
        {
            for (var index = 0; index < SegmentSeeds.Length; index++)
            {
                yield return new FuzzSeed($"segments-{type}-{index}", (byte[])[(byte)type, (byte)(type is 2 ? 1 : 0), .. EncodeFields(SegmentSeeds[index])]);
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetStructuredAppendSeeds()
    {
        for (var index = 0; index < StructuredAppendSeedTexts.Length; index++)
        {
            foreach (var (level, maxVersion, flags) in new (byte, byte, byte)[] { (1, 39, 0), (0, 0, 0), (2, 1, 1), (3, 4, 2), (1, 2, 3) })
            {
                yield return FuzzSeed.FromText($"structured-append-{index}-{level}-{maxVersion}-{flags}", [level, maxVersion, flags], StructuredAppendSeedTexts[index]);
            }
        }

        for (var index = 0; index < SegmentSeeds.Length; index++)
        {
            yield return new FuzzSeed($"structured-append-segments-{index}", (byte[])[1, 0, 4, .. EncodeFields(SegmentSeeds[index])]);
        }
    }

    private static byte[] EncodeFields(string[] fields)
    {
        var result = new List<byte>();
        foreach (var field in fields)
        {
            if (result.Count > 0)
            {
                result.Add(0);
            }

            var selector = Selectors.First(item => field.StartsWith(item.Name + ":", StringComparison.Ordinal)).Selector;
            result.Add(selector);
            result.AddRange(selector is 3 ? Encoding.Latin1.GetBytes(field[2..]) : FuzzText.Encode(field[2..]));
        }

        return [.. result];
    }

    private static void SegmentsTarget(ReadOnlySpan<byte> data)
    {
        var type = (QRCodeType)(FuzzInput.ReadByte(ref data) % 3);
        var level = (ErrorCorrectionLevel)(FuzzInput.ReadByte(ref data) % 4);
        if (!TryReadSegments(data, out var segments, out var expected))
            return;

        QRCode? Create()
        {
            try
            {
                return type switch
                {
                    QRCodeType.MicroQR => QRCode.CreateMicroQR(segments, level),
                    QRCodeType.RMQR => QRCode.CreateRMQR(segments, level),
                    _ => QRCode.Create(segments, level),
                };
            }
            catch (ArgumentException)
            {
                // No segment of data, an ECI designator in a Micro QR or rMQR code, or an error correction level the type does not have
                return null;
            }
            catch (InvalidOperationException)
            {
                // Too long, or a mode the Micro QR versions do not have
                return null;
            }
        }

        var qr = Create();
        if (qr is null)
            return;

        FuzzAssert.Equal(type, qr.Type, "The QR code type");
        FuzzAssert.True(qr.StructuredAppend is null, "A single symbol is not part of a structured append sequence");
        BarcodesFuzzTargets.CheckSize(qr);
        BarcodesFuzzTargets.CheckFinderPattern(qr);
        BarcodesFuzzTargets.CheckFormatInformation(qr, level);
        var other = Create();
        FuzzAssert.True(other is not null && BarcodesFuzzTargets.SameModules(qr, other), "Encoding the same segments twice gives different symbols");

        if (type is not QRCodeType.Standard)
            return;

        // The segments are encoded as they are, in the smallest version that holds them
        var decoded = QRDataDecoder.Decode(DataEncoder.Encode(segments, qr.Version, level, structuredAppend: null), qr.Version);
        FuzzAssert.True(decoded.StructuredAppend is null, "A single symbol has a structured append header");
        FuzzAssert.Equal(expected.Count, decoded.Segments.Count, "The number of segments");
        for (var i = 0; i < expected.Count; i++)
        {
            FuzzAssert.Equal(expected[i].Mode, decoded.Segments[i].Mode, "The mode of the segment");
            FuzzAssert.Equal(expected[i].EciAssignmentNumber, decoded.Segments[i].EciAssignmentNumber, "The ECI assignment number");
            FuzzAssert.True(expected[i].Data.AsSpan().SequenceEqual(decoded.Segments[i].Data), "The data of the segment");
        }

        if (qr.Version > 1)
        {
            var bitCount = QRSymbolFormat.GetBitCount(segments, QRCodeType.Standard, qr.Version - 1);
            FuzzAssert.True(bitCount < 0 || bitCount > QRCodeVersion.GetDataCodewords(qr.Version - 1, level) * 8, "The segments fit in a smaller version");
        }
    }

    private static void StructuredAppendTarget(ReadOnlySpan<byte> data)
    {
        var level = (ErrorCorrectionLevel)(FuzzInput.ReadByte(ref data) % 4);
        var maxVersion = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var flags = FuzzInput.ReadByte(ref data);
        var minVersion = 1 + ((flags >> 3) % maxVersion);
        var useEci = (flags & 1) is not 0;
        var kind = (flags >> 1) & 3;
        var options = new QRCodeOptions { MinimumVersion = minVersion, MaximumVersion = maxVersion };

        // The data units of the input, with the ECI designator in force for each of them
        List<(byte Value, int Eci)> expected;
        QRStructuredAppendPlan plan;
        Func<IReadOnlyList<QRCode>> create;
        string? text = null;
        try
        {
            switch (kind)
            {
                case 0 or 3:
                    text = FuzzInput.ReadText(data);
                    if (text.Length is 0)
                        return;

                    options.EciAssignmentNumber = useEci ? QRCodeOptions.Utf8EciAssignmentNumber : null;
                    expected = [];
                    plan = QRStructuredAppendEncoder.Plan(text, level, minVersion, maxVersion, options.EciAssignmentNumber);
                    var textCopy = text;
                    create = () => QRCode.CreateStructuredAppend(textCopy, level, options);
                    break;

                case 1:
                    if (data.IsEmpty)
                        return;

                    options.EciAssignmentNumber = useEci ? 3 : null;
                    var eci = useEci ? 3 : -1;
                    expected = [.. data.ToArray().Select(b => (b, eci))];
                    plan = QRStructuredAppendEncoder.Plan(useEci ? [QRCodeSegment.CreateEci(3), QRCodeSegment.CreateByte(data)] : [QRCodeSegment.CreateByte(data)], level, minVersion, maxVersion);
                    var bytes = data.ToArray();
                    create = () => QRCode.CreateStructuredAppend(bytes, level, options);
                    break;

                default:
                    if (!TryReadSegments(data, out var segments, out var expectedSegments) || !segments.Any(segment => segment.Mode is not QRCodeSegmentMode.Eci))
                        return;

                    expected = Flatten(expectedSegments);
                    plan = QRStructuredAppendEncoder.Plan(segments, level, minVersion, maxVersion);
                    create = () => QRCode.CreateStructuredAppend(segments, level, options);
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            FuzzAssert.True(ex.Message.Contains("too long", StringComparison.Ordinal), "Unexpected exception: " + ex.Message);
            return;
        }

        FuzzAssert.True(plan.Parts.Length is >= 1 and <= 16, $"A sequence of {plan.Parts.Length} symbols");
        FuzzAssert.True(plan.Version >= minVersion && plan.Version <= maxVersion, $"Version {plan.Version} is not in {minVersion}-{maxVersion}");

        // Read back every symbol: its header, then its data
        var decodedText = new StringBuilder();
        var decodedUnits = new List<(byte Value, int Eci)>();
        for (var i = 0; i < plan.Parts.Length; i++)
        {
            var decoded = QRDataDecoder.Decode(DataEncoder.Encode(plan.Parts[i], plan.Version, level, new QRCodeStructuredAppend(i, plan.Parts.Length, plan.Parity)), plan.Version);
            FuzzAssert.Equal(((int, int, int)?)(i, plan.Parts.Length, plan.Parity), decoded.StructuredAppend, "The structured append header");
            FuzzAssert.True(decoded.Segments.Any(segment => segment.Mode is not QRCodeSegmentMode.Eci), "A symbol without data");
            if (useEci && kind is not 2)
            {
                FuzzAssert.Equal(QRCodeSegmentMode.Eci, decoded.Segments[0].Mode, "Every symbol starts with the ECI designator");
            }

            var eci = -1;
            foreach (var segment in decoded.Segments)
            {
                if (segment.Mode is QRCodeSegmentMode.Eci)
                {
                    eci = segment.EciAssignmentNumber;
                    continue;
                }

                decodedUnits.AddRange(segment.Data.Select(b => (b, eci)));
                if (text is not null)
                {
                    FuzzAssert.False(useEci && segment.Mode is QRCodeSegmentMode.Kanji, "A text with an ECI designator has a Kanji segment");
                    decodedText.Append(segment.Mode switch
                    {
                        QRCodeSegmentMode.Kanji => ShiftJis.GetString(segment.Data),
                        QRCodeSegmentMode.Byte => DecodeUtf8(segment.Data),
                        _ => Encoding.ASCII.GetString(segment.Data),
                    });
                }
            }
        }

        if (text is not null)
        {
            FuzzAssert.Equal(BarcodesFuzzTargets.ReplaceLoneSurrogates(text), decodedText.ToString(), "The text of the sequence");
        }
        else
        {
            FuzzAssert.Equal(expected.Count, decodedUnits.Count, "The number of bytes of the sequence");
            for (var i = 0; i < expected.Count; i++)
            {
                FuzzAssert.Equal(expected[i], decodedUnits[i], $"The byte {i} of the sequence and its ECI designator");
            }
        }

        var parity = 0;
        foreach (var (value, _) in decodedUnits)
        {
            parity ^= value;
        }

        FuzzAssert.Equal(parity, (int)plan.Parity, "The parity is the XOR of the bytes of the data");

        // The public API gives the symbols of the plan
        var symbols = create();
        FuzzAssert.Equal(plan.Parts.Length, symbols.Count, "The number of symbols");
        for (var i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i];
            FuzzAssert.Equal((QRCodeType.Standard, plan.Version, level), (symbol.Type, symbol.Version, symbol.ErrorCorrectionLevel), "The symbol of the sequence");
            FuzzAssert.Equal((i, symbols.Count, plan.Parity), (symbol.StructuredAppend!.Index, symbol.StructuredAppend.Count, symbol.StructuredAppend.Parity), "The place of the symbol in the sequence");
            BarcodesFuzzTargets.CheckSize(symbol);
            BarcodesFuzzTargets.CheckFinderPattern(symbol);
            BarcodesFuzzTargets.CheckFormatInformation(symbol, level);
        }
    }

    // Each symbol holds whole characters: its UTF-8 bytes decode alone
    private static string DecodeUtf8(byte[] data)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            FuzzAssert.Fail("A symbol ends in the middle of a character");
            throw;
        }
    }

    private static List<(byte Value, int Eci)> Flatten(List<QRDecodedSegment> segments)
    {
        var result = new List<(byte, int)>();
        var eci = -1;
        foreach (var segment in segments)
        {
            if (segment.Mode is QRCodeSegmentMode.Eci)
            {
                eci = segment.EciAssignmentNumber;
            }
            else
            {
                result.AddRange(segment.Data.Select(b => (b, eci)));
            }
        }

        return result;
    }

    /// <summary>Reads the segments of the input, and the data that a reader gets back from each of them.</summary>
    private static bool TryReadSegments(ReadOnlySpan<byte> data, out List<QRCodeSegment> segments, out List<QRDecodedSegment> expected)
    {
        segments = [];
        expected = [];
        while (!data.IsEmpty)
        {
            var field = FuzzInput.ReadUntil(ref data, 0);
            if (field.IsEmpty)
                continue;

            var selector = field[0] % Selectors.Length;
            var content = field[1..];
            var text = FuzzInput.ReadText(content);
            try
            {
                switch (selector)
                {
                    case 0:
                        segments.Add(QRCodeSegment.CreateNumeric(text));
                        expected.Add(new QRDecodedSegment(QRCodeSegmentMode.Numeric, Encoding.ASCII.GetBytes(text), 0));
                        break;
                    case 1:
                        segments.Add(QRCodeSegment.CreateAlphanumeric(text));
                        expected.Add(new QRDecodedSegment(QRCodeSegmentMode.Alphanumeric, Encoding.ASCII.GetBytes(text), 0));
                        break;
                    case 2:
                        segments.Add(QRCodeSegment.CreateByte(text));
                        expected.Add(new QRDecodedSegment(QRCodeSegmentMode.Byte, Encoding.UTF8.GetBytes(text), 0));
                        break;
                    case 3:
                        segments.Add(QRCodeSegment.CreateByte(content));
                        expected.Add(new QRDecodedSegment(QRCodeSegmentMode.Byte, content.ToArray(), 0));
                        break;
                    case 4:
                        segments.Add(QRCodeSegment.CreateKanji(text));

                        // Kanji mode encodes the characters that decode to themselves
                        var shiftJis = ShiftJis.GetBytes(text);
                        FuzzAssert.Equal(text, ShiftJis.GetString(shiftJis), "A character of a Kanji segment does not decode to itself");
                        expected.Add(new QRDecodedSegment(QRCodeSegmentMode.Kanji, shiftJis, 0));
                        break;
                    default:
                        // Up to 6 decimal digits, any byte standing for a digit
                        var number = 0;
                        foreach (var b in content[..Math.Min(content.Length, 6)])
                        {
                            number = (number * 10) + ((b - '0' + 250) % 10);
                        }

                        segments.Add(QRCodeSegment.CreateEci(number));
                        expected.Add(new QRDecodedSegment(QRCodeSegmentMode.Eci, [], number));
                        break;
                }
            }
            catch (ArgumentException)
            {
                // Empty, or a character that the mode cannot encode
                return false;
            }
        }

        return segments.Count > 0;
    }

    private static Encoding GetShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("shift_jis", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
