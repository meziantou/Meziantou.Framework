using System.Buffers;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

internal sealed class HttpBasicAuthenticationHandler : AuthenticationHandler<HttpBasicAuthenticationOptions>
{
    private const int StackallocThreshold = 256;

    private static readonly AuthenticateResult MissingCredentialsResult = AuthenticateResult.Fail("Missing credentials");
    private static readonly AuthenticateResult CredentialsTooLongResult = AuthenticateResult.Fail("Credentials are too long");
    private static readonly AuthenticateResult InvalidBase64CredentialsResult = AuthenticateResult.Fail("Invalid Base64 credentials");
    private static readonly AuthenticateResult InvalidCredentialsFormatResult = AuthenticateResult.Fail("Invalid credentials format");
    private static readonly AuthenticateResult InvalidUsernameOrPasswordResult = AuthenticateResult.Fail("Invalid username or password");

    public HttpBasicAuthenticationHandler(
        IOptionsMonitor<HttpBasicAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationHeaderValues))
            return AuthenticateResult.NoResult();

        if (!AuthenticationHeaderValue.TryParse(authorizationHeaderValues, out var headerValue))
            return AuthenticateResult.NoResult();

        if (!string.Equals(headerValue.Scheme, HttpBasicAuthenticationDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        if (string.IsNullOrWhiteSpace(headerValue.Parameter))
            return MissingCredentialsResult;

        if (headerValue.Parameter.Length > Options.MaxCredentialLength)
            return CredentialsTooLongResult;

        if (!TryDecodeCredentials(headerValue.Parameter, out var credentials))
        {
            return InvalidBase64CredentialsResult;
        }

        var separatorIndex = credentials.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0)
            return InvalidCredentialsFormatResult;

        var username = credentials[..separatorIndex];
        var password = credentials[(separatorIndex + 1)..];
        var isValid = await Options.ValidateCredentials.Invoke(Context, username, password).ConfigureAwait(false);
        if (!isValid)
            return InvalidUsernameOrPasswordResult;

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.NameIdentifier, username),
        };

        var identity = new ClaimsIdentity(claims, authenticationType: Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (Options.Realm is null)
        {
            Response.Headers.WWWAuthenticate = "Basic charset=\"UTF-8\"";
        }
        else
        {
            var escapedRealm = EscapeHeaderValue(Options.Realm);
            Response.Headers.WWWAuthenticate = $"Basic realm=\"{escapedRealm}\", charset=\"UTF-8\"";
        }

        return Task.CompletedTask;
    }

    private static string EscapeHeaderValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool TryDecodeCredentials(string encodedCredentials, out string credentials)
    {
        byte[]? rentedBuffer = null;

        try
        {
            var maxDecodedLength = GetMaximumDecodedLength(encodedCredentials.Length);
            var credentialBytes = maxDecodedLength <= StackallocThreshold ? stackalloc byte[maxDecodedLength] : (rentedBuffer = ArrayPool<byte>.Shared.Rent(maxDecodedLength));

            if (!Convert.TryFromBase64String(encodedCredentials, credentialBytes, out var bytesWritten))
            {
                credentials = "";
                return false;
            }

            credentials = Encoding.UTF8.GetString(credentialBytes[..bytesWritten]);
            return true;
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    private static int GetMaximumDecodedLength(int encodedLength)
    {
        return (int)((encodedLength + 3L) / 4L * 3L);
    }
}
