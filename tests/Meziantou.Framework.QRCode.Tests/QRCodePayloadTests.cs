using Meziantou.Framework.InlineSnapshotTesting;
using Xunit;

namespace Meziantou.Framework.Tests;

public class QRCodePayloadTests
{
    [Fact]
    public void Wifi_WPA_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Wifi("MyNetwork", "MyPassword123");

        InlineSnapshot.Validate(payload, "WIFI:T:WPA;S:MyNetwork;P:MyPassword123;;");
    }

    [Fact]
    public void Wifi_WEP_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Wifi("MyNetwork", "MyKey", WifiAuthentication.WEP);

        InlineSnapshot.Validate(payload, "WIFI:T:WEP;S:MyNetwork;P:MyKey;;");
    }

    [Fact]
    public void Wifi_Open_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Wifi("OpenNet", authentication: WifiAuthentication.None);

        InlineSnapshot.Validate(payload, "WIFI:T:nopass;S:OpenNet;;");
    }

    [Fact]
    public void Wifi_Hidden_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Wifi("HiddenNet", "pass", hidden: true);

        InlineSnapshot.Validate(payload, "WIFI:T:WPA;S:HiddenNet;P:pass;H:true;;");
    }

    [Fact]
    public void Wifi_SpecialCharactersEscaped()
    {
        var payload = QRCodePayload.Wifi("My;Net", "pass:word");

        InlineSnapshot.Validate(payload, """WIFI:T:WPA;S:My\;Net;P:pass\:word;;""");
    }

    [Fact]
    public void Wifi_PasswordWithBackslashAndQuote()
    {
        var payload = QRCodePayload.Wifi("Net", """p\a"ss""");

        InlineSnapshot.Validate(payload, """WIFI:T:WPA;S:Net;P:p\\a\"ss;;""");
    }

    [Fact]
    public void Wifi_ThrowsWhenSsidIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.Wifi(null!));
    }

    [Fact]
    public void VCard_FullContact_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.VCard("Doe", "John",
            phone: "+1234567890", email: "john@example.com",
            organization: "ACME", title: "Engineer",
            url: "https://example.com", address: "123 Main St");

        InlineSnapshot.Validate(payload, """
            BEGIN:VCARD
            VERSION:3.0
            N:Doe;John;;;
            FN:John Doe
            TEL:+1234567890
            EMAIL:john@example.com
            ORG:ACME
            TITLE:Engineer
            URL:https://example.com
            ADR:;;123 Main St;;;;
            END:VCARD
            """);
    }

    [Fact]
    public void VCard_MinimalContact_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.VCard("Doe");

        InlineSnapshot.Validate(payload, """
            BEGIN:VCARD
            VERSION:3.0
            N:Doe;;;;
            FN:Doe
            END:VCARD
            """);
    }

    [Fact]
    public void VCard_ThrowsWhenLastNameIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.VCard(null!));
    }

    [Fact]
    public void Email_WithSubjectAndBody_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Email("test@example.com", "Hello World", "Message body");

        InlineSnapshot.Validate(payload, "mailto:test@example.com?subject=Hello%20World&body=Message%20body");
    }

    [Fact]
    public void Email_AddressOnly_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Email("test@example.com");

        InlineSnapshot.Validate(payload, "mailto:test@example.com");
    }

    [Fact]
    public void Email_WithSubjectOnly_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Email("test@example.com", subject: "Hello");

        InlineSnapshot.Validate(payload, "mailto:test@example.com?subject=Hello");
    }

    [Fact]
    public void Email_WithBodyOnly_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Email("test@example.com", body: "Body text");

        InlineSnapshot.Validate(payload, "mailto:test@example.com?body=Body%20text");
    }

    [Fact]
    public void Email_ThrowsWhenAddressIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.Email(null!));
    }

    [Fact]
    public void Phone_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Phone("+1234567890");

        InlineSnapshot.Validate(payload, "tel:+1234567890");
    }

    [Fact]
    public void Phone_ThrowsWhenNumberIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.Phone(null!));
    }

    [Fact]
    public void Sms_WithMessage_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Sms("+1234567890", "Hello!");

        InlineSnapshot.Validate(payload, "smsto:+1234567890:Hello!");
    }

    [Fact]
    public void Sms_NumberOnly_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Sms("+1234567890");

        InlineSnapshot.Validate(payload, "smsto:+1234567890");
    }

    [Fact]
    public void Sms_ThrowsWhenNumberIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.Sms(null!));
    }

    [Fact]
    public void Geolocation_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Geolocation(48.8566, 2.3522);

        InlineSnapshot.Validate(payload, "geo:48.8566,2.3522");
    }

    [Fact]
    public void Geolocation_NegativeCoordinates()
    {
        var payload = QRCodePayload.Geolocation(-33.8688, 151.2093);

        InlineSnapshot.Validate(payload, "geo:-33.8688,151.2093");
    }

    [Fact]
    public void CalendarEvent_Full_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.CalendarEvent(
            "Team Meeting",
            new DateTime(2025, 6, 15, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 15, 15, 0, 0, DateTimeKind.Utc),
            location: "Room 42",
            description: "Weekly sync");

        InlineSnapshot.Validate(payload, """
            BEGIN:VEVENT
            SUMMARY:Team Meeting
            DTSTART:20250615T140000Z
            DTEND:20250615T150000Z
            LOCATION:Room 42
            DESCRIPTION:Weekly sync
            END:VEVENT
            """);
    }

    [Fact]
    public void CalendarEvent_Minimal_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.CalendarEvent(
            "Meeting",
            new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));

        InlineSnapshot.Validate(payload, """
            BEGIN:VEVENT
            SUMMARY:Meeting
            DTSTART:20250101T090000Z
            DTEND:20250101T100000Z
            END:VEVENT
            """);
    }

    [Fact]
    public void CalendarEvent_ThrowsWhenSummaryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.CalendarEvent(
            null!,
            new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void MeCard_FullContact_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.MeCard("Doe", "John",
            phone: "+1234567890", email: "john@example.com",
            organization: "ACME", url: "https://example.com",
            address: "123 Main St", note: "A friend");

        InlineSnapshot.Validate(payload, "MECARD:N:Doe,John;TEL:+1234567890;EMAIL:john@example.com;ORG:ACME;URL:https\\://example.com;ADR:123 Main St;NOTE:A friend;;");
    }

    [Fact]
    public void MeCard_MinimalContact_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.MeCard("Doe");

        InlineSnapshot.Validate(payload, "MECARD:N:Doe;;");
    }

    [Fact]
    public void MeCard_SpecialCharactersEscaped()
    {
        var payload = QRCodePayload.MeCard("O;Brien", "John");

        InlineSnapshot.Validate(payload, """MECARD:N:O\;Brien,John;;""");
    }

    [Fact]
    public void MeCard_ThrowsWhenLastNameIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.MeCard(null!));
    }

    [Fact]
    public void OneTimePassword_Totp_Default_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.OneTimePassword(
            OneTimePasswordType.Totp,
            "JBSWY3DPEHPK3PXP",
            "user@example.com",
            issuer: "GitHub");

        InlineSnapshot.Validate(payload, "otpauth://totp/GitHub:user%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub");
    }

    [Fact]
    public void OneTimePassword_Totp_CustomOptions_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.OneTimePassword(
            OneTimePasswordType.Totp,
            "JBSWY3DPEHPK3PXP",
            "user@example.com",
            issuer: "MyApp",
            algorithm: OneTimePasswordAlgorithm.SHA256,
            digits: 8,
            period: 60);

        InlineSnapshot.Validate(payload, "otpauth://totp/MyApp:user%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=MyApp&algorithm=SHA256&digits=8&period=60");
    }

    [Fact]
    public void OneTimePassword_Hotp_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.OneTimePassword(
            OneTimePasswordType.Hotp,
            "JBSWY3DPEHPK3PXP",
            "user@example.com",
            issuer: "Service",
            counter: 42);

        InlineSnapshot.Validate(payload, "otpauth://hotp/Service:user%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Service&counter=42");
    }

    [Fact]
    public void OneTimePassword_NoIssuer_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.OneTimePassword(
            OneTimePasswordType.Totp,
            "JBSWY3DPEHPK3PXP",
            "user@example.com");

        InlineSnapshot.Validate(payload, "otpauth://totp/user%40example.com?secret=JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void OneTimePassword_ThrowsWhenSecretIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.OneTimePassword(
            OneTimePasswordType.Totp, null!, "user@example.com"));
    }

    [Fact]
    public void OneTimePassword_ThrowsWhenAccountNameIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.OneTimePassword(
            OneTimePasswordType.Totp, "JBSWY3DPEHPK3PXP", null!));
    }

    [Fact]
    public void Bitcoin_AddressOnly_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Bitcoin("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");

        InlineSnapshot.Validate(payload, "bitcoin:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
    }

    [Fact]
    public void Bitcoin_WithAmount_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Bitcoin("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", amount: 0.05m);

        InlineSnapshot.Validate(payload, "bitcoin:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa?amount=0.05");
    }

    [Fact]
    public void Bitcoin_Full_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Bitcoin("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
            amount: 1.5m, label: "Donation", message: "Thanks!");

        InlineSnapshot.Validate(payload, "bitcoin:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa?amount=1.5&label=Donation&message=Thanks%21");
    }

    [Fact]
    public void Bitcoin_ThrowsWhenAddressIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.Bitcoin(null!));
    }

    [Fact]
    public void SepaPayment_Full_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.SepaPayment(
            "Red Cross",
            "DE89370400440532013000",
            12.50m,
            bic: "COBADEFFXXX",
            remittanceText: "Donation");

        InlineSnapshot.Validate(payload, """
            BCD
            002
            1
            SCT
            COBADEFFXXX
            Red Cross
            DE89370400440532013000
            EUR12.50


            Donation

            """);
    }

    [Fact]
    public void SepaPayment_Minimal_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.SepaPayment(
            "John Doe",
            "NL91ABNA0417164300",
            100.00m);

        InlineSnapshot.Validate(payload, """
            BCD
            002
            1
            SCT

            John Doe
            NL91ABNA0417164300
            EUR100.00




            """);
    }

    [Fact]
    public void SepaPayment_WithReference_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.SepaPayment(
            "Company",
            "DE89370400440532013000",
            50.00m,
            remittanceReference: "RF18539007547034");

        InlineSnapshot.Validate(payload, """
            BCD
            002
            1
            SCT

            Company
            DE89370400440532013000
            EUR50.00

            RF18539007547034


            """);
    }

    [Fact]
    public void SepaPayment_ThrowsWhenBothRemittanceFieldsSet()
    {
        Assert.Throws<ArgumentException>(() => QRCodePayload.SepaPayment(
            "Name", "DE89370400440532013000", 10m,
            remittanceReference: "RF18", remittanceText: "Text"));
    }

    [Fact]
    public void SepaPayment_ThrowsWhenBeneficiaryNameIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.SepaPayment(null!, "IBAN", 10m));
    }

    [Fact]
    public void SepaPayment_ThrowsWhenIbanIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodePayload.SepaPayment("Name", null!, 10m));
    }

    // ───── Edge cases for encoding and special characters ─────

    [Fact]
    public void Email_SpecialCharactersInSubjectAreEncoded()
    {
        var payload = QRCodePayload.Email("test@example.com", subject: "Hello & World", body: "a=b&c=d");

        InlineSnapshot.Validate(payload, "mailto:test@example.com?subject=Hello%20%26%20World&body=a%3Db%26c%3Dd");
    }

    [Fact]
    public void VCard_WithAddress_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.VCard("Doe", address: "123 Main St");

        InlineSnapshot.Validate(payload, """
            BEGIN:VCARD
            VERSION:3.0
            N:Doe;;;;
            FN:Doe
            ADR:;;123 Main St;;;;
            END:VCARD
            """);
    }

    [Fact]
    public void Wifi_CommaInSsid()
    {
        var payload = QRCodePayload.Wifi("Net,Work", "pass");

        InlineSnapshot.Validate(payload, """WIFI:T:WPA;S:Net\,Work;P:pass;;""");
    }

    [Fact]
    public void MeCard_WithUrl_ColonEscaped()
    {
        var payload = QRCodePayload.MeCard("Doe", url: "https://example.com");

        InlineSnapshot.Validate(payload, """MECARD:N:Doe;URL:https\://example.com;;""");
    }

    [Fact]
    public void OneTimePassword_SHA512_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.OneTimePassword(
            OneTimePasswordType.Totp,
            "JBSWY3DPEHPK3PXP",
            "user@example.com",
            algorithm: OneTimePasswordAlgorithm.SHA512);

        InlineSnapshot.Validate(payload, "otpauth://totp/user%40example.com?secret=JBSWY3DPEHPK3PXP&algorithm=SHA512");
    }

    [Fact]
    public void Bitcoin_LabelWithSpaces_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Bitcoin("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", label: "My Wallet");

        InlineSnapshot.Validate(payload, "bitcoin:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa?label=My%20Wallet");
    }

    [Fact]
    public void Bitcoin_MessageOnly_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.Bitcoin("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", message: "Thanks");

        InlineSnapshot.Validate(payload, "bitcoin:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa?message=Thanks");
    }

    [Fact]
    public void SepaPayment_WithInformation_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.SepaPayment("Doe", "DE89370400440532013000", 25.00m, information: "Thank you");

        InlineSnapshot.Validate(payload, """
            BCD
            002
            1
            SCT

            Doe
            DE89370400440532013000
            EUR25.00



            Thank you
            """);
    }

    [Fact]
    public void Geolocation_WholeNumbers()
    {
        var payload = QRCodePayload.Geolocation(0, 0);

        InlineSnapshot.Validate(payload, "geo:0,0");
    }

    [Fact]
    public void CalendarEvent_WithDescription_ProducesExpectedPayload()
    {
        var payload = QRCodePayload.CalendarEvent(
            "Review",
            new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 3, 1, 11, 0, 0, DateTimeKind.Utc),
            description: "Quarterly review");

        InlineSnapshot.Validate(payload, """
            BEGIN:VEVENT
            SUMMARY:Review
            DTSTART:20250301T100000Z
            DTEND:20250301T110000Z
            DESCRIPTION:Quarterly review
            END:VEVENT
            """);
    }
}
