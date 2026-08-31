using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Unicode;
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
    private static readonly AuthenticateResult InvalidCredentialsEncodingResult = AuthenticateResult.Fail("Credentials are not valid UTF-8");
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

        var decodeResult = DecodeCredentials(headerValue.Parameter, out var credentials);
        if (decodeResult is not CredentialsDecodeResult.Success)
        {
            return decodeResult is CredentialsDecodeResult.InvalidBase64 ? InvalidBase64CredentialsResult : InvalidCredentialsEncodingResult;
        }

        var separatorIndex = credentials.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0)
            return InvalidCredentialsFormatResult;

        var username = credentials[..separatorIndex];
        var password = credentials[(separatorIndex + 1)..];
        var principal = await Options.ValidateCredentials.Invoke(Context, username, password).ConfigureAwait(false);
        if (principal is null)
            return InvalidUsernameOrPasswordResult;

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

    private static CredentialsDecodeResult DecodeCredentials(string encodedCredentials, out string credentials)
    {
        byte[]? rentedBuffer = null;

        try
        {
            var maxDecodedLength = GetMaximumDecodedLength(encodedCredentials.Length);
            var credentialBytes = maxDecodedLength <= StackallocThreshold ? stackalloc byte[maxDecodedLength] : (rentedBuffer = ArrayPool<byte>.Shared.Rent(maxDecodedLength));

            if (!Convert.TryFromBase64String(encodedCredentials, credentialBytes, out var bytesWritten))
            {
                credentials = "";
                return CredentialsDecodeResult.InvalidBase64;
            }

            return TranscodeUtf8(credentialBytes[..bytesWritten], out credentials);
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    private static CredentialsDecodeResult TranscodeUtf8(ReadOnlySpan<byte> credentialBytes, out string credentials)
    {
        char[]? rentedBuffer = null;

        try
        {
            // A UTF-8 sequence never produces more UTF-16 code units than it has bytes.
            var credentialChars = credentialBytes.Length <= StackallocThreshold ? stackalloc char[credentialBytes.Length] : (rentedBuffer = ArrayPool<char>.Shared.Rent(credentialBytes.Length));

            // replaceInvalidSequences: false keeps malformed input from silently collapsing onto U+FFFD,
            // which would make unrelated byte sequences decode to the same credentials.
            if (Utf8.ToUtf16(credentialBytes, credentialChars, out _, out var charsWritten, replaceInvalidSequences: false) is not OperationStatus.Done)
            {
                credentials = "";
                return CredentialsDecodeResult.InvalidEncoding;
            }

            credentials = new string(credentialChars[..charsWritten]);
            return CredentialsDecodeResult.Success;
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(rentedBuffer);
            }
        }
    }

    private static int GetMaximumDecodedLength(int encodedLength)
    {
        return (int)((encodedLength + 3L) / 4L * 3L);
    }

    private enum CredentialsDecodeResult
    {
        Success,
        InvalidBase64,
        InvalidEncoding,
    }
}
