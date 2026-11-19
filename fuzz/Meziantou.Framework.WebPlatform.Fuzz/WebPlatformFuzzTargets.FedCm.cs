using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The parsers of the responses of the endpoints of a FedCM identity provider: the well-known file, the config file, the
// accounts, the client metadata, the identity assertion and the disconnect endpoints
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The tokens of the fedcm target: the JSON punctuation and literals, and the members of the dictionaries of the endpoints.</summary>
    private static string[] FedCmTokens =>
    [
        "{", "}", "[", "]", ":", ",", "\"", "\\u", "\\uD800", "true", "false", "null", "1e400", "-0", "\uFEFF",
        "\"provider_urls\"", "\"accounts_endpoint\"", "\"login_url\"", "\"client_metadata_endpoint\"", "\"id_assertion_endpoint\"",
        "\"disconnect_endpoint\"", "\"branding\"", "\"icons\"", "\"url\"", "\"size\"", "\"supports_use_other_account\"", "\"account_label\"",
        "\"accounts\"", "\"id\"", "\"name\"", "\"email\"", "\"tel\"", "\"username\"", "\"given_name\"", "\"picture\"", "\"approved_clients\"",
        "\"login_hints\"", "\"domain_hints\"", "\"label_hints\"", "\"privacy_policy_url\"", "\"terms_of_service_url\"",
        "\"client_is_third_party_to_top_frame_origin\"", "\"token\"", "\"continue_on\"", "\"error\"", "\"account_id\"",
    ];

    /// <summary>
    /// Parses the input as the response of every endpoint of an identity provider: the parsers return null for an invalid
    /// response and never throw. A parsed well-known file has at most one provider URL, the strings of the dictionaries are
    /// scalar value strings (USVString), every account is displayable, the token of an identity assertion is a JSON text,
    /// and the accounts and the disconnected account survive a round trip through their JSON serialization.
    /// </summary>
    public static void FedCm(ReadOnlySpan<byte> data)
    {
        if (IdentityProviderResponses.ParseWellKnown(data) is { } wellKnown)
        {
            FuzzAssert.True(wellKnown.ProviderUrls is null or { Count: <= 1 }, "The well-known file has more than one provider URL");
            CheckScalarValues([.. wellKnown.ProviderUrls ?? [], wellKnown.AccountsEndpoint, wellKnown.LoginUrl]);
        }

        if (IdentityProviderResponses.ParseConfig(data) is { } config)
        {
            CheckScalarValues([config.AccountsEndpoint, config.ClientMetadataEndpoint, config.IdAssertionEndpoint, config.LoginUrl, config.DisconnectEndpoint, config.AccountLabel]);
        }

        if (IdentityProviderResponses.ParseAccounts(data) is { } accounts)
        {
            foreach (var account in accounts)
            {
                FuzzAssert.True(account.Name is not null || account.Email is not null || account.Tel is not null || account.Username is not null, $"The account {FuzzAssert.Format(account.Id)} is not displayable");
                CheckScalarValues([account.Id, account.Name, account.Email, account.Tel, account.Username, account.GivenName, account.Picture, .. account.ApprovedClients ?? [], .. account.LoginHints ?? [], .. account.DomainHints ?? [], .. account.LabelHints ?? []]);
            }

            // The accounts serialized as JSON are parsed to the same accounts
            var json = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["accounts"] = accounts.Select(account => new Dictionary<string, object?>
                {
                    ["id"] = account.Id,
                    ["name"] = account.Name,
                    ["email"] = account.Email,
                    ["tel"] = account.Tel,
                    ["username"] = account.Username,
                    ["given_name"] = account.GivenName,
                    ["picture"] = account.Picture,
                    ["approved_clients"] = account.ApprovedClients,
                    ["login_hints"] = account.LoginHints,
                    ["domain_hints"] = account.DomainHints,
                    ["label_hints"] = account.LabelHints,
                }.Where(member => member.Value is not null).ToDictionary()).ToList(),
            });
            var reparsed = IdentityProviderResponses.ParseAccounts(json);
            FuzzAssert.True(reparsed is not null, "The serialized accounts are not parsed");
            FuzzAssert.Equal(FormatAccounts(accounts), FormatAccounts(reparsed), "The accounts after a round trip");
        }

        if (IdentityProviderResponses.ParseClientMetadata(data) is { } metadata)
        {
            CheckScalarValues([metadata.PrivacyPolicyUrl, metadata.TermsOfServiceUrl]);
        }

        if (IdentityProviderResponses.ParseAssertion(data) is { } assertion)
        {
            CheckScalarValues([assertion.ContinueOn, assertion.Error?.Error, assertion.Error?.Url]);
            if (assertion.TokenJson is { } token)
            {
                try
                {
                    using var document = JsonDocument.Parse(token);
                }
                catch (JsonException ex)
                {
                    FuzzAssert.Fail($"The token {FuzzAssert.Format(token)} is not a JSON text: {ex.Message}");
                }
            }
        }

        if (IdentityProviderResponses.ParseDisconnect(data) is { } accountId)
        {
            CheckScalarValues([accountId]);
            var json = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["account_id"] = accountId });
            FuzzAssert.Equal(accountId, IdentityProviderResponses.ParseDisconnect(json), "The disconnected account after a round trip");
        }
    }

    private static string FormatAccounts(IReadOnlyList<IdentityProviderAccount> accounts)
        => string.Join(';', accounts.Select(account => string.Join('|', account.Id, account.Name, account.Email, account.Tel, account.Username, account.GivenName, account.Picture,
            account.ApprovedClients is null ? "-" : string.Join(',', account.ApprovedClients), account.LoginHints is null ? "-" : string.Join(',', account.LoginHints),
            account.DomainHints is null ? "-" : string.Join(',', account.DomainHints), account.LabelHints is null ? "-" : string.Join(',', account.LabelHints))));

    /// <summary>The strings converted to USVString have no unpaired surrogate.</summary>
    private static void CheckScalarValues(IEnumerable<string?> values)
    {
        foreach (var value in values)
        {
            if (value is null)
                continue;

            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                FuzzAssert.False(char.IsSurrogate(value[i]), $"The string {FuzzAssert.Format(value)} has an unpaired surrogate");
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetFedCmSeeds()
    {
        // The responses of the unit tests (HostDeviceApisTests.IdentityProviderResponses_*) and of the handlers of fedcm/support
        string[] responses =
        [
            """{ "provider_urls": ["https://idp.example/fedcm.json"] }""",
            """{ "accounts_endpoint": "/accounts", "login_url": "/login" }""",
            """{ "provider_urls": ["https://a.example/1", "https://a.example/2"] }""",
            """{ "provider_urls": "https://idp.example/fedcm.json" }""",
            "null",
            "[]",
            """{ "provider_urls": [1, true, null] """,
            """{ "accounts_endpoint": "a", "id_assertion_endpoint": "t", "login_url": "l" }""",
            """{ "accounts_endpoint": "a", "client_metadata_endpoint": "m", "id_assertion_endpoint": "t", "login_url": "l", "disconnect_endpoint": "d", "supports_use_other_account": true, "account_label": "x", "branding": { "name": "IdP", "icons": [{ "url": "i.png", "size": 32 }] } }""",
            """{ "accounts_endpoint": 12, "id_assertion_endpoint": 1.5, "login_url": false, "supports_use_other_account": "" }""",
            """{ "accounts_endpoint": "a", "id_assertion_endpoint": "t", "login_url": "l", "branding": { "icons": [{ "size": 1 }] } }""",
            """{ "accounts": [{ "id": "1", "name": "John", "email": "j@idp.example", "given_name": "J", "picture": "p.png", "approved_clients": ["a", "b"], "login_hints": ["h"], "domain_hints": ["d"], "label_hints": ["l"] }] }""",
            """{ "accounts": [{ "id": "1", "username": "u" }, { "id": 2, "tel": "+1" }] }""",
            """{ "accounts": [{ "id": "1" }] }""",
            """{ "accounts": [{ "id": "\uD800", "name": "\u00E9" }] }""",
            """{ "privacy_policy_url": "p", "terms_of_service_url": "t", "client_is_third_party_to_top_frame_origin": true }""",
            """{ "token": { "a": [1, null] } }""",
            """{ "continue_on": "/continue" }""",
            """{ "error": { "error": "access_denied", "url": "/error" } }""",
            """{ "error": "access_denied" }""",
            """{ "account_id": 1e21 }""",
            """{ "account_id": [1, [2, 3]] }""",
            "\uFEFF{ \"account_id\": \"1\" }",
        ];
        foreach (var (response, index) in responses.Select((response, index) => (response, index)))
        {
            yield return new FuzzSeed($"fedcm-{index}", Encoding.UTF8.GetBytes(response));
        }
    }
}
