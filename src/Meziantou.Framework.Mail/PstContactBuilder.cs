using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A contact (IPM.Contact) to add to a folder of a <see cref="PstWriter"/>.</summary>
public sealed class PstContactBuilder : PstMessageBuilder
{
    /// <summary>Initializes a new instance of the <see cref="PstContactBuilder"/> class.</summary>
    public PstContactBuilder()
        : base("IPM.Contact")
    {
    }

    /// <summary>Gets or sets the display name. The writer composes it from the given name and the surname when it is not set.</summary>
    public string? DisplayName
    {
        get => Properties.GetValue<string>(PropertyIds.DisplayName);
        set => Properties.SetValue(PropertyIds.DisplayName, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the name the contact is filed under.</summary>
    public string? FileAs
    {
        get => Properties.GetValue<string>(NamedProperties.FileUnder);
        set => Properties.SetValue(NamedProperties.FileUnder, value);
    }

    /// <summary>Gets or sets the given (first) name.</summary>
    public string? GivenName
    {
        get => Properties.GetValue<string>(PropertyIds.GivenName);
        set => Properties.SetValue(PropertyIds.GivenName, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the middle name.</summary>
    public string? MiddleName
    {
        get => Properties.GetValue<string>(PropertyIds.MiddleName);
        set => Properties.SetValue(PropertyIds.MiddleName, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the surname (last name).</summary>
    public string? Surname
    {
        get => Properties.GetValue<string>(PropertyIds.Surname);
        set => Properties.SetValue(PropertyIds.Surname, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the nickname.</summary>
    public string? Nickname
    {
        get => Properties.GetValue<string>(PropertyIds.Nickname);
        set => Properties.SetValue(PropertyIds.Nickname, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the name of the company.</summary>
    public string? CompanyName
    {
        get => Properties.GetValue<string>(PropertyIds.CompanyName);
        set => Properties.SetValue(PropertyIds.CompanyName, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the job title.</summary>
    public string? JobTitle
    {
        get => Properties.GetValue<string>(PropertyIds.Title);
        set => Properties.SetValue(PropertyIds.Title, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the department.</summary>
    public string? DepartmentName
    {
        get => Properties.GetValue<string>(PropertyIds.DepartmentName);
        set => Properties.SetValue(PropertyIds.DepartmentName, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the first email address (SMTP).</summary>
    public string? Email1Address
    {
        get => Properties.GetValue<string>(NamedProperties.Email1EmailAddress);
        set => SetEmail(value, NamedProperties.Email1EmailAddress, NamedProperties.Email1AddressType, NamedProperties.Email1DisplayName, NamedProperties.Email1OriginalDisplayName);
    }

    /// <summary>Gets or sets the second email address (SMTP).</summary>
    public string? Email2Address
    {
        get => Properties.GetValue<string>(NamedProperties.Email2EmailAddress);
        set => SetEmail(value, NamedProperties.Email2EmailAddress, NamedProperties.Email2AddressType, NamedProperties.Email2DisplayName, NamedProperties.Email2OriginalDisplayName);
    }

    /// <summary>Gets or sets the third email address (SMTP).</summary>
    public string? Email3Address
    {
        get => Properties.GetValue<string>(NamedProperties.Email3EmailAddress);
        set => SetEmail(value, NamedProperties.Email3EmailAddress, NamedProperties.Email3AddressType, NamedProperties.Email3DisplayName, NamedProperties.Email3OriginalDisplayName);
    }

    /// <summary>Gets or sets the business telephone number.</summary>
    public string? BusinessTelephoneNumber
    {
        get => Properties.GetValue<string>(PropertyIds.BusinessTelephoneNumber);
        set => Properties.SetValue(PropertyIds.BusinessTelephoneNumber, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the home telephone number.</summary>
    public string? HomeTelephoneNumber
    {
        get => Properties.GetValue<string>(PropertyIds.HomeTelephoneNumber);
        set => Properties.SetValue(PropertyIds.HomeTelephoneNumber, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the mobile telephone number.</summary>
    public string? MobileTelephoneNumber
    {
        get => Properties.GetValue<string>(PropertyIds.MobileTelephoneNumber);
        set => Properties.SetValue(PropertyIds.MobileTelephoneNumber, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the birthday.</summary>
    public DateTime? Birthday
    {
        get => Properties.Get(PropertyIds.Birthday) as DateTime?;
        set => Properties.SetValue(PropertyIds.Birthday, PstPropertyType.Time, value);
    }

    /// <summary>Gets or sets the address of the business web page.</summary>
    public string? BusinessHomePage
    {
        get => Properties.GetValue<string>(PropertyIds.BusinessHomePage);
        set => Properties.SetValue(PropertyIds.BusinessHomePage, PstPropertyType.Unicode, value);
    }

    /// <summary>Sets the home address.</summary>
    /// <param name="address">The address, or <see langword="null"/> to remove it.</param>
    public void SetHomeAddress(PstPostalAddress? address)
    {
        Properties.SetValue(PropertyIds.HomeAddressStreet, PstPropertyType.Unicode, address?.Street);
        Properties.SetValue(PropertyIds.HomeAddressCity, PstPropertyType.Unicode, address?.City);
        Properties.SetValue(PropertyIds.HomeAddressStateOrProvince, PstPropertyType.Unicode, address?.StateOrProvince);
        Properties.SetValue(PropertyIds.HomeAddressPostalCode, PstPropertyType.Unicode, address?.PostalCode);
        Properties.SetValue(PropertyIds.HomeAddressCountry, PstPropertyType.Unicode, address?.Country);
    }

    /// <summary>Sets the business address.</summary>
    /// <param name="address">The address, or <see langword="null"/> to remove it.</param>
    public void SetBusinessAddress(PstPostalAddress? address)
    {
        Properties.SetValue(NamedProperties.WorkAddressStreet, address?.Street);
        Properties.SetValue(NamedProperties.WorkAddressCity, address?.City);
        Properties.SetValue(NamedProperties.WorkAddressState, address?.StateOrProvince);
        Properties.SetValue(NamedProperties.WorkAddressPostalCode, address?.PostalCode);
        Properties.SetValue(NamedProperties.WorkAddressCountry, address?.Country);
        Properties.SetValue(PropertyIds.BusinessAddressStreet, PstPropertyType.Unicode, address?.Street);
        Properties.SetValue(PropertyIds.BusinessAddressCity, PstPropertyType.Unicode, address?.City);
        Properties.SetValue(PropertyIds.BusinessAddressStateOrProvince, PstPropertyType.Unicode, address?.StateOrProvince);
        Properties.SetValue(PropertyIds.BusinessAddressPostalCode, PstPropertyType.Unicode, address?.PostalCode);
        Properties.SetValue(PropertyIds.BusinessAddressCountry, PstPropertyType.Unicode, address?.Country);
    }

    internal void Complete()
    {
        if (IsCopy)
        {
            return;
        }

        if (DisplayName is null)
        {
            var name = string.Join(' ', new[] { GivenName, MiddleName, Surname }.Where(part => !string.IsNullOrEmpty(part)));
            if (name.Length > 0)
            {
                DisplayName = name;
            }
        }

        Subject ??= DisplayName;
        if (FileAs is null && (Surname is not null || GivenName is not null))
        {
            FileAs = Surname is null || GivenName is null ? Surname ?? GivenName : Surname + ", " + GivenName;
        }
    }

    private void SetEmail(string? value, NamedPropertyDefinition address, NamedPropertyDefinition addressType, NamedPropertyDefinition displayName, NamedPropertyDefinition originalDisplayName)
    {
        Properties.SetValue(address, value);
        Properties.SetValue(addressType, value is null ? null : "SMTP");
        Properties.SetValue(displayName, value);
        Properties.SetValue(originalDisplayName, value);
    }
}
