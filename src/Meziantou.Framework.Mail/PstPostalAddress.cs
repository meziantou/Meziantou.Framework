namespace Meziantou.Framework.Mail;

/// <summary>A postal address of a contact.</summary>
public sealed class PstPostalAddress
{
    /// <summary>Gets or sets the street.</summary>
    public string? Street { get; set; }

    /// <summary>Gets or sets the city.</summary>
    public string? City { get; set; }

    /// <summary>Gets or sets the state or the province.</summary>
    public string? StateOrProvince { get; set; }

    /// <summary>Gets or sets the postal code.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Gets or sets the country.</summary>
    public string? Country { get; set; }

    /// <summary>Gets whether no part of the address is set.</summary>
    public bool IsEmpty => Street is null && City is null && StateOrProvince is null && PostalCode is null && Country is null;

    /// <summary>Returns the parts of the address, separated by commas.</summary>
    /// <returns>The address.</returns>
    public override string ToString() => string.Join(", ", new[] { Street, City, StateOrProvince, PostalCode, Country }.Where(part => !string.IsNullOrEmpty(part)));
}
