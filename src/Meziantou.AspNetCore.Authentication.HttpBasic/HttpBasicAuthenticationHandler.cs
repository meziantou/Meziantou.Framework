using System.Buffers;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    private static readonly AuthenticateResult ControlCharacterInCredentialsResult = AuthenticateResult.Fail("Credentials contain a control character");
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

        var decodeResult = DecodeCredentials(headerValue.Parameter, ArrayPool<byte>.Shared, ArrayPool<char>.Shared, out var credentials);
        if (decodeResult is not CredentialsDecodeResult.Success)
        {
            return decodeResult is CredentialsDecodeResult.InvalidBase64 ? InvalidBase64CredentialsResult : InvalidCredentialsEncodingResult;
        }

        // RFC 7617 forbids control characters (CTL in RFC 5234: U+0000-U+001F and U+007F) in both the user-id and the password.
        // They are valid UTF-8, so strict transcoding lets them through. The separator is not a control character,
        // so checking the whole payload covers both components.
        if (ContainsControlCharacter(credentials))
            return ControlCharacterInCredentialsResult;

        // RFC 7617 defines userid as *<TEXT excluding ":">, so an empty user-id is well-formed.
        // Whether it is acceptable is the credential validator's decision, not the parser's.
        var separatorIndex = credentials.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex < 0)
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
        var challenge = Options.Realm is null
            ? "Basic charset=\"UTF-8\""
            : $"Basic realm=\"{EscapeHeaderValue(Options.Realm)}\", charset=\"UTF-8\"";

        // Append instead of assigning so a challenge already written by another scheme is preserved.
        Response.Headers.Append(HeaderNames.WWWAuthenticate, challenge);

        return Task.CompletedTask;
    }

    private static string EscapeHeaderValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool ContainsControlCharacter(ReadOnlySpan<char> value)
    {
        return value.ContainsAnyInRange('\u0000', '\u001F') || value.Contains('\u007F');
    }

    // The buffers hold the plaintext credentials, so they are zeroed on every path before being released.
    // The whole span is cleared rather than the written prefix because a failed decode does not report
    // how much it wrote. The pools are parameters so tests can observe what is returned to them.
    internal static CredentialsDecodeResult DecodeCredentials(string encodedCredentials, ArrayPool<byte> bytePool, ArrayPool<char> charPool, out string credentials)
    {
        byte[]? rentedBuffer = null;
        var maxDecodedLength = GetMaximumDecodedLength(encodedCredentials.Length);
        var credentialBytes = maxDecodedLength <= StackallocThreshold ? stackalloc byte[maxDecodedLength] : (rentedBuffer = bytePool.Rent(maxDecodedLength)).AsSpan(0, maxDecodedLength);

        try
        {
            if (!Convert.TryFromBase64String(encodedCredentials, credentialBytes, out var bytesWritten))
            {
                credentials = "";
                return CredentialsDecodeResult.InvalidBase64;
            }

            return TranscodeUtf8(credentialBytes[..bytesWritten], charPool, out credentials);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialBytes);
            if (rentedBuffer is not null)
            {
                bytePool.Return(rentedBuffer);
            }
        }
    }

    private static CredentialsDecodeResult TranscodeUtf8(ReadOnlySpan<byte> credentialBytes, ArrayPool<char> charPool, out string credentials)
    {
        char[]? rentedBuffer = null;

        // A UTF-8 sequence never produces more UTF-16 code units than it has bytes.
        var credentialChars = credentialBytes.Length <= StackallocThreshold ? stackalloc char[credentialBytes.Length] : (rentedBuffer = charPool.Rent(credentialBytes.Length)).AsSpan(0, credentialBytes.Length);

        try
        {
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
            CryptographicOperations.ZeroMemory(unsafe(MemoryMarshal.AsBytes(credentialChars)));
            if (rentedBuffer is not null)
            {
                charPool.Return(rentedBuffer);
            }
        }
    }

    private static int GetMaximumDecodedLength(int encodedLength)
    {
        return (int)((encodedLength + 3L) / 4L * 3L);
    }

    internal enum CredentialsDecodeResult
    {
        Success,
        InvalidBase64,
        InvalidEncoding,
    }
}
