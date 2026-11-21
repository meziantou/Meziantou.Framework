using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>A recipient of a message to write.</summary>
public sealed class MapiRecipientBuilder
{
    /// <summary>Initializes a new instance of the <see cref="MapiRecipientBuilder"/> class.</summary>
    public MapiRecipientBuilder()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MapiRecipientBuilder"/> class with an SMTP address.</summary>
    /// <param name="displayName">The display name of the recipient.</param>
    /// <param name="emailAddress">The SMTP address of the recipient.</param>
    /// <param name="recipientType">The type of the recipient.</param>
    public MapiRecipientBuilder(string? displayName, string emailAddress, MapiRecipientType recipientType = MapiRecipientType.To)
    {
        DisplayName = displayName ?? emailAddress;
        EmailAddress = emailAddress;
        AddressType = "SMTP";
        RecipientType = recipientType;
    }

    /// <summary>Gets the properties of the recipient. The other members of the builder read and write them.</summary>
    public MapiPropertyBag Properties { get; } = new();

    /// <summary>Gets or sets the display name.</summary>
    public string? DisplayName
    {
        get => Properties.GetValue<string>(PropertyIds.DisplayName);
        set => Properties.SetValue(PropertyIds.DisplayName, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the address, in the format of <see cref="AddressType"/>.</summary>
    public string? EmailAddress
    {
        get => Properties.GetValue<string>(PropertyIds.EmailAddress);
        set => Properties.SetValue(PropertyIds.EmailAddress, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the type of the address, such as <c>SMTP</c>.</summary>
    public string? AddressType
    {
        get => Properties.GetValue<string>(PropertyIds.AddressType);
        set => Properties.SetValue(PropertyIds.AddressType, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the SMTP address, when <see cref="EmailAddress"/> has another format.</summary>
    public string? SmtpAddress
    {
        get => Properties.GetValue<string>(PropertyIds.SmtpAddress);
        set => Properties.SetValue(PropertyIds.SmtpAddress, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the type of the recipient.</summary>
    public MapiRecipientType RecipientType
    {
        get => Properties.Get(PropertyIds.RecipientType) is int value ? (MapiRecipientType)value : MapiRecipientType.To;
        set => Properties.SetValue(PropertyIds.RecipientType, MapiPropertyType.Integer32, (int)value);
    }
}
