namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A physical address (https://w3c.github.io/contact-picker/#contactaddress): the shipping address of a payment request
/// (<see cref="PaymentRequest.ShippingAddress"/>, <see cref="PaymentResponse.ShippingAddress"/>). The user agent UI backend
/// creates one from the input of the user (<see cref="PaymentSheetPrompt.ChangeShippingAddressAsync"/>,
/// <see cref="PaymentSheetResponse.ShippingAddress"/>).
/// </summary>
/// <seealso href="https://w3c.github.io/contact-picker/#contactaddress"/>
[IdlInterface("ContactAddress", "https://w3c.github.io/contact-picker/#contactaddress")]
public sealed class ContactAddress
{
    /// <summary>Creates an empty address, whose members are set by the initializer.</summary>
    [NotInIdl("The addresses are created by the user agent UI backend")]
    public ContactAddress()
    {
    }

    /// <summary>Gets the city, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-city"/>
    [IdlMember("city", "https://w3c.github.io/contact-picker/#dom-contactaddress-city")]
    public string City { get; init; } = "";

    /// <summary>Gets the CLDR (Common Locale Data Repository) region code of the country (e.g. <c>FR</c>), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-country"/>
    [IdlMember("country", "https://w3c.github.io/contact-picker/#dom-contactaddress-country")]
    public string Country { get; init; } = "";

    /// <summary>Gets the dependent locality or sublocality (e.g. a neighborhood), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-dependentlocality"/>
    [IdlMember("dependentLocality", "https://w3c.github.io/contact-picker/#dom-contactaddress-dependentlocality")]
    public string DependentLocality { get; init; } = "";

    /// <summary>Gets the organization, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-organization"/>
    [IdlMember("organization", "https://w3c.github.io/contact-picker/#dom-contactaddress-organization")]
    public string Organization { get; init; } = "";

    /// <summary>Gets the phone number of the recipient, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-phone"/>
    [IdlMember("phone", "https://w3c.github.io/contact-picker/#dom-contactaddress-phone")]
    public string Phone { get; init; } = "";

    /// <summary>Gets the postal code, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-postalcode"/>
    [IdlMember("postalCode", "https://w3c.github.io/contact-picker/#dom-contactaddress-postalcode")]
    public string PostalCode { get; init; } = "";

    /// <summary>Gets the name of the recipient, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-recipient"/>
    [IdlMember("recipient", "https://w3c.github.io/contact-picker/#dom-contactaddress-recipient")]
    public string Recipient { get; init; } = "";

    /// <summary>Gets the top-level administrative subdivision of the country (e.g. a state), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-region"/>
    [IdlMember("region", "https://w3c.github.io/contact-picker/#dom-contactaddress-region")]
    public string Region { get; init; } = "";

    /// <summary>Gets the sorting code (e.g. a CEDEX in France), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-sortingcode"/>
    [IdlMember("sortingCode", "https://w3c.github.io/contact-picker/#dom-contactaddress-sortingcode")]
    public string SortingCode { get; init; } = "";

    /// <summary>Gets the lines of the address that are not covered by the other members (e.g. the street and the house number).</summary>
    /// <seealso href="https://w3c.github.io/contact-picker/#dom-contactaddress-addressline"/>
    [IdlMember("addressLine", "https://w3c.github.io/contact-picker/#dom-contactaddress-addressline")]
    public IReadOnlyList<string> AddressLine { get; init; } = [];

    /// <summary>
    /// The address shared with the page while the user interacts with the payment sheet (https://w3c.github.io/payment-request/#shipping-address-changed-algorithm):
    /// the members of the redact list (the organization, the phone number, the recipient and the address line) are removed.
    /// </summary>
    internal ContactAddress Redact() => new()
    {
        City = City,
        Country = Country,
        DependentLocality = DependentLocality,
        PostalCode = PostalCode,
        Region = Region,
        SortingCode = SortingCode,
    };

    /// <summary>A copy of an address of the backend, so that the page sees an object that the backend no longer changes.</summary>
    internal ContactAddress Copy() => new()
    {
        City = City,
        Country = Country,
        DependentLocality = DependentLocality,
        Organization = Organization,
        Phone = Phone,
        PostalCode = PostalCode,
        Recipient = Recipient,
        Region = Region,
        SortingCode = SortingCode,
        AddressLine = [.. AddressLine],
    };
}
