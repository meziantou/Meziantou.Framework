namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to create QR code payload strings for common content types.
/// </summary>
public static class QRCodePayload
{
    /// <summary>
    /// Creates a WiFi network configuration payload.
    /// </summary>
    /// <param name="ssid">The network SSID.</param>
    /// <param name="password">The network password. Can be null for open networks.</param>
    /// <param name="authentication">The authentication type.</param>
    /// <param name="hidden">Whether the network is hidden.</param>
    /// <returns>A string formatted as a WiFi QR code payload.</returns>
    public static string Wifi(string ssid, string? password = null, WifiAuthentication authentication = WifiAuthentication.WPA, bool hidden = false)
    {
        ArgumentNullException.ThrowIfNull(ssid);

        var sb = new StringBuilder();
        sb.Append("WIFI:");
        sb.Append("T:");
        sb.Append(authentication switch
        {
            WifiAuthentication.None => "nopass",
            WifiAuthentication.WEP => "WEP",
            WifiAuthentication.WPA => "WPA",
            _ => throw new ArgumentOutOfRangeException(nameof(authentication), authentication, "Unsupported authentication type."),
        });
        sb.Append(";S:");
        AppendWifiEscaped(sb, ssid);
        sb.Append(';');
        if (password is not null)
        {
            sb.Append("P:");
            AppendWifiEscaped(sb, password);
            sb.Append(';');
        }

        if (hidden)
        {
            sb.Append("H:true;");
        }

        sb.Append(';');
        return sb.ToString();
    }

    /// <summary>
    /// Creates a vCard 3.0 contact payload.
    /// </summary>
    /// <param name="lastName">The contact's last name.</param>
    /// <param name="firstName">The contact's first name.</param>
    /// <param name="phone">The contact's phone number.</param>
    /// <param name="email">The contact's email address.</param>
    /// <param name="organization">The contact's organization.</param>
    /// <param name="title">The contact's title.</param>
    /// <param name="url">The contact's website URL.</param>
    /// <param name="address">The contact's address.</param>
    /// <returns>A string formatted as a vCard 3.0 payload.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "The URL is embedded as a string in the vCard payload")]
    public static string VCard(string lastName, string? firstName = null, string? phone = null, string? email = null, string? organization = null, string? title = null, string? url = null, string? address = null)
    {
        ArgumentNullException.ThrowIfNull(lastName);

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD").Append(CrLf);
        sb.Append("VERSION:3.0").Append(CrLf);
        sb.Append("N:");
        AppendVCardEscaped(sb, lastName);
        sb.Append(';');
        AppendVCardEscaped(sb, firstName);
        sb.Append(";;;").Append(CrLf);

        sb.Append("FN:");
        if (firstName is not null)
        {
            AppendVCardEscaped(sb, firstName);
            sb.Append(' ');
        }

        AppendVCardEscaped(sb, lastName);
        sb.Append(CrLf);

        AppendVCardProperty(sb, "TEL:", phone);
        AppendVCardProperty(sb, "EMAIL:", email);
        AppendVCardProperty(sb, "ORG:", organization);
        AppendVCardProperty(sb, "TITLE:", title);
        AppendVCardProperty(sb, "URL:", url);

        if (address is not null)
        {
            sb.Append("ADR:;;");
            AppendVCardEscaped(sb, address);
            sb.Append(";;;;").Append(CrLf);
        }

        sb.Append("END:VCARD");
        return sb.ToString();
    }

