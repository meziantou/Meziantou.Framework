namespace Meziantou.Framework.Email.Storage;

/// <summary>A record of an offline address book: the header record, which describes the address list, or the record of an Address Book object (a user, a contact, a distribution list...). The values are <see cref="int"/>, <see cref="bool"/>, <see cref="string"/>, byte arrays, and arrays of them for the multi-valued properties.</summary>
public sealed class OabRecord
{
    /// <summary>Gets the values of the record, by property tag. A property the record does not have is absent.</summary>
    public IDictionary<MapiPropertyTag, object> Values { get; } = new Dictionary<MapiPropertyTag, object>();

    /// <summary>Gets the X500 distinguished name (<c>PidTagEmailAddress</c>), the primary key of the record.</summary>
    public string? EmailAddress => GetString(0x3003);

    /// <summary>Gets the SMTP address (<c>PidTagSmtpAddress</c>).</summary>
    public string? SmtpAddress => GetString(0x39FE);

    /// <summary>Gets the display name (<c>PidTagDisplayName</c>).</summary>
    public string? DisplayName => GetString(0x3001);

    /// <summary>Gets the given name (<c>PidTagGivenName</c>).</summary>
    public string? GivenName => GetString(0x3A06);

    /// <summary>Gets the surname (<c>PidTagSurname</c>).</summary>
    public string? Surname => GetString(0x3A11);

    /// <summary>Gets the account name (<c>PidTagAccount</c>).</summary>
    public string? Account => GetString(0x3A00);

    /// <summary>Gets the job title (<c>PidTagTitle</c>).</summary>
    public string? Title => GetString(0x3A17);

    /// <summary>Gets the company (<c>PidTagCompanyName</c>).</summary>
    public string? CompanyName => GetString(0x3A16);

    /// <summary>Gets the department (<c>PidTagDepartmentName</c>).</summary>
    public string? DepartmentName => GetString(0x3A18);

    /// <summary>Gets the office location (<c>PidTagOfficeLocation</c>).</summary>
    public string? OfficeLocation => GetString(0x3A19);

    /// <summary>Gets the business telephone number (<c>PidTagBusinessTelephoneNumber</c>).</summary>
    public string? BusinessTelephoneNumber => GetString(0x3A08);

    /// <summary>Gets the mobile telephone number (<c>PidTagMobileTelephoneNumber</c>).</summary>
    public string? MobileTelephoneNumber => GetString(0x3A1C);

    /// <summary>Gets the proxy addresses (<c>PidTagAddressBookProxyAddresses</c>), such as <c>SMTP:laura@example.com</c>.</summary>
    public IReadOnlyList<string> ProxyAddresses => Find(0x800F) as string[] ?? [];

    /// <summary>Gets the type of the object (<c>PidTagObjectType</c>): 6 for a mail user, 8 for a distribution list.</summary>
    public int? ObjectType => Find(0x0FFE) as int?;

    /// <summary>Gets the display type (<c>PidTagDisplayType</c>): 0 for a mailbox user, 1 for a distribution list, 6 for a remote mail user...</summary>
    public int? DisplayType => Find(0x3900) as int?;

    /// <summary>Gets the value of a property, whatever its type.</summary>
    /// <param name="id">The identifier of the property, such as 0x3001 for the display name.</param>
    /// <returns>The value, or <see langword="null"/> when the record does not have the property.</returns>
    public object? Find(ushort id)
    {
        foreach (var (tag, value) in Values)
        {
            if (tag.Id == id)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Returns the value of a string property, of type <see cref="MapiPropertyType.Unicode"/> or <see cref="MapiPropertyType.String8"/>.</summary>
    /// <param name="id">The identifier of the property.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public string? GetString(ushort id) => Find(id) as string;

    /// <summary>Returns the display name, or the address.</summary>
    /// <returns>The display name.</returns>
    public override string ToString() => DisplayName ?? SmtpAddress ?? EmailAddress ?? "";
}
