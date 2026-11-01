# Meziantou.Framework.Barcodes

QR and barcode generation library with SVG and PNG renderers, plus a console renderer for QR codes. Implements QR code specifications from scratch with support for standard QR, Micro QR, and Rectangular Micro QR (rMQR) codes, and supports Code 39 / Code 128 linear barcodes.

## Supported QR Code Types

| Type | Method | Sizes | Description |
|------|--------|-------|-------------|
| Standard QR | `QRCode.Create(...)` | 21x21 to 177x177 | ISO/IEC 18004, versions 1-40 |
| Micro QR | `QRCode.CreateMicroQR(...)` | 11x11 to 17x17 | ISO/IEC 18004, versions M1-M4 |
| rMQR | `QRCode.CreateRMQR(...)` | Rectangular (width > height) | ISO/IEC 23941 |

## Supported Barcode Types

| Type | Method | Description |
|------|--------|-------------|
| Code 39 | `Barcode.CreateCode39(...)` | Supports standard Code 39 alphabet, optional Mod 43 checksum |
| Code 93 | `Barcode.CreateCode93(...)` | Supports standard and extended Code 93 character encoding |
| Code 128 | `Barcode.CreateCode128(...)` | Supports all ASCII characters, control characters included, with automatic switching between Code Sets A, B and C |
| EAN-8 | `Barcode.CreateEan8(...)` | Supports 7/8-digit payloads and optional 2/5-digit add-on extension |
| EAN-13 | `Barcode.CreateEan13(...)` | Supports 12/13-digit payloads and optional 2/5-digit add-on extension |
| UPC-A | `Barcode.CreateUpcA(...)` | Supports 11/12-digit payloads and optional 2/5-digit add-on extension |
| Codabar | `Barcode.CreateCodabar(...)` | Supports standard Codabar payload characters and configurable start/stop guards |
| ITF | `Barcode.CreateItf(...)` | Supports numeric payloads with an even number of digits |

## Usage

The types are in the `Meziantou.Framework.Barcodes` namespace.

### Generate QR codes (Standard, Micro QR, rMQR)

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs;region=QRCode_Create;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

### Generate barcodes (Code 39, Code 93, Code 128, EAN/UPC, Codabar, ITF)

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCodeAdditionalTypesTests.cs;region=Barcode_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var code39 = Barcode.CreateCode39("ABC-123", includeChecksum: true);
var code93 = Barcode.CreateCode93("ABC123");
var code128 = Barcode.CreateCode128("SKU-123456");
var ean8 = Barcode.CreateEan8("5512345");
var ean13 = Barcode.CreateEan13("400638133393");
var upcA = Barcode.CreateUpcA("03600029145", extension: "12");
var codabar = Barcode.CreateCodabar("40156", startCharacter: 'A', stopCharacter: 'B');
var itf = Barcode.CreateItf("12345678");

// Type, width in modules and height (a linear barcode is one module high)
Assert.Equal((BarcodeType.Code39, 129, 1), (code39.Type, code39.Width, code39.Height));
Assert.Equal((BarcodeType.Code93, 91, 1), (code93.Type, code93.Width, code93.Height));
Assert.Equal((BarcodeType.Code128, 123, 1), (code128.Type, code128.Width, code128.Height));
Assert.Equal((BarcodeType.Ean8, 67, 1), (ean8.Type, ean8.Width, ean8.Height));
Assert.Equal((BarcodeType.Ean13, 95, 1), (ean13.Type, ean13.Width, ean13.Height));
Assert.Equal((BarcodeType.UpcA, 124, 1), (upcA.Type, upcA.Width, upcA.Height));
Assert.Equal((BarcodeType.Codabar, 71, 1), (codabar.Type, codabar.Width, codabar.Height));
Assert.Equal((BarcodeType.Itf, 81, 1), (itf.Type, itf.Width, itf.Height));

// The human-readable text, with the characters the barcode adds (check digits, add-on, Codabar guards)
Assert.Equal("ABC-123W", code39.Text);
Assert.Equal("55123457", ean8.Text);
Assert.Equal("4006381333931", ean13.Text);
Assert.Equal("036000291452 12", upcA.Text);
Assert.Equal("A40156B", codabar.Text);
```
<!-- endref -->

### Render as SVG

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeSvgRendererTests.cs;region=QRCode_ToSvg;format=md-fenced-code-block;dedent=true -->
```csharp
var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);
var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);
var barcode = Barcode.CreateCode128("SKU-123456");

var svg = qrCode.ToSvg();
Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 330 330\">", svg);

var microSvg = microQr.ToSvg(new QRCodeSvgOptions { ModuleSize = 2, QuietZoneModules = 2 });
Assert.Contains("viewBox=\"0 0 30 30\"", microSvg);

var customSvg = qrCode.ToSvg(new QRCodeSvgOptions
{
    ModuleSize = 10,
    QuietZoneModules = 4,
    DarkColor = Color.FromRgb(0x00, 0x00, 0x00),
    LightColor = Color.FromRgb(0xff, 0xff, 0xff),
    LogoImageHref = "https://example.com/logo.svg", // Also supports data URIs
    LogoSizePercent = 20,
});
Assert.Contains("<image href=\"https://example.com/logo.svg\"", customSvg);

