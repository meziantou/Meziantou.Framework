using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Tests.Encodings;

/// <summary>
/// The detector of the legacy encodings, with the tests of chardetng 1.0.0 (https://github.com/hsivonen/chardetng, copyright
/// Mozilla Foundation, Apache-2.0 or MIT license), the detector it is a port of. Where chardetng falls back to windows-1252,
/// the detector guesses nothing or windows-1252.
/// </summary>
public sealed class LegacyEncodingDetectorTests
{
    private static string Guess(ReadOnlySpan<byte> bytes, bool allowUtf8 = false)
    {
        var detector = new LegacyEncodingDetector();
        detector.Feed(bytes, last: true);
        return WebEncodingRegistry.GetName(detector.Guess(allowUtf8) ?? WebEncodingId.Windows1252);
    }

    // As for the tests of chardetng, the characters that are not in the encoding are numeric character references
    // (the Persian letters of the text of windows-1256)
    private static byte[] Encode(string text, string encodingLabel)
    {
        Assert.True(WebEncodingRegistry.TryGetEncoding(encodingLabel, out var encoding));
        var bytes = new System.Buffers.ArrayBufferWriter<byte>();
        WebEncodingRegistry.CreateEncoder(encoding).EncodeHtml(text, bytes);
        return bytes.WrittenSpan.ToArray();
    }

    private static byte[] Repeat(ReadOnlySpan<byte> prefix, int count, ReadOnlySpan<byte> repeated, ReadOnlySpan<byte> suffix = default)
    {
        var bytes = new List<byte>();
        bytes.AddRange(prefix);
        for (var i = 0; i < count; i++)
        {
            bytes.AddRange(repeated);
        }

        bytes.AddRange(suffix);
        return [.. bytes];
    }

