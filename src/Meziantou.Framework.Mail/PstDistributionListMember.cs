namespace Meziantou.Framework.Mail;

/// <summary>A member of a distribution list.</summary>
public sealed class PstDistributionListMember
{
    /// <summary>Gets or sets the display name of the member.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the address of the member.</summary>
    public string? EmailAddress { get; set; }

    /// <summary>Gets or sets the type of the address, such as <c>SMTP</c>.</summary>
    public string? AddressType { get; set; } = "SMTP";

    internal byte[]? EntryId { get; set; }

    /// <summary>Returns the entry identifier of the member as stored in the list: a wrapped reference to a contact, or a one-off address.</summary>
    /// <returns>A copy of the entry identifier, or <see langword="null"/> when the list does not have one.</returns>
    public byte[]? GetEntryId() => EntryId?.ToArray();

    /// <summary>Returns the display name and the address of the member.</summary>
    /// <returns>The display name and the address of the member.</returns>
    public override string ToString() => $"{DisplayName} <{EmailAddress}>";
}
