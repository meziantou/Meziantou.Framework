using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of the <c>fedcm</c> tests (<c>fedcm/support/*.py</c>, and <c>/.well-known/web-identity</c>, which serve.py
/// routes to its Python file): the endpoints of the identity provider, which check the requests of the user agent as
/// <c>request-params-check.py</c> does. The handlers of the email verification protocol (<c>delegation-*.py</c>) and of the
/// navigation interception (<c>intercept_header.py</c>, <c>redirect_post.py</c>) are not emulated.
/// </summary>
internal static partial class WptPythonHandlers
{
    // fedcm/support/keys.py
    private const string ClientMetadataCounterKey = "bdc14e3e-b8bc-44a1-8eec-78da5fdacbc3";
    private const string ManifestUrlInManifestListKey = "7f3f7478-b7f0-41c5-b357-f3ac16f5f25a";
    private const string AccountPictureCounterKey = "9dca7d23-149d-4a21-8eda-851329365d10";
    private const string AccountPictureUncachedCounterKey = "ee1a8abc-f656-4f6b-a14f-d63670ff031b";
    private const string WellKnownFormatKey = "a1b2c3d4-e5f6-7890-1234-56789abcdef0";
    private const string WellKnownPath = "/.well-known/web-identity";

    /// <summary>The PNG of the account pictures (a 1x1 grayscale image).</summary>
    private static readonly byte[] AccountPicture = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNiAAAABgADNjd8qAAAAABJRU5ErkJggg==");

    private const string OneAccount = """

        {
         "accounts": [{
           "id": "1234",
           "given_name": "John",
           "name": "John Doe",
           "email": "john_doe@idp.example",
           "approved_clients": ["123", "456", "789"],
           "login_hints": ["john_doe"],
           "domain_hints": ["idp.example", "example"]
          }]
        }

        """;

    private static readonly Dictionary<string, Handler> FedCmHandlers = new(StringComparer.Ordinal)
    {
        [WellKnownPath] = WebIdentity,
        ["/fedcm/support/acao-cors.py"] = (request, response, _) =>
        {
            if (request.Header("Origin") is { } origin)
            {
                response.Set("Access-Control-Allow-Origin", origin);
            }
        },
        ["/fedcm/support/no-cors.py"] = (_, _, _) => { },
        ["/fedcm/support/account_picture.py"] = (request, response, stash) => AccountPictureHandler(request, response, stash, AccountPictureCounterKey, "/fedcm/support/account_picture.py", "max-age=3600"),
        ["/fedcm/support/account_picture_uncached.py"] = (request, response, stash) => AccountPictureHandler(request, response, stash, AccountPictureUncachedCounterKey, "/fedcm/support/account_picture_uncached.py", "max-age=0"),
        ["/fedcm/support/account_picture_get_count.py"] = (_, response, stash) => response.Content = stash.TakeValue(AccountPictureCounterKey, "/fedcm/support/account_picture.py") as string ?? "0",
        ["/fedcm/support/account_picture_uncached_get_count.py"] = (_, response, stash) => response.Content = stash.TakeValue(AccountPictureUncachedCounterKey, "/fedcm/support/account_picture_uncached.py") as string ?? "0",
        ["/fedcm/support/accounts.py"] = (request, response, _) => AccountsHandler(request, response, OneAccount),
        ["/fedcm/support/accounts_check_same_site_strict.py"] = (request, response, _) =>
        {
            if (FedCmAccountsCheck(request, response))
                return;

            var cookies = ParseRequestCookies(request);
            if (cookies.Exists(cookie => cookie.Key == "same_site_strict" && cookie.Value == "1"))
            {
                FedCmError(response, 546, "Should not send SameSite=Strict cookies");
            }
            else if (cookies.Exists(cookie => cookie.Key == "same_site_lax" && cookie.Value == "1"))
            {
                FedCmError(response, 547, "Should not send SameSite=Lax cookies");
            }
            else if (request.Header("Sec-Fetch-Site") != "cross-site")
            {
                FedCmError(response, 538, "Wrong Sec-Fetch-Site header");
            }
            else
            {
                Json(response, OneAccount);
            }
        },
        ["/fedcm/support/accounts_no_approved_clients.py"] = (request, response, _) => AccountsHandler(request, response, """
            {
             "accounts": [{
               "id": "1234",
               "given_name": "John",
               "name": "John Doe",
               "email": "john_doe@idp.example",
               "login_hints": ["john_doe"],
               "domain_hints": ["idp.example", "example"]
              },
              {
               "id": "jane_doe",
               "given_name": "Jane",
               "name": "Jane Doe",
               "email": "jane_doe@idp.example"
              }]
            }
            """),
        ["/fedcm/support/no_accounts.py"] = (request, response, _) => AccountsHandler(request, response, """{ "accounts": [] }"""),
        ["/fedcm/support/picture_account.py"] = (request, response, _) => AccountsHandler(request, response, """
            {
             "accounts": [{
               "id": "1234",
               "given_name": "John",
               "name": "John Doe",
               "email": "john_doe@idp.example",
               "picture": "https://localhost/profile/123",
               "approved_clients": ["123", "456", "789"],
               "login_hints": ["john_doe"],
               "domain_hints": ["idp.example", "example"]
              }]
            }
            """),
        ["/fedcm/support/single_account.py"] = (request, response, _) => AccountsHandler(request, response, """
            {
             "accounts": [
              {
               "id": "john_doe",
               "given_name": "John",
               "name": "John Doe",
               "email": "john_doe@idp.example",
               "approved_clients": ["123", "456", "789"]
              }
              ]
            }
            """),
        ["/fedcm/support/single_identifier_accounts.py"] = (request, response, _) => AccountsHandler(request, response, """
            {
             "accounts": [
              {
               "id": "john_doe",
               "username": "john_doe"
              },
              {
               "id": "john_doe2",
               "tel": "+1 (416) 000-0000"
              }
              ]
            }
            """),
        ["/fedcm/support/two_accounts.py"] = (request, response, _) => AccountsHandler(request, response, """
            {
             "accounts": [
              {
               "id": "jane_doe",
               "given_name": "Jane",
               "name": "Jane Doe",
               "email": "jane_doe@idp.example",
               "approved_clients": ["123", "abc"]
              },
              {
               "id": "john_doe",
               "given_name": "John",
               "name": "John Doe",
               "email": "john_doe@idp.example",
               "approved_clients": ["123", "456", "789"],
               "login_hints": ["john_doe"],
               "domain_hints": ["idp.example", "example"]
              }
              ]
            }
            """),
        ["/fedcm/support/variable_accounts.py"] = (request, response, _) =>
        {
            const string John = """{ "id": "1234", "given_name": "John", "name": "John Doe", "email": "john_doe@idp.example", "approved_clients": ["123", "456", "789"], "login_hints": ["john_doe"], "hosted_domains": ["idp.example", "example"] }""";
            const string Jane = """{ "id": "jane_doe", "given_name": "Jane", "name": "Jane Doe", "email": "jane_doe@idp.example", "approved_clients": ["123", "abc"] }""";
            var accounts = ParseRequestCookies(request).Find(cookie => cookie.Key == "accounts").Value switch
            {
                "1" => John,
                "2" => John + ", " + Jane,
                _ => "",
            };
            AccountsHandler(request, response, "{ \"accounts\": [" + accounts + "] }");
        },
        ["/fedcm/support/client_metadata.py"] = (request, response, stash) =>
        {
            if (FedCmClientMetadataCheck(request, response))
                return;

            var counter = int.TryParse(stash.TakeValue(ClientMetadataCounterKey, "/fedcm/support/client_metadata.py") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value + 1 : 1;
            stash.Put(ClientMetadataCounterKey, counter.ToString(CultureInfo.InvariantCulture), "/fedcm/support/client_metadata.py");
            Json(response, "{ \"privacy_policy_url\": \"https://privacypolicy" + counter.ToString(CultureInfo.InvariantCulture) + ".com\" }");
        },
        ["/fedcm/support/client_metadata_clear_count.py"] = (_, response, stash) => response.Content = stash.TakeValue(ClientMetadataCounterKey, "/fedcm/support/client_metadata.py") as string ?? "",
        ["/fedcm/support/client_metadata_iframe.py"] = (request, response, _) =>
        {
            if (FedCmClientMetadataCheck(request, response))
                return;

            if (string.IsNullOrEmpty(request.Get("top_frame_origin")))
            {
                FedCmError(response, 560, "Missing top_frame_origin");
                return;
            }

            Json(response, """{ "client_is_third_party_to_top_frame_origin": true }""");
        },
        ["/fedcm/support/manifest.py"] = (request, response, _) => ManifestHandler(request, response, """
            {
              "accounts_endpoint": "accounts.py",
              "client_metadata_endpoint": "client_metadata.py",
              "id_assertion_endpoint": "token.py",
              "disconnect_endpoint": "disconnect.py",
              "login_url": "login.html"
            }
            """),
        ["/fedcm/support/manifest_iframe.py"] = (request, response, _) => ManifestHandler(request, response, """
            {
              "accounts_endpoint": "accounts.py",
              "client_metadata_endpoint": "client_metadata_iframe.py",
              "id_assertion_endpoint": "token.py",
              "login_url": "login.html"
            }
            """),
        ["/fedcm/support/token.py"] = (request, response, _) =>
        {
            if (FedCmTokenCheck(request, response))
                return;

            response.Set("Content-Type", "application/json");
            if (!request.Has("nocors"))
            {
                AllowCredentialedCors(request, response);
            }

            response.Content = """{"token": "token"}""";
        },
        ["/fedcm/support/token_check_disclosure_shown_false.py"] = (request, response, _) => DisclosureTokenHandler(request, response, "false", null),
        ["/fedcm/support/token_check_disclosure_shown_true.py"] = (request, response, _) => DisclosureTokenHandler(request, response, "true", "name,email,picture"),
        ["/fedcm/support/token_check_params.py"] = (request, response, _) =>
        {
            if (FedCmTokenCheck(request, response))
                return;

            // A missing parameter is a KeyError: the response is a 500
            using var parameters = JsonDocument.Parse(GetPost(request, "params") ?? throw new KeyNotFoundException("params"));
            if (parameters.RootElement.GetProperty("a string").GetString() != "a value")
            {
                FedCmError(response, 550, "Incorrect parameters");
                return;
            }

            TokenResponse(request, response, "token");
        },
        ["/fedcm/support/token_check_same_site_strict.py"] = (request, response, _) =>
        {
            if (FedCmTokenCheck(request, response))
                return;

            var cookies = ParseRequestCookies(request);
            if (cookies.Exists(cookie => cookie.Key == "same_site_strict" && cookie.Value == "1"))
            {
                FedCmError(response, 546, "Should not send SameSite=Strict cookies");
            }
            else if (cookies.Exists(cookie => cookie.Key == "same_site_lax" && cookie.Value == "1"))
            {
                FedCmError(response, 547, "Should not send SameSite=Lax cookies");
            }
            else
            {
                TokenResponse(request, response, "token");
            }
        },
        ["/fedcm/support/token_with_account_id.py"] = (request, response, _) => TokenHandler(request, response, request => "account_id=" + GetPost(request, "account_id")),
        ["/fedcm/support/token_with_auto_selected_flag.py"] = (request, response, _) => TokenHandler(request, response, request => "is_auto_selected=" + GetPost(request, "is_auto_selected")),
        ["/fedcm/support/token_with_rp_mode.py"] = (request, response, _) => TokenHandler(request, response, request => "mode=" + (GetPost(request, "mode") ?? throw new KeyNotFoundException("mode"))),
        ["/fedcm/support/token_with_http_error.py"] = (request, response, _) =>
        {
            if (FedCmTokenCheck(request, response))
                return;

            TokenResponse(request, response, "token");
            response.Status = 403;
            response.StatusText = "Forbidden";
        },
        ["/fedcm/support/error_with_code_and_url.py"] = (request, response, _) => ErrorTokenHandler(request, response, """{"error": {"code": "unauthorized_client", "url": "error.html"}}"""),
        ["/fedcm/support/error_with_error_and_url_format.py"] = (request, response, _) => ErrorTokenHandler(request, response, """{"error": {"error": "unauthorized_client", "url": "error.html"}}"""),
        ["/fedcm/support/flexible_tokens.py"] = (request, response, _) =>
        {
            if (FedCmTokenCheck(request, response))
                return;

            using var parameters = JsonDocument.Parse(GetPost(request, "params") ?? throw new KeyNotFoundException("params"));
            var tokenType = parameters.RootElement.ValueKind == JsonValueKind.Object && parameters.RootElement.TryGetProperty("token_type", out var type) ? type.GetString() : "string";
            response.Set("Content-Type", "application/json");
            AllowCredentialedCors(request, response);
            response.Content = tokenType switch
            {
                "positive-number" => """{"token": 12345}""",
                "negative-number" => """{"token": -42}""",
                "boolean-true" => """{"token": true}""",
                "boolean-false" => """{"token": false}""",
                "null" => """{"token": null}""",
                "zero" => """{"token": 0}""",
                "float" => """{"token": 3.14159}""",
                "array" => """{"token": ["token1", "token2", "token3"]}""",
                "empty-array" => """{"token": []}""",
                "mixed-array" => """{"token": ["string", 123, true, null, {"key": "value"}]}""",
                "object" => """{"token": {"access_token": "abc123", "token_type": "Bearer", "expires_in": 3600}}""",
                "empty-object" => """{"token": {}}""",
                "nested-object" => """{"token": {"user": {"id": "123", "profile": {"name": "Test User", "preferences": {"theme": "dark"}}}}}""",
                _ => """{"token": "simple_string_token"}""",
            };
        },
        ["/fedcm/support/continue_on.py"] = (request, response, _) =>
        {
            if (FedCmTokenCheck(request, response))
                return;

            response.Set("Content-Type", "application/json");
            AllowCredentialedCors(request, response);
            var account = GetPost(request, "account_id");
            var nonce = GetPost(request, "nonce") ?? "";
            var parameters = GetPost(request, "params") ?? "";
            response.Content = nonce == "token"
                ? "{\"token\": \"account=" + account + "\"}"
                : "{\"continue_on\": \"resolve.html?selected=" + account + "&" + nonce + "&" + parameters + "\"}";
        },
        ["/fedcm/support/continue_on_flexible_tokens.py"] = (request, response, _) =>
        {
            if (FedCmTokenCheck(request, response))
                return;

            response.Set("Content-Type", "application/json");
            AllowCredentialedCors(request, response);
            var account = GetPost(request, "account_id");
            var parameters = GetPost(request, "params") ?? throw new KeyNotFoundException("params");
            response.Content = "{\"continue_on\": \"resolve_flexible_tokens.html?selected=" + account + "&params=" + PythonQuote(parameters) + "\"}";
        },
        ["/fedcm/support/disconnect.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "application/json");
            AllowCredentialedCors(request, response);
            if (FedCmCommonCheck(request, response, "cors") || FedCmCredentialedCheck(request, response) || FedCmPostCheck(request, response))
                return;

            if (string.IsNullOrEmpty(GetPost(request, "account_hint")))
            {
                FedCmError(response, 544, "Missing 'account_hint' POST parameter");
                return;
            }

            response.Content = "{\"account_id\": \"" + GetPost(request, "account_hint") + "\"}";
        },
        ["/fedcm/support/disconnect_failure.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "application/json");
            AllowCredentialedCors(request, response);
            FedCmError(response, 599, "Server failure");
        },
        ["/fedcm/support/select_manifest_in_root_manifest.py"] = (request, response, stash) =>
        {
            stash.TakeValue(ManifestUrlInManifestListKey, WellKnownPath);
            stash.Put(ManifestUrlInManifestListKey, request.Get("manifest_url") ?? "", WellKnownPath);
            response.Content = WellKnownPath;
        },
        ["/fedcm/support/set-well-known-format.py"] = (request, response, stash) =>
        {
            stash.TakeValue(WellKnownFormatKey, WellKnownPath);
            stash.Put(WellKnownFormatKey, request.Get("format") ?? "default", WellKnownPath);
            response.Content = WellKnownPath;
        },
        ["/fedcm/support/set_accounts_cookie.py"] = (request, response, _) =>
        {
            var value = "accounts=" + request.Query + "; SameSite=None; Secure; HttpOnly";
            response.Set("Set-Cookie", value);
            response.Set("Content-Type", "text/html");
            response.Content = """

                <!DOCTYPE html>
                <script>
                // The important part of this page are the headers.
                // If this page was opened as a popup, notify the opener.
                if (window.opener) {
                  window.opener.postMessage("done_loading", "*");
                  window.close();
                }
                </script>
                Sent header value: {}".format(header_value)

                """;
        },
    };

    /// <summary>
    /// <c>/.well-known/web-identity</c>: the well-known file of the identity provider, whose format is set by
    /// <c>set-well-known-format.py</c> and whose config URL is set by <c>select_manifest_in_root_manifest.py</c>.
    /// </summary>
    private static void WebIdentity(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var format = stash.TakeValue(WellKnownFormatKey, WellKnownPath) as string;
        var hostname = request.Url.Hostname;
        var baseUrl = "https://" + hostname + ":8443";
        var manifestUrl = stash.TakeValue(ManifestUrlInManifestListKey, WellKnownPath) as string;
        if (string.IsNullOrEmpty(manifestUrl))
        {
            manifestUrl = baseUrl + "/fedcm/support/manifest.py";
        }

        (int Status, string Message)? error = ParseRequestCookies(request).Count > 0 ? (530, "Cookie should not be sent to manifest list endpoint")
            : request.Header("Accept") != "application/json" ? (531, "Wrong Accept")
            : request.Header("Sec-Fetch-Dest") != "webidentity" ? (532, "Wrong Sec-Fetch-Dest header")
            : request.HasHeader("Referer") ? (533, "Should not have Referer")
            : request.HasHeader("Origin") ? (534, "Should not have Origin")
            : request.Header("Sec-Fetch-Mode") != "no-cors" ? (535, "Wrong Sec-Fetch-Mode header")
            : request.Header("Sec-Fetch-Site") != "cross-site" ? (536, "Wrong Sec-Fetch-Site header")
            : null;
        if (error is { } failure)
        {
            FedCmError(response, failure.Status, failure.Message);
            return;
        }

        response.Set("Content-Type", "application/json");
        response.Content = format switch
        {
            "direct" => "{ \"accounts_endpoint\": \"" + baseUrl + "/fedcm/support/accounts.py\", \"login_url\": \"" + baseUrl + "/fedcm/support/login.html\" }",
            "delegation" => "{ \"accounts_endpoint\": \"https://" + hostname + "/fedcm/support/accounts.py\", \"login_url\": \"https://" + hostname + "/fedcm/support/login.html\" }",
            "empty" => """{ "accounts_endpoint": "", "login_url": "" }""",
            "missing" => "{ }",
            "partial_accounts" => "{ \"accounts_endpoint\": \"" + baseUrl + "/fedcm/support/accounts.py\" }",
            "partial_login" => "{ \"login_url\": \"" + baseUrl + "/fedcm/support/login.html\" }",
            _ => "{ \"provider_urls\": [ \"" + manifestUrl + "\" ] }",
        };
    }

    private static void AccountPictureHandler(WptHandlerRequest request, WptHandlerResponse response, WptStash stash, string key, string path, string cacheControl)
    {
        if (FedCmCommonCheck(request, response, "no-cors", accept: null) || FedCmUncredentialedCheck(request, response))
            return;

        if (request.HasHeader("Origin"))
        {
            FedCmError(response, 539, "Should not have Origin");
            return;
        }

        var counter = int.TryParse(stash.TakeValue(key, path) as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value + 1 : 1;
        stash.Put(key, counter.ToString(CultureInfo.InvariantCulture), path);
        response.Set("Content-Type", "image/png");
        response.Set("Cache-Control", cacheControl);
        response.Body = AccountPicture;
    }

    private static void AccountsHandler(WptHandlerRequest request, WptHandlerResponse response, string accounts)
    {
        if (!FedCmAccountsCheck(request, response))
        {
            Json(response, accounts);
        }
    }

    private static void ManifestHandler(WptHandlerRequest request, WptHandlerResponse response, string manifest)
    {
        // request-params-check.py: manifestCheck
        if (FedCmCommonCheck(request, response, "no-cors") || FedCmUncredentialedCheck(request, response))
            return;

        if (request.HasHeader("Origin"))
        {
            FedCmError(response, 539, "Should not have Origin");
            return;
        }

        Json(response, manifest);
    }

    private static void TokenHandler(WptHandlerRequest request, WptHandlerResponse response, Func<WptHandlerRequest, string> token)
    {
        if (!FedCmTokenCheck(request, response))
        {
            TokenResponse(request, response, token(request));
        }
    }

    private static void TokenResponse(WptHandlerRequest request, WptHandlerResponse response, string token)
    {
        response.Set("Content-Type", "application/json");
        AllowCredentialedCors(request, response);
        response.Content = "{\"token\": \"" + token + "\"}";
    }

    private static void ErrorTokenHandler(WptHandlerRequest request, WptHandlerResponse response, string body)
    {
        if (FedCmTokenCheck(request, response))
            return;

        response.Set("Content-Type", "application/json");
        response.Status = 401;
        response.StatusText = "Unauthorized";
        response.Content = body;
    }

    private static void DisclosureTokenHandler(WptHandlerRequest request, WptHandlerResponse response, string disclosureTextShown, string? disclosureShownFor)
    {
        if (FedCmTokenCheck(request, response))
            return;

        var nonce = GetPost(request, "nonce") ?? "";
        if (GetPost(request, "disclosure_text_shown") != disclosureTextShown)
        {
            FedCmError(response, 560, "disclosure_text_shown is not " + disclosureTextShown);
        }
        else if (GetPost(request, "disclosure_shown_for") != disclosureShownFor)
        {
            FedCmError(response, 561, "disclosure_shown_for is not " + (disclosureShownFor ?? "None"));
        }
        else if ((GetPost(request, "fields") ?? "") != nonce)
        {
            FedCmError(response, 562, "fields does not match nonce");
        }
        else
        {
            TokenResponse(request, response, "token");
        }
    }

    private static void AllowCredentialedCors(WptHandlerRequest request, WptHandlerResponse response)
    {
        response.Set("Access-Control-Allow-Origin", request.Header("Origin"));
        response.Set("Access-Control-Allow-Credentials", "true");
    }

    private static void Json(WptHandlerResponse response, string content)
    {
        response.Set("Content-Type", "application/json");
        response.Content = content;
    }

    /// <summary>A response of <c>request-params-check.py</c>: <c>return (status, [], message)</c>.</summary>
    private static void FedCmError(WptHandlerResponse response, int status, string message)
    {
        response.Status = status;
        response.Content = message;
    }

    // request-params-check.py: the checks return true (and set the error response) when the request is not the expected one

    private static bool FedCmCommonCheck(WptHandlerRequest request, WptHandlerResponse response, string mode, string? accept = "application/json")
    {
        (int Status, string Message)? error = accept is not null && request.Header("Accept") != accept ? (531, "Wrong Accept")
            : request.Header("Sec-Fetch-Dest") != "webidentity" ? (532, "Wrong Sec-Fetch-Dest header")
            : !string.IsNullOrEmpty(request.Header("Referer")) ? (533, "Should not have Referer")
            : request.Header("Sec-Fetch-Mode") != mode ? (534, "Wrong Sec-Fetch-Mode header")
            : null;
        if (error is { } failure)
        {
            FedCmError(response, failure.Status, failure.Message);
            return true;
        }

        return false;
    }

    private static bool FedCmUncredentialedCheck(WptHandlerRequest request, WptHandlerResponse response)
    {
        if (ParseRequestCookies(request).Count > 0)
        {
            FedCmError(response, 535, "Cookie should not be sent to this endpoint");
            return true;
        }

        if (request.Header("Sec-Fetch-Site") != "cross-site")
        {
            FedCmError(response, 536, "Wrong Sec-Fetch-Site header");
            return true;
        }

        return false;
    }

    private static bool FedCmCredentialedCheck(WptHandlerRequest request, WptHandlerResponse response)
    {
        if (ParseRequestCookies(request).Find(cookie => cookie.Key == "cookie").Value != "1")
        {
            FedCmError(response, 537, "Missing cookie");
            return true;
        }

        return false;
    }

    private static bool FedCmPostCheck(WptHandlerRequest request, WptHandlerResponse response)
    {
        (int Status, string Message)? error = string.IsNullOrEmpty(request.Header("Origin")) ? (540, "Missing Origin")
            : request.Method != "POST" ? (541, "Method is not POST")
            : request.Header("Content-Type") != "application/x-www-form-urlencoded" ? (542, "Wrong Content-Type")
            : string.IsNullOrEmpty(GetPost(request, "client_id")) ? (543, "Missing 'client_id' POST parameter")
            : null;
        if (error is { } failure)
        {
            FedCmError(response, failure.Status, failure.Message);
            return true;
        }

        return false;
    }

    private static bool FedCmAccountsCheck(WptHandlerRequest request, WptHandlerResponse response)
    {
        if (FedCmCommonCheck(request, response, "no-cors") || FedCmCredentialedCheck(request, response))
            return true;

        if (request.HasHeader("Origin"))
        {
            FedCmError(response, 539, "Should not have Origin");
            return true;
        }

        return false;
    }

    private static bool FedCmClientMetadataCheck(WptHandlerRequest request, WptHandlerResponse response)
    {
        if (request.Get("skip_checks", "0") == "1")
            return false;

        if (FedCmCommonCheck(request, response, "no-cors") || FedCmUncredentialedCheck(request, response))
            return true;

        if (string.IsNullOrEmpty(request.Header("Origin")))
        {
            FedCmError(response, 540, "Missing Origin");
            return true;
        }

        return false;
    }

    private static bool FedCmTokenCheck(WptHandlerRequest request, WptHandlerResponse response)
    {
        if (FedCmCommonCheck(request, response, "cors") || FedCmCredentialedCheck(request, response))
            return true;

        if (request.Header("Sec-Fetch-Site") == "none")
        {
            FedCmError(response, 538, "Wrong Sec-Fetch-Site header");
            return true;
        }

        if (FedCmPostCheck(request, response))
            return true;

        if (string.IsNullOrEmpty(GetPost(request, "account_id")))
        {
            FedCmError(response, 544, "Missing 'account_id' POST parameter");
            return true;
        }

        if (string.IsNullOrEmpty(GetPost(request, "disclosure_text_shown")))
        {
            FedCmError(response, 545, "Missing 'disclosure_text_shown' POST parameter");
            return true;
        }

        return false;
    }
}