    // The text of windows-1258 has the tone marks of Vietnamese as combining characters
    [Theory]
    [InlineData("Ääni", "windows-1252")] // test_fi
    [InlineData("Tämä", "windows-1252")] // test_fi_bis
    [InlineData("Este é um teste de codificação de caracteres.", "windows-1252")] // test_pt
    [InlineData("Þetta er kóðunarpróf á staf. Fyrir sum tungumál sem nota latneska stafi þurfum við meira inntak til að taka ákvörðunina.", "windows-1252")] // test_is
    [InlineData("Русский", "windows-1251")] // test_ru_short
    [InlineData("Это тест кодировки символов.", "windows-1251")] // test_ru
    [InlineData("Это тест кодировки символов.", "ISO-8859-5")] // test_ru_iso
    [InlineData("Это тест кодировки символов.", "IBM866")] // test_ru_ibm
    [InlineData("Это тест кодировки символов.", "KOI8-U")] // test_ru_koi
    [InlineData("Це тест на кодування символів.", "windows-1251")] // test_uk
    [InlineData("Це тест на кодування символів.", "KOI8-U")] // test_uk_koi
    [InlineData("Ελληνικά", "windows-1253")] // test_el_short
    [InlineData("Πρόκειται για δοκιμή κωδικοποίησης χαρακτήρων: Άρης", "windows-1253")] // test_el
    [InlineData("Πρόκειται για δοκιμή κωδικοποίησης χαρακτήρων: Άρης", "ISO-8859-7")] // test_el_iso
    [InlineData("Straße", "windows-1252")] // test_de
    [InlineData("\U000005E2\U000005D1\U000005E8\U000005D9\U000005EA", "windows-1255")] // test_he
    [InlineData("น\U00000E35\U00000E48ค\U00000E37อการทดสอบการเข\U00000E49ารห\U00000E31สอ\U00000E31กขระ", "windows-874")] // test_th
    [InlineData("Đây la\U00000300 mô\U00000323t thư\U00000309 nghiê\U00000323m ma\U00000303 ho\U00000301a ky\U00000301 tư\U00000323.", "windows-1258")] // test_vi
    [InlineData("Bu bir karakter kodlama testidir. Latince karakterleri kullanan bazı dillerde karar vermek için daha fazla girdiye ihtiyacımız var.", "windows-1254")] // test_tr
    [InlineData("这是一个字符编码测试。", "GBK")] // test_simplified
    [InlineData("這是一個字符編碼測試。", "Big5")] // test_traditional
    [InlineData("이것은 문자 인코딩 테스트입니다.", "EUC-KR")] // test_ko
    [InlineData("これは文字実験です。", "Shift_JIS")] // test_shift
    [InlineData("これは文字実験です。", "EUC-JP")] // test_euc
    [InlineData("\U00000647\U00000630\U00000627 \U00000647\U00000648 \U00000627\U0000062E\U0000062A\U00000628\U00000627\U00000631 \U0000062A\U00000631\U00000645\U0000064A\U00000632 \U00000627\U00000644\U00000623\U0000062D\U00000631\U00000641.", "windows-1256")] // test_ar
    [InlineData("\U00000647\U00000630\U00000627 \U00000647\U00000648 \U00000627\U0000062E\U0000062A\U00000628\U00000627\U00000631 \U0000062A\U00000631\U00000645\U0000064A\U00000632 \U00000627\U00000644\U00000623\U0000062D\U00000631\U00000641.", "ISO-8859-6")] // test_ar_iso
    [InlineData("\U00000627\U000006CC\U00000646 \U000006CC\U000006A9 \U0000062A\U00000633\U0000062A \U00000631\U00000645\U00000632\U000006AF\U00000630\U00000627\U00000631\U000006CC \U000006A9\U00000627\U00000631\U00000627\U000006A9\U0000062A\U00000631 \U00000627\U00000633\U0000062A.", "windows-1256")] // test_fa
    [InlineData(".\U000005DD\U000005D9\U000005D5\U000005D5\U000005EA \U000005D3\U000005D5\U000005D3\U000005D9\U000005E7 \U000005DF\U000005D7\U000005D1\U000005DE \U000005D5\U000005D4\U000005D6", "ISO-8859-8")] // test_visual
    [InlineData("\U000005D3\U000005D0\U000005B8\U000005E1 \U000005D0\U000005D9\U000005D6 \U000005D0\U000005B7 \U000005D8\U000005E2\U000005E1\U000005D8 \U000005E4\U000005BF\U000005D0\U000005B7\U000005E8 \U000005E7\U000005D0\U000005B8\U000005D3\U000005D9\U000005E8\U000005D5\U000005E0\U000005D2 \U000005E4\U000005D5\U000005DF \U000005DB\U000005D0\U000005B7\U000005E8\U000005D0\U000005B7\U000005E7\U000005D8\U000005E2\U000005E8.", "windows-1255")] // test_yi
    [InlineData("è", "windows-1252")] // test_it
    [InlineData("isn\U00002019t", "windows-1252")] // test_en
    [InlineData("Rock \U00002019n Roll", "windows-1252")] // test_en_bis
    [InlineData("Codificació de caràcters", "windows-1252")] // test_ca
    [InlineData("või", "windows-1252")] // test_et
    [InlineData("To jest test kodowania znaków. W przypadku niektórych języków, które używają znaków łacińskich, potrzebujemy więcej danych, aby podjąć decyzję.", "ISO-8859-2")] // test_pl_iso
    [InlineData("To jest test kodowania znaków. W przypadku niektórych języków, które używają znaków łacińskich, potrzebujemy więcej danych, aby podjąć decyzję.", "windows-1250")] // test_pl
    [InlineData("Tai simbolių kodavimo testas. Kai kurioms kalboms, naudojančioms lotyniškus rašmenis, mums reikia daugiau informacijos, kad galėtume priimti sprendimą.", "windows-1257")] // test_lt
    [InlineData("Šis ir rakstzīmju kodēšanas tests. Dažās valodās, kurās tiek izmantotas latīņu valodas burti, lēmuma pieņemšanai mums ir nepieciešams vairāk ieguldījuma.", "windows-1257")] // test_lv
    [InlineData("Šis ir rakstzīmju kodēšanas tests. Dažās valodās, kurās tiek izmantotas latīņu valodas burti, lēmuma pieņemšanai mums ir nepieciešams vairāk ieguldījuma.", "ISO-8859-4")] // test_lv_iso_8859_4
    [InlineData("\U000000A0\U000000A0 \U000000A0", "windows-1252")] // test_a0
    [InlineData("\U000000A0\U000000A0", "windows-1252")] // test_a0a0
    [InlineData(" \U000000A9 ", "windows-1252")] // test_space_copyright_space
    [InlineData(" \U000000BA ", "windows-1252")] // test_space_masculine_space
    [InlineData(" \U000000AA ", "windows-1252")] // test_space_feminine_space
    [InlineData(".\U000000BA ", "windows-1252")] // test_period_masculine_space
    [InlineData(".\U000000AA ", "windows-1252")] // test_period_feminine_space
    [InlineData(" M\U000000AA ", "windows-1252")] // test_maria
    [InlineData(" D\U000000AA ", "windows-1252")] // test_dona
    [InlineData(" N\U000000AA ", "windows-1252")] // test_nuestra
    [InlineData(" S\U000000AA ", "windows-1252")] // test_senora
    [InlineData(" 42\U000000AA ", "windows-1252")] // test_digit_feminine
    [InlineData(" 42\U000000BA ", "windows-1252")] // test_digit_masculine
    [InlineData(" XIV\U000000AA ", "windows-1252")] // test_roman_feminine
    [InlineData(" XIV\U000000BA ", "windows-1252")] // test_roman_masculine
    [InlineData("N\U000000BA1", "windows-1252")] // test_numero_uno
    [InlineData("N\U000000BA", "windows-1252")] // test_numero
    [InlineData(" \U000020AC9", "windows-1252")] // test_euro
    [InlineData("ﾊｰﾄﾞｳｪｱﾊｰﾄﾞｳｪｱﾊｰﾄﾞｳｪｱﾊｰﾄﾞｳｪｱﾊｰﾄﾞｳｪｱ", "Shift_JIS")] // test_shift_jis_half_width_katakana
    public void Text(string text, string encoding)
    {
        Assert.Equal(encoding, Guess(Encode(text, encoding)));
    }

