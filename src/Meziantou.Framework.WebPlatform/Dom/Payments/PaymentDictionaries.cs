namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A payment method accepted by a payment request (https://w3c.github.io/payment-request/#paymentmethoddata-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethoddata"/>
[IdlInterface("PaymentMethodData", "https://w3c.github.io/payment-request/#dom-paymentmethoddata")]
public sealed class PaymentMethodData
{
    /// <summary>Gets the payment method identifier: a standardized identifier (e.g. <c>secure-payment-confirmation</c>) or an <c>https:</c> URL.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethoddata-supportedmethods"/>
    [IdlMember("supportedMethods", "https://w3c.github.io/payment-request/#dom-paymentmethoddata-supportedmethods")]
    public required string SupportedMethods { get; init; }

    /// <summary>Gets the data of the payment method (a JavaScript object, or a value that can be serialized to JSON from .NET), or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethoddata-data"/>
    [IdlMember("data", "https://w3c.github.io/payment-request/#dom-paymentmethoddata-data")]
    public object? Data { get; init; }
}

/// <summary>A monetary amount (https://w3c.github.io/payment-request/#paymentcurrencyamount-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcurrencyamount"/>
[IdlInterface("PaymentCurrencyAmount", "https://w3c.github.io/payment-request/#dom-paymentcurrencyamount")]
public sealed class PaymentCurrencyAmount
{
    /// <summary>Gets the well-formed 3-letter alphabetic currency code (e.g. <c>USD</c>); the payment request uses its upper case form.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcurrencyamount-currency"/>
    [IdlMember("currency", "https://w3c.github.io/payment-request/#dom-paymentcurrencyamount-currency")]
    public required string Currency { get; init; }

    /// <summary>Gets the amount: a valid decimal monetary value (e.g. <c>-1.50</c>), as is.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcurrencyamount-value"/>
    [IdlMember("value", "https://w3c.github.io/payment-request/#dom-paymentcurrencyamount-value")]
    public required string Value { get; init; }
}

/// <summary>The members shared by the details of a payment request and their updates (https://w3c.github.io/payment-request/#paymentdetailsbase-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsbase"/>
[IdlInterface("PaymentDetailsBase", "https://w3c.github.io/payment-request/#dom-paymentdetailsbase")]
public class PaymentDetailsBase
{
    /// <summary>Gets the line items of the payment, or null when the member is missing.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsbase-displayitems"/>
    [IdlMember("displayItems", "https://w3c.github.io/payment-request/#dom-paymentdetailsbase-displayitems")]
    public IReadOnlyList<PaymentItem>? DisplayItems { get; init; }

    /// <summary>Gets the shipping options that the user can choose from, or null when the member is missing.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsbase-shippingoptions"/>
    [IdlMember("shippingOptions", "https://w3c.github.io/payment-request/#dom-paymentdetailsbase-shippingoptions")]
    public IReadOnlyList<PaymentShippingOption>? ShippingOptions { get; init; }

    /// <summary>Gets the modifiers of the details for particular payment methods, or null when the member is missing.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsbase-modifiers"/>
    [IdlMember("modifiers", "https://w3c.github.io/payment-request/#dom-paymentdetailsbase-modifiers")]
    public IReadOnlyList<PaymentDetailsModifier>? Modifiers { get; init; }
}

/// <summary>The details of a payment request given to its constructor (https://w3c.github.io/payment-request/#paymentdetailsinit-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsinit"/>
[IdlInterface("PaymentDetailsInit", "https://w3c.github.io/payment-request/#dom-paymentdetailsinit")]
public sealed class PaymentDetailsInit : PaymentDetailsBase
{
    /// <summary>Gets the identifier of the payment request, or null to let the user agent generate a unique identifier.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsinit-id"/>
    [IdlMember("id", "https://w3c.github.io/payment-request/#dom-paymentdetailsinit-id")]
    public string? Id { get; init; }

    /// <summary>Gets the total amount of the payment, which cannot be negative.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsinit-total"/>
    [IdlMember("total", "https://w3c.github.io/payment-request/#dom-paymentdetailsinit-total")]
    public required PaymentItem Total { get; init; }
}

/// <summary>An update of the details of a payment request (https://w3c.github.io/payment-request/#paymentdetailsupdate-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsupdate"/>
[IdlInterface("PaymentDetailsUpdate", "https://w3c.github.io/payment-request/#dom-paymentdetailsupdate")]
public sealed class PaymentDetailsUpdate : PaymentDetailsBase
{
    /// <summary>Gets the reason why the goods cannot be shipped to the chosen address, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-error"/>
    [IdlMember("error", "https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-error")]
    public string? Error { get; init; }

    /// <summary>Gets the new total amount, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-total"/>
    [IdlMember("total", "https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-total")]
    public PaymentItem? Total { get; init; }

    /// <summary>Gets the validation errors of the shipping address, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-shippingaddresserrors"/>
    [IdlMember("shippingAddressErrors", "https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-shippingaddresserrors")]
    public AddressErrors? ShippingAddressErrors { get; init; }

    /// <summary>Gets the validation errors of the payer details, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-payererrors"/>
    [IdlMember("payerErrors", "https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-payererrors")]
    public PayerErrors? PayerErrors { get; init; }

    /// <summary>Gets the errors specific to the payment method (a JavaScript object), or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-paymentmethoderrors"/>
    [IdlMember("paymentMethodErrors", "https://w3c.github.io/payment-request/#dom-paymentdetailsupdate-paymentmethoderrors")]
    public object? PaymentMethodErrors { get; init; }
}

/// <summary>The details that apply when the user selects a payment method (https://w3c.github.io/payment-request/#paymentdetailsmodifier-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier"/>
[IdlInterface("PaymentDetailsModifier", "https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier")]
public sealed class PaymentDetailsModifier
{
    /// <summary>Gets the payment method identifier to which the modifier applies.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier-supportedmethods"/>
    [IdlMember("supportedMethods", "https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier-supportedmethods")]
    public required string SupportedMethods { get; init; }

    /// <summary>Gets the total that replaces the total of the details for the payment method, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier-total"/>
    [IdlMember("total", "https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier-total")]
    public PaymentItem? Total { get; init; }

    /// <summary>Gets the line items appended to the line items of the details for the payment method, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier-additionaldisplayitems"/>
    [IdlMember("additionalDisplayItems", "https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier-additionaldisplayitems")]
    public IReadOnlyList<PaymentItem>? AdditionalDisplayItems { get; init; }

    /// <summary>Gets the data of the payment method (a JavaScript object, or a value that can be serialized to JSON from .NET), or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier-data"/>
    [IdlMember("data", "https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier-data")]
    public object? Data { get; init; }
}

/// <summary>The kind of address that a payment request collects (https://w3c.github.io/payment-request/#paymentshippingtype-enum).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingtype"/>
[IdlInterface("PaymentShippingType", "https://w3c.github.io/payment-request/#dom-paymentshippingtype")]
public enum PaymentShippingType
{
    /// <summary>The address is the destination of the shipping (the default).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingtype-shipping"/>
    [IdlMember("shipping", "https://w3c.github.io/payment-request/#dom-paymentshippingtype-shipping")]
    Shipping,

    /// <summary>The address is the destination of a delivery (e.g. a food delivery).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingtype-delivery"/>
    [IdlMember("delivery", "https://w3c.github.io/payment-request/#dom-paymentshippingtype-delivery")]
    Delivery,

    /// <summary>The address is the one of a service pickup (e.g. a laundry pickup).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingtype-pickup"/>
    [IdlMember("pickup", "https://w3c.github.io/payment-request/#dom-paymentshippingtype-pickup")]
    Pickup,
}

/// <summary>The information that a payment request collects (https://w3c.github.io/payment-request/#paymentoptions-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentoptions"/>
[IdlInterface("PaymentOptions", "https://w3c.github.io/payment-request/#dom-paymentoptions")]
public sealed class PaymentOptions
{
    /// <summary>Gets whether the name of the payer is collected.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentoptions-requestpayername"/>
    [IdlMember("requestPayerName", "https://w3c.github.io/payment-request/#dom-paymentoptions-requestpayername")]
    public bool RequestPayerName { get; init; }

    /// <summary>Gets whether the billing address of the payment method is collected.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentoptions-requestbillingaddress"/>
    [IdlMember("requestBillingAddress", "https://w3c.github.io/payment-request/#dom-paymentoptions-requestbillingaddress")]
    public bool RequestBillingAddress { get; init; }

    /// <summary>Gets whether the email address of the payer is collected.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentoptions-requestpayeremail"/>
    [IdlMember("requestPayerEmail", "https://w3c.github.io/payment-request/#dom-paymentoptions-requestpayeremail")]
    public bool RequestPayerEmail { get; init; }

    /// <summary>Gets whether the phone number of the payer is collected.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentoptions-requestpayerphone"/>
    [IdlMember("requestPayerPhone", "https://w3c.github.io/payment-request/#dom-paymentoptions-requestpayerphone")]
    public bool RequestPayerPhone { get; init; }

    /// <summary>Gets whether a shipping address is collected (and a shipping option chosen).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentoptions-requestshipping"/>
    [IdlMember("requestShipping", "https://w3c.github.io/payment-request/#dom-paymentoptions-requestshipping")]
    public bool RequestShipping { get; init; }

    /// <summary>Gets the kind of address collected when <see cref="RequestShipping"/> is true.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentoptions-shippingtype"/>
    [IdlMember("shippingType", "https://w3c.github.io/payment-request/#dom-paymentoptions-shippingtype")]
    public PaymentShippingType ShippingType { get; init; }
}

/// <summary>A line item of a payment (https://w3c.github.io/payment-request/#paymentitem-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentitem"/>
[IdlInterface("PaymentItem", "https://w3c.github.io/payment-request/#dom-paymentitem")]
public sealed class PaymentItem
{
    /// <summary>Gets the description of the item.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentitem-label"/>
    [IdlMember("label", "https://w3c.github.io/payment-request/#dom-paymentitem-label")]
    public required string Label { get; init; }

    /// <summary>Gets the amount of the item.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentitem-amount"/>
    [IdlMember("amount", "https://w3c.github.io/payment-request/#dom-paymentitem-amount")]
    public required PaymentCurrencyAmount Amount { get; init; }

    /// <summary>Gets whether the amount is not final (e.g. a shipping cost that depends on the address).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentitem-pending"/>
    [IdlMember("pending", "https://w3c.github.io/payment-request/#dom-paymentitem-pending")]
    public bool Pending { get; init; }
}

/// <summary>The data given to <see cref="PaymentResponse.Complete"/> (https://w3c.github.io/payment-request/#paymentcompletedetails-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcompletedetails"/>
[IdlInterface("PaymentCompleteDetails", "https://w3c.github.io/payment-request/#dom-paymentcompletedetails")]
public sealed class PaymentCompleteDetails
{
    /// <summary>Gets the data for the payment method of the response (a JavaScript object), or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcompletedetails-data"/>
    [IdlMember("data", "https://w3c.github.io/payment-request/#dom-paymentcompletedetails-data")]
    public object? Data { get; init; }
}

/// <summary>The outcome of a payment given to <see cref="PaymentResponse.Complete"/> (https://w3c.github.io/payment-request/#paymentcomplete-enum).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcomplete"/>
[IdlInterface("PaymentComplete", "https://w3c.github.io/payment-request/#dom-paymentcomplete")]
public enum PaymentComplete
{
    /// <summary>The processing of the payment failed.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcomplete-fail"/>
    [IdlMember("fail", "https://w3c.github.io/payment-request/#dom-paymentcomplete-fail")]
    Fail,

    /// <summary>The payment was processed successfully.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcomplete-success"/>
    [IdlMember("success", "https://w3c.github.io/payment-request/#dom-paymentcomplete-success")]
    Success,

    /// <summary>The page did not tell whether the payment succeeded (the default).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentcomplete-unknown"/>
    [IdlMember("unknown", "https://w3c.github.io/payment-request/#dom-paymentcomplete-unknown")]
    Unknown,
}

/// <summary>A shipping option that the user can choose (https://w3c.github.io/payment-request/#paymentshippingoption-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingoption"/>
[IdlInterface("PaymentShippingOption", "https://w3c.github.io/payment-request/#dom-paymentshippingoption")]
public sealed class PaymentShippingOption
{
    /// <summary>Gets the identifier of the option, unique in the payment request.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingoption-id"/>
    [IdlMember("id", "https://w3c.github.io/payment-request/#dom-paymentshippingoption-id")]
    public required string Id { get; init; }

    /// <summary>Gets the description of the option.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingoption-label"/>
    [IdlMember("label", "https://w3c.github.io/payment-request/#dom-paymentshippingoption-label")]
    public required string Label { get; init; }

    /// <summary>Gets the cost of the option.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingoption-amount"/>
    [IdlMember("amount", "https://w3c.github.io/payment-request/#dom-paymentshippingoption-amount")]
    public required PaymentCurrencyAmount Amount { get; init; }

    /// <summary>Gets whether the option is selected by default (the last selected option wins).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentshippingoption-selected"/>
    [IdlMember("selected", "https://w3c.github.io/payment-request/#dom-paymentshippingoption-selected")]
    public bool Selected { get; init; }
}

/// <summary>The errors given to <see cref="PaymentResponse.Retry"/> (https://w3c.github.io/payment-request/#paymentvalidationerrors-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentvalidationerrors"/>
[IdlInterface("PaymentValidationErrors", "https://w3c.github.io/payment-request/#dom-paymentvalidationerrors")]
public sealed class PaymentValidationErrors
{
    /// <summary>Gets the validation errors of the payer details, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentvalidationerrors-payer"/>
    [IdlMember("payer", "https://w3c.github.io/payment-request/#dom-paymentvalidationerrors-payer")]
    public PayerErrors? Payer { get; init; }

    /// <summary>Gets the validation errors of the shipping address, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentvalidationerrors-shippingaddress"/>
    [IdlMember("shippingAddress", "https://w3c.github.io/payment-request/#dom-paymentvalidationerrors-shippingaddress")]
    public AddressErrors? ShippingAddress { get; init; }

    /// <summary>Gets a general description of the error, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentvalidationerrors-error"/>
    [IdlMember("error", "https://w3c.github.io/payment-request/#dom-paymentvalidationerrors-error")]
    public string? Error { get; init; }

    /// <summary>Gets the errors specific to the payment method (a JavaScript object), or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentvalidationerrors-paymentmethod"/>
    [IdlMember("paymentMethod", "https://w3c.github.io/payment-request/#dom-paymentvalidationerrors-paymentmethod")]
    public object? PaymentMethod { get; init; }
}

/// <summary>The validation errors of the payer details (https://w3c.github.io/payment-request/#payererrors-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-payererrors"/>
[IdlInterface("PayerErrors", "https://w3c.github.io/payment-request/#dom-payererrors")]
public sealed class PayerErrors
{
    /// <summary>Gets the error of the email address, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-payererrors-email"/>
    [IdlMember("email", "https://w3c.github.io/payment-request/#dom-payererrors-email")]
    public string? Email { get; init; }

    /// <summary>Gets the error of the name, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-payererrors-name"/>
    [IdlMember("name", "https://w3c.github.io/payment-request/#dom-payererrors-name")]
    public string? Name { get; init; }

    /// <summary>Gets the error of the phone number, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-payererrors-phone"/>
    [IdlMember("phone", "https://w3c.github.io/payment-request/#dom-payererrors-phone")]
    public string? Phone { get; init; }
}

/// <summary>The validation errors of an address (https://w3c.github.io/payment-request/#addresserrors-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors"/>
[IdlInterface("AddressErrors", "https://w3c.github.io/payment-request/#dom-addresserrors")]
public sealed class AddressErrors
{
    /// <summary>Gets the error of the address line, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-addressline"/>
    [IdlMember("addressLine", "https://w3c.github.io/payment-request/#dom-addresserrors-addressline")]
    public string? AddressLine { get; init; }

    /// <summary>Gets the error of the city, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-city"/>
    [IdlMember("city", "https://w3c.github.io/payment-request/#dom-addresserrors-city")]
    public string? City { get; init; }

    /// <summary>Gets the error of the country, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-country"/>
    [IdlMember("country", "https://w3c.github.io/payment-request/#dom-addresserrors-country")]
    public string? Country { get; init; }

    /// <summary>Gets the error of the dependent locality, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-dependentlocality"/>
    [IdlMember("dependentLocality", "https://w3c.github.io/payment-request/#dom-addresserrors-dependentlocality")]
    public string? DependentLocality { get; init; }

    /// <summary>Gets the error of the organization, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-organization"/>
    [IdlMember("organization", "https://w3c.github.io/payment-request/#dom-addresserrors-organization")]
    public string? Organization { get; init; }

    /// <summary>Gets the error of the phone number, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-phone"/>
    [IdlMember("phone", "https://w3c.github.io/payment-request/#dom-addresserrors-phone")]
    public string? Phone { get; init; }

    /// <summary>Gets the error of the postal code, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-postalcode"/>
    [IdlMember("postalCode", "https://w3c.github.io/payment-request/#dom-addresserrors-postalcode")]
    public string? PostalCode { get; init; }

    /// <summary>Gets the error of the recipient, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-recipient"/>
    [IdlMember("recipient", "https://w3c.github.io/payment-request/#dom-addresserrors-recipient")]
    public string? Recipient { get; init; }

    /// <summary>Gets the error of the region, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-region"/>
    [IdlMember("region", "https://w3c.github.io/payment-request/#dom-addresserrors-region")]
    public string? Region { get; init; }

    /// <summary>Gets the error of the sorting code, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-addresserrors-sortingcode"/>
    [IdlMember("sortingCode", "https://w3c.github.io/payment-request/#dom-addresserrors-sortingcode")]
    public string? SortingCode { get; init; }
}
