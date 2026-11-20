using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A recipient of a message.</summary>
public sealed class PstRecipient
{
    internal PstRecipient(PstPropertyCollection properties)
    {
        Properties = properties;
    }

    /// <summary>Gets the properties of the recipient (a row of the recipient table).</summary>
    public PstPropertyCollection Properties { get; }

    /// <summary>Gets the display name of the recipient.</summary>
    public string? DisplayName => Properties.GetString(PropertyIds.DisplayName);

    /// <summary>Gets the address of the recipient, in the format of <see cref="AddressType"/>.</summary>
    public string? EmailAddress => Properties.GetString(PropertyIds.EmailAddress);

    /// <summary>Gets the type of the address, such as <c>SMTP</c> or <c>EX</c>.</summary>
    public string? AddressType => Properties.GetString(PropertyIds.AddressType);

    /// <summary>Gets the SMTP address of the recipient, or the address when its type is <c>SMTP</c>.</summary>
    public string? SmtpAddress => Properties.GetString(PropertyIds.SmtpAddress) ?? (string.Equals(AddressType, "SMTP", StringComparison.OrdinalIgnoreCase) ? EmailAddress : null);

    /// <summary>Gets the type of the recipient: To, Cc or Bcc.</summary>
    public PstRecipientType RecipientType => (PstRecipientType)((Properties.GetInt32(PropertyIds.RecipientType) ?? 1) & 0x0F);

    /// <summary>Returns the display name and the address of the recipient.</summary>
    /// <returns>The display name and the address of the recipient.</returns>
    public override string ToString() => EmailAddress is null ? DisplayName ?? "" : $"{DisplayName} <{EmailAddress}>";
}