var barcodeSvg = barcode.ToSvg(new BarcodeSvgOptions
{
    ModuleWidth = 2,
    ModuleHeight = 80,
    QuietZoneModules = 10,
});
Assert.Contains("viewBox=\"0 0 286 80\"", barcodeSvg);
```
<!-- endref -->

Logo images are currently supported in the SVG renderer only.

### Render as PNG

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodePngRendererTests.cs;region=QRCode_ToPng;format=md-fenced-code-block;dedent=true -->
```csharp
var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);
var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);
var barcode = Barcode.CreateCode39("ABC-123", includeChecksum: true);

// The content of a PNG file, to save with File.WriteAllBytes("qrcode.png", png)
var png = qrCode.ToPng();
Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]); // the PNG signature

// Any writable stream, such as File.Create("micro-qrcode.png")
using var stream = new MemoryStream();
microQr.WriteToPng(stream, new QRCodePngOptions
{
    ModuleSize = 4,
    QuietZoneModules = 2,
    DarkColor = Color.FromRgb(0x1f, 0x29, 0x37),
    LightColor = Color.FromRgb(0xf9, 0xfa, 0xfb),
});

var barcodePng = barcode.ToPng(new BarcodePngOptions
{
    ModuleWidth = 2,
    ModuleHeight = 80,
    DarkColor = Color.FromRgb(0x0f, 0x76, 0x6e),
    LightColor = Color.FromRgb(0xec, 0xfe, 0xff),
});
```
<!-- endref -->

### Render to console

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeConsoleRendererTests.cs;region=QRCode_ToConsoleString;format=md-fenced-code-block;dedent=true -->
```csharp
var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);
var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

qrCode.WriteToConsole();
var text = qrCode.ToConsoleString();

var microText = microQr.ToConsoleString(new QRCodeConsoleOptions
{
    QuietZoneModules = 1,
    ModuleWidth = 2,
    ModuleHeight = 2, // 2x bigger output that can look more square on many terminals
});

Assert.Equal("""

      ██████████████  ██  ██
      ██          ██  ████
      ██  ██████  ██  ██
      ██  ██████  ██
      ██  ██████  ██  ██████
      ██          ██    ████
      ██████████████  ██
                        ████
      ████    ██████    ████
        ██  ██      ████
      ████████          ████

    """, microText, ignoreLineEndingDifferences: true);
```
<!-- endref -->

### Content helpers

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodePayloadTests.cs;region=QRCodePayload_Create;format=md-fenced-code-block;dedent=true -->
```csharp
// WiFi
var wifi = QRCodePayload.Wifi("MyNetwork", "password123", WifiAuthentication.WPA);
Assert.Equal("WIFI:T:WPA;S:MyNetwork;P:password123;;", wifi);

// vCard
var vcard = QRCodePayload.VCard("Doe", "John", phone: "+1234567890", email: "john@example.com");
Assert.StartsWith("BEGIN:VCARD\r\nVERSION:3.0\r\nN:Doe;John;;;\r\nFN:John Doe\r\n", vcard);

// MeCard (compact contact format)
var mecard = QRCodePayload.MeCard("Doe", "John", phone: "+1234567890");
Assert.Equal("MECARD:N:Doe,John;TEL:+1234567890;;", mecard);

// Email
var email = QRCodePayload.Email("test@example.com", subject: "Hello");
Assert.Equal("mailto:test@example.com?subject=Hello", email);

// Phone
var phone = QRCodePayload.Phone("+1234567890");
Assert.Equal("tel:+1234567890", phone);

// SMS
var sms = QRCodePayload.Sms("+1234567890", "Hello!");
Assert.Equal("smsto:+1234567890:Hello!", sms);

// Geolocation
var geo = QRCodePayload.Geolocation(48.8566, 2.3522);
Assert.Equal("geo:48.8566,2.3522", geo);

// Calendar event
var evt = QRCodePayload.CalendarEvent("Meeting",
    new DateTime(2025, 6, 15, 14, 0, 0, DateTimeKind.Utc),
    new DateTime(2025, 6, 15, 15, 0, 0, DateTimeKind.Utc),
    location: "Room 42");
Assert.Contains("DTSTART:20250615T140000Z\r\n", evt);

// OTP Auth (2FA setup)
var otp = QRCodePayload.OneTimePassword(OneTimePasswordType.Totp,
    "JBSWY3DPEHPK3PXP", "user@example.com", issuer: "MyApp");
Assert.Equal("otpauth://totp/MyApp:user%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=MyApp", otp);

// Bitcoin payment
var btc = QRCodePayload.Bitcoin("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", amount: 0.05m);
Assert.Equal("bitcoin:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa?amount=0.05", btc);

// SEPA payment (EPC QR)
var sepa = QRCodePayload.SepaPayment("Example", "DE00000000000000000000", 12.50m,
    remittanceText: "Donation");
Assert.StartsWith("BCD\n002\n1\nSCT\n", sepa);
```
<!-- endref -->
