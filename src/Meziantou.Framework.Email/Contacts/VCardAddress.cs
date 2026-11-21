namespace Meziantou.Framework.Email.Contacts;

/// <summary>A postal address of a vCard (<c>ADR</c>): the post office box, the extended address, the street, the locality, the region, the postal code and the country.</summary>
public sealed class VCardAddress
{
    /// <summary>Gets or sets the post office box.</summary>
    public string? PostOfficeBox { get; set; }

    /// <summary>Gets or sets the extended address, such as an apartment or a suite.</summary>
    public string? ExtendedAddress { get; set; }

    /// <summary>Gets or sets the street.</summary>
    public string? Street { get; set; }

    /// <summary>Gets or sets the locality (the city).</summary>
    public string? Locality { get; set; }

    /// <summary>Gets or sets the region (the state or the province).</summary>
    public string? Region { get; set; }

    /// <summary>Gets or sets the postal code.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Gets or sets the country.</summary>
    public string? Country { get; set; }

    /// <summary>Gets the types, in lower case, such as <c>work</c>, <c>home</c> or <c>pref</c>.</summary>
    public IList<string> Types { get; } = [];

    /// <summary>Gets whether the address has a type.</summary>
    /// <param name="type">The type, without case.</param>
    /// <returns><see langword="true"/> when the address has the type.</returns>
    public bool HasType(string type) => Types.Contains(type, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the parts of the address, separated by commas.</summary>
    /// <returns>The address.</returns>
    public override string ToString() => string.Join(", ", new[] { PostOfficeBox, ExtendedAddress, Street, Locality, Region, PostalCode, Country }.Where(part => !string.IsNullOrEmpty(part)));
}
