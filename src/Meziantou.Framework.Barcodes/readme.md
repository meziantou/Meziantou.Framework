# Meziantou.Framework.Barcodes

QR and barcode generation library with SVG and PNG renderers, plus a console renderer for QR codes, and decoders that read QR codes, Data Matrix, PDF417 and Aztec symbols back from their modules. Implements QR code specifications from scratch with support for standard QR, Micro QR, and Rectangular Micro QR (rMQR) codes, Data Matrix (including GS1 Data Matrix), PDF417 and Aztec codes, and linear barcodes from Code 39 to ITF.

## Supported QR Code Types

| Type | Method | Sizes | Description |
|------|--------|-------|-------------|
| Standard QR | `QRCode.Create(...)` | 21x21 to 177x177 | ISO/IEC 18004, versions 1-40 |
| Micro QR | `QRCode.CreateMicroQR(...)` | 11x11 to 17x17 | ISO/IEC 18004, versions M1-M4 |
| rMQR | `QRCode.CreateRMQR(...)` | Rectangular (width > height) | ISO/IEC 23941 |

## Supported 2D Barcode Types

| Type | Method | Sizes | Description |
|------|--------|-------|-------------|
| Data Matrix | `Barcode2D.CreateDataMatrix(...)` | 10x10 to 144x144, rectangular 8x18 to 16x48 | ISO/IEC 16022 ECC 200, with automatic switching between the encodation schemes |
| GS1 Data Matrix | `Barcode2D.CreateGs1DataMatrix(...)` | Same as Data Matrix | GS1 element strings, with the leading FNC1 and the FNC1 separators |
| PDF417 | `Barcode2D.CreatePdf417(...)` | 3 to 90 rows, 1 to 30 columns | ISO/IEC 15438, error correction levels 0 to 8 |
| Aztec | `Barcode2D.CreateAztec(...)` | 15x15 to 151x151 | ISO/IEC 24778, compact and full-range symbols |

## Supported Barcode Types

| Type | Method | Description |
|------|--------|-------------|
| Code 39 | `Barcode.CreateCode39(...)` | Supports standard Code 39 alphabet, optional Mod 43 checksum |
| Code 93 | `Barcode.CreateCode93(...)` | Supports standard and extended Code 93 character encoding |
| Code 128 | `Barcode.CreateCode128(...)` | Supports all ASCII characters, control characters included, with automatic switching between Code Sets A, B and C |
| GS1-128 | `Barcode.CreateGs1128(...)` | Supports GS1 element strings, with the leading FNC1 and the FNC1 separators after variable-length elements; optionally checked against the GS1 dictionary of application identifiers (`Gs1ValidationMode.Dictionary`) |
| EAN-8 | `Barcode.CreateEan8(...)` | Supports 7/8-digit payloads and optional 2/5-digit add-on extension |
| EAN-13 | `Barcode.CreateEan13(...)` | Supports 12/13-digit payloads and optional 2/5-digit add-on extension |
| UPC-A | `Barcode.CreateUpcA(...)` | Supports 11/12-digit payloads and optional 2/5-digit add-on extension |
| UPC-E | `Barcode.CreateUpcE(...)` | Supports 6/7/8-digit payloads, compression of 11/12-digit UPC-A numbers, and optional 2/5-digit add-on extension |
| GS1 DataBar | `Barcode.CreateGs1DataBar(...)`, `CreateGs1DataBarStacked(...)`, `CreateGs1DataBarStackedOmnidirectional(...)`, `CreateGs1DataBarLimited(...)` | Supports 13/14-digit GTINs; the stacked variants have one matrix row per module of height |
| GS1 DataBar Expanded | `Barcode.CreateGs1DataBarExpanded(...)`, `CreateGs1DataBarExpandedStacked(...)` | Supports GS1 element strings, with the compressed encodations of GTINs, weights, dates and prices |
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
var upcE = Barcode.CreateUpcE("0425261");
var dataBar = Barcode.CreateGs1DataBar("0950110153000");

// Type, width in modules and height (a linear barcode is one module high)
Assert.Equal((BarcodeType.Code39, 129, 1), (code39.Type, code39.Width, code39.Height));
Assert.Equal((BarcodeType.Code93, 91, 1), (code93.Type, code93.Width, code93.Height));
Assert.Equal((BarcodeType.Code128, 123, 1), (code128.Type, code128.Width, code128.Height));
Assert.Equal((BarcodeType.Ean8, 67, 1), (ean8.Type, ean8.Width, ean8.Height));
Assert.Equal((BarcodeType.Ean13, 95, 1), (ean13.Type, ean13.Width, ean13.Height));
Assert.Equal((BarcodeType.UpcA, 124, 1), (upcA.Type, upcA.Width, upcA.Height));
Assert.Equal((BarcodeType.Codabar, 71, 1), (codabar.Type, codabar.Width, codabar.Height));
Assert.Equal((BarcodeType.Itf, 81, 1), (itf.Type, itf.Width, itf.Height));
Assert.Equal((BarcodeType.UpcE, 51, 1), (upcE.Type, upcE.Width, upcE.Height));
Assert.Equal((BarcodeType.Gs1DataBar, 96, 1), (dataBar.Type, dataBar.Width, dataBar.Height));