    /// <summary>
    /// Creates an email (mailto:) payload.
    /// </summary>
    /// <param name="address">The email address.</param>
    /// <param name="subject">The email subject.</param>
    /// <param name="body">The email body.</param>
    /// <returns>A string formatted as a mailto URI.</returns>
    public static string Email(string address, string? subject = null, string? body = null)
    {
        ArgumentNullException.ThrowIfNull(address);

        var sb = new StringBuilder();
        sb.Append("mailto:");
        sb.Append(EscapeMailAddress(address));

        if (subject is not null || body is not null)
        {
            var separator = '?';
            if (subject is not null)
            {
                sb.Append(separator);
                sb.Append("subject=");
                sb.Append(Uri.EscapeDataString(subject));
                separator = '&';
            }

            if (body is not null)
            {
                sb.Append(separator);
                sb.Append("body=");
                sb.Append(Uri.EscapeDataString(body));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates a phone number (tel:) payload.
    /// </summary>
    /// <param name="number">The phone number.</param>
    /// <returns>A string formatted as a tel URI.</returns>
    public static string Phone(string number)
    {
        ArgumentNullException.ThrowIfNull(number);

        return "tel:" + EscapePhoneNumber(number);
    }

    /// <summary>
    /// Creates an SMS payload.
    /// </summary>
    /// <param name="number">The phone number.</param>
    /// <param name="message">The message text.</param>
    /// <returns>A string formatted as an SMS payload.</returns>
    public static string Sms(string number, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(number);

        // The number is escaped because a ':' in it would shift the recipient/message split.
        // The message is the trailing free text, so it needs no escaping.
        if (message is not null)
        {
            return "smsto:" + EscapePhoneNumber(number) + ":" + message;
        }

        return "smsto:" + EscapePhoneNumber(number);
    }

    /// <summary>
    /// Percent-encodes a mail address, keeping the <c>@</c> readable.
    /// </summary>
    /// <remarks>
    /// The optional parameters were already escaped, but the address was not, so a '?' in it
    /// started the query string early and let the caller-supplied address inject its own
    /// parameters - <c>Email("a@b.com?bcc=c@d.com", "Hi")</c> produced a mailto whose bcc won
    /// and whose intended subject was swallowed into it.
    /// </remarks>
    private static string EscapeMailAddress(string address)
    {
        // An addr-spec has a single '@' between the local part and the domain. Only that one is
        // left readable; any other '@' is not a separator, so it stays encoded.
        var separatorIndex = address.AsSpan().LastIndexOf('@');
        if (separatorIndex < 0)
        {
            return Uri.EscapeDataString(address);
        }

        return Uri.EscapeDataString(address[..separatorIndex]) + "@" + Uri.EscapeDataString(address[(separatorIndex + 1)..]);
    }

    /// <summary>
    /// Percent-encodes a phone number, keeping the leading <c>+</c> that <c>tel:</c> needs.
    /// </summary>
    private static string EscapePhoneNumber(string number)
    {
        // RFC 3966 allows '+' only as the leading international prefix, so only a leading one is
        // left readable; a '+' anywhere else is not structural and stays encoded.
        if (number is ['+', ..])
        {
            return "+" + Uri.EscapeDataString(number[1..]);
        }

        return Uri.EscapeDataString(number);
    }

    /// <summary>
    /// Creates a geolocation payload.
    /// </summary>
    /// <param name="latitude">The latitude in decimal degrees.</param>
    /// <param name="longitude">The longitude in decimal degrees.</param>
    /// <returns>A string formatted as a geo URI.</returns>
    public static string Geolocation(double latitude, double longitude)
    {
        return string.Create(CultureInfo.InvariantCulture, $"geo:{latitude},{longitude}");
    }

    /// <summary>
    /// Creates a calendar event (VEVENT) payload.
    /// </summary>
    /// <param name="summary">The event summary/title.</param>
    /// <param name="start">The start date and time (UTC).</param>
    /// <param name="end">The end date and time (UTC).</param>
    /// <param name="location">The event location.</param>
    /// <param name="description">The event description.</param>
    /// <returns>A string formatted as an iCalendar VEVENT.</returns>
    public static string CalendarEvent(string summary, DateTime start, DateTime end, string? location = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var sb = new StringBuilder();
        sb.Append("BEGIN:VEVENT").Append(CrLf);
        AppendVCardProperty(sb, "SUMMARY:", summary);
        sb.Append("DTSTART:").Append(start.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)).Append(CrLf);
        sb.Append("DTEND:").Append(end.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)).Append(CrLf);

        AppendVCardProperty(sb, "LOCATION:", location);
        AppendVCardProperty(sb, "DESCRIPTION:", description);

        sb.Append("END:VEVENT");
        return sb.ToString();
    }

    /// <summary>
    /// vCard 3.0 and iCalendar both require CRLF between content lines (RFC 6350 and RFC 5545),
    /// so the separator is written explicitly instead of through <see cref="StringBuilder.AppendLine()"/>,
    /// whose separator depends on the platform.
    /// </summary>
    private const string CrLf = "\r\n";

    private static void AppendVCardProperty(StringBuilder sb, string name, string? value)
    {
        if (value is null)
        {
            return;
        }

        sb.Append(name);
        AppendVCardEscaped(sb, value);
        sb.Append(CrLf);
    }

    /// <summary>
    /// Escapes a vCard 3.0 / iCalendar text value per RFC 6350 section 3.4 and RFC 5545 section 3.3.11.
    /// </summary>
    /// <remarks>
    /// Without this, a value containing a new line injects arbitrary properties into the card, and
    /// the far more common case of a name or address containing <c>;</c> or <c>,</c> silently
    /// changes which structured component the text lands in.
    /// </remarks>
    private static void AppendVCardEscaped(StringBuilder sb, string? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case ';':
                    sb.Append("\\;");
                    break;
                case ',':
                    sb.Append("\\,");
                    break;
                case '\r':
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
    }

    /// <summary>
    /// Creates a MeCard contact payload. MeCard is a compact contact format widely supported on Android.
    /// </summary>
    /// <param name="lastName">The contact's last name.</param>
    /// <param name="firstName">The contact's first name.</param>
    /// <param name="phone">The contact's phone number.</param>
    /// <param name="email">The contact's email address.</param>
    /// <param name="organization">The contact's organization.</param>
    /// <param name="url">The contact's website URL.</param>
    /// <param name="address">The contact's address.</param>
    /// <param name="note">A note about the contact.</param>
    /// <returns>A string formatted as a MeCard payload.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "The URL is embedded as a string in the MeCard payload")]
    public static string MeCard(string lastName, string? firstName = null, string? phone = null, string? email = null, string? organization = null, string? url = null, string? address = null, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(lastName);

        var sb = new StringBuilder();
        sb.Append("MECARD:N:");
        AppendMeCardEscaped(sb, lastName);
        if (firstName is not null)
        {
            sb.Append(',');
            AppendMeCardEscaped(sb, firstName);
        }

        sb.Append(';');

        if (phone is not null)
        {
            sb.Append("TEL:");
            AppendMeCardEscaped(sb, phone);
            sb.Append(';');
        }

        if (email is not null)
        {
            sb.Append("EMAIL:");
            AppendMeCardEscaped(sb, email);
            sb.Append(';');
        }

        if (organization is not null)
        {
            sb.Append("ORG:");
            AppendMeCardEscaped(sb, organization);
            sb.Append(';');
        }

        if (url is not null)
        {
            sb.Append("URL:");
            AppendMeCardEscaped(sb, url);
            sb.Append(';');
        }

        if (address is not null)
        {
            sb.Append("ADR:");
            AppendMeCardEscaped(sb, address);
            sb.Append(';');
        }

        if (note is not null)
        {
            sb.Append("NOTE:");
            AppendMeCardEscaped(sb, note);
            sb.Append(';');
        }

        sb.Append(';');
        return sb.ToString();
    }

    /// <summary>
    /// Creates an OTP Auth URI for two-factor authentication setup.
    /// Supported by Google Authenticator, Authy, Microsoft Authenticator, and most TOTP/HOTP apps.
    /// </summary>
    /// <param name="type">The OTP type (TOTP or HOTP).</param>
    /// <param name="secret">The base32-encoded shared secret.</param>
    /// <param name="accountName">The account name (e.g., user email).</param>
    /// <param name="issuer">The issuer name (e.g., service name).</param>
    /// <param name="algorithm">The hash algorithm. Default is SHA1.</param>
    /// <param name="digits">The number of digits in the OTP code: 6, 7 or 8 (RFC 4226). Default is 6. Google Authenticator documents only 6 and 8.</param>
    /// <param name="period">For TOTP: the time step in seconds, greater than 0. Default is 30. Ignored for HOTP.</param>
    /// <param name="counter">For HOTP: the initial counter value, required and greater than or equal to 0. Ignored for TOTP.</param>
    /// <returns>A string formatted as an otpauth URI.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="digits"/> is not 6, 7 or 8; or <paramref name="period"/> is not positive for TOTP; or <paramref name="counter"/> is negative for HOTP.</exception>
    /// <exception cref="ArgumentException"><paramref name="counter"/> is <see langword="null"/> for HOTP.</exception>
    public static string OneTimePassword(OneTimePasswordType type, string secret, string accountName, string? issuer = null, OneTimePasswordAlgorithm algorithm = OneTimePasswordAlgorithm.SHA1, int digits = 6, int period = 30, long? counter = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(accountName);

        // An authenticator cannot provision a URI with these values, so they are rejected rather
        // than emitted. See https://github.com/google/google-authenticator/wiki/Key-Uri-Format
        if (digits is < 6 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), digits, "The number of digits must be 6, 7 or 8.");
        }

