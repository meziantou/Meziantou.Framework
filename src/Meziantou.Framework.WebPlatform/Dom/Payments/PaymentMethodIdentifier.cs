using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The payment method identifiers (https://w3c.github.io/payment-method-id/): a standardized identifier (e.g.
/// <c>secure-payment-confirmation</c>) or an <c>https:</c> URL without credentials.
/// </summary>
public static class PaymentMethodIdentifier
{
    /// <summary>
    /// Returns whether a string is a valid payment method identifier (https://w3c.github.io/payment-method-id/#dfn-validate-a-payment-method-identifier):
    /// a string that the basic URL parser parses is valid when it is an <c>https:</c> URL without username nor password; another
    /// string is valid when it follows the syntax of the standardized payment method identifiers (lower case ASCII letters and
    /// digits, in parts separated by <c>-</c> that start with a letter).
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    public static bool IsValid(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return UrlParser.Parse(identifier, baseUrl: null) is { } url ? IsValidUrlBased(url) : IsValidStandardized(identifier);
    }

    /// <summary>Returns whether a string is a URL-based payment method identifier: a valid identifier that the basic URL parser parses.</summary>
    /// <param name="identifier">The identifier.</param>
    public static bool IsUrlBased(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return UrlParser.Parse(identifier, baseUrl: null) is { } url && IsValidUrlBased(url);
    }

    /// <summary>https://w3c.github.io/payment-method-id/#dfn-validate-a-url-based-payment-method-identifier</summary>
    internal static bool IsValidUrlBased(UrlRecord url) => url.Scheme == "https" && !url.IncludesCredentials;

    /// <summary>
    /// https://w3c.github.io/payment-method-id/#dfn-validate-a-standardized-payment-method-identifier: <c>stdpmi = part *( "-" part )</c>,
    /// <c>part = 1loweralpha *( DIGIT / loweralpha )</c>.
    /// </summary>
    internal static bool IsValidStandardized(string identifier)
    {
        var startOfPart = true;
        foreach (var c in identifier)
        {
            if (c is >= 'a' and <= 'z')
            {
                startOfPart = false;
            }
            else if (c is >= '0' and <= '9' && !startOfPart)
            {
            }
            else if (c == '-' && !startOfPart)
            {
                startOfPart = true;
            }
            else
            {
                return false;
            }
        }

        return !startOfPart;
    }

    /// <summary>
    /// The key of an identifier in the set of the identifiers of a payment request: URL-based identifiers are compared with the
    /// URL equality (their serializations), the other identifiers as strings.
    /// </summary>
    internal static string GetComparisonKey(string identifier) => UrlParser.Parse(identifier, baseUrl: null) is { } url ? url.Serialize() : identifier;
}

/// <summary>The checks of the amounts of a payment request (https://w3c.github.io/payment-request/#validity-checkers).</summary>
internal static class PaymentAmounts
{
    /// <summary>
    /// https://w3c.github.io/payment-request/#dfn-check-and-canonicalize-amount: a <see cref="RangeErrorException"/> for a
    /// currency code that is not well-formed, a <see cref="TypeErrorException"/> for a value that is not a valid decimal
    /// monetary value. Returns the amount whose currency is in upper case.
    /// </summary>
    public static PaymentCurrencyAmount CheckAndCanonicalize(PaymentCurrencyAmount amount)
    {
        if (!IsWellFormedCurrencyCode(amount.Currency))
            throw new RangeErrorException($"'{amount.Currency}' is not a well-formed currency code");

        if (!IsValidDecimalMonetaryValue(amount.Value))
            throw new TypeErrorException($"'{amount.Value}' is not a valid decimal monetary value");

        return new PaymentCurrencyAmount { Currency = Infra.InfraAscii.ToAsciiUppercase(amount.Currency), Value = amount.Value };
    }

    /// <summary>https://w3c.github.io/payment-request/#dfn-check-and-canonicalize-total-amount: the value of a total cannot be negative.</summary>
    public static PaymentCurrencyAmount CheckAndCanonicalizeTotal(PaymentCurrencyAmount amount)
    {
        var result = CheckAndCanonicalize(amount);
        if (result.Value is ['-', ..])
            throw new TypeErrorException("The value of a total cannot be negative");

        return result;
    }

    /// <summary>A line item whose amount is checked and canonicalized.</summary>
    public static PaymentItem CheckItem(PaymentItem item, bool total) => new()
    {
        Label = item.Label,
        Amount = total ? CheckAndCanonicalizeTotal(item.Amount) : CheckAndCanonicalize(item.Amount),
        Pending = item.Pending,
    };

    /// <summary>https://tc39.es/ecma402/#sec-iswellformedcurrencycode: three ASCII letters.</summary>
    public static bool IsWellFormedCurrencyCode(string currency) => currency.Length == 3 && currency.All(char.IsAsciiLetter);

    /// <summary>https://w3c.github.io/payment-request/#dfn-valid-decimal-monetary-value: <c>^-?[0-9]+(\.[0-9]+)?$</c>.</summary>
    public static bool IsValidDecimalMonetaryValue(string value)
    {
        var span = value.AsSpan();
        if (span.StartsWith('-'))
        {
            span = span[1..];
        }

        var dot = span.IndexOf('.');
        var integer = dot < 0 ? span : span[..dot];
        if (integer.IsEmpty || integer.ContainsAnyExceptInRange('0', '9'))
            return false;

        if (dot < 0)
            return true;

        var fraction = span[(dot + 1)..];
        return !fraction.IsEmpty && !fraction.ContainsAnyExceptInRange('0', '9');
    }
}
