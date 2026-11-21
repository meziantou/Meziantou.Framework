using System.Globalization;
using System.Text;
using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A contact in the vCard format (<c>.vcf</c>), in the versions 2.1, 3.0 ([RFC 2426]) and 4.0 ([RFC 6350]). The common properties have members; the others are kept in <see cref="OtherProperties"/> and written back.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Mail.Tests/CalendarTests.cs" ref:region="VCard_Usage" ref:dedent="true">
/// var card = VCard.Load(path).Single();
/// Assert.Equal("Mary Ann O'Brien", card.FormattedName);
/// Assert.Equal("Example, Ltd.", card.Organization);
/// Assert.Equal("mary@example.com", card.Emails.First(email =&gt; email.HasType("pref")).Value);
/// Assert.Equal("Springfield", card.Addresses.Single().Locality);
///
/// // The card in another version of the format
/// var text = card.ToString(VCardVersion.V40);
/// Assert.Contains("EMAIL;PREF=1;TYPE=internet,work:mary@example.com", text, StringComparison.Ordinal);
/// </code>
/// </example>
public sealed class VCard
{
    private const long MaxSize = 64 * 1024 * 1024;

    /// <summary>Gets or sets the version the card was read in. <see cref="ToString(VCardVersion)"/> writes any version.</summary>
    public VCardVersion Version { get; set; } = VCardVersion.V40;

    /// <summary>Gets or sets the formatted name (<c>FN</c>), the name to show.</summary>
    public string? FormattedName { get; set; }

    /// <summary>Gets or sets the family name (the first component of <c>N</c>).</summary>
    public string? FamilyName { get; set; }

    /// <summary>Gets or sets the given name.</summary>
    public string? GivenName { get; set; }

    /// <summary>Gets or sets the additional names, such as the middle name.</summary>
    public string? AdditionalNames { get; set; }

    /// <summary>Gets or sets the honorific prefixes, such as "Dr.".</summary>
    public string? NamePrefix { get; set; }

    /// <summary>Gets or sets the honorific suffixes, such as "Jr.".</summary>
    public string? NameSuffix { get; set; }

    /// <summary>Gets the nicknames (<c>NICKNAME</c>).</summary>
    public IList<string> Nicknames { get; } = [];

    /// <summary>Gets the email addresses (<c>EMAIL</c>).</summary>
    public IList<VCardValue> Emails { get; } = [];

    /// <summary>Gets the telephone numbers (<c>TEL</c>).</summary>
    public IList<VCardValue> Phones { get; } = [];

    /// <summary>Gets the postal addresses (<c>ADR</c>).</summary>
    public IList<VCardAddress> Addresses { get; } = [];

    /// <summary>Gets the URLs (<c>URL</c>).</summary>
    public IList<VCardValue> Urls { get; } = [];

    /// <summary>Gets or sets the name of the organization (the first component of <c>ORG</c>).</summary>
    public string? Organization { get; set; }

    /// <summary>Gets the units of the organization (the other components of <c>ORG</c>), such as the department.</summary>
    public IList<string> OrganizationUnits { get; } = [];

    /// <summary>Gets or sets the job title (<c>TITLE</c>).</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the role (<c>ROLE</c>).</summary>
    public string? Role { get; set; }

    /// <summary>Gets or sets the birthday (<c>BDAY</c>), when it is a complete date.</summary>
    public DateOnly? Birthday { get; set; }

    /// <summary>Gets or sets the note (<c>NOTE</c>).</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets the unique identifier (<c>UID</c>).</summary>
    public string? Uid { get; set; }

    /// <summary>Gets the categories (<c>CATEGORIES</c>).</summary>
    public IList<string> Categories { get; } = [];

    /// <summary>Gets or sets the photo (<c>PHOTO</c>) when it is in the card, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? Photo { get; set; }

    /// <summary>Gets or sets the media type of <see cref="Photo"/>, such as <c>image/jpeg</c>.</summary>
    public string? PhotoMediaType { get; set; }

    /// <summary>Gets the other properties, written back as they were read.</summary>
    public IList<ContentLine> OtherProperties { get; } = [];

