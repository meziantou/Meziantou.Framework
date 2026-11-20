using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A recipient of a message to write.</summary>
public sealed class PstRecipientBuilder
{
    /// <summary>Initializes a new instance of the <see cref="PstRecipientBuilder"/> class.</summary>
    public PstRecipientBuilder()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PstRecipientBuilder"/> class with an SMTP address.</summary>
    /// <param name="displayName">The display name of the recipient.</param>
    /// <param name="emailAddress">The SMTP address of the recipient.</param>
    /// <param name="recipientType">The type of the recipient.</param>
    public PstRecipientBuilder(string? displayName, string emailAddress, PstRecipientType recipientType = PstRecipientType.To)
    {
        DisplayName = displayName ?? emailAddress;
        EmailAddress = emailAddress;
        AddressType = "SMTP";
        RecipientType = recipientType;
    }

    /// <summary>Gets the properties of the recipient. The other members of the builder read and write them.</summary>
    public PstPropertyBag Properties { get; } = new();

    /// <summary>Gets or sets the display name.</summary>
    public string? DisplayName
    {
        get => Properties.GetValue<string>(PropertyIds.DisplayName);
        set => Properties.SetValue(PropertyIds.DisplayName, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the address, in the format of <see cref="AddressType"/>.</summary>
    public string? EmailAddress
    {
        get => Properties.GetValue<string>(PropertyIds.EmailAddress);
        set => Properties.SetValue(PropertyIds.EmailAddress, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the type of the address, such as <c>SMTP</c>.</summary>
    public string? AddressType
    {
        get => Properties.GetValue<string>(PropertyIds.AddressType);
        set => Properties.SetValue(PropertyIds.AddressType, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the SMTP address, when <see cref="EmailAddress"/> has another format.</summary>
    public string? SmtpAddress
    {
        get => Properties.GetValue<string>(PropertyIds.SmtpAddress);
        set => Properties.SetValue(PropertyIds.SmtpAddress, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the type of the recipient.</summary>
    public PstRecipientType RecipientType
    {
        get => Properties.Get(PropertyIds.RecipientType) is int value ? (PstRecipientType)value : PstRecipientType.To;
        set => Properties.SetValue(PropertyIds.RecipientType, PstPropertyType.Integer32, (int)value);
    }
}
