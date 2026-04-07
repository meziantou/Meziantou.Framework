# Meziantou.Framework.QRCode

QR code generation library with SVG and console renderers. Implements the QR code specification from scratch with support for standard QR, Micro QR, and Rectangular Micro QR (rMQR) codes.

## Supported QR Code Types

| Type | Method | Sizes | Description |
|------|--------|-------|-------------|
| Standard QR | `QRCode.Create(...)` | 21x21 to 177x177 | ISO/IEC 18004, versions 1-40 |
| Micro QR | `QRCode.CreateMicroQR(...)` | 11x11 to 17x17 | ISO/IEC 18004, versions M1-M4 |
| rMQR | `QRCode.CreateRMQR(...)` | Rectangular (e.g., 7x43) | ISO/IEC 23941 |

## Usage

### Generate a QR code

```csharp
// Standard QR code
var qr = QRCode.Create("https://example.com");
var qr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);

// Micro QR code (smaller, single finder pattern)
var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

// Rectangular Micro QR code (narrow rectangular shape)
var rmqr = QRCode.CreateRMQR("https://example.com", ErrorCorrectionLevel.M);
```

### Render as SVG

```csharp
var svg = qr.ToSvg();
var svg = qr.ToSvg(new QRCodeSvgOptions
{
    ModuleSize = 10,
    QuietZoneModules = 4,
    DarkColor = "#000000",
    LightColor = "#ffffff",
});
```

### Render to console

```csharp
qr.WriteToConsole();
var text = qr.ToConsoleString();
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
var sepa = QRCodePayload.SepaPayment("Red Cross", "DE89370400440532013000", 12.50m,
    remittanceText: "Donation");
```