// The human-readable text, with the characters the barcode adds (check digits, add-on, Codabar guards)
Assert.Equal("ABC-123W", code39.Text);
Assert.Equal("55123457", ean8.Text);
Assert.Equal("4006381333931", ean13.Text);
Assert.Equal("036000291452 12", upcA.Text);
Assert.Equal("A40156B", codabar.Text);
Assert.Equal("04252614", upcE.Text);
Assert.Equal("(01)09501101530003", dataBar.Text);
```
<!-- endref -->

### Generate 2D barcodes (Data Matrix, PDF417, Aztec)

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/DataMatrixTests.cs;region=Barcode2D_DataMatrix;format=md-fenced-code-block;dedent=true -->
```csharp
// The smallest square symbol that holds the data
var dataMatrix = Barcode2D.CreateDataMatrix("https://www.meziantou.net");
Assert.Equal((Barcode2DType.DataMatrix, 22, 22), (dataMatrix.Type, dataMatrix.Width, dataMatrix.Height));

// A rectangular symbol, for a narrow space
var rectangle = Barcode2D.CreateDataMatrix("ABC-12345", new DataMatrixOptions { Shape = DataMatrixShape.Rectangle });
Assert.Equal((32, 8), (rectangle.Width, rectangle.Height));

// A symbol of an exact size: the data is padded
var fixedSize = Barcode2D.CreateDataMatrix("42", new DataMatrixOptions { Height = 24, Width = 24 });
Assert.Equal((24, 24), (fixedSize.Width, fixedSize.Height));

// Binary data
var binary = Barcode2D.CreateDataMatrix([0x00, 0xFF, 0x10, 0x80]);
Assert.Equal((14, 14), (binary.Width, binary.Height));
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/Pdf417Tests.cs;region=Barcode2D_Pdf417;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new Pdf417Options
{
    Columns = 4,       // 4 data columns: 17 × (4 + 4) + 1 = 137 modules wide
    SecurityLevel = 3, // 16 error correction codewords
    RowHeight = 3,     // each row of the symbol is 3 modules high
};
var pdf417 = Barcode2D.CreatePdf417("Hello, PDF417!", options);

Assert.Equal(Barcode2DType.Pdf417, pdf417.Type);
Assert.Equal(137, pdf417.Width);
Assert.Equal(21, pdf417.Height); // 7 rows of 3 modules
string svg = pdf417.ToSvg();
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/AztecTests.cs;region=Barcode2D_Aztec;format=md-fenced-code-block;dedent=true -->
```csharp
var barcode = Barcode2D.CreateAztec("Hello, World!", new AztecOptions
{
    ErrorCorrectionPercent = 50, // 50% of the symbol and 3 codewords are error correction
    Compact = true, // 1 to 4 layers: 15 × 15 to 27 × 27 modules
});

Assert.Equal(Barcode2DType.Aztec, barcode.Type);
Assert.Equal(19, barcode.Width);
Assert.Equal(19, barcode.Height);
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

### Read QR codes and 2D barcodes

`QRCodeDecoder` reads the data of standard QR codes, Micro QR codes and rMQR codes from their modules, with error correction, ECI designators and structured append. To read QR codes from images, use `Meziantou.Framework.Imaging.Barcodes`.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs;region=QRCodeDecoder_Decode;format=md-fenced-code-block;dedent=true -->
```csharp
var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.M);

var result = QRCodeDecoder.Decode(qrCode);

Assert.Equal("https://www.meziantou.net", result.Text);
Assert.Equal((QRCodeType.Standard, 2, ErrorCorrectionLevel.M), (result.Type, result.Version, result.ErrorCorrectionLevel));
```
<!-- endref -->

`Barcode2DDecoder` reads Data Matrix, PDF417 and Aztec symbols from their modules, in any orientation.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/DataMatrixTests.cs;region=Barcode2DDecoder_Decode;format=md-fenced-code-block;dedent=true -->
```csharp
var dataMatrix = Barcode2D.CreateDataMatrix("https://www.meziantou.net");

var result = Barcode2DDecoder.Decode(dataMatrix);

Assert.Equal("https://www.meziantou.net", result.Text);
Assert.Equal(Barcode2DType.DataMatrix, result.Type);
```
<!-- endref -->