    /// <summary>Creates the card of a contact of Outlook: its names, email addresses, telephone numbers, postal addresses, organization, web pages, birthday, note and categories.</summary>
    /// <param name="contact">The contact.</param>
    /// <returns>The card.</returns>
    public static VCard FromPst(PstContact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return CalendarConverter.ToVCard(contact);
    }

    /// <summary>Creates the builder of a contact of Outlook from the card. Outlook has two postal addresses (business and home), three email addresses, and a business, home, mobile and fax telephone number: the other values are not converted.</summary>
    /// <returns>The builder.</returns>
    public PstContactBuilder ToPstContactBuilder() => CalendarConverter.ToContact(this);

    /// <summary>Reads the cards of a text (a <c>.vcf</c> file can contain several).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<VCard> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new List<VCard>();
        VCard? current = null;
        var depth = 0;
        foreach (var line in ContentLineReader.Parse(text))
        {
            if (line.Name == "BEGIN" && line.Value.Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase))
            {
                // A nested card (AGENT of vCard 2.1) is skipped
                if (depth++ == 0)
                {
                    current = new VCard { Version = VCardVersion.V30 };
                }

                continue;
            }

            if (line.Name == "END" && line.Value.Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (depth > 0 && --depth == 0 && current is not null)
                {
                    result.Add(current);
                    current = null;
                }

                continue;
            }

