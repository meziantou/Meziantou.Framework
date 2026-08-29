namespace Meziantou.Framework.Tests;

internal static class QRCodePngScenarios
{
    private const string AlphanumericCharset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    public static IReadOnlyList<Scenario> All { get; } =
    [
        // ───── Numeric mode: the encoder groups digits three at a time, so the remainder matters ─────
        new("NumericSingleDigit", "7") { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("NumericTwoDigits", "42") { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("NumericThreeDigits", "123") { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("NumericRemainderOne", "1234567") { ErrorCorrectionLevel = ErrorCorrectionLevel.Q },
        new("NumericRemainderTwo", "12345678") { ErrorCorrectionLevel = ErrorCorrectionLevel.Q },
        new("NumericLeadingZeros", "000000000001") { ErrorCorrectionLevel = ErrorCorrectionLevel.H },

        // ───── Alphanumeric mode: characters are packed in pairs, so an odd count leaves a short group ─────
        new("AlphanumericOddLength", "ABC") { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("AlphanumericEvenLength", "AB") { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("AlphanumericFullCharset", AlphanumericCharset) { ErrorCorrectionLevel = ErrorCorrectionLevel.Q },
        new("AlphanumericWithSpaces", "HELLO WORLD"),

        // ───── Byte mode ─────
        new("ByteLowercase", "hello world") { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("ByteUrl", "https://www.meziantou.net/"),
        new("ByteAllPrintableAscii", CreatePrintableAscii()) { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("ByteWithNewLinesAndTabs", "line1\nline2\r\ncolumn1\tcolumn2"),
        new("ByteAccentedCharacters", "café crème brûlée"),
        new("ByteEmoji", "\U0001F600\U0001F680\U0001F389") { ErrorCorrectionLevel = ErrorCorrectionLevel.L },

        // ───── Kanji mode and its fallbacks ─────
        new("KanjiSingleCharacter", "漢") { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("KanjiSentence", "こんにちは世界"),
        // The first and last characters of the two Shift JIS ranges Kanji mode accepts.
        new("KanjiRangeBoundaries", "　滌漾熙"),
        new("MixedKanjiAndAsciiUsesByteMode", "Hello漢字World"),

        // ───── Version boundaries ─────
        // Version 7 is the first one that carries a version information block.
        new("Version7WithVersionInformation", Repeat('B', 196)) { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        // Versions 10+ widen the character count indicator, and 27+ widen it again.
        new("Version10CharacterCountIndicator", Repeat('7', 553)) { ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        new("Version27CharacterCountIndicator", Repeat('7', 3284)) { ErrorCorrectionLevel = ErrorCorrectionLevel.L, ModuleSize = 1 },
        // The largest symbol the encoder can produce, filled to the last byte.
        new("Version40AtMaximumCapacity", Repeat('x', 1273)) { ErrorCorrectionLevel = ErrorCorrectionLevel.H, ModuleSize = 1 },

        // ───── Payload helpers ─────
        new("PayloadWifi", QRCodePayload.Wifi("Guest Network", "p@ssw0rd", WifiAuthentication.WPA)),
        new("PayloadVCard", QRCodePayload.VCard("Doe", "John", "+1-555-0100", "john.doe@example.com", "Contoso", "Developer", "https://example.com", "1 Main St")) { ModuleSize = 1 },
        new("PayloadEmail", QRCodePayload.Email("john.doe@example.com", "Hello", "How are you?")),
        new("PayloadGeolocation", QRCodePayload.Geolocation(48.858370, 2.294481)),
        new("PayloadOneTimePassword", QRCodePayload.OneTimePassword(OneTimePasswordType.Totp, "JBSWY3DPEHPK3PXP", "john.doe@example.com", "Contoso")),
        new("PayloadSepaPayment", QRCodePayload.SepaPayment("John Doe", "FR7630006000011234567890189", 12.34m, "AGRIFRPP", remittanceText: "Invoice 42")) { ModuleSize = 1 },

        // ───── Rendering options ─────
        new("RenderSmallestModuleWithoutQuietZone", "SCAN ME") { ModuleSize = 1, QuietZoneModules = 0 },
        new("RenderLargeQuietZone", "SCAN ME") { ModuleSize = 1, QuietZoneModules = 16 },
        new("RenderCustomColors", "SCAN ME") { ModuleSize = 3, DarkColor = Color.FromRgb(0x00, 0x33, 0x66), LightColor = Color.FromRgb(0xFF, 0xF8, 0xE7) },
        new("RenderTransparentLightColor", "SCAN ME") { ModuleSize = 3, LightColor = Color.Transparent },

        // ───── Micro QR and rMQR ─────
        new("MicroQRNumeric", "12345") { Type = QRCodeType.MicroQR, ErrorCorrectionLevel = ErrorCorrectionLevel.L, ModuleSize = 4 },
        new("MicroQRAlphanumeric", "HELLO") { Type = QRCodeType.MicroQR, ModuleSize = 4 },
        new("MicroQRByte", "Hello World!") { Type = QRCodeType.MicroQR, ErrorCorrectionLevel = ErrorCorrectionLevel.L, ModuleSize = 4 },
        new("RMQRUrl", "https://example.com") { Type = QRCodeType.RMQR, ModuleSize = 4 },
        new("RMQRHighErrorCorrection", "AB") { Type = QRCodeType.RMQR, ErrorCorrectionLevel = ErrorCorrectionLevel.H, ModuleSize = 4 },
    ];

    public static Scenario Get(string name)
    {
        foreach (var scenario in All)
        {
            if (string.Equals(scenario.Name, name, StringComparison.Ordinal))
                return scenario;
        }

        throw new InvalidOperationException($"There is no QR code scenario named '{name}'.");
    }

    private static string Repeat(char value, int count) => new(value, count);

    private static string CreatePrintableAscii()
    {
        var buffer = new StringBuilder();
        for (var c = ' '; c <= '~'; c++)
        {
            buffer.Append(c);
        }

        return buffer.ToString();
    }

    /// <summary>
    /// One QR code payload rendered with one set of PNG options.
    /// </summary>
    /// <remarks>
    /// This file has no test-framework dependency on purpose: an external QR code reader (ZXing) can
    /// compile it as-is to check that every scenario below round-trips through a real decoder, which
    /// keeps the snapshots honest about producing images a scanner can actually read.
    /// </remarks>
    internal sealed record Scenario(string Name, string Text)
    {
        /// <summary>Gets the kind of symbol to build. Standard QR unless stated otherwise.</summary>
        public QRCodeType Type { get; init; } = QRCodeType.Standard;

        public ErrorCorrectionLevel ErrorCorrectionLevel { get; init; } = ErrorCorrectionLevel.M;

        public int ModuleSize { get; init; } = 2;

        public int QuietZoneModules { get; init; } = 4;

        public Color DarkColor { get; init; } = Color.Black;

        public Color LightColor { get; init; } = Color.White;

        /// <summary>Gets whether a standard QR code reader is expected to decode the rendered image.</summary>
        /// <remarks>Micro QR and rMQR are outside what most readers, including ZXing, support.</remarks>
        public bool IsDecodable => Type is QRCodeType.Standard;

        public QRCode CreateQRCode()
        {
            return Type switch
            {
                QRCodeType.MicroQR => QRCode.CreateMicroQR(Text, ErrorCorrectionLevel),
                QRCodeType.RMQR => QRCode.CreateRMQR(Text, ErrorCorrectionLevel),
                _ => QRCode.Create(Text, ErrorCorrectionLevel),
            };
        }

        public byte[] CreatePng()
        {
            return CreateQRCode().ToPng(new QRCodePngOptions
            {
                ModuleSize = ModuleSize,
                QuietZoneModules = QuietZoneModules,
                DarkColor = DarkColor,
                LightColor = LightColor,
            });
        }
    }
}
