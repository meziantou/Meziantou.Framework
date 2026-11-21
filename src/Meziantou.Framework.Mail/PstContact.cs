using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail;

/// <summary>A contact (IPM.Contact).</summary>
public sealed class PstContact : PstMessage
{
    internal PstContact(MessageSource source, PstPropertyCollection properties, int embeddingDepth)
        : base(source, properties, embeddingDepth)
    {
    }

    /// <summary>Gets the display name of the contact.</summary>
    public string? DisplayName => Properties.GetString(PropertyIds.DisplayName);

    /// <summary>Gets the name the contact is filed under, such as "Last, First".</summary>
    public string? FileAs => Properties.GetString(NamedProperties.FileUnder.Name);

    /// <summary>Gets the title before the name, such as "Dr.".</summary>
    public string? DisplayNamePrefix => Properties.GetString(PropertyIds.DisplayNamePrefix);

    /// <summary>Gets the given (first) name.</summary>
    public string? GivenName => Properties.GetString(PropertyIds.GivenName);

    /// <summary>Gets the middle name.</summary>
    public string? MiddleName => Properties.GetString(PropertyIds.MiddleName);

    /// <summary>Gets the surname (last name).</summary>
    public string? Surname => Properties.GetString(PropertyIds.Surname);

    /// <summary>Gets the suffix after the name, such as "Jr.".</summary>
    public string? Generation => Properties.GetString(PropertyIds.Generation);

    /// <summary>Gets the initials.</summary>
    public string? Initials => Properties.GetString(PropertyIds.Initials);

    /// <summary>Gets the nickname.</summary>
    public string? Nickname => Properties.GetString(PropertyIds.Nickname);

    /// <summary>Gets the name of the company.</summary>
    public string? CompanyName => Properties.GetString(PropertyIds.CompanyName);

    /// <summary>Gets the job title.</summary>
    public string? JobTitle => Properties.GetString(PropertyIds.Title);

    /// <summary>Gets the department.</summary>
    public string? DepartmentName => Properties.GetString(PropertyIds.DepartmentName);

    /// <summary>Gets the office location.</summary>
    public string? OfficeLocation => Properties.GetString(PropertyIds.OfficeLocation);

    /// <summary>Gets the first email address.</summary>
    public string? Email1Address => Properties.GetString(NamedProperties.Email1EmailAddress.Name);

    /// <summary>Gets the display name of the first email address.</summary>
    public string? Email1DisplayName => Properties.GetString(NamedProperties.Email1DisplayName.Name);

    /// <summary>Gets the type of the first email address, such as <c>SMTP</c>.</summary>
    public string? Email1AddressType => Properties.GetString(NamedProperties.Email1AddressType.Name);

    /// <summary>Gets the second email address.</summary>
    public string? Email2Address => Properties.GetString(NamedProperties.Email2EmailAddress.Name);

    /// <summary>Gets the display name of the second email address.</summary>
    public string? Email2DisplayName => Properties.GetString(NamedProperties.Email2DisplayName.Name);

    /// <summary>Gets the third email address.</summary>
    public string? Email3Address => Properties.GetString(NamedProperties.Email3EmailAddress.Name);

    /// <summary>Gets the display name of the third email address.</summary>
    public string? Email3DisplayName => Properties.GetString(NamedProperties.Email3DisplayName.Name);

    /// <summary>Gets the email addresses of the contact that are set, in order.</summary>
    public IReadOnlyList<string> EmailAddresses => new[] { Email1Address, Email2Address, Email3Address }.Where(address => !string.IsNullOrEmpty(address)).Select(address => address!).ToArray();

    /// <summary>Gets the instant messaging address.</summary>
    public string? InstantMessagingAddress => Properties.GetString(NamedProperties.InstantMessagingAddress.Name);

    /// <summary>Gets the business telephone number.</summary>
    public string? BusinessTelephoneNumber => Properties.GetString(PropertyIds.BusinessTelephoneNumber);

    /// <summary>Gets the home telephone number.</summary>
    public string? HomeTelephoneNumber => Properties.GetString(PropertyIds.HomeTelephoneNumber);

    /// <summary>Gets the mobile telephone number.</summary>
    public string? MobileTelephoneNumber => Properties.GetString(PropertyIds.MobileTelephoneNumber);

    /// <summary>Gets the business fax number.</summary>
    public string? BusinessFaxNumber => Properties.GetString(PropertyIds.BusinessFaxNumber);

    /// <summary>Gets the home address.</summary>
    public PstPostalAddress HomeAddress => new()
    {
        Street = Properties.GetString(PropertyIds.HomeAddressStreet),
        City = Properties.GetString(PropertyIds.HomeAddressCity),
        StateOrProvince = Properties.GetString(PropertyIds.HomeAddressStateOrProvince),
        PostalCode = Properties.GetString(PropertyIds.HomeAddressPostalCode),
        Country = Properties.GetString(PropertyIds.HomeAddressCountry),
    };

    /// <summary>Gets the business address.</summary>
    public PstPostalAddress BusinessAddress => new()
    {
        Street = Properties.GetString(NamedProperties.WorkAddressStreet.Name) ?? Properties.GetString(PropertyIds.BusinessAddressStreet),
        City = Properties.GetString(NamedProperties.WorkAddressCity.Name) ?? Properties.GetString(PropertyIds.BusinessAddressCity),
        StateOrProvince = Properties.GetString(NamedProperties.WorkAddressState.Name) ?? Properties.GetString(PropertyIds.BusinessAddressStateOrProvince),
        PostalCode = Properties.GetString(NamedProperties.WorkAddressPostalCode.Name) ?? Properties.GetString(PropertyIds.BusinessAddressPostalCode),
        Country = Properties.GetString(NamedProperties.WorkAddressCountry.Name) ?? Properties.GetString(PropertyIds.BusinessAddressCountry),
    };

    /// <summary>Gets the birthday.</summary>
    public DateTime? Birthday => Properties.GetDateTime(PropertyIds.Birthday);

    /// <summary>Gets the address of the personal web page.</summary>
    public string? PersonalHomePage => Properties.GetString(PropertyIds.PersonalHomePage);

    /// <summary>Gets the address of the business web page.</summary>
    public string? BusinessHomePage => Properties.GetString(PropertyIds.BusinessHomePage) ?? Properties.GetString(NamedProperties.Html.Name);
}