            if (depth == 1)
            {
                current?.Read(line);
            }
        }

        return result;
    }

    /// <summary>Reads the cards of a file, in UTF-8 (or Latin-1 when the bytes are not UTF-8).</summary>
    /// <param name="data">The content of the file.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<VCard> Load(ReadOnlySpan<byte> data) => Parse(ContentLineReader.DecodeFile(data));

    /// <summary>Reads the cards of a stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The cards.</returns>
    /// <exception cref="MailResourceLimitException">The content is larger than 64 MB.</exception>
    public static IReadOnlyList<VCard> Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(StreamHelper.ReadAll(stream, MaxSize));
    }

    /// <summary>Reads the cards of a <c>.vcf</c> file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The cards.</returns>
    /// <exception cref="MailResourceLimitException">The content is larger than 64 MB.</exception>
    public static IReadOnlyList<VCard> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Writes cards in a <c>.vcf</c> file, in UTF-8.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="cards">The cards.</param>
    /// <param name="version">The version of the format.</param>
    public static void Save(string path, IEnumerable<VCard> cards, VCardVersion version = VCardVersion.V40)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(cards);
        File.WriteAllText(path, string.Concat(cards.Select(card => card.ToString(version))), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Returns the card in the version it was read in (4.0 for a new card).</summary>
    /// <returns>The card.</returns>
    public override string ToString() => ToString(Version);

    /// <summary>Returns the card in a version of the format.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The card, with CRLF line breaks.</returns>
    public string ToString(VCardVersion version)
    {
        var lines = new List<ContentLine>
        {
            new("VERSION", version switch { VCardVersion.V21 => "2.1", VCardVersion.V30 => "3.0", _ => "4.0" }),
        };

        var vcard21 = version == VCardVersion.V21;
        // vCard 2.1 escapes the backslashes and the semicolons, not the commas, except in the values of a list
        string Text(string value) => vcard21 ? ContentLineWriter.ReplaceLineBreaks(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal), "\\n") : ContentLine.EncodeText(value);
        string ListItem(string value) => vcard21 ? Text(value).Replace(",", "\\,", StringComparison.Ordinal) : ContentLine.EncodeText(value);
        string Components(params string?[] values) => string.Join(";", values.Select(value => Text(value ?? "")));

        // vCard 3.0 and 4.0 require FN and 3.0 requires N: they are written even when they are empty
        lines.Add(new ContentLine("FN", Text(FormattedName ?? string.Join(" ", new[] { NamePrefix, GivenName, AdditionalNames, FamilyName, NameSuffix }.Where(part => !string.IsNullOrEmpty(part))))));
        if (FamilyName is not null || GivenName is not null || AdditionalNames is not null || NamePrefix is not null || NameSuffix is not null || version != VCardVersion.V40)
        {
            lines.Add(new ContentLine("N", Components(FamilyName, GivenName, AdditionalNames, NamePrefix, NameSuffix)));
        }

        if (Nicknames.Count > 0)
        {
            lines.Add(new ContentLine("NICKNAME", string.Join(",", Nicknames.Select(ListItem))));
        }

        foreach (var email in Emails)
        {
            lines.Add(Typed("EMAIL", Text(email.Value), vcard21 && !email.HasType("internet") ? [.. email.Types, "internet"] : email.Types, version));
        }

        foreach (var phone in Phones)
        {
            lines.Add(Typed("TEL", Text(phone.Value), phone.Types, version));
        }

        foreach (var address in Addresses)
        {
            lines.Add(Typed("ADR", Components(address.PostOfficeBox, address.ExtendedAddress, address.Street, address.Locality, address.Region, address.PostalCode, address.Country), address.Types, version));
        }

        foreach (var url in Urls)
        {
            lines.Add(Typed("URL", Text(url.Value), url.Types, version));
        }

        if (Organization is not null || OrganizationUnits.Count > 0)
        {
            lines.Add(new ContentLine("ORG", Components([Organization, .. OrganizationUnits])));
        }

        AddText(lines, "TITLE", Title, Text);
        AddText(lines, "ROLE", Role, Text);
        if (Birthday is { } birthday)
        {
            lines.Add(new ContentLine("BDAY", birthday.ToString(version == VCardVersion.V40 ? "yyyyMMdd" : "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        AddText(lines, "NOTE", Note, Text);
        AddText(lines, "UID", Uid, Text);
        if (Categories.Count > 0)
        {
            lines.Add(new ContentLine("CATEGORIES", string.Join(",", Categories.Select(ListItem))));
        }

        if (Photo is { } photo)
        {
            var mediaType = PhotoMediaType ?? "image/jpeg";
            ContentLine line;
            if (version == VCardVersion.V40)
            {
                line = new ContentLine("PHOTO", "data:" + mediaType + ";base64," + Convert.ToBase64String(photo.Span));
            }
            else
            {
                line = new ContentLine("PHOTO", Convert.ToBase64String(photo.Span));
                line.Parameters.Add(new ContentLineParameter("ENCODING", vcard21 ? "BASE64" : "b"));
                line.Parameters.Add(new ContentLineParameter("TYPE", mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? mediaType[6..].ToUpperInvariant() : mediaType));
            }

            lines.Add(line);
        }

        foreach (var line in OtherProperties)
        {
            if (line.Name is not ("VERSION" or "BEGIN" or "END"))
            {
                lines.Add(line);
            }
        }

        var builder = new StringBuilder("BEGIN:VCARD\r\n");
        foreach (var line in lines)
        {
            ContentLineWriter.Write(builder, line, vcard21);
        }

        builder.Append("END:VCARD\r\n");
        return builder.ToString();
    }

    private static void AddText(List<ContentLine> lines, string name, string? value, Func<string, string> encode)
    {
        if (value is not null)
        {
            lines.Add(new ContentLine(name, encode(value)));
        }
    }

    /// <summary>Returns a property with its types: <c>TYPE=work,voice</c>, or <c>TYPE=work;PREF=1</c> in vCard 4.0, or <c>WORK;VOICE</c> in vCard 2.1.</summary>
    private static ContentLine Typed(string name, string value, IEnumerable<string> types, VCardVersion version)
    {
        var line = new ContentLine(name, value);
        var list = types.ToList();
        if (version == VCardVersion.V40 && list.Remove("pref"))
        {
            line.Parameters.Add(new ContentLineParameter("PREF", "1"));
        }

        // The writer of vCard 2.1 writes the types that are words without a name (TEL;WORK;VOICE)
        if (list.Count > 0)
        {
            line.Parameters.Add(new ContentLineParameter("TYPE", list));
        }

        return line;
    }

    private void Read(ContentLine line)
    {
        switch (line.Name)
        {
            case "VERSION":
                Version = line.Value.Trim() switch { "2.1" => VCardVersion.V21, "4.0" => VCardVersion.V40, _ => VCardVersion.V30 };
                break;
            case "FN":
                FormattedName = line.GetText();
                break;
            case "N":
                {
                    var parts = line.GetComponents();
                    FamilyName = Part(parts, 0);
                    GivenName = Part(parts, 1);
                    AdditionalNames = Part(parts, 2);
                    NamePrefix = Part(parts, 3);
                    NameSuffix = Part(parts, 4);
                    break;
                }

            case "NICKNAME":
                foreach (var value in line.GetValues())
                {
                    Nicknames.Add(value);
                }

                break;
            case "EMAIL":
                Emails.Add(new VCardValue(line.GetText(), line.GetTypes()));
                break;
            case "TEL":
                Phones.Add(new VCardValue(line.GetText(), line.GetTypes()));
                break;
            case "URL":
                Urls.Add(new VCardValue(line.GetText(), line.GetTypes()));
                break;
            case "ADR":
                {
                    var parts = line.GetComponents();
                    var address = new VCardAddress
                    {
                        PostOfficeBox = Part(parts, 0),
                        ExtendedAddress = Part(parts, 1),
                        Street = Part(parts, 2),
                        Locality = Part(parts, 3),
                        Region = Part(parts, 4),
                        PostalCode = Part(parts, 5),
                        Country = Part(parts, 6),
                    };

                    foreach (var type in line.GetTypes())
                    {
                        address.Types.Add(type);
                    }

                    Addresses.Add(address);
                    break;
                }

            case "ORG":
                {
                    var parts = line.GetComponents();
                    Organization = Part(parts, 0);
                    foreach (var unit in parts.Skip(1).Where(part => part.Length > 0))
                    {
                        OrganizationUnits.Add(unit);
                    }

                    break;
                }

            case "TITLE":
                Title = line.GetText();
                break;
            case "ROLE":
                Role = line.GetText();
                break;
            case "NOTE":
                Note = line.GetText();
                break;
            case "UID":
                Uid = line.GetText();
                break;
            case "CATEGORIES":
                foreach (var value in line.GetValues())
                {
                    Categories.Add(value);
                }

                break;
            case "BDAY" when ParseDate(line.Value) is { } date:
                Birthday = date;
                break;
            case "PHOTO" when ReadPhoto(line):
                break;
            default:
                OtherProperties.Add(line);
                break;
        }
    }

    private bool ReadPhoto(ContentLine line)
    {
        var value = line.Value.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = value.IndexOf(',', StringComparison.Ordinal);
            var header = comma > 0 ? value[5..comma] : "";
            if (comma < 0 || !header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var data = DecodeBase64(value[(comma + 1)..]);
            if (data is null)
            {
                return false;
            }

            Photo = data;
            PhotoMediaType = header[..^7];
            return true;
        }

        var encoding = line.GetParameter("ENCODING") ?? line.Parameters.FirstOrDefault(parameter => parameter.Name is null && parameter.Values.Any(v => v.Equals("BASE64", StringComparison.OrdinalIgnoreCase)))?.Values[0];
        if (encoding is null || !(encoding.Equals("b", StringComparison.OrdinalIgnoreCase) || encoding.Equals("BASE64", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var bytes = DecodeBase64(value);
        if (bytes is null)
        {
            return false;
        }

        Photo = bytes;
        var type = line.GetTypes().FirstOrDefault(t => !t.Equals("base64", StringComparison.OrdinalIgnoreCase));
        PhotoMediaType = type is null ? null : type.Contains('/', StringComparison.Ordinal) ? type : "image/" + type;
        return true;
    }

    private static byte[]? DecodeBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(string.Concat(value.Where(c => !char.IsWhiteSpace(c))));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static DateOnly? ParseDate(string value)
    {
        value = value.Trim();
        var end = value.IndexOf('T', StringComparison.Ordinal);
        if (end >= 0)
        {
            value = value[..end];
        }

        return DateOnly.TryParseExact(value, ["yyyyMMdd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static string? Part(IReadOnlyList<string> parts, int index) => index < parts.Count && parts[index].Length > 0 ? parts[index] : null;
}
