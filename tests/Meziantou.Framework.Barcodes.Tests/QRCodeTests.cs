using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.SnapshotTesting;
namespace Meziantou.Framework.Barcodes.Tests;

public class QRCodeTests
{
    [Fact]
    public void ReadmeExample_Create()
    {
        #region QRCode_Create
        // Standard QR code
        var standardQr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);

        // Micro QR code (smaller, single finder pattern)
        var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

        // Rectangular Micro QR code (narrow rectangular shape)
        var rmqr = QRCode.CreateRMQR("https://www.meziantou.net", ErrorCorrectionLevel.M);

        // Format info: type, width and height in modules
        Assert.Equal((QRCodeType.Standard, 25, 25), (standardQr.Type, standardQr.Width, standardQr.Height));
        Assert.Equal((QRCodeType.MicroQR, 11, 11), (microQr.Type, microQr.Width, microQr.Height));
        Assert.Equal((QRCodeType.RMQR, 43, 13), (rmqr.Type, rmqr.Width, rmqr.Height));
        #endregion
    }

    [Fact]
    public void Documentation_GettingStarted()
    {
        #region QRCode_GettingStarted
        var qrCode = QRCode.Create("https://www.meziantou.net");

        // The bytes of a PNG file, to save with File.WriteAllBytes("qrcode.png", png)
        var png = qrCode.ToPng(new QRCodePngOptions { ModuleSize = 4 });
        #endregion

        DocumentationImages.AssertPng("GettingStartedQRCode", png);
    }

    [Fact]
    public void Documentation_ErrorCorrectionLevel()
    {
        #region QRCode_ErrorCorrectionLevel
        var low = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.L);
        var high = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.H);

        // More redundancy takes more modules: the version is the smallest one that holds the data
        Assert.Equal((2, 25), (low.Version, low.Width));
        Assert.Equal((4, 33), (high.Version, high.Width));

        var lowPng = low.ToPng(new QRCodePngOptions { ModuleSize = 4 });
        var highPng = high.ToPng(new QRCodePngOptions { ModuleSize = 4 });
        #endregion

        DocumentationImages.AssertPng("ErrorCorrectionLow", lowPng);
        DocumentationImages.AssertPng("ErrorCorrectionHigh", highPng);
    }

    [Fact]
    public void Documentation_EncodingModes()
    {
        #region QRCode_EncodingModes
        // Uppercase letters, digits and " $%*+-./:" take fewer bits than other characters
        Assert.Equal(2, QRCode.Create("HTTPS://WWW.EXAMPLE.COM/BLOG/QRCODE").Version);
        Assert.Equal(3, QRCode.Create("https://www.example.com/blog/qrcode").Version);

        // Digits alone take even fewer
        Assert.Equal(1, QRCode.Create("0123456789012345678901234567890123").Version);

        // Any other text is encoded as UTF-8, and binary data as it is
        Assert.Equal(1, QRCode.Create("Caf\u00E9").Version);
        Assert.Equal(1, QRCode.Create([0x00, 0x01, 0xFE, 0xFF]).Version);

        // A text that mixes kinds of characters is split into segments of different modes when it gives a smaller code:
        // the lowercase letters in byte mode, the order number in numeric mode
        Assert.Equal(1, QRCode.Create("order 1234567890123456").Version);
        Assert.Equal(2, QRCode.Create(Encoding.UTF8.GetBytes("order 1234567890123456")).Version);
        #endregion
    }

    [Fact]
    public void Documentation_Modules()
    {
        #region QRCode_Modules
        var qrCode = QRCode.Create("HELLO");

        // The modules, row by row: true for a dark module
        var text = new StringBuilder();
        for (var row = 0; row < qrCode.Height; row++)
        {
            for (var column = 0; column < qrCode.Width; column++)
            {
                text.Append(qrCode[row, column] ? '#' : '.');
            }

            text.Append('\n');
        }

        Assert.StartsWith("#######.", text.ToString()); // the top-left finder pattern, then its separator
        #endregion
    }

    [Fact]
    public void Documentation_SmallSymbols()
    {
        #region QRCode_SmallSymbols
        var microQr = QRCode.CreateMicroQR("12345");
        var rmqr = QRCode.CreateRMQR("HELLO WORLD");

        Assert.Equal((1, 11, 11), (microQr.Version, microQr.Width, microQr.Height));
        Assert.Equal((27, 13), (rmqr.Width, rmqr.Height));

        var microPng = microQr.ToPng(new QRCodePngOptions { ModuleSize = 6, QuietZoneModules = 2 });
        var rmqrPng = rmqr.ToPng(new QRCodePngOptions { ModuleSize = 6, QuietZoneModules = 2 });
        #endregion

        DocumentationImages.AssertPng("MicroQR", microPng);
        DocumentationImages.AssertPng("RectangularMicroQR", rmqrPng);
    }

    // ───── Numeric mode ─────

    [Fact]
    public void Create_Numeric_1Digit()
    {
        var qr = QRCode.Create("7", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Numeric_2Digits()
    {
        var qr = QRCode.Create("42", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Numeric_3Digits()
    {
        var qr = QRCode.Create("123", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Numeric_8Digits()
    {
        var qr = QRCode.Create("01234567", ErrorCorrectionLevel.M);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Numeric_Long_Version3()
    {
        var qr = QRCode.Create(new string('1', 100), ErrorCorrectionLevel.L);

        Assert.Equal(3, qr.Version);
        Assert.Equal(29, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Alphanumeric mode ─────

    [Fact]
    public void Create_Alphanumeric_1Char()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Alphanumeric_3Chars()
    {
        var qr = QRCode.Create("ABC", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Alphanumeric_SpecialChars()
    {
        var qr = QRCode.Create("$%*+-./:", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Alphanumeric_HelloWorld()
    {
        var qr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Alphanumeric_ECLevel_Q()
    {
        var qr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.Q);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Byte mode ─────

    [Fact]
    public void Create_Byte_Lowercase()
    {
        var qr = QRCode.Create("hello world", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_Url()
    {
        var qr = QRCode.Create("https://example.com", ErrorCorrectionLevel.M);

        Assert.Equal(2, qr.Version);
        Assert.Equal(25, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_Version7Transition_ErrorCorrectionQ()
    {
        var qr = QRCode.Create("fserwrjthwekjfghredjkgdrjkgfdjkhghfhjkdsghhjsdfghjsdfghkkkkkkkkkkkkkkkklkia", ErrorCorrectionLevel.Q);

        Assert.Equal(7, qr.Version);
        Assert.Equal(45, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_UTF8_Accented()
    {
        var qr = QRCode.Create("café", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_UTF8_Emoji()
    {
        var qr = QRCode.Create("\U0001F600", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_Binary_WithSnapshot()
    {
        var qr = QRCode.Create(new byte[] { 0x00, 0xFF, 0x48, 0x65 }, ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Kanji mode ─────

    [Fact]
    public void Create_Kanji_SingleCharacter()
    {
        var qr = QRCode.Create("漢", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Kanji_MultipleCharacters()
    {
        var qr = QRCode.Create("漢字", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Kanji_Katakana()
    {
        var qr = QRCode.Create("アイウ", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // Pairs of characters that share a Shift JIS code: a best-fit mapping of the encoder, or a code that the JIS X 0208
    // table and code page 932 decode differently. Both are encoded in byte mode, so that every reader gives them back.
    [Theory]
    [InlineData("\u00A2", "\uFFE0")]
    [InlineData("\u00A3", "\uFFE1")]
    [InlineData("\u00AC", "\uFFE2")]
    [InlineData("\u301C", "\uFF5E")]
    [InlineData("\u2016", "\u2225")]
    [InlineData("\u2212", "\uFF0D")]
    [InlineData("\u2015", "\u2014")]
    [InlineData("\u00AB", "\u226A")]
    [InlineData("\u00B7", "\u30FB")]
    public void Create_Kanji_AmbiguousShiftJisCharacters_AreEncodedInByteMode(string value, string other)
    {
        Assert.True(HasSameModules(QRCode.Create(Encoding.UTF8.GetBytes(value)), QRCode.Create(value)));
        Assert.False(HasSameModules(QRCode.Create(value), QRCode.Create(other)));
        Assert.False(HasSameModules(QRCode.CreateMicroQR(value), QRCode.CreateMicroQR(other)));
        Assert.False(HasSameModules(QRCode.CreateRMQR(value), QRCode.CreateRMQR(other)));
    }

    [Theory]
    [InlineData("\u226A")] // ≪, the target of the best-fit mapping of «
    [InlineData("\u30FB")] // ・, the target of the best-fit mapping of ·
    [InlineData("\uFF3C")] // ＼, 0x815F in code page 932
    [InlineData("\u2460")] // ①, an NEC special character, not in JIS X 0208
    [InlineData("\u7E8A")] // 纊, an IBM extension, not in JIS X 0208
    public void Create_Kanji_DetectsTheCharactersEveryReaderDecodes(string value)
    {
        var isKanji = value is "\u226A" or "\u30FB";
        Assert.Equal(!isKanji, HasSameModules(QRCode.Create(Encoding.UTF8.GetBytes(value)), QRCode.Create(value)));
    }

    private static bool HasSameModules(QRCode first, QRCode second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
            return false;

        for (var row = 0; row < first.Height; row++)
        {
            for (var column = 0; column < first.Width; column++)
            {
                if (first[row, column] != second[row, column])
                    return false;
            }
        }

        return true;
    }

    // ───── Mixed modes ─────

    [Fact]
    public void Create_MixedModes_EncodesEachRunInItsOwnMode()
    {
        // Byte mode for the whole text needs version 11: a byte segment and a numeric segment fit in version 6
        var qr = QRCode.Create("a" + new string('1', 300), ErrorCorrectionLevel.L);

        Assert.Equal((6, 41), (qr.Version, qr.Size));
    }

    [Fact]
    public void Create_MixedModes_AtMaximumCapacity()
    {
        // Version 40-L holds 23,648 data bits: 28 for the byte segment, 18 + 23,600 for 7,080 digits
        Assert.Equal(40, QRCode.Create("a" + new string('7', 7080), ErrorCorrectionLevel.L).Version);
        Assert.Throws<InvalidOperationException>(() => QRCode.Create("a" + new string('7', 7081), ErrorCorrectionLevel.L));
    }

    [Fact]
    public void Create_MixedModes_NonAsciiText_UsesASingleMode()
    {
        // Readers guess the encoding of each byte segment, and could take a short UTF-8 segment for another encoding
        var data = "\u00E9" + new string('1', 300);

        Assert.True(HasSameModules(QRCode.Create(Encoding.UTF8.GetBytes(data), ErrorCorrectionLevel.L), QRCode.Create(data, ErrorCorrectionLevel.L)));
    }

    [Theory]
    [InlineData("HELLO WORLD")]
    [InlineData("hello world")]
    [InlineData("0123456789")]
    [InlineData("漢字のテスト")]
    [InlineData("https://www.meziantou.net/")]
    public void Create_SingleMode_WhenSegmentsAreNotSmaller(string data)
    {
        // The whole text in byte mode, as the binary overload encodes it, unless a single other mode is smaller
        var bytes = QRCode.Create(Encoding.UTF8.GetBytes(data));
        var text = QRCode.Create(data);
        Assert.Equal(data is "hello world" or "https://www.meziantou.net/", HasSameModules(bytes, text));
    }

    // ───── ECI ─────

    [Fact]
    public void Documentation_Eci()
    {
        #region QRCode_Eci
        // Declare the text as UTF-8 (ECI assignment number 26), so that readers do not guess its encoding
        var options = new QRCodeOptions { EciAssignmentNumber = QRCodeOptions.Utf8EciAssignmentNumber };
        var qrCode = QRCode.Create("Caf\u00E9 cr\u00E8me", ErrorCorrectionLevel.M, options);

        // Binary data can declare any encoding, such as ISO-8859-1 (ECI assignment number 3)
        var latin1 = QRCode.Create([0x63, 0x61, 0x66, 0xE9], ErrorCorrectionLevel.M, new QRCodeOptions { EciAssignmentNumber = 3 });
        #endregion

        Assert.Equal((1, 1), (qrCode.Version, latin1.Version));
    }

    [Fact]
    public void Create_Eci_IsOptIn()
    {
        var utf8 = new QRCodeOptions { EciAssignmentNumber = QRCodeOptions.Utf8EciAssignmentNumber };

        Assert.True(HasSameModules(QRCode.Create("\u00E9"), QRCode.Create("\u00E9", ErrorCorrectionLevel.M, options: null)));
        Assert.True(HasSameModules(QRCode.Create("\u00E9"), QRCode.Create("\u00E9", ErrorCorrectionLevel.M, new QRCodeOptions())));
        Assert.True(HasSameModules(QRCode.Create(Encoding.UTF8.GetBytes("\u00E9")), QRCode.Create("\u00E9")));
        Assert.False(HasSameModules(QRCode.Create("\u00E9"), QRCode.Create("\u00E9", ErrorCorrectionLevel.M, utf8)));
        Assert.True(HasSameModules(QRCode.Create(Encoding.UTF8.GetBytes("h\u00E9llo"), ErrorCorrectionLevel.M, utf8), QRCode.Create("h\u00E9llo", ErrorCorrectionLevel.M, utf8)));
    }

    [Fact]
    public void Create_Eci_KanjiAreEncodedAsUtf8()
    {
        var utf8 = new QRCodeOptions { EciAssignmentNumber = QRCodeOptions.Utf8EciAssignmentNumber };

        Assert.True(HasSameModules(QRCode.Create(Encoding.UTF8.GetBytes("漢字"), ErrorCorrectionLevel.M, utf8), QRCode.Create("漢字", ErrorCorrectionLevel.M, utf8)));
    }

    [Fact]
    public void Create_Eci_HeaderIsCountedInTheCapacity()
    {
        // Version 40-L holds 2,953 bytes, or 2,952 after the 12 bits of an ECI header
        var options = new QRCodeOptions { EciAssignmentNumber = 3 };

        Assert.Equal(40, QRCode.Create(new byte[2953], ErrorCorrectionLevel.L).Version);
        Assert.Equal(40, QRCode.Create(new byte[2952], ErrorCorrectionLevel.L, options).Version);
        Assert.Throws<InvalidOperationException>(() => QRCode.Create(new byte[2953], ErrorCorrectionLevel.L, options));
        Assert.Throws<InvalidOperationException>(() => QRCode.Create(new string('x', 2953), ErrorCorrectionLevel.L, new QRCodeOptions { EciAssignmentNumber = 26 }));

        // Version 1-L holds 17 bytes, or 16 after the ECI header
        Assert.Equal(1, QRCode.Create(new byte[16], ErrorCorrectionLevel.L, options).Version);
        Assert.Equal(2, QRCode.Create(new byte[17], ErrorCorrectionLevel.L, options).Version);
    }

    [Fact]
    public void Create_Eci_TextAcceptsOnlyUtf8()
    {
        Assert.Throws<ArgumentException>("options", () => QRCode.Create("abc", ErrorCorrectionLevel.M, new QRCodeOptions { EciAssignmentNumber = 3 }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000)]
    public void QRCodeOptions_EciAssignmentNumberOutOfRange_Throws(int value)
    {
        var options = new QRCodeOptions();

        Assert.Throws<ArgumentOutOfRangeException>(nameof(value), () => options.EciAssignmentNumber = value);
    }

    // ───── Segments ─────

    [Fact]
    public void Documentation_Segments()
    {
        #region QRCode_Segments
        // An ISO-8859-1 text followed by a long number: the text in byte mode, after an ECI designator that declares its
        // encoding (assignment number 3), and the digits in numeric mode
        QRCodeSegment[] segments =
        [
            QRCodeSegment.CreateEci(3),
            QRCodeSegment.CreateByte(Encoding.Latin1.GetBytes("Facture n\u00B0")),
            QRCodeSegment.CreateNumeric("12345678901234567890123456789012345"),
        ];
        var qrCode = QRCode.Create(segments, ErrorCorrectionLevel.M);

        // The same bytes in a single byte segment need a larger symbol
        var bytes = Encoding.Latin1.GetBytes("Facture n\u00B012345678901234567890123456789012345");
        var singleSegment = QRCode.Create(bytes, ErrorCorrectionLevel.M, new QRCodeOptions { EciAssignmentNumber = 3 });
        Assert.Equal((3, 4), (qrCode.Version, singleSegment.Version));
        #endregion
    }

    [Fact]
    public void CreateSegments_SameSymbolAsTheOtherOverloads()
    {
        var utf8 = new QRCodeOptions { EciAssignmentNumber = QRCodeOptions.Utf8EciAssignmentNumber };

        Assert.True(HasSameModules(QRCode.Create("HELLO WORLD"), QRCode.Create([QRCodeSegment.CreateAlphanumeric("HELLO WORLD")])));
        Assert.True(HasSameModules(QRCode.Create("0123456789"), QRCode.Create([QRCodeSegment.CreateNumeric("0123456789")])));
        Assert.True(HasSameModules(QRCode.Create("漢字のテスト"), QRCode.Create([QRCodeSegment.CreateKanji("漢字のテスト")])));
        Assert.True(HasSameModules(QRCode.Create("h\u00E9llo"), QRCode.Create([QRCodeSegment.CreateByte("h\u00E9llo")])));
        Assert.True(HasSameModules(QRCode.Create([1, 2, 255]), QRCode.Create([QRCodeSegment.CreateByte([1, 2, 255])])));
        Assert.True(HasSameModules(QRCode.Create("h\u00E9llo", ErrorCorrectionLevel.Q, utf8), QRCode.Create([QRCodeSegment.CreateEci(QRCodeOptions.Utf8EciAssignmentNumber), QRCodeSegment.CreateByte("h\u00E9llo")], ErrorCorrectionLevel.Q)));
        Assert.True(HasSameModules(QRCode.Create([QRCodeSegment.CreateEci(26), QRCodeSegment.CreateByte("h\u00E9llo")]), QRCode.Create([QRCodeSegment.CreateByte("h\u00E9llo")], ErrorCorrectionLevel.M, utf8)));
        Assert.True(HasSameModules(QRCode.Create("order 1234567890123456"), QRCode.Create([QRCodeSegment.CreateByte("order "), QRCodeSegment.CreateNumeric("1234567890123456")])));
    }

    [Fact]
    public void CreateSegments_AreEncodedAsTheyAre()
    {
        // Version 1-L holds 152 bits: 41 digits in one numeric segment (4 + 10 + 137 bits), not in two (2 × 14 + 137 bits),
        // nor in byte mode (4 + 8 + 328 bits)
        Assert.Equal(1, QRCode.Create([QRCodeSegment.CreateNumeric(new string('1', 41))], ErrorCorrectionLevel.L).Version);
        Assert.Equal(2, QRCode.Create([QRCodeSegment.CreateNumeric(new string('1', 20)), QRCodeSegment.CreateNumeric(new string('1', 21))], ErrorCorrectionLevel.L).Version);
        Assert.Equal(3, QRCode.Create([QRCodeSegment.CreateByte(new string('1', 41))], ErrorCorrectionLevel.L).Version);
    }

    [Fact]
    public void CreateSegments_VersionRange()
    {
        Assert.Equal(7, QRCode.Create([QRCodeSegment.CreateNumeric("1")], ErrorCorrectionLevel.M, new QRCodeOptions { MinimumVersion = 7 }).Version);
        Assert.Throws<InvalidOperationException>(() => QRCode.Create([QRCodeSegment.CreateByte(new byte[100])], ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 3 }));
        Assert.Throws<InvalidOperationException>(() => QRCode.Create([QRCodeSegment.CreateByte(new byte[3000])], ErrorCorrectionLevel.L));
        Assert.Throws<ArgumentException>("options", () => QRCode.Create([QRCodeSegment.CreateNumeric("1")], ErrorCorrectionLevel.M, new QRCodeOptions { MinimumVersion = 3, MaximumVersion = 2 }));
    }

    [Fact]
    public void CreateSegments_InvalidSegments_Throw()
    {
        Assert.Throws<ArgumentNullException>("segments", () => QRCode.Create((IEnumerable<QRCodeSegment>)null!));
        Assert.Throws<ArgumentException>("segments", () => QRCode.Create(Array.Empty<QRCodeSegment>()));
        Assert.Throws<ArgumentException>("segments", () => QRCode.Create([QRCodeSegment.CreateEci(26)]));
        Assert.Throws<ArgumentException>("segments", () => QRCode.Create([QRCodeSegment.CreateNumeric("1"), null!]));
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.Create([QRCodeSegment.CreateNumeric("1")], (ErrorCorrectionLevel)42));
    }

    [Fact]
    public void QRCodeSegment_Mode()
    {
        Assert.Equal(QRCodeSegmentMode.Numeric, QRCodeSegment.CreateNumeric("0123456789").Mode);
        Assert.Equal(QRCodeSegmentMode.Alphanumeric, QRCodeSegment.CreateAlphanumeric("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:").Mode);
        Assert.Equal(QRCodeSegmentMode.Byte, QRCodeSegment.CreateByte("any text \u00E9 😀").Mode);
        Assert.Equal(QRCodeSegmentMode.Byte, QRCodeSegment.CreateByte([0]).Mode);
        Assert.Equal(QRCodeSegmentMode.Kanji, QRCodeSegment.CreateKanji("漢字ひらがなカタカナ").Mode);
        Assert.Equal(QRCodeSegmentMode.Eci, QRCodeSegment.CreateEci(999999).Mode);
    }

    [Theory]
    [InlineData("12a")]
    [InlineData("１２")]
    [InlineData("-1")]
    public void QRCodeSegment_CreateNumeric_InvalidCharacter_Throws(string value)
    {
        Assert.Throws<ArgumentException>("digits", () => QRCodeSegment.CreateNumeric(value));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("A_B")]
    [InlineData("\u00C9")]
    public void QRCodeSegment_CreateAlphanumeric_InvalidCharacter_Throws(string value)
    {
        Assert.Throws<ArgumentException>("text", () => QRCodeSegment.CreateAlphanumeric(value));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("漢a")]
    [InlineData("ｱ")]
    [InlineData("\uFF5E")]
    [InlineData("\u00A2")]
    public void QRCodeSegment_CreateKanji_InvalidCharacter_Throws(string value)
    {
        Assert.Throws<ArgumentException>("text", () => QRCodeSegment.CreateKanji(value));
    }

    [Fact]
    public void QRCodeSegment_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("digits", () => QRCodeSegment.CreateNumeric(null!));
        Assert.Throws<ArgumentException>("digits", () => QRCodeSegment.CreateNumeric(""));
        Assert.Throws<ArgumentNullException>("text", () => QRCodeSegment.CreateAlphanumeric(null!));
        Assert.Throws<ArgumentException>("text", () => QRCodeSegment.CreateAlphanumeric(""));
        Assert.Throws<ArgumentNullException>("text", () => QRCodeSegment.CreateByte((string)null!));
        Assert.Throws<ArgumentException>("text", () => QRCodeSegment.CreateByte(""));
        Assert.Throws<ArgumentException>("data", () => QRCodeSegment.CreateByte(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentNullException>("text", () => QRCodeSegment.CreateKanji(null!));
        Assert.Throws<ArgumentException>("text", () => QRCodeSegment.CreateKanji(""));
        Assert.Throws<ArgumentOutOfRangeException>("assignmentNumber", () => QRCodeSegment.CreateEci(-1));
        Assert.Throws<ArgumentOutOfRangeException>("assignmentNumber", () => QRCodeSegment.CreateEci(1_000_000));
    }

    [Fact]
    public void QRCodeSegment_CreateByte_CopiesTheData()
    {
        var data = new byte[] { 1, 2, 3 };
        var segment = QRCodeSegment.CreateByte(data);
        var expected = QRCode.Create([segment]);
        data[0] = 42;

        Assert.True(HasSameModules(expected, QRCode.Create([segment])));
    }

    [Fact]
    public void CreateSegments_Snapshot()
    {
        // Large modules and a quiet zone, so that the snapshot can be scanned
        var qr = QRCode.Create([QRCodeSegment.CreateAlphanumeric("ORDER "), QRCodeSegment.CreateNumeric("20261110123456"), QRCodeSegment.CreateByte(" ok")], ErrorCorrectionLevel.M);

        Snapshot.Validate(qr.ToPng(new QRCodePngOptions { ModuleSize = 8 }), SnapshotType.Png);
    }

    // ───── Structured append ─────

    [Fact]
    public void Documentation_StructuredAppend()
    {
        #region QRCode_StructuredAppend
        var data = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 30));

        // Symbols of version 10 at most (57 × 57 modules), instead of one symbol of version 30 (137 × 137 modules)
        var symbols = QRCode.CreateStructuredAppend(data, ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 10 });
        Assert.Equal(7, symbols.Count);
        Assert.Equal((10, 30), (symbols[0].Version, QRCode.Create(data, ErrorCorrectionLevel.M).Version));

        // Each symbol knows its position in the sequence
        Assert.Equal((0, 7), (symbols[0].StructuredAppend!.Index, symbols[0].StructuredAppend!.Count));
        Assert.Equal((6, 7), (symbols[6].StructuredAppend!.Index, symbols[6].StructuredAppend!.Count));

        // Print the symbols in order, side by side
        var images = symbols.Select(symbol => symbol.ToPng(new QRCodePngOptions { ModuleSize = 4 })).ToList();
        #endregion

        Assert.HasCount(7, images);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CreateStructuredAppend_Snapshot(int index)
    {
        // One snapshot per symbol of a sequence of 3, with large modules and a quiet zone, so that each one can be scanned
        var symbols = QRCode.CreateStructuredAppend("Structured append splits this text across three QR codes.", ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 2 });

        Assert.HasCount(3, symbols);
        Snapshot.Validate(symbols[index].ToPng(new QRCodePngOptions { ModuleSize = 8 }), SnapshotType.Png);
    }

    [Fact]
    public void CreateStructuredAppend_SmallData_GivesOneSymbol()
    {
        var symbols = QRCode.CreateStructuredAppend("HELLO");

        var symbol = Assert.Single(symbols);
        Assert.Equal((1, QRCodeType.Standard, ErrorCorrectionLevel.M), (symbol.Version, symbol.Type, symbol.ErrorCorrectionLevel));
        Assert.Equal((0, 1), (symbol.StructuredAppend!.Index, symbol.StructuredAppend.Count));
        Assert.Equal("1/1", symbol.StructuredAppend.ToString());

        // 'H' ^ 'E' ^ 'L' ^ 'L' ^ 'O'
        Assert.Equal(0x42, symbol.StructuredAppend.Parity);
        Assert.False(HasSameModules(symbol, QRCode.Create("HELLO")));
    }

    [Fact]
    public void Create_IsNotPartOfASequence()
    {
        Assert.Null(QRCode.Create("HELLO").StructuredAppend);
        Assert.Null(QRCode.CreateMicroQR("12345").StructuredAppend);
        Assert.Null(QRCode.CreateRMQR("12345").StructuredAppend);
    }

    [Fact]
    public void CreateStructuredAppend_HeaderPrecedesTheData()
    {
        // ISO/IEC 18004, 8.3: mode indicator 0011, the position and the count minus 1 on 4 bits, then the parity on 8 bits
        var codewords = DataEncoder.Encode([QRCodeSegment.CreateNumeric("1")], 1, ErrorCorrectionLevel.L, new QRCodeStructuredAppend(index: 2, count: 5, parity: 0xA7));

        Assert.Equal(0b0011_0010, codewords[0]);
        Assert.Equal(0b0100_1010, codewords[1]);
        Assert.Equal(0b0111, codewords[2] >> 4);

        // Then the numeric segment: 0001, a count of 1 on 10 bits, and the digit on 4 bits
        Assert.Equal(0b0001, codewords[2] & 0xF);
        Assert.Equal(0b0000_0000, codewords[3]);
        Assert.Equal(0b01_0001, codewords[4] >> 2);
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void CreateStructuredAppend_UsesTheFewestSymbolsThenTheSmallestVersion(ErrorCorrectionLevel ecLevel)
    {
        var data = string.Concat(Enumerable.Repeat("Lorem ipsum 0123456789 DOLOR SIT AMET ", 40));
        var options = new QRCodeOptions { MaximumVersion = 12 };

        var symbols = QRCode.CreateStructuredAppend(data, ecLevel, options);
        var version = symbols[0].Version;
        for (var i = 0; i < symbols.Count; i++)
        {
            Assert.Equal((i, symbols.Count, symbols[0].StructuredAppend!.Parity), (symbols[i].StructuredAppend!.Index, symbols[i].StructuredAppend!.Count, symbols[i].StructuredAppend!.Parity));
            Assert.Equal((version, ecLevel), (symbols[i].Version, symbols[i].ErrorCorrectionLevel));
        }

        // The largest version gives no fewer symbols, and the version below gives more
        Assert.Equal(symbols.Count, QRCode.CreateStructuredAppend(data, ecLevel, new QRCodeOptions { MinimumVersion = 12, MaximumVersion = 12 }).Count);
        Assert.HasCountGreaterThan(symbols.Count, QRCode.CreateStructuredAppend(data, ecLevel, new QRCodeOptions { MaximumVersion = version - 1 }));
    }

    [Fact]
    public void CreateStructuredAppend_ShortSegments_SmallerVersionsCanNeedFewerSymbols()
    {
        // The byte count indicator grows from 8 to 16 bits at version 10: a version 9-L symbol holds 91 segments of one byte,
        // a version 10-L symbol 77
        var options = new QRCodeOptions { MaximumVersion = 10 };

        var symbols = QRCode.CreateStructuredAppend(Enumerable.Repeat(QRCodeSegment.CreateByte("a"), 1300), ErrorCorrectionLevel.L, options);
        Assert.Equal((15, 9), (symbols.Count, symbols[0].Version));

        var single = Assert.Single(QRCode.CreateStructuredAppend(Enumerable.Repeat(QRCodeSegment.CreateByte("a"), 90), ErrorCorrectionLevel.L, options));
        Assert.Equal(9, single.Version);
    }

    [Fact]
    public void CreateStructuredAppend_MinimumVersion()
    {
        var symbols = QRCode.CreateStructuredAppend("HELLO", ErrorCorrectionLevel.M, new QRCodeOptions { MinimumVersion = 5 });

        Assert.Equal(5, Assert.Single(symbols).Version);
    }

    [Fact]
    public void CreateStructuredAppend_ParityIsTheXorOfTheBytes()
    {
        var data = new byte[1000];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 7) + 3);
        }

        var parity = 0;
        foreach (var b in data)
        {
            parity ^= b;
        }

        var symbols = QRCode.CreateStructuredAppend(data, ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 5 });
        Assert.HasCountGreaterThan(1, symbols);
        foreach (var symbol in symbols)
        {
            Assert.Equal(parity, symbol.StructuredAppend!.Parity);
        }

        // Kanji are counted as their Shift JIS bytes (漢 is 0x8ABF), the other characters as their UTF-8 bytes
        Assert.Equal(0x8A ^ 0xBF, Assert.Single(QRCode.CreateStructuredAppend("漢")).StructuredAppend!.Parity);
        Assert.Equal(0xC3 ^ 0xA9, Assert.Single(QRCode.CreateStructuredAppend("\u00E9")).StructuredAppend!.Parity);
        Assert.Equal('A' ^ '1', Assert.Single(QRCode.CreateStructuredAppend([QRCodeSegment.CreateAlphanumeric("A"), QRCodeSegment.CreateEci(26), QRCodeSegment.CreateNumeric("1")])).StructuredAppend!.Parity);
    }

    [Fact]
    public void CreateStructuredAppend_SixteenSymbolsAtMost()
    {
        // A version 1-L symbol has 19 data codewords: 15 bytes after the structured append header (20 bits) and the byte
        // segment header (12 bits)
        var options = new QRCodeOptions { MaximumVersion = 1 };

        Assert.HasCount(16, QRCode.CreateStructuredAppend(new byte[16 * 15], ErrorCorrectionLevel.L, options));
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateStructuredAppend(new byte[(16 * 15) + 1], ErrorCorrectionLevel.L, options));
        // A version 40-L symbol holds 2,951 bytes after the headers
        Assert.HasCount(16, QRCode.CreateStructuredAppend(new string('x', 16 * 2951), ErrorCorrectionLevel.L));
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateStructuredAppend(new string('x', (16 * 2951) + 1), ErrorCorrectionLevel.L));
    }

    [Fact]
    public void CreateStructuredAppend_Segments()
    {
        // The numeric segment is split across the symbols, and each symbol repeats the ECI designator in force at its start
        QRCodeSegment[] segments = [QRCodeSegment.CreateAlphanumeric("PART LIST "), QRCodeSegment.CreateNumeric(new string('7', 120)), QRCodeSegment.CreateEci(26), QRCodeSegment.CreateByte(" end \u2713")];
        var symbols = QRCode.CreateStructuredAppend(segments, ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 1 });

        Assert.HasCount(6, symbols);
        Assert.Throws<ArgumentException>("segments", () => QRCode.CreateStructuredAppend([QRCodeSegment.CreateEci(26)]));
        Assert.Throws<ArgumentNullException>("segments", () => QRCode.CreateStructuredAppend((IEnumerable<QRCodeSegment>)null!));
    }

    [Fact]
    public void CreateStructuredAppend_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("data", () => QRCode.CreateStructuredAppend((string)null!));
        Assert.Throws<ArgumentException>("data", () => QRCode.CreateStructuredAppend(""));
        Assert.Throws<ArgumentException>("data", () => QRCode.CreateStructuredAppend(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.CreateStructuredAppend("A", (ErrorCorrectionLevel)42));
        Assert.Throws<ArgumentException>("options", () => QRCode.CreateStructuredAppend("A", ErrorCorrectionLevel.M, new QRCodeOptions { EciAssignmentNumber = 3 }));
        Assert.Throws<ArgumentException>("options", () => QRCode.CreateStructuredAppend("A", ErrorCorrectionLevel.M, new QRCodeOptions { MinimumVersion = 3, MaximumVersion = 2 }));
    }

    // ───── Size constraints ─────

    [Fact]
    public void Documentation_FixedSize()
    {
        #region QRCode_FixedSize
        // Every label gets a version 4 code (33 × 33 modules), whatever the length of its data
        var exact = new QRCodeOptions { MinimumVersion = 4, MaximumVersion = 4 };
        var shortLabel = QRCode.Create("A1", ErrorCorrectionLevel.Q, exact);
        var longLabel = QRCode.Create("https://example.com/items/1234", ErrorCorrectionLevel.Q, exact);
        Assert.Equal((33, 33), (shortLabel.Size, longLabel.Size));

        // A code that must fit in the space of a version 2 code: larger data throws instead of giving a larger code
        var bounded = new QRCodeOptions { MaximumVersion = 2 };
        Assert.Throws<InvalidOperationException>(() => QRCode.Create(new string('x', 100), ErrorCorrectionLevel.M, bounded));

        // The Micro QR version, and the rMQR height and width, in modules
        var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L, new MicroQRCodeOptions { MinimumVersion = 3, MaximumVersion = 3 });
        var rmqr = QRCode.CreateRMQR("12345", ErrorCorrectionLevel.M, new RMQRCodeOptions { MaximumHeight = 7 });
        Assert.Equal((15, 15), (microQr.Width, microQr.Height));
        Assert.Equal((43, 7), (rmqr.Width, rmqr.Height));

        // The size of the image is set by the renderer: 33 modules of 10 pixels, plus a quiet zone of 4 modules on each side
        var png = shortLabel.ToPng(new QRCodePngOptions { ModuleSize = 10 });
        #endregion

        Assert.NotEmpty(png);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(27)]
    [InlineData(40)]
    public void Create_ExactVersion(int version)
    {
        var options = new QRCodeOptions { MinimumVersion = version, MaximumVersion = version };

        Assert.Equal(version, QRCode.Create("HELLO", ErrorCorrectionLevel.H, options).Version);
        Assert.Equal(version, QRCode.Create("h\u00E9llo", ErrorCorrectionLevel.H, options).Version);
        Assert.Equal(version, QRCode.Create([1, 2, 3], ErrorCorrectionLevel.H, options).Version);
    }

    [Fact]
    public void Create_VersionRange_UsesTheSmallestVersionThatHoldsTheData()
    {
        var options = new QRCodeOptions { MinimumVersion = 3, MaximumVersion = 6 };

        Assert.Equal(3, QRCode.Create("HELLO", ErrorCorrectionLevel.M, options).Version);
        Assert.Equal(5, QRCode.Create(new string('x', 70), ErrorCorrectionLevel.M, options).Version);
        Assert.Equal(5, QRCode.Create(new byte[70], ErrorCorrectionLevel.M, options).Version);
    }

    [Fact]
    public void Create_DataTooLongForTheMaximumVersion_Throws()
    {
        var options = new QRCodeOptions { MaximumVersion = 2 };

        // Version 2-M holds 26 bytes
        Assert.Equal(2, QRCode.Create(new string('x', 26), ErrorCorrectionLevel.M, options).Version);
        var exception = Assert.Throws<InvalidOperationException>(() => QRCode.Create(new string('x', 27), ErrorCorrectionLevel.M, options));
        Assert.Equal("The data is too long to be encoded in a QR code of version 1 to 2.", exception.Message);
        Assert.Throws<InvalidOperationException>(() => QRCode.Create(new byte[27], ErrorCorrectionLevel.M, options));
        Assert.Throws<InvalidOperationException>(() => QRCode.Create(new string('x', 26), ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 2, EciAssignmentNumber = 26 }));
    }

    [Fact]
    public void Create_MinimumVersionGreaterThanMaximumVersion_Throws()
    {
        var options = new QRCodeOptions { MinimumVersion = 5, MaximumVersion = 4 };

        Assert.Throws<ArgumentException>("options", () => QRCode.Create("HELLO", ErrorCorrectionLevel.M, options));
        Assert.Throws<ArgumentException>("options", () => QRCode.Create([1], ErrorCorrectionLevel.M, options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    [InlineData(-1)]
    public void QRCodeOptions_InvalidVersion_Throws(int value)
    {
        var options = new QRCodeOptions();

        Assert.Throws<ArgumentOutOfRangeException>(nameof(value), () => options.MinimumVersion = value);
        Assert.Throws<ArgumentOutOfRangeException>(nameof(value), () => options.MaximumVersion = value);
    }

    // ───── Higher versions / different sizes ─────

    [Fact]
    public void Create_Version4_ByteMode()
    {
        var qr = QRCode.Create(new string('x', 43), ErrorCorrectionLevel.M);

        Assert.Equal(4, qr.Version);
        Assert.Equal(33, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Version7Plus_HasVersionInfo()
    {
        var qr = QRCode.Create(new string('x', 123), ErrorCorrectionLevel.M);

        Assert.Equal(8, qr.Version);
        Assert.Equal(49, qr.Size);
    }

    [Fact]
    public void Create_Numeric_LongString_Version6()
    {
        var qr = QRCode.Create(new string('0', 300), ErrorCorrectionLevel.L);

        Assert.Equal(6, qr.Version);
        Assert.Equal(41, qr.Size);
    }

    // ───── EC levels ─────

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Create_AllErrorCorrectionLevels_ProducesValidQRCode(ErrorCorrectionLevel level)
    {
        var qr = QRCode.Create("HELLO WORLD", level);

        Assert.True(qr.Size > 0);
        Assert.True(qr.Version >= 1);
    }

    [Fact]
    public void Create_ErrorCorrectionLevel_H()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.H);

        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Version / size transitions ─────

    [Fact]
    public void Create_LongerData_ProducesHigherVersion()
    {
        var qrShort = QRCode.Create("A", ErrorCorrectionLevel.L);
        var qrLong = QRCode.Create(new string('A', 200), ErrorCorrectionLevel.L);

        Assert.True(qrLong.Version > qrShort.Version);
    }

    [Fact]
    public void Create_HigherECLevel_MayRequireHigherVersion()
    {
        var qrL = QRCode.Create(new string('A', 25), ErrorCorrectionLevel.L);
        var qrH = QRCode.Create(new string('A', 25), ErrorCorrectionLevel.H);

        Assert.True(qrH.Version >= qrL.Version);
    }

    public static TheoryData<int, int> NumericPayloadLengthForAllVersions => CreateNumericPayloadLengthForAllVersions();

    [Theory]
    [MemberData(nameof(NumericPayloadLengthForAllVersions))]
    public void Create_Numeric_HasOneQRCodePerVersion(int expectedVersion, int payloadLength)
    {
        var qr = QRCode.Create(new string('1', payloadLength), ErrorCorrectionLevel.L);

        Assert.Equal(expectedVersion, qr.Version);
        Assert.Equal(17 + (expectedVersion * 4), qr.Size);
    }

    // ───── Binary data coverage ─────

    [Fact]
    public void Create_Binary_LargerPayload()
    {
        var data = new byte[50];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 5);
        }

        var qr = QRCode.Create(data, ErrorCorrectionLevel.L);

        Assert.Equal(3, qr.Version);
        Assert.Equal(29, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Version group transitions (CCI bits change) ─────

    [Fact]
    public void Create_Numeric_Version10_CCIBitsChange()
    {
        // Versions 10+ use 12-bit CCI for numeric (vs 10-bit for 1-9)
        var qr = QRCode.Create(new string('1', 272), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 6);
        Assert.True(qr.Size >= 41);
    }

    [Fact]
    public void Create_Alphanumeric_Version10_CCIBitsChange()
    {
        // Versions 10+ use 11-bit CCI for alphanumeric (vs 9-bit for 1-9)
        var qr = QRCode.Create(new string('A', 175), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 6);
        Assert.True(qr.Size >= 41);
    }

    [Fact]
    public void Create_Numeric_Version27_CCIBitsChange()
    {
        // Versions 27+ use 14-bit CCI for numeric (vs 12-bit for 10-26)
        var qr = QRCode.Create(new string('1', 2200), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 21);
        Assert.True(qr.Size >= 101);
    }

    // ───── Kanji encoding edge cases ─────

    [Fact]
    public void Create_Kanji_E040Range()
    {
        // U+6F3E is Shift JIS 0xE040, the first value of the second range Kanji mode accepts.
        var qr = QRCode.Create("漾", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_MixedCJKAndASCII_FallsToByteMode()
    {
        // Mixed CJK and ASCII cannot use Kanji mode - falls to byte
        var qr = QRCode.Create("Hello漢字World", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Properties ─────

    [Fact]
    public void Create_StandardQR_HasCorrectType()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);

        Assert.Equal(QRCodeType.Standard, qr.Type);
        Assert.Equal(qr.Width, qr.Height);
        Assert.Equal(qr.Width, qr.Size);
    }

    // ───── Error cases ─────

    [Fact]
    public void Create_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => QRCode.Create((string)null!));
    }

    [Fact]
    public void Create_EmptyData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QRCode.Create(""));
    }

    [Fact]
    public void Create_EmptyBinaryData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QRCode.Create(Array.Empty<byte>(), ErrorCorrectionLevel.M));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Create_UndefinedErrorCorrectionLevel_ThrowsArgumentOutOfRangeException(int value)
    {
        var level = (ErrorCorrectionLevel)value;

        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.Create("123", level));
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.Create([1, 2, 3], level));
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.CreateMicroQR("123", level));
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.CreateRMQR("123", level));
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Create_Binary_ReportsTheErrorCorrectionLevel(ErrorCorrectionLevel ecLevel)
    {
        Assert.Equal(ecLevel, QRCode.Create([1, 2, 3], ecLevel).ErrorCorrectionLevel);
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Create_ReportsTheErrorCorrectionLevel(ErrorCorrectionLevel ecLevel)
    {
        Assert.Equal(ecLevel, QRCode.Create("HELLO WORLD", ecLevel).ErrorCorrectionLevel);
    }

    [Fact]
    public void Create_DataTooLong_ThrowsInvalidOperationException()
    {
        var longData = new string('A', 10000);

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData));
    }

    [Fact]
    public void Create_ByteDataTooLong_ThrowsInvalidOperationException()
    {
        var longData = new string('x', 2954);

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData, ErrorCorrectionLevel.L));
    }

    [Fact]
    public void Create_BinaryDataTooLong_ThrowsInvalidOperationException()
    {
        var longData = new byte[2954];

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData, ErrorCorrectionLevel.L));
    }

    [Fact]
    public void Create_AlphanumericDataTooLong_ThrowsInvalidOperationException()
    {
        // Version 40-Q holds 2420 alphanumeric characters.
        var longData = new string('A', 2421);

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData, ErrorCorrectionLevel.Q));
    }

    [Fact]
    public void Create_AlphanumericAtMaximumCapacity_Succeeds()
    {
        var qr = QRCode.Create(new string('A', 2420), ErrorCorrectionLevel.Q);

        Assert.Equal(40, qr.Version);
    }

    // ───── Determinism ─────

    [Fact]
    public void Create_Deterministic_SameInputProducesSameOutput()
    {
        var qr1 = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);
        var qr2 = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);

        Assert.Equal(qr1.Size, qr2.Size);
        for (var row = 0; row < qr1.Size; row++)
        {
            for (var col = 0; col < qr1.Size; col++)
            {
                Assert.Equal(qr1[row, col], qr2[row, col]);
            }
        }
    }

    [Fact]
    public void Create_DifferentData_ProducesDifferentOutput()
    {
        var qr1 = QRCode.Create("AAA", ErrorCorrectionLevel.M);
        var qr2 = QRCode.Create("BBB", ErrorCorrectionLevel.M);

        var different = false;
        for (var row = 0; row < qr1.Size && !different; row++)
        {
            for (var col = 0; col < qr1.Size && !different; col++)
            {
                if (qr1[row, col] != qr2[row, col])
                {
                    different = true;
                }
            }
        }

        Assert.True(different);
    }

    private static string RenderAsSvg(QRCode qr)
    {
        return qr.ToSvg(new QRCodeSvgOptions
        {
            ModuleSize = 1,
            QuietZoneModules = 0,
        });
    }

    private static TheoryData<int, int> CreateNumericPayloadLengthForAllVersions()
    {
        var data = new TheoryData<int, int>();
        var versionByLength = new Dictionary<int, int>();

        var minLength = 1;
        const int MaxLength = 7089;
        for (var version = 1; version <= 40; version++)
        {
            var payloadLength = FindMinimumLengthForVersion(version, minLength, MaxLength, versionByLength);
            data.Add(version, payloadLength);
            minLength = payloadLength + 1;
        }

        return data;
    }

    private static int FindMinimumLengthForVersion(int expectedVersion, int minLength, int maxLength, Dictionary<int, int> versionByLength)
    {
        var left = minLength;
        var right = maxLength;
        var bestLength = maxLength;

        while (left <= right)
        {
            var middle = left + ((right - left) / 2);
            var actualVersion = GetVersionForNumericPayloadLength(middle, versionByLength);
            if (actualVersion >= expectedVersion)
            {
                bestLength = middle;
                right = middle - 1;
            }
            else
            {
                left = middle + 1;
            }
        }

        var resolvedVersion = GetVersionForNumericPayloadLength(bestLength, versionByLength);
        if (resolvedVersion != expectedVersion)
        {
            throw new InvalidOperationException($"Unable to find payload for QR version {expectedVersion}. Resolved version: {resolvedVersion}.");
        }

        return bestLength;
    }

    private static int GetVersionForNumericPayloadLength(int payloadLength, Dictionary<int, int> versionByLength)
    {
        if (!versionByLength.TryGetValue(payloadLength, out var version))
        {
            version = QRCode.Create(new string('1', payloadLength), ErrorCorrectionLevel.L).Version;
            versionByLength[payloadLength] = version;
        }

        return version;
    }

    // ───── Capacity tables (ISO/IEC 18004 Table 7/9) ─────

    /// <summary>
    /// Maximum byte-mode payload for each version, indexed by L, M, Q, H.
    /// </summary>
    private static readonly int[][] ByteCapacities =
    [
        [17, 14, 11, 7], [32, 26, 20, 14], [53, 42, 32, 24], [78, 62, 46, 34],
        [106, 84, 60, 44], [134, 106, 74, 58], [154, 122, 86, 64], [192, 152, 108, 84],
        [230, 180, 130, 98], [271, 213, 151, 119], [321, 251, 177, 137], [367, 287, 203, 155],
        [425, 331, 241, 177], [458, 362, 258, 194], [520, 412, 292, 220], [586, 450, 322, 250],
        [644, 504, 364, 280], [718, 560, 394, 310], [792, 624, 442, 338], [858, 666, 482, 382],
        [929, 711, 509, 403], [1003, 779, 565, 439], [1091, 857, 611, 461], [1171, 911, 661, 511],
        [1273, 997, 715, 535], [1367, 1059, 751, 593], [1465, 1125, 805, 625], [1528, 1190, 868, 658],
        [1628, 1264, 908, 698], [1732, 1370, 982, 742], [1840, 1452, 1030, 790], [1952, 1538, 1112, 842],
        [2068, 1628, 1168, 898], [2188, 1722, 1228, 958], [2303, 1809, 1283, 983], [2431, 1911, 1351, 1051],
        [2563, 1989, 1423, 1093], [2699, 2099, 1499, 1139], [2809, 2213, 1579, 1219], [2953, 2331, 1663, 1273],
    ];

    public static TheoryData<int, ErrorCorrectionLevel> AllVersionsAndErrorCorrectionLevels()
    {
        var data = new TheoryData<int, ErrorCorrectionLevel>();
        foreach (var ecLevel in new[] { ErrorCorrectionLevel.L, ErrorCorrectionLevel.M, ErrorCorrectionLevel.Q, ErrorCorrectionLevel.H })
        {
            for (var version = 1; version <= 40; version++)
            {
                data.Add(version, ecLevel);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndErrorCorrectionLevels))]
    public void Create_AtMaximumCapacity_SelectsExpectedVersion(int version, ErrorCorrectionLevel ecLevel)
    {
        var capacity = ByteCapacities[version - 1][(int)ecLevel];
        var payload = new byte[capacity];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)('0' + (i % 10));
        }

        var qr = QRCode.Create(payload, ecLevel);

        Assert.Equal(version, qr.Version);
        Assert.Equal(17 + (version * 4), qr.Size);
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndErrorCorrectionLevels))]
    public void Create_OneByteOverCapacity_MovesToTheNextVersion(int version, ErrorCorrectionLevel ecLevel)
    {
        var payload = new byte[ByteCapacities[version - 1][(int)ecLevel] + 1];

        if (version == 40)
        {
            Assert.Throws<InvalidOperationException>(() => QRCode.Create(payload, ecLevel));
            return;
        }

        Assert.Equal(version + 1, QRCode.Create(payload, ecLevel).Version);
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Create_VersionSelectionIsMonotonicInPayloadLength(ErrorCorrectionLevel ecLevel)
    {
        // A non-monotonic capacity table lets DetermineVersion skip versions, which produces a
        // needlessly large symbol for some lengths and no symbol at all for others.
        var maximum = ByteCapacities[^1][(int)ecLevel];
        var previousVersion = 0;
        for (var length = 1; length <= maximum; length++)
        {
            var version = QRCode.Create(new byte[length], ecLevel).Version;
            Assert.True(version >= previousVersion, $"Payload of {length} bytes selected version {version} after version {previousVersion}.");
            previousVersion = version;
        }
    }

    // ───── Symbol structure (ISO/IEC 18004) ─────

    /// <summary>
    /// Alignment pattern centre coordinates for versions 1 to 40 (ISO/IEC 18004 Annex E).
    /// </summary>
    private static readonly int[][] AlignmentPatternPositions =
    [
        [], [6, 18], [6, 22], [6, 26], [6, 30],
        [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
        [6, 30, 54], [6, 32, 58], [6, 34, 62], [6, 26, 46, 66], [6, 26, 48, 70],
        [6, 26, 50, 74], [6, 30, 54, 78], [6, 30, 56, 82], [6, 30, 58, 86], [6, 34, 62, 90],
        [6, 28, 50, 72, 94], [6, 26, 50, 74, 98], [6, 30, 54, 78, 102], [6, 28, 54, 80, 106], [6, 32, 58, 84, 110],
        [6, 30, 58, 86, 114], [6, 34, 62, 90, 118], [6, 26, 50, 74, 98, 122], [6, 30, 54, 78, 102, 126], [6, 26, 52, 78, 104, 130],
        [6, 30, 56, 82, 108, 134], [6, 34, 60, 86, 112, 138], [6, 30, 58, 86, 114, 142], [6, 34, 62, 90, 118, 146], [6, 30, 54, 78, 102, 126, 150],
        [6, 24, 50, 76, 102, 128, 154], [6, 28, 54, 80, 106, 132, 158], [6, 32, 58, 84, 110, 136, 162], [6, 26, 54, 82, 110, 138, 166], [6, 30, 58, 86, 114, 142, 170],
    ];

    public static TheoryData<int> AllVersions()
    {
        var data = new TheoryData<int>();
        for (var version = 1; version <= 40; version++)
        {
            data.Add(version);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_PlacesTheThreeFinderPatternsAndTheirSeparators(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);
        var size = qr.Size;

        foreach (var (row, column) in new[] { (0, 0), (0, size - 7), (size - 7, 0) })
        {
            AssertFinderPattern(qr, row, column);
        }

        // The separator is the light band between a finder pattern and the rest of the symbol.
        for (var i = 0; i <= 7; i++)
        {
            Assert.False(qr[7, i], $"Separator module (7, {i}) is dark.");
            Assert.False(qr[i, 7], $"Separator module ({i}, 7) is dark.");
            Assert.False(qr[7, size - 1 - i], $"Separator module (7, {size - 1 - i}) is dark.");
            Assert.False(qr[i, size - 8], $"Separator module ({i}, {size - 8}) is dark.");
            Assert.False(qr[size - 8, i], $"Separator module ({size - 8}, {i}) is dark.");
            Assert.False(qr[size - 1 - i, 7], $"Separator module ({size - 1 - i}, 7) is dark.");
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_PlacesTheTimingPatterns(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);

        for (var i = 8; i < qr.Size - 8; i++)
        {
            Assert.Equal(i % 2 == 0, qr[6, i], $"Horizontal timing module at column {i} is wrong.");
            Assert.Equal(i % 2 == 0, qr[i, 6], $"Vertical timing module at row {i} is wrong.");
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_PlacesTheDarkModule(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);

        Assert.True(qr[(4 * version) + 9, 8]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_PlacesTheAlignmentPatterns(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);
        var positions = AlignmentPatternPositions[version - 1];

        foreach (var centerRow in positions)
        {
            foreach (var centerColumn in positions)
            {
                // The three corners are taken by the finder patterns.
                var isCorner = (centerRow <= 8 && centerColumn <= 8) ||
                               (centerRow <= 8 && centerColumn >= qr.Size - 9) ||
                               (centerRow >= qr.Size - 9 && centerColumn <= 8);
                if (isCorner)
                {
                    continue;
                }

                for (var row = -2; row <= 2; row++)
                {
                    for (var column = -2; column <= 2; column++)
                    {
                        var expected = Math.Abs(row) == 2 || Math.Abs(column) == 2 || (row == 0 && column == 0);
                        if (expected != qr[centerRow + row, centerColumn + column])
                        {
                            Assert.Fail($"Alignment pattern centred on ({centerRow}, {centerColumn}) is wrong at offset ({row}, {column}).");
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndErrorCorrectionLevels))]
    public void Create_WritesTwoIdenticalFormatInformationCopies(int version, ErrorCorrectionLevel ecLevel)
    {
        var qr = CreateWithVersion(version, ecLevel);

        Assert.Equal(ReadFormatInformation(qr, secondCopy: false), ReadFormatInformation(qr, secondCopy: true));
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndErrorCorrectionLevels))]
    public void Create_FormatInformationEncodesTheErrorCorrectionLevelAndAValidMask(int version, ErrorCorrectionLevel ecLevel)
    {
        var qr = CreateWithVersion(version, ecLevel);
        var formatInformation = ReadFormatInformation(qr, secondCopy: false) ^ FormatInformationMask;

        Assert.True(IsValidBch15_5(formatInformation), $"Format information 0x{formatInformation:X4} is not a valid BCH(15,5) code word.");

        var expectedIndicator = ecLevel switch
        {
            ErrorCorrectionLevel.L => 0b01,
            ErrorCorrectionLevel.M => 0b00,
            ErrorCorrectionLevel.Q => 0b11,
            _ => 0b10,
        };

        Assert.Equal(expectedIndicator, (formatInformation >> 13) & 0b11);
        Assert.InRange((formatInformation >> 10) & 0b111, 0, 7);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_WritesVersionInformationFromVersion7Onwards(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);
        if (version < 7)
        {
            // Below version 7 those modules carry data, so there is nothing to check beyond the
            // absence of a reserved block; the format information tests cover the rest.
            return;
        }

        var bottomLeft = ReadVersionInformation(qr, topRight: false);
        var topRight = ReadVersionInformation(qr, topRight: true);

        Assert.Equal(bottomLeft, topRight);
        Assert.Equal(version, bottomLeft >> 12);
        Assert.True(IsValidGolay18_6(bottomLeft), $"Version information 0x{bottomLeft:X5} is not a valid Golay(18,6) code word.");
    }

    [Fact]
    public void CreateMicroQR_PlacesASingleFinderPatternAndTheTimingPatterns()
    {
        var qr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

        AssertFinderPattern(qr, 0, 0);

        // Micro QR runs its timing patterns along the top row and the leftmost column.
        for (var i = 8; i < qr.Size; i++)
        {
            Assert.Equal(i % 2 == 0, qr[0, i], $"Horizontal timing module at column {i} is wrong.");
            Assert.Equal(i % 2 == 0, qr[i, 0], $"Vertical timing module at row {i} is wrong.");
        }

        // There is no second or third finder pattern, so the other corners carry data.
        for (var i = 0; i <= 7; i++)
        {
            Assert.False(qr[7, i], $"Separator module (7, {i}) is dark.");
            Assert.False(qr[i, 7], $"Separator module ({i}, 7) is dark.");
        }
    }

    private static void AssertFinderPattern(QRCode qr, int row, int column)
    {
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                var expected = r is 0 or 6 || c is 0 or 6 || (r is >= 2 and <= 4 && c is >= 2 and <= 4);
                if (expected != qr[row + r, column + c])
                {
                    Assert.Fail($"Finder pattern at ({row}, {column}) is wrong at offset ({r}, {c}).");
                }
            }
        }
    }

    /// <summary>The value the 15 format information bits are XORed with before being written.</summary>
    private const int FormatInformationMask = 0b101_0100_0001_0010;

    /// <summary>BCH(15,5) generator polynomial x^10 + x^8 + x^5 + x^4 + x^2 + x + 1.</summary>
    private const int Bch15_5Generator = 0b101_0011_0111;

    /// <summary>Golay(18,6) generator polynomial x^12 + x^11 + x^10 + x^9 + x^8 + x^5 + x^2 + 1.</summary>
    private const int Golay18_6Generator = 0b1_1111_0010_0101;

    /// <summary>
    /// Reads the 15 format information bits, most significant bit first.
    /// </summary>
    private static int ReadFormatInformation(QRCode qr, bool secondCopy)
    {
        var size = qr.Size;
        var positions = new List<(int Row, int Column)>(15);
        if (secondCopy)
        {
            for (var i = 0; i < 7; i++)
            {
                positions.Add((size - 1 - i, 8));
            }

            for (var i = 0; i < 8; i++)
            {
                positions.Add((8, size - 8 + i));
            }
        }
        else
        {
            for (var column = 0; column <= 5; column++)
            {
                positions.Add((8, column));
            }

            positions.Add((8, 7));
            positions.Add((8, 8));
            positions.Add((7, 8));

            for (var row = 5; row >= 0; row--)
            {
                positions.Add((row, 8));
            }
        }

        var value = 0;
        foreach (var (row, column) in positions)
        {
            value = (value << 1) | (qr[row, column] ? 1 : 0);
        }

        return value;
    }

    /// <summary>
    /// Reads the 18 version information bits. Bit <c>i</c> sits at row <c>size - 11 + (i % 3)</c>
    /// and column <c>i / 3</c> in the bottom-left block, transposed in the top-right block.
    /// </summary>
    private static int ReadVersionInformation(QRCode qr, bool topRight)
    {
        var value = 0;
        for (var i = 17; i >= 0; i--)
        {
            var row = qr.Size - 11 + (i % 3);
            var column = i / 3;
            var bit = topRight ? qr[column, row] : qr[row, column];
            value = (value << 1) | (bit ? 1 : 0);
        }

        return value;
    }

    private static bool IsValidBch15_5(int value)
    {
        for (var bit = 14; bit >= 10; bit--)
        {
            if ((value & (1 << bit)) != 0)
            {
                value ^= Bch15_5Generator << (bit - 10);
            }
        }

        return value == 0;
    }

    private static bool IsValidGolay18_6(int value)
    {
        for (var bit = 17; bit >= 12; bit--)
        {
            if ((value & (1 << bit)) != 0)
            {
                value ^= Golay18_6Generator << (bit - 12);
            }
        }

        return value == 0;
    }

    [Fact]
    public void Documentation_Decode()
    {
        #region QRCodeDecoder_Decode
        var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.M);

        var result = QRCodeDecoder.Decode(qrCode);

        Assert.Equal("https://www.meziantou.net", result.Text);
        Assert.Equal((QRCodeType.Standard, 2, ErrorCorrectionLevel.M), (result.Type, result.Version, result.ErrorCorrectionLevel));
        #endregion
    }

    [Fact]
    public void Documentation_DecodeModules()
    {
        #region QRCodeDecoder_DecodeModules
        // The modules, true for dark, indexed by row then column, without quiet zone
        var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.Q);
        var modules = new bool[qrCode.Height, qrCode.Width];
        for (var row = 0; row < qrCode.Height; row++)
        {
            for (var column = 0; column < qrCode.Width; column++)
            {
                modules[row, column] = qrCode[row, column];
            }
        }

        // Damage the symbol: the error correction restores the data
        for (var column = 9; column < 14; column++)
        {
            modules[12, column] = !modules[12, column];
        }

        Assert.True(QRCodeDecoder.TryDecode(modules, out var result));
        Assert.Equal("HELLO WORLD", result.Text);
        Assert.True(result.CorrectedErrorCount > 0);
        #endregion
    }

    [Fact]
    public void Documentation_MergeStructuredAppend()
    {
        #region QRCodeDecoder_MergeStructuredAppend
        var text = "Structured append splits this text across several QR codes, and the reader puts it back together.";
        var symbols = QRCode.CreateStructuredAppend(text, ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 2 });

        // The symbols can be read in any order
        var results = symbols.Reverse().Select(QRCodeDecoder.Decode).ToList();

        Assert.Equal(text, QRCodeDecoder.MergeStructuredAppend(results));
        #endregion
    }

    public static TheoryData<int, ErrorCorrectionLevel> AllVersionsAndLevels()
    {
        var data = new TheoryData<int, ErrorCorrectionLevel>();
        for (var version = 1; version <= 40; version++)
        {
            foreach (var level in Enum.GetValues<ErrorCorrectionLevel>())
            {
                data.Add(version, level);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndLevels))]
    public void Decode_AllVersionsAndLevels(int version, ErrorCorrectionLevel ecLevel)
    {
        var qr = CreateWithVersion(version, ecLevel);

        var result = QRCodeDecoder.Decode(qr);

        Assert.Equal((QRCodeType.Standard, version, ecLevel, 0, false), (result.Type, result.Version, result.ErrorCorrectionLevel, result.CorrectedErrorCount, result.IsMirrored));
        Assert.HasCount(ByteCapacities[version - 1][(int)ecLevel], result.Text);
        Assert.True(result.MaskPattern is >= 0 and <= 7);
        Assert.Null(result.StructuredAppend);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("12")]
    [InlineData("0123456789012345678901234567890")]
    [InlineData("HELLO WORLD")]
    [InlineData(" $%*+-./:")]
    [InlineData("Hello, World!")]
    [InlineData("caf\u00E9")]
    [InlineData("\U0001F600 emoji")]
    [InlineData("日本語")]
    [InlineData("ABC12345678901234567890abc日本語")]
    public void Decode_Text(string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        foreach (var level in Enum.GetValues<ErrorCorrectionLevel>())
        {
            Assert.Equal(text, QRCodeDecoder.Decode(QRCode.Create(text, level)).Text);
        }
    }

    [Fact]
    public void Decode_Segments()
    {
        QRCodeSegment[] segments =
        [
            QRCodeSegment.CreateAlphanumeric("ID:"),
            QRCodeSegment.CreateNumeric("0123456789"),
            QRCodeSegment.CreateKanji("漢字"),
            QRCodeSegment.CreateByte([0x00, 0xFF]),
        ];

        var result = QRCodeDecoder.Decode(QRCode.Create(segments));

        Assert.Equal(
            [QRCodeSegmentMode.Alphanumeric, QRCodeSegmentMode.Numeric, QRCodeSegmentMode.Kanji, QRCodeSegmentMode.Byte],
            result.Segments.Select(segment => segment.Mode).ToArray());
        Assert.Equal(["ID:", "0123456789", "漢字", null], result.Segments.Select(segment => segment.Text).ToArray());
        Assert.Equal([0x00, 0xFF], result.Segments[3].Bytes.ToArray());
        Assert.True(HasSameModules(QRCode.Create(segments), QRCode.Create(result.Segments)));
    }

    [Fact]
    public void Decode_Eci()
    {
        var utf8 = QRCodeDecoder.Decode(QRCode.Create("Caf\u00E9 cr\u00E8me", ErrorCorrectionLevel.M, new QRCodeOptions { EciAssignmentNumber = QRCodeOptions.Utf8EciAssignmentNumber }));
        var latin1 = QRCodeDecoder.Decode(QRCode.Create([0x63, 0x61, 0x66, 0xE9], ErrorCorrectionLevel.M, new QRCodeOptions { EciAssignmentNumber = 3 }));
        var shiftJis = QRCodeDecoder.Decode(QRCode.Create([QRCodeSegment.CreateEci(20), QRCodeSegment.CreateByte([0x93, 0xFA, 0x96, 0x7B])]));

        Assert.Equal("Caf\u00E9 cr\u00E8me", utf8.Text);
        Assert.Equal(26, utf8.Segments[0].EciAssignmentNumber);
        Assert.Equal("caf\u00E9", latin1.Text);
        Assert.Equal("日本", shiftJis.Text);
    }

    [Fact]
    public void Decode_InvalidUtf8WithoutEci_IsLatin1()
    {
        Assert.Equal("caf\u00E9", QRCodeDecoder.Decode(QRCode.Create([0x63, 0x61, 0x66, 0xE9])).Text);
    }

    [Fact]
    public void Decode_StructuredAppend()
    {
        var text = string.Concat(Enumerable.Repeat("0123456789ABCDEFGHIJ abcdefghij 日本語 caf\u00E9 ", 8));
        var symbols = QRCode.CreateStructuredAppend(text, ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 3 });
        var results = symbols.Select(QRCodeDecoder.Decode).ToArray();

        Assert.HasCountGreaterThan(1, results);
        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal((i, results.Length, symbols[i].StructuredAppend!.Parity), (results[i].StructuredAppend!.Index, results[i].StructuredAppend!.Count, results[i].StructuredAppend!.Parity));
        }

        Assert.Equal(text, QRCodeDecoder.MergeStructuredAppend(results.Reverse()));
        Assert.Throws<ArgumentException>("results", () => QRCodeDecoder.MergeStructuredAppend(results.Skip(1)));
        Assert.Throws<ArgumentException>("results", () => QRCodeDecoder.MergeStructuredAppend([.. results, results[0]]));
        Assert.Throws<ArgumentException>("results", () => QRCodeDecoder.MergeStructuredAppend([]));
        Assert.Throws<ArgumentException>("results", () => QRCodeDecoder.MergeStructuredAppend([QRCodeDecoder.Decode(QRCode.Create("HELLO"))]));
        Assert.Throws<ArgumentNullException>("results", () => QRCodeDecoder.MergeStructuredAppend(null!));
    }

    [Fact]
    public void Decode_StructuredAppend_CharacterSplitAcrossSymbolsWithTheSameEci()
    {
        // Each symbol repeats the ECI designator, and the first one ends in the middle of the 2 bytes of 'é'
        var text = new string('\u00E9', 20);
        var symbols = QRCode.CreateStructuredAppend([QRCodeSegment.CreateEci(QRCodeOptions.Utf8EciAssignmentNumber), QRCodeSegment.CreateByte(text)], ErrorCorrectionLevel.L, new QRCodeOptions { MaximumVersion = 1 });

        var results = symbols.Select(QRCodeDecoder.Decode).ToArray();

        Assert.HasCountGreaterThan(1, results);
        Assert.Equal(26, results[1].Segments[0].EciAssignmentNumber);
        Assert.Equal(text, QRCodeDecoder.MergeStructuredAppend(results));
    }

    [Fact]
    public void Decode_LargeMatrixThatIsNotAQRCode_IsRejectedWithoutTransposing()
    {
        // 10,000 x 10,000 modules: a mirrored retry would copy them for nothing
        Assert.False(QRCodeDecoder.TryDecode(new bool[10_000, 10_000], out _));
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Decode_CorrectsCodewordErrors(ErrorCorrectionLevel ecLevel)
    {
        foreach (var version in (int[])[1, 2, 5, 7, 10, 20, 27, 40])
        {
            var qr = CreateWithVersion(version, ecLevel);
            var expected = QRCodeDecoder.Decode(qr).Text;

            // Corrupt the first codewords of each block, up to the number the error correction fixes
            var correctable = QRCodeVersion.GetECCodewordsPerBlock(version, ecLevel) / 2;
            var blockCount = QRCodeVersion.GetBlockCount(version, ecLevel);
            var codewordPositions = GetCodewordModules(version);
            var modules = CopyModules(qr);
            for (var i = 0; i < correctable * blockCount; i++)
            {
                foreach (var (row, column) in codewordPositions[i])
                {
                    modules[row, column] = !modules[row, column];
                }
            }

            var result = QRCodeDecoder.Decode(modules);
            Assert.Equal(expected, result.Text);
            Assert.Equal(correctable * blockCount, result.CorrectedErrorCount);

            // One more error in the first block is too many
            foreach (var (row, column) in codewordPositions[correctable * blockCount])
            {
                modules[row, column] = !modules[row, column];
            }

            if (QRCodeDecoder.TryDecode(modules, out var corrupted))
            {
                Assert.NotEqual(expected, corrupted.Text);
            }
        }
    }

    [Fact]
    public void Decode_FormatInformationWithErrors()
    {
        var qr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);
        var modules = CopyModules(qr);

        // Three errors in the first copy, the second copy is intact
        modules[8, 0] = !modules[8, 0];
        modules[8, 2] = !modules[8, 2];
        modules[2, 8] = !modules[2, 8];
        Assert.Equal("HELLO WORLD", QRCodeDecoder.Decode(modules).Text);

        // Both copies damaged beyond repair
        for (var i = 0; i < 8; i++)
        {
            modules[8, i is 6 ? 8 : i] = !modules[8, i is 6 ? 8 : i];
            modules[8, qr.Size - 1 - i] = !modules[8, qr.Size - 1 - i];
        }

        Assert.False(QRCodeDecoder.TryDecode(modules, out _));
    }

    [Fact]
    public void Decode_Mirrored()
    {
        var qr = QRCode.Create("Mirrored QR code", ErrorCorrectionLevel.M);
        var transposed = new bool[qr.Size, qr.Size];
        for (var row = 0; row < qr.Size; row++)
        {
            for (var column = 0; column < qr.Size; column++)
            {
                transposed[column, row] = qr[row, column];
            }
        }

        var result = QRCodeDecoder.Decode(transposed);

        Assert.Equal("Mirrored QR code", result.Text);
        Assert.True(result.IsMirrored);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void Decode_InvalidMatrix()
    {
        Assert.Throws<ArgumentNullException>("modules", () => QRCodeDecoder.Decode((bool[,])null!));
        Assert.Throws<ArgumentNullException>("qrCode", () => QRCodeDecoder.Decode((QRCode)null!));
        Assert.Throws<FormatException>(() => QRCodeDecoder.Decode(new bool[22, 22]));
        Assert.Throws<FormatException>(() => QRCodeDecoder.Decode(new bool[21, 21]));
        Assert.Throws<FormatException>(() => QRCodeDecoder.Decode(new bool[10, 30]));
        Assert.False(QRCodeDecoder.TryDecode(new bool[0, 0], out _));

        var random = new Random(42);
        for (var i = 0; i < 100; i++)
        {
            var size = 21 + (4 * random.Next(10));
            var modules = new bool[size, size];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                {
                    modules[row, column] = random.Next(2) is 0;
                }
            }

            // Random modules almost never have a valid format and valid codewords, but must not throw
            _ = QRCodeDecoder.TryDecode(modules, out _);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void ReedSolomonDecoder_CorrectsUpToHalfTheErrorCorrectionCodewords()
    {
        var random = new Random(1);
        foreach (var (field, firstRoot) in new[] { (BinaryGaloisField.QRCode256, 0), (BinaryGaloisField.Field256, 1), (BinaryGaloisField.Aztec64, 1), (BinaryGaloisField.Aztec16, 1), (BinaryGaloisField.Aztec1024, 1), (BinaryGaloisField.Aztec4096, 1) })
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var ecCount = random.Next(1, Math.Min(30, field.Size - 2));
                var dataCount = random.Next(1, Math.Min(60, field.Size - 1 - ecCount));
                var codewords = new int[dataCount + ecCount];
                for (var i = 0; i < dataCount; i++)
                {
                    codewords[i] = random.Next(field.Size);
                }

                if (firstRoot is 0)
                {
                    var bytes = codewords[..dataCount].Select(value => (byte)value).ToArray();
                    GaloisField.ComputeRemainder(bytes, GaloisField.GenerateGeneratorPolynomial(ecCount)).Select(value => (int)value).ToArray().CopyTo(codewords, dataCount);
                }
                else
                {
                    field.ComputeErrorCorrection(codewords.AsSpan(0, dataCount), codewords.AsSpan(dataCount));
                }

                var original = codewords.ToArray();
                Assert.Equal(0, ReedSolomonDecoder.Correct(field, codewords, ecCount, firstRoot));

                var errorCount = random.Next(0, (ecCount / 2) + 2);
                var positions = Enumerable.Range(0, codewords.Length).OrderBy(_ => random.Next()).Take(errorCount).ToArray();
                foreach (var position in positions)
                {
                    codewords[position] ^= random.Next(1, field.Size);
                }

                var corrected = ReedSolomonDecoder.Correct(field, codewords, ecCount, firstRoot);
                if (positions.Length * 2 <= ecCount)
                {
                    Assert.Equal(positions.Length, corrected);
                    Assert.Equal(original, codewords);
                }
                else if (corrected >= 0)
                {
                    Assert.NotEqual(original, codewords);
                }
            }
        }
    }

    private static bool[,] CopyModules(QRCode qr)
    {
        var modules = new bool[qr.Height, qr.Width];
        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                modules[row, column] = qr[row, column];
            }
        }

        return modules;
    }

    // The modules of each codeword, in placement order (ISO/IEC 18004, 7.7.3)
    private static List<List<(int Row, int Column)>> GetCodewordModules(int version)
    {
        var builder = new MatrixBuilder(version);
        var total = QRCodeVersion.GetTotalCodewords(version);
        builder.PlaceFunctionPatternsAndData(new byte[total]);
        var result = new List<List<(int Row, int Column)>>();
        var size = builder.Size;
        var bitIndex = 0;
        for (var column = size - 1; column >= 0; column -= 2)
        {
            if (column == 6)
            {
                column--;
            }

            var isUpward = ((size - 1 - column) / 2) % 2 == 0;
            for (var i = 0; i < size; i++)
            {
                var row = isUpward ? size - 1 - i : i;
                for (var offset = 0; offset < 2 && column - offset >= 0; offset++)
                {
                    if (builder.IsReserved[row, column - offset] || bitIndex >= total * 8)
                        continue;

                    if (bitIndex % 8 is 0)
                    {
                        result.Add([]);
                    }

                    result[^1].Add((row, column - offset));
                    bitIndex++;
                }
            }
        }

        return result;
    }

    private static QRCode CreateWithVersion(int version, ErrorCorrectionLevel ecLevel)
    {
        var payload = new byte[ByteCapacities[version - 1][(int)ecLevel]];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)('0' + (i % 10));
        }

        return QRCode.Create(payload, ecLevel);
    }
}