        if (type == OneTimePasswordType.Totp)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        }
        else if (type == OneTimePasswordType.Hotp)
        {
            if (counter is null)
            {
                throw new ArgumentException("A HOTP URI requires an initial counter.", nameof(counter));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(counter.Value, nameof(counter));
        }

        var sb = new StringBuilder();
        sb.Append("otpauth://");
        sb.Append(type switch
        {
            OneTimePasswordType.Totp => "totp",
            OneTimePasswordType.Hotp => "hotp",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported OTP type."),
        });
        sb.Append('/');

        if (issuer is not null)
        {
            sb.Append(Uri.EscapeDataString(issuer));
            sb.Append(':');
        }

        sb.Append(Uri.EscapeDataString(accountName));
        sb.Append("?secret=");
        sb.Append(Uri.EscapeDataString(secret));

        if (issuer is not null)
        {
            sb.Append("&issuer=");
            sb.Append(Uri.EscapeDataString(issuer));
        }

        if (algorithm != OneTimePasswordAlgorithm.SHA1)
        {
            sb.Append("&algorithm=");
            sb.Append(algorithm switch
            {
                OneTimePasswordAlgorithm.SHA256 => "SHA256",
                OneTimePasswordAlgorithm.SHA512 => "SHA512",
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported algorithm."),
            });
        }

        if (digits != 6)
        {
            sb.Append("&digits=");
            sb.Append(digits.ToString(CultureInfo.InvariantCulture));
        }

        if (type == OneTimePasswordType.Totp && period != 30)
        {
            sb.Append("&period=");
            sb.Append(period.ToString(CultureInfo.InvariantCulture));
        }

        if (type == OneTimePasswordType.Hotp)
        {
            sb.Append("&counter=");
            sb.Append(counter.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates a Bitcoin payment URI (BIP-0021 standard).
    /// </summary>
    /// <param name="address">The Bitcoin address.</param>
    /// <param name="amount">The amount in BTC.</param>
    /// <param name="label">A label for the address (e.g., recipient name).</param>
    /// <param name="message">A message describing the transaction.</param>
    /// <returns>A string formatted as a BIP-0021 Bitcoin URI.</returns>
    public static string Bitcoin(string address, decimal? amount = null, string? label = null, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(address);

        var sb = new StringBuilder();
        sb.Append("bitcoin:");
        sb.Append(Uri.EscapeDataString(address));

        var separator = '?';
        if (amount is not null)
        {
            sb.Append(separator);
            sb.Append("amount=");
            sb.Append(amount.Value.ToString(CultureInfo.InvariantCulture));
            separator = '&';
        }

        if (label is not null)
        {
            sb.Append(separator);
            sb.Append("label=");
            sb.Append(Uri.EscapeDataString(label));
            separator = '&';
        }

        if (message is not null)
        {
            sb.Append(separator);
            sb.Append("message=");
            sb.Append(Uri.EscapeDataString(message));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates an EPC QR code payload for SEPA credit transfers (European Payments Council standard EPC069-12).
    /// </summary>
    /// <param name="beneficiaryName">The name of the beneficiary (max 70 characters).</param>
    /// <param name="iban">The IBAN of the beneficiary.</param>
    /// <param name="amount">The amount in EUR (0.01 to 999999999.99), a whole number of cents.</param>
    /// <param name="bic">The BIC/SWIFT code of the beneficiary's bank.</param>
    /// <param name="remittanceReference">A structured remittance reference (ISO 11649). Mutually exclusive with <paramref name="remittanceText"/>.</param>
    /// <param name="remittanceText">An unstructured remittance text (max 140 characters). Mutually exclusive with <paramref name="remittanceReference"/>.</param>
    /// <param name="information">Beneficiary to originator information (max 70 characters).</param>
    /// <returns>A string formatted as an EPC QR code payload.</returns>
    /// <remarks>
    /// The payload is at most 331 bytes in UTF-8, the limit of EPC069-12. The guidelines also require a symbol of version 13
    /// at most with error correction level <see cref="ErrorCorrectionLevel.M"/>: create the QR code with
    /// <see cref="QRCode.Create(string, ErrorCorrectionLevel)"/> and its default level.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is out of range or has a fraction of a cent.</exception>
    /// <exception cref="ArgumentException">A value is too long or contains a control character, both remittance values are set, or the payload exceeds 331 bytes in UTF-8.</exception>
    public static string SepaPayment(string beneficiaryName, string iban, decimal amount, string? bic = null, string? remittanceReference = null, string? remittanceText = null, string? information = null)
    {
        ArgumentNullException.ThrowIfNull(beneficiaryName);
        ArgumentNullException.ThrowIfNull(iban);

        if (remittanceReference is not null && remittanceText is not null)
        {
            throw new ArgumentException("Only one of remittanceReference or remittanceText can be specified.", nameof(remittanceReference));
        }

        ValidateSepaField(beneficiaryName, 70, nameof(beneficiaryName));
        ValidateSepaField(iban, 34, nameof(iban));
        ValidateSepaField(bic, 11, nameof(bic));
        ValidateSepaField(remittanceReference, 35, nameof(remittanceReference));
        ValidateSepaField(remittanceText, 140, nameof(remittanceText));
        ValidateSepaField(information, 70, nameof(information));

        if (amount is < 0.01m or > 999999999.99m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must be between 0.01 and 999999999.99.");
        }

        // The amount is written with two decimals. Rounding a value such as 0.015 would silently
        // change the payment the payer is asked to make, so it is rejected instead. 10.000 is still
        // accepted because its value is a whole number of cents.
        if (amount % 0.01m != 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must be a whole number of cents.");
        }

        // EPC069-12 separates the twelve elements with a line feed, so the separator is written
        // explicitly rather than through AppendLine, whose separator depends on the platform.
        // The elements are positional, so an empty element before a populated one keeps its
        // separator, but the last populated element must not be followed by a separator.
        string?[] elements =
        [
            "BCD",                  // Service Tag
            "002",                  // Version
            "1",                    // Character set (1 = UTF-8)
            "SCT",                  // Identification code
            bic,
            beneficiaryName,
            iban,
            "EUR" + amount.ToString("F2", CultureInfo.InvariantCulture),
            null,                   // Purpose
            remittanceReference,    // Structured remittance
            remittanceText,         // Unstructured remittance
            information,            // Beneficiary to originator information
        ];

        var count = elements.Length;
        while (string.IsNullOrEmpty(elements[count - 1]))
        {
            count--;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            sb.Append(elements[i]);
        }

        var payload = sb.ToString();
        var byteCount = Encoding.UTF8.GetByteCount(payload);
        if (byteCount > SepaPaymentMaxPayloadBytes)
        {
            throw new ArgumentException($"The payload is {byteCount.ToString(CultureInfo.InvariantCulture)} bytes in UTF-8, but EPC069-12 allows at most {SepaPaymentMaxPayloadBytes.ToString(CultureInfo.InvariantCulture)} bytes. Shorten the text values; characters outside ASCII take several bytes.", paramName: null);
        }

        return payload;
    }

    /// <summary>
    /// EPC069-12 limits the whole payload to 331 bytes, the byte capacity of a version 13 symbol
    /// at error correction level M, the largest symbol the guidelines allow.
    /// </summary>
    private const int SepaPaymentMaxPayloadBytes = 331;

    /// <summary>
    /// Validates a single EPC element.
    /// </summary>
    /// <remarks>
    /// EPC069-12 is positional and has no escape mechanism, so a control character in a value
    /// cannot be represented. A line feed would shift every following element down by one and
    /// silently redirect the payment, so the value is rejected rather than mangled.
    /// </remarks>
    private static void ValidateSepaField(string? value, int maxLength, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                throw new ArgumentException($"The value cannot contain control characters because the EPC format cannot escape them.", parameterName);
            }
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName);
        }
    }

    private static void AppendMeCardEscaped(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            if (c is '\\' or ';' or ':' or ',')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }
    }

    private static void AppendWifiEscaped(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            if (c is '\\' or ';' or ',' or ':' or '"')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }
    }
}
