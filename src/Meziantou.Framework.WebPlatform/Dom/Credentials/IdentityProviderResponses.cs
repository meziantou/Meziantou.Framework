using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An account of an identity provider (https://w3c-fedid.github.io/FedCM/#dictdef-identityprovideraccount), returned by its
/// accounts endpoint and shown by the account chooser of a FedCM request (<see cref="IdentityAccountChooserPrompt"/>).
/// </summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#dictdef-identityprovideraccount"/>
public sealed class IdentityProviderAccount
{
    internal IdentityProviderAccount(string id)
    {
        Id = id;
    }

    /// <summary>Gets the identifier of the account at the identity provider.</summary>
    public string Id { get; }

    /// <summary>Gets the full name of the user, or null.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the email address of the account, or null.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the phone number of the account, or null.</summary>
    public string? Tel { get; init; }

    /// <summary>Gets the user name of the account, or null.</summary>
    public string? Username { get; init; }

    /// <summary>Gets the given name of the user, or null.</summary>
    public string? GivenName { get; init; }

    /// <summary>Gets the URL of the picture of the account, as the identity provider returned it, or null.</summary>
    public string? Picture { get; init; }

    /// <summary>Gets the client identifiers of the sites that the account is registered with, or null.</summary>
    public IReadOnlyList<string>? ApprovedClients { get; init; }

    /// <summary>Gets the login hints that match the account, or null.</summary>
    public IReadOnlyList<string>? LoginHints { get; init; }

    /// <summary>Gets the domain hints that match the account, or null.</summary>
    public IReadOnlyList<string>? DomainHints { get; init; }

    /// <summary>Gets the labels of the account, or null.</summary>
    public IReadOnlyList<string>? LabelHints { get; init; }

    /// <summary>Gets the URL of the config file of the identity provider of the account (set by the request that shows the account).</summary>
    public string ConfigUrl { get; internal set; } = "";

    /// <summary>
    /// Gets whether the account is connected to the site (https://w3c-fedid.github.io/FedCM/#compute-the-connection-status):
    /// the user signs in with it, else the user signs up (set by the request that shows the account).
    /// </summary>
    public bool IsConnected { get; internal set; }

    /// <summary>Gets the content of the picture of the account, fetched before the account is shown (https://w3c-fedid.github.io/FedCM/#fetch-the-account-picture), or null when it could not be fetched.</summary>
    public ReadOnlyMemory<byte>? PictureContent { get; internal set; }

    /// <summary>Gets the URL of the privacy policy of the site, shown to the user who signs up, or null.</summary>
    public string? PrivacyPolicyUrl { get; internal set; }

    /// <summary>Gets the URL of the terms of service of the site, shown to the user who signs up, or null.</summary>
    public string? TermsOfServiceUrl { get; internal set; }

    /// <summary>https://w3c-fedid.github.io/FedCM/#idp-api-accounts-endpoint: an account is displayable when it has a name, an email, a phone number or a user name.</summary>
    internal bool IsDisplayable => Email is not null || Name is not null || Tel is not null || Username is not null;
}

/// <summary>https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderwellknown</summary>
internal sealed record IdentityProviderWellKnown(IReadOnlyList<string>? ProviderUrls, string? AccountsEndpoint, string? LoginUrl);

/// <summary>https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderapiconfig (the branding is not used).</summary>
internal sealed record IdentityProviderApiConfig(string AccountsEndpoint, string? ClientMetadataEndpoint, string IdAssertionEndpoint, string LoginUrl, string? DisconnectEndpoint, bool SupportsUseOtherAccount, string? AccountLabel);

/// <summary>https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderclientmetadata</summary>
internal sealed record IdentityProviderClientMetadata(string? PrivacyPolicyUrl, string? TermsOfServiceUrl, bool ClientIsThirdPartyToTopFrameOrigin);

/// <summary>
/// https://w3c-fedid.github.io/FedCM/#dictdef-identityassertionresponse: the token is the JSON text of the <c>token</c>
/// member (null when the member is missing), converted to a value of the realm of the site when the credential is created.
/// </summary>
internal sealed record IdentityAssertionResponse(string? TokenJson, string? ContinueOn, IdentityCredentialErrorInit? Error);

/// <summary>
/// The parsing of the responses of the endpoints of an identity provider: the JSON text of the body is parsed (as
/// <c>JSON.parse()</c> does) and converted to the dictionary of the endpoint as WebIDL converts a JavaScript value
/// (https://webidl.spec.whatwg.org/#js-dictionary): the members of another type are converted (e.g. a number to a string),
/// and a missing required member or a value that cannot be converted (e.g. a string for a sequence) is a failure (null).
/// </summary>
internal static class IdentityProviderResponses
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 256 };

    /// <summary>https://w3c-fedid.github.io/FedCM/#fetch-the-well-known-file: a failure when <c>provider_urls</c> has more than one URL.</summary>
    public static IdentityProviderWellKnown? ParseWellKnown(ReadOnlySpan<byte> body) => Parse(body, root =>
    {
        var providerUrls = GetStringSequence(root, "provider_urls");
        return providerUrls is { Count: > 1 } ? null : new IdentityProviderWellKnown(providerUrls, GetString(root, "accounts_endpoint"), GetString(root, "login_url"));
    });

    /// <summary>https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderapiconfig</summary>
    public static IdentityProviderApiConfig? ParseConfig(ReadOnlySpan<byte> body) => Parse(body, root =>
    {
        // The members are converted in lexicographical order (the branding is converted, but not used)
        var accountsEndpoint = GetString(root, "accounts_endpoint") ?? throw new FormatException("accounts_endpoint is required");
        var accountLabel = GetString(root, "account_label");
        GetBranding(root);
        var clientMetadataEndpoint = GetString(root, "client_metadata_endpoint");
        var disconnectEndpoint = GetString(root, "disconnect_endpoint");
        var idAssertionEndpoint = GetString(root, "id_assertion_endpoint") ?? throw new FormatException("id_assertion_endpoint is required");
        var loginUrl = GetString(root, "login_url") ?? throw new FormatException("login_url is required");
        var supportsUseOtherAccount = GetMember(root, "supports_use_other_account") is { } supports && ToBoolean(supports);
        return new IdentityProviderApiConfig(accountsEndpoint, clientMetadataEndpoint, idAssertionEndpoint, loginUrl, disconnectEndpoint, supportsUseOtherAccount, accountLabel);
    });

    /// <summary>
    /// https://w3c-fedid.github.io/FedCM/#fetch-the-accounts: the accounts of the <c>accounts</c> member (an empty list when it is
    /// missing), or a failure when an account is not displayable.
    /// </summary>
    public static IReadOnlyList<IdentityProviderAccount>? ParseAccounts(ReadOnlySpan<byte> body) => Parse<IReadOnlyList<IdentityProviderAccount>>(body, root =>
    {
        if (GetMember(root, "accounts") is not { } accounts)
            return [];

        if (accounts.ValueKind != JsonValueKind.Array)
            throw new FormatException("accounts is not a sequence");

        var result = new List<IdentityProviderAccount>(accounts.GetArrayLength());
        foreach (var item in accounts.EnumerateArray())
        {
            var account = ParseAccount(item);
            if (!account.IsDisplayable)
                return null;

            result.Add(account);
        }

        return result;
    });

    /// <summary>
    /// The accounts of <c>navigator.login.setStatus()</c>, which the tentative accounts push of FedCM (https://github.com/w3c-fedid/FedCM/issues/575)
    /// gives as the accounts of the identity provider: a failure when an account is not a dictionary with an <c>id</c>.
    /// </summary>
    internal static IdentityProviderAccount ParseAccount(JsonElement item)
    {
        if (item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            throw new FormatException("An account is not a dictionary");

        // The members are converted in lexicographical order
        var approvedClients = GetStringSequence(item, "approved_clients");
        var domainHints = GetStringSequence(item, "domain_hints");
        var email = GetString(item, "email");
        var givenName = GetString(item, "given_name");
        var id = GetString(item, "id") ?? throw new FormatException("id is required");
        var labelHints = GetStringSequence(item, "label_hints");
        var loginHints = GetStringSequence(item, "login_hints");
        var name = GetString(item, "name");
        var picture = GetString(item, "picture");
        var tel = GetString(item, "tel");
        var username = GetString(item, "username");
        return new IdentityProviderAccount(id)
        {
            Name = name,
            Email = email,
            Tel = tel,
            Username = username,
            GivenName = givenName,
            Picture = picture,
            ApprovedClients = approvedClients,
            LoginHints = loginHints,
            DomainHints = domainHints,
            LabelHints = labelHints,
        };
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderclientmetadata</summary>
    public static IdentityProviderClientMetadata? ParseClientMetadata(ReadOnlySpan<byte> body) => Parse(body, root =>
    {
        var thirdParty = GetMember(root, "client_is_third_party_to_top_frame_origin") is { } value && ToBoolean(value);
        var privacyPolicyUrl = GetString(root, "privacy_policy_url");
        var termsOfServiceUrl = GetString(root, "terms_of_service_url");
        return new IdentityProviderClientMetadata(privacyPolicyUrl, termsOfServiceUrl, thirdParty);
    });

    /// <summary>https://w3c-fedid.github.io/FedCM/#dictdef-identityassertionresponse</summary>
    public static IdentityAssertionResponse? ParseAssertion(ReadOnlySpan<byte> body) => Parse(body, root =>
    {
        var continueOn = GetString(root, "continue_on");
        IdentityCredentialErrorInit? error = null;
        if (GetMember(root, "error") is { } errorValue)
        {
            if (errorValue.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                throw new FormatException("error is not a dictionary");

            error = new IdentityCredentialErrorInit { Error = GetString(errorValue, "error"), Url = GetString(errorValue, "url") };
        }

        var token = GetMember(root, "token") is { } tokenValue ? tokenValue.GetRawText() : null;
        return new IdentityAssertionResponse(token, continueOn, error);
    });

    /// <summary>https://w3c-fedid.github.io/FedCM/#dictdef-disconnectedaccount: the identifier of the disconnected account.</summary>
    public static string? ParseDisconnect(ReadOnlySpan<byte> body) => Parse(body, root => GetString(root, "account_id") ?? throw new FormatException("account_id is required"));

    /// <summary>
    /// Parses the JSON text of a body (https://infra.spec.whatwg.org/#parse-json-bytes-to-a-javascript-value: the body is
    /// decoded as UTF-8) and converts its value with <paramref name="convert"/>: a value that is not a dictionary (e.g. a
    /// number) cannot be converted, and an invalid JSON text or a conversion that throws is a failure (null).
    /// </summary>
    private static T? Parse<T>(ReadOnlySpan<byte> body, Func<JsonElement, T?> convert)
        where T : class
    {
        try
        {
            var text = Encoding.UTF8.GetString(body);
            if (text.StartsWith('\uFEFF', StringComparison.Ordinal))
            {
                text = text[1..];
            }

            using var document = JsonDocument.Parse(text, Options);
            var root = document.RootElement;
            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                return null;

            return convert(root);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The value of a member of a dictionary (the last one, as <c>JSON.parse()</c> keeps the last duplicate), or null when it is missing (or the dictionary is null).</summary>
    private static JsonElement? GetMember(JsonElement dictionary, string name)
    {
        if (dictionary.ValueKind != JsonValueKind.Object)
            return null;

        JsonElement? result = null;
        foreach (var property in dictionary.EnumerateObject())
        {
            if (property.NameEquals(name))
            {
                result = property.Value;
            }
        }

        return result;
    }

    private static string? GetString(JsonElement dictionary, string name) => GetMember(dictionary, name) is { } value ? ToUsvString(value) : null;

    private static List<string>? GetStringSequence(JsonElement dictionary, string name)
    {
        if (GetMember(dictionary, name) is not { } value)
            return null;

        if (value.ValueKind != JsonValueKind.Array)
            throw new FormatException(name + " is not a sequence");

        var result = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            result.Add(ToUsvString(item));
        }

        return result;
    }

    /// <summary>The branding of the config file (https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderbranding), converted to check its types.</summary>
    private static void GetBranding(JsonElement root)
    {
        if (GetMember(root, "branding") is not { } branding)
            return;

        if (branding.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            throw new FormatException("branding is not a dictionary");

        _ = GetString(branding, "background_color");
        _ = GetString(branding, "color");
        if (GetMember(branding, "icons") is { } icons)
        {
            if (icons.ValueKind != JsonValueKind.Array)
                throw new FormatException("icons is not a sequence");

            foreach (var icon in icons.EnumerateArray())
            {
                if (icon.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                    throw new FormatException("An icon is not a dictionary");

                _ = GetString(icon, "url") ?? throw new FormatException("The url of an icon is required");
            }
        }

        _ = GetString(branding, "name");
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-boolean: ToBoolean of a JSON value.</summary>
    private static bool ToBoolean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.False or JsonValueKind.Null => false,
        JsonValueKind.String => value.GetString()!.Length > 0,
        JsonValueKind.Number => GetNumber(value) != 0,
        _ => true,
    };

    /// <summary>https://webidl.spec.whatwg.org/#js-USVString: ToString of a JSON value, whose unpaired surrogates are replaced by U+FFFD.</summary>
    internal static string ToUsvString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => ToScalarValues(GetStringValue(value)),
        JsonValueKind.Number => NumberToString(GetNumber(value)),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => string.Join(',', value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Null ? "" : ToUsvString(item))),
        _ => "[object Object]",
    };

    /// <summary>The value of a JSON string, whose escaped unpaired surrogates (e.g. <c>\uD800</c>) are kept as <c>JSON.parse()</c> keeps them.</summary>
    private static string GetStringValue(JsonElement value)
    {
        try
        {
            return value.GetString()!;
        }
        catch (InvalidOperationException)
        {
            // System.Text.Json does not decode the escaped unpaired surrogates: they are decoded here
            return DecodeJsonString(value.GetRawText());
        }
    }

    /// <summary>Decodes the raw text of a JSON string (with its quotes), including the escaped unpaired surrogates.</summary>
    private static string DecodeJsonString(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        for (var i = 1; i < raw.Length - 1; i++)
        {
            var c = raw[i];
            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            c = raw[++i];
            switch (c)
            {
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u':
                    builder.Append((char)int.Parse(raw.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-USVString: the unpaired surrogates are replaced by U+FFFD.</summary>
    private static string ToScalarValues(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsSurrogate(value[i]) && !(char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])))
            {
                var builder = new StringBuilder(value.Length);
                for (var j = 0; j < value.Length; j++)
                {
                    var c = value[j];
                    if (char.IsHighSurrogate(c) && j + 1 < value.Length && char.IsLowSurrogate(value[j + 1]))
                    {
                        builder.Append(c).Append(value[++j]);
                    }
                    else
                    {
                        builder.Append(char.IsSurrogate(c) ? '\uFFFD' : c);
                    }
                }

                return builder.ToString();
            }

            if (char.IsHighSurrogate(value[i]))
            {
                i++;
            }
        }

        return value;
    }

    /// <summary>The value of a JSON number: the numbers too large for a double are infinite, as in <c>JSON.parse()</c>.</summary>
    private static double GetNumber(JsonElement value) => value.TryGetDouble(out var number) ? number : double.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>https://tc39.es/ecma262/#sec-numeric-types-number-tostring for the finite numbers of a JSON text.</summary>
    private static string NumberToString(double value)
    {
        if (double.IsInfinity(value))
            return value > 0 ? "Infinity" : "-Infinity";

        if (value == 0)
            return "0";

        var abs = Math.Abs(value);
        if (abs is >= 1e-6 and < 1e21)
            return value.ToString("0.#####################", CultureInfo.InvariantCulture) is var fixedText && double.Parse(fixedText, CultureInfo.InvariantCulture) == value ? fixedText : value.ToString("R", CultureInfo.InvariantCulture);

        // The exponent notation: 1e+21, 1.5e-7
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Replace("E+", "e+", StringComparison.Ordinal).Replace("E-", "e-", StringComparison.Ordinal);
    }
}
