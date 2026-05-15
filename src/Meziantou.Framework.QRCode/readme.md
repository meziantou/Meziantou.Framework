# Meziantou.Framework.QRCode

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
| Code 128 | `Barcode.CreateCode128(...)` | Supports Code Set B and automatic switching to Code Set C for numeric runs |
| EAN-8 | `Barcode.CreateEan8(...)` | Supports 7/8-digit payloads and optional 2/5-digit add-on extension |
| EAN-13 | `Barcode.CreateEan13(...)` | Supports 12/13-digit payloads and optional 2/5-digit add-on extension |
| UPC-A | `Barcode.CreateUpcA(...)` | Supports 11/12-digit payloads and optional 2/5-digit add-on extension |
| Codabar | `Barcode.CreateCodabar(...)` | Supports standard Codabar payload characters and configurable start/stop guards |
| ITF | `Barcode.CreateItf(...)` | Supports numeric payloads with an even number of digits |

## Usage

### Generate QR codes (Standard, Micro QR, rMQR)

```csharp
// Standard QR code
var standardQr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);

// Micro QR code (smaller, single finder pattern)
var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

// Rectangular Micro QR code (narrow rectangular shape)
var rmqr = QRCode.CreateRMQR("https://www.meziantou.net", ErrorCorrectionLevel.M);

// Format info
Console.WriteLine($"{standardQr.Type} {standardQr.Width}x{standardQr.Height}");
Console.WriteLine($"{microQr.Type} {microQr.Width}x{microQr.Height}");
Console.WriteLine($"{rmqr.Type} {rmqr.Width}x{rmqr.Height}");
```

### Generate barcodes (Code 39, Code 93, Code 128, EAN/UPC, Codabar, ITF)

```csharp
var code39 = Barcode.CreateCode39("ABC-123", includeChecksum: true);
var code93 = Barcode.CreateCode93("ABC123");
var code128 = Barcode.CreateCode128("SKU-123456");
var ean8 = Barcode.CreateEan8("5512345");
var ean13 = Barcode.CreateEan13("400638133393");
var upcA = Barcode.CreateUpcA("03600029145", extension: "12");
var codabar = Barcode.CreateCodabar("40156", startCharacter: 'A', stopCharacter: 'B');
var itf = Barcode.CreateItf("12345678");

Console.WriteLine($"{code39.Type} {code39.Width}x{code39.Height}");
Console.WriteLine($"{code93.Type} {code93.Width}x{code93.Height}");
Console.WriteLine($"{code128.Type} {code128.Width}x{code128.Height}");
Console.WriteLine($"{ean8.Type} {ean8.Width}x{ean8.Height}");
Console.WriteLine($"{ean13.Type} {ean13.Width}x{ean13.Height}");
Console.WriteLine($"{upcA.Type} {upcA.Width}x{upcA.Height}");
Console.WriteLine($"{codabar.Type} {codabar.Width}x{codabar.Height}");
Console.WriteLine($"{itf.Type} {itf.Width}x{itf.Height}");
```

### Render as SVG

```csharp
var svg = standardQr.ToSvg();
var microSvg = microQr.ToSvg(new QRCodeSvgOptions { ModuleSize = 2, QuietZoneModules = 2 });
var rmqrSvg = rmqr.ToSvg(new QRCodeSvgOptions { ModuleSize = 2, QuietZoneModules = 2 });

var customSvg = standardQr.ToSvg(new QRCodeSvgOptions
{
    ModuleSize = 10,
    QuietZoneModules = 4,
    DarkColor = Color.FromRgb(0x00, 0x00, 0x00),
    LightColor = Color.FromRgb(0xff, 0xff, 0xff),
    LogoImageHref = "https://example.com/logo.svg", // Also supports data URIs
    LogoSizePercent = 20,
});

var barcodeSvg = code128.ToSvg(new BarcodeSvgOptions
{
    ModuleWidth = 2,
    ModuleHeight = 80,
    QuietZoneModules = 10,
});
```

Logo images are currently supported in the SVG renderer only.

### Render as PNG

```csharp
var png = standardQr.ToPng();
File.WriteAllBytes("qrcode.png", png);

using var stream = File.Create("micro-qrcode.png");
microQr.WriteToPng(stream, new QRCodePngOptions
{
    ModuleSize = 4,
    QuietZoneModules = 2,
    DarkColor = Color.FromRgb(0x1f, 0x29, 0x37),
    LightColor = Color.FromRgb(0xf9, 0xfa, 0xfb),
});

var barcodePng = code39.ToPng(new BarcodePngOptions
{
    ModuleWidth = 2,
    ModuleHeight = 80,
    DarkColor = Color.FromRgb(0x0f, 0x76, 0x6e),
    LightColor = Color.FromRgb(0xec, 0xfe, 0xff),
});
File.WriteAllBytes("barcode.png", barcodePng);
```

### Render to console

```csharp
standardQr.WriteToConsole();
var standardText = standardQr.ToConsoleString();
var microText = microQr.ToConsoleString(new QRCodeConsoleOptions
{
    QuietZoneModules = 1,
    ModuleWidth = 2,
    ModuleHeight = 2, // 2x bigger output that can look more square on many terminals
});

var rmqrText = rmqr.ToConsoleString(new QRCodeConsoleOptions
{
    QuietZoneModules = 1,
    ModuleWidth = 2,
    ModuleHeight = 2,
});
```

### Content helpers

```csharp
// WiFi
var wifi = QRCodePayload.Wifi("MyNetwork", "password123", WifiAuthentication.WPA);

// vCard
var vcard = QRCodePayload.VCard("Doe", "John", phone: "+1234567890", email: "john@example.com");

// MeCard (compact contact format)
var mecard = QRCodePayload.MeCard("Doe", "John", phone: "+1234567890");

// Email
var email = QRCodePayload.Email("test@example.com", subject: "Hello");

// Phone
var phone = QRCodePayload.Phone("+1234567890");

// SMS
var sms = QRCodePayload.Sms("+1234567890", "Hello!");

// Geolocation
var geo = QRCodePayload.Geolocation(48.8566, 2.3522);

// Calendar event
var evt = QRCodePayload.CalendarEvent("Meeting",
    new DateTime(2025, 6, 15, 14, 0, 0, DateTimeKind.Utc),
    new DateTime(2025, 6, 15, 15, 0, 0, DateTimeKind.Utc),
    location: "Room 42");

// OTP Auth (2FA setup)
var otp = QRCodePayload.OneTimePassword(OneTimePasswordType.Totp,
    "JBSWY3DPEHPK3PXP", "user@example.com", issuer: "MyApp");

// Bitcoin payment
var btc = QRCodePayload.Bitcoin("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", amount: 0.05m);

// SEPA payment (EPC QR)
var sepa = QRCodePayload.SepaPayment("Example", "DE00000000000000000000", 12.50m,
    remittanceText: "Donation");
```