    [Fact]
    public void Iso2022JP_IsNotDetected()
    {
        // The bytes of ISO-2022-JP are ASCII bytes
        Assert.Equal("windows-1252", Guess(Encode("\U000065E5\U0000672C\U00008A9E", "ISO-2022-JP")));
        var detector = new LegacyEncodingDetector();
        detector.Feed(Encode("\U000065E5\U0000672C\U00008A9E", "ISO-2022-JP"), last: true);
        Assert.False(detector.NonAsciiSeen);
        Assert.Null(detector.Guess(allowUtf8: true));
    }

    [Fact]
    public void Empty()
    {
        var detector = new LegacyEncodingDetector();
        detector.Feed([], last: true);
        Assert.False(detector.NonAsciiSeen);
        Assert.Null(detector.Guess(allowUtf8: true));
    }

    [Fact]
    public void Utf8_IsOnlyGuessedWhenAllowed()
    {
        var bytes = "caf\U000000E9 \U00003053\U0000308C\U0000306F"u8.ToArray();
        var detector = new LegacyEncodingDetector();
        detector.Feed(bytes, last: true);
        Assert.True(detector.NonAsciiSeen);
        Assert.Equal(WebEncodingId.Utf8, detector.Guess(allowUtf8: true));

        // Nothing else is guessed for valid UTF-8
        Assert.Null(detector.Guess(allowUtf8: false));
    }

    [Fact]
    public void Utf8_TruncatedCharacterAtTheEnd()
    {
        var bytes = "caf\U000000E9"u8.ToArray();
        var detector = new LegacyEncodingDetector();
        detector.Feed(bytes.AsSpan(0, bytes.Length - 1), last: false);
        Assert.Equal(WebEncodingId.Utf8, detector.Guess(allowUtf8: true));

        detector = new LegacyEncodingDetector();
        detector.Feed(bytes.AsSpan(0, bytes.Length - 1), last: true);
        Assert.NotEqual(WebEncodingId.Utf8, detector.Guess(allowUtf8: true));
    }

    [Theory]
    [InlineData("I\U00000092")] // test_i_apostrophe: not Turkish
    [InlineData("Don\U000000B4t ")] // test_en_windows1252
    public void Windows1252Bytes(string latin1)
    {
        Assert.Equal("windows-1252", Guess(System.Text.Encoding.Latin1.GetBytes(latin1)));
    }

    // The context of the first non-ASCII byte is the two ASCII bytes before it, whichever buffers they are in
    [Theory]
    [InlineData("n", ".", "\U000000BA", "1")] // test_streaming_numero_one_by_one
    [InlineData("n.", "\U000000BA", "1", "")] // test_streaming_numero_two_together
    [InlineData(" n", ".", "\U000000BA", "1")] // test_streaming_numero_one_by_one_extra_before
    [InlineData("n", ".\U000000BA", "1", "")] // test_streaming_numero_one_before
    [InlineData("rrn.", "\U000000BA", "1", "")] // test_streaming_numero_longer_first_buffer
    public void Streaming_SpanishNumero(string first, string second, string third, string fourth)
    {
        var detector = new LegacyEncodingDetector();
        detector.Feed(System.Text.Encoding.Latin1.GetBytes(first), last: false);
        detector.Feed(System.Text.Encoding.Latin1.GetBytes(second), last: false);
        detector.Feed(System.Text.Encoding.Latin1.GetBytes(third), last: false);
        detector.Feed(System.Text.Encoding.Latin1.GetBytes(fourth), last: true);
        Assert.Equal(WebEncodingId.Windows1252, detector.Guess(allowUtf8: false));
    }

    [Fact]
    public void Streaming_SameGuessAsOneBuffer()
    {
        var bytes = Encode("\U00003053\U0000308C\U0000306F\U00006587\U00005B57\U00005B9F\U00009A13\U00003067\U00003059\U00003002", "EUC-JP");
        var detector = new LegacyEncodingDetector();
        foreach (var b in bytes)
        {
            detector.Feed([b], last: false);
        }

        detector.Feed([], last: true);
        Assert.Equal(WebEncodingId.EucJP, detector.Guess(allowUtf8: false));
    }

    // The inside of the long runs of ASCII bytes of a buffer is not fed to the candidates: the scores are the ones of the
    // bytes fed one by one
    [Fact]
    public void Streaming_AsciiRuns_SameScoresAsByteByByte()
    {
        const string Japanese = "\U00003053\U0000308C\U0000306F\U00006587\U00005B57\U00005B9F\U00009A13\U00003067\U00003059\U00003002";
        const string Russian = "\U0000042D\U00000442\U0000043E \U00000442\U00000435\U00000441\U00000442 \U0000041A\U0000041E\U00000414.";
        byte[][] fragments =
        [
            Encode(Japanese, "Shift_JIS"), Encode(Japanese, "EUC-JP"), Encode("\U00008FD9\U0000662F\U00004E00\U00004E2A\U00003002", "GBK"),
            Encode("\U00009019\U0000662F\U00004E00\U0000500B\U00003002", "Big5"), Encode("\U0000C774\U0000AC83\U0000C740 \U0000BB38\U0000C790", "EUC-KR"),
            Encode(Russian, "windows-1251"), Encode(Russian, "KOI8-U"), Encode(Russian, "IBM866"), Encode("\U000005E2\U000005D1\U000005E8\U000005D9\U000005EA, \U000005D6\U000005D4", "windows-1255"),
            Encode("\U00000647\U00000630\U00000627 fran\U000000E7ais", "windows-1256"), Encode("codifica\U000000E7\U000000E3o \U000000C4\U000000E4ni n.\U000000BA1 M\U000000AA \U000000A9 XIV\U000000BA", "windows-1252"),
            Encode("\U00000E19\U00000E35\U00000E48\U00000E04\U00000E37\U00000E2D", "windows-874"), [0x81], [0xFF], [0xA0, 0xA0], [0x8F, 0xFE],
        ];
        const string Ascii = "abcXYZ NnMDSIVX 0123456789 .,:;?! <p class=\"a\">\r\n\t&#1740; ~[]{}@`\u001B";
        var random = new Random(12345);
#pragma warning disable CA5394 // The inputs are reproducible (seeded), not secret
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var bytes = new List<byte>();
            var fragmentCount = random.Next(1, 12);
            for (var i = 0; i < fragmentCount; i++)
            {
                // Runs of ASCII bytes of all the lengths, between the fragments of text
                var asciiLength = random.Next(4) == 0 ? random.Next(0, 6) : random.Next(0, 200);
                for (var j = 0; j < asciiLength; j++)
                {
                    bytes.Add((byte)Ascii[random.Next(Ascii.Length)]);
                }

                var fragment = fragments[random.Next(fragments.Length)];
                bytes.AddRange(fragment.AsSpan(0, random.Next(1, fragment.Length + 1)));
            }
#pragma warning restore CA5394

            var whole = new LegacyEncodingDetector();
            whole.Feed([.. bytes], last: true);
            var byteByByte = new LegacyEncodingDetector();
            foreach (var b in bytes)
            {
                byteByByte.Feed([b], last: false);
            }

            byteByByte.Feed([], last: true);
            Assert.Equal(byteByByte.GetScores(), whole.GetScores());
            Assert.Equal(byteByByte.Guess(allowUtf8: true), whole.Guess(allowUtf8: true));
#pragma warning disable CA5394
        }
#pragma warning restore CA5394
    }

    // The extensions of the multi-byte encodings that the decoders do not support are penalized, but do not disqualify
    // the encoding; the other invalid sequences do
    [Fact]
    public void Big5_UnmappedPair() => Assert.Equal("Big5", Guess(Repeat([], 40, [0xA4, 0x40], [0x81, 0x40, 0xA4, 0x40]))); // test_big5_pua

    [Fact]
    public void Big5_MacSingleByteA0() => Assert.Equal("Big5", Guess(Repeat([], 80, [0xA4, 0x40], [0x81, 0x40, 0xA0, 0x20]))); // test_big5_single_byte_a0

    [Fact]
    public void Big5_MacSingleByteFF() => Assert.Equal("Big5", Guess(Repeat([], 80, [0xA4, 0x40], [0x81, 0x40, 0xFF, 0x20]))); // test_big5_single_byte_ff

    [Fact]
    public void Big5_InvalidPair() => Assert.Equal("IBM866", Guess(Repeat([], 40, [0xA4, 0x40], [0x81, 0x40, 0xA0, 0xA0]))); // test_not_big5

    [Theory]
    [InlineData(new byte[] { 0xC9, 0xA1, 0xB0, 0xA1, 0x20 }, "EUC-KR")] // test_euc_kr_pua
    [InlineData(new byte[] { 0xFE, 0xA1, 0xB0, 0xA1, 0x20 }, "EUC-KR")] // test_euc_kr_pua_bis
    [InlineData(new byte[] { 0xFF, 0x20 }, "EUC-KR")] // test_euc_kr_single_byte_ff
    [InlineData(new byte[] { 0x81, 0x20 }, "EUC-KR")] // test_euc_kr_single_byte_81
    [InlineData(new byte[] { 0x84, 0x20 }, "EUC-KR")] // test_euc_kr_single_byte_84
    [InlineData(new byte[] { 0xC9, 0xA0, 0xB0, 0xA1, 0x20 }, "GBK")] // test_not_euc_kr
    public void EucKR_Extensions(byte[] prefix, string encoding)
    {
        Assert.Equal(encoding, Guess(Repeat(prefix, 40, [0xC5, 0xD7, 0xBD, 0xBA, 0xC6, 0xAE, 0x2E, 0x20])));
    }

    [Theory]
    [InlineData(new byte[] { 0x87, 0xE5 }, "Shift_JIS")] // test_shift_jis_x0213
    [InlineData(new byte[] { 0xFD }, "Shift_JIS")] // test_shift_jis_single_byte_fd
    [InlineData(new byte[] { 0x84, 0xE0 }, "GBK")] // test_not_shift_jis
    [InlineData(new byte[] { 0x87, 0x7D }, "GBK")] // test_not_shift_jis_bis
    public void ShiftJis_Extensions(byte[] prefix, string encoding)
    {
        Assert.Equal(encoding, Guess(Repeat(prefix, 40, [0x82, 0xC9, 0x82, 0xD9, 0x82, 0xF1, 0x82, 0xB2])));
    }

    [Theory]
    [InlineData(new byte[] { 0xAD, 0xBF }, "EUC-JP")] // test_euc_jp_x0213
    [InlineData(new byte[] { 0x8F, 0xFE, 0xF6 }, "EUC-JP")] // test_euc_jp_x0213_other_plane
    [InlineData(new byte[] { 0x8F, 0xFE, 0xF7 }, "windows-1252")] // test_not_euc_jp
    [InlineData(new byte[] { 0xA8, 0xDF }, "Big5")] // test_not_euc_jp_bis
    public void EucJP_Extensions(byte[] prefix, string encoding)
    {
        Assert.Equal(encoding, Guess(Repeat(prefix, 80, [0xA4, 0xCB, 0xA4, 0xDB, 0xA4, 0xF3, 0xA4, 0xB4])));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF }, "GBK")] // test_gbk_single_byte_ff
    [InlineData(new byte[] { 0xA0, 0x20 }, "GBK")] // test_gbk_single_byte_a0
    [InlineData(new byte[] { 0xFE, 0x20 }, "GBK")] // test_gbk_single_byte_fe
    [InlineData(new byte[] { 0xFC, 0x20 }, "ISO-8859-5")] // test_not_gbk_single_byte_fc
    public void Gbk_Extensions(byte[] prefix, string encoding)
    {
        Assert.Equal(encoding, Guess(Repeat(prefix, 80, [0xB5, 0xC4])));
    }
}
