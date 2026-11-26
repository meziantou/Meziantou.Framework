using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Meziantou.AspNetCore.Authentication.HttpBasic;

internal sealed class HttpBasicAuthenticationHandler : AuthenticationHandler<HttpBasicAuthenticationOptions>
{
    private const int StackallocThreshold = 256;

    private static readonly SearchValues<char> Base64Characters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=");

    // AuthenticateResult cannot be changed once created, so the results that do not depend on the request are shared, with their task.
    private static readonly Task<AuthenticateResult> NoResult = Task.FromResult(AuthenticateResult.NoResult());
    private static readonly Task<AuthenticateResult> MissingCredentialsResult = FailTask("Missing credentials");
    private static readonly Task<AuthenticateResult> CredentialsTooLongResult = FailTask("Credentials are too long");
    private static readonly Task<AuthenticateResult> InvalidBase64CredentialsResult = FailTask("Invalid Base64 credentials");
    private static readonly Task<AuthenticateResult> InvalidCredentialsEncodingResult = FailTask("Credentials are not valid UTF-8");
    private static readonly Task<AuthenticateResult> InvalidCredentialsFormatResult = FailTask("Invalid credentials format");
    private static readonly Task<AuthenticateResult> ControlCharacterInCredentialsResult = FailTask("Credentials contain a control character");
    private static readonly AuthenticateResult InvalidUsernameOrPassword = AuthenticateResult.Fail("Invalid username or password");
    private static readonly Task<AuthenticateResult> InvalidUsernameOrPasswordResult = Task.FromResult(InvalidUsernameOrPassword);

    public HttpBasicAuthenticationHandler(
        IOptionsMonitor<HttpBasicAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadCredentials(out var username, out var password, out var failure))
            return failure;

        ValueTask<ClaimsPrincipal?> validation;
        try
        {
            validation = Options.ValidateCredentials.Invoke(Context, username, password);
        }
        catch (Exception ex)
        {
            // Report the exception through the task, as an async method would.
            return Task.FromException<AuthenticateResult>(ex);
        }

        // Validators often complete synchronously: skip the async state machine and its task for the failed result.
        if (validation.IsCompletedSuccessfully)
            return validation.Result is { } principal ? Task.FromResult(CreateSuccessResult(principal)) : InvalidUsernameOrPasswordResult;

        return AwaitValidationAsync(validation);
    }

    private async Task<AuthenticateResult> AwaitValidationAsync(ValueTask<ClaimsPrincipal?> validation)
    {
        var principal = await validation.ConfigureAwait(false);
        if (principal is null)
            return InvalidUsernameOrPassword;

        return CreateSuccessResult(principal);
    }

    private AuthenticateResult CreateSuccessResult(ClaimsPrincipal principal)
    {
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    // Returns false, with the result to return, when the header does not hold well-formed credentials.
    private bool TryReadCredentials(out string username, out string password, [NotNullWhen(false)] out Task<AuthenticateResult>? failure)
    {
        username = "";
        password = "";
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationHeaderValues))
        {
            failure = NoResult;
            return false;
        }

        switch (ParseAuthorizationHeader(authorizationHeaderValues, out var encodedCredentials))
        {
            case AuthorizationHeaderParseResult.NotBasic:
                failure = NoResult;
                return false;

            case AuthorizationHeaderParseResult.MissingCredentials:
                failure = MissingCredentialsResult;
                return false;
        }

        if (encodedCredentials.Length > Options.MaxCredentialLength)
        {
            failure = CredentialsTooLongResult;
            return false;
        }

        failure = DecodeCredentials(encodedCredentials, ArrayPool<byte>.Shared, ArrayPool<char>.Shared, out username, out password) switch
        {
            CredentialsDecodeResult.Success => null,
            CredentialsDecodeResult.InvalidBase64 => InvalidBase64CredentialsResult,
            CredentialsDecodeResult.InvalidEncoding => InvalidCredentialsEncodingResult,
            CredentialsDecodeResult.ControlCharacter => ControlCharacterInCredentialsResult,
            _ => InvalidCredentialsFormatResult,
        };

        return failure is null;
    }

    // Gives the result of AuthenticationHeaderValue.TryParse followed by the scheme and parameter checks,
    // without parsing the common headers with it: that allocates the parsed value and the parameter.
    internal static AuthorizationHeaderParseResult ParseAuthorizationHeader(StringValues authorizationHeaderValues, out ReadOnlySpan<char> encodedCredentials)
    {
        encodedCredentials = default;
        if (authorizationHeaderValues.Count is 1)
        {
            var value = authorizationHeaderValues[0];
            if (value is { Length: > 0 } && char.IsAsciiLetter(value[0]))
            {
                // The header starts with a token, so it is the scheme the parser would read. The scheme cannot be "Basic"
                // when its first five characters are not: such a header is ignored whether the parser accepts it or not.
                // Ascii.EqualsIgnoreCase, unlike StringComparison.OrdinalIgnoreCase, never matches a non-ASCII character,
                // which cannot be part of a token.
                if (value.Length < HttpBasicAuthenticationDefaults.AuthenticationScheme.Length || !Ascii.EqualsIgnoreCase(value.AsSpan(0, HttpBasicAuthenticationDefaults.AuthenticationScheme.Length), HttpBasicAuthenticationDefaults.AuthenticationScheme))
                    return AuthorizationHeaderParseResult.NotBasic;

                // "Basic", spaces or tabs, then only Base64 characters: the parser returns the Base64 characters as the parameter.
                var parameter = value.AsSpan(HttpBasicAuthenticationDefaults.AuthenticationScheme.Length);
                var trimmedParameter = parameter.TrimStart(" \t");
                if (trimmedParameter.Length > 0 && trimmedParameter.Length < parameter.Length && !trimmedParameter.ContainsAnyExcept(Base64Characters))
                {
                    encodedCredentials = trimmedParameter;
                    return AuthorizationHeaderParseResult.Basic;
                }
            }
        }

        if (!AuthenticationHeaderValue.TryParse(authorizationHeaderValues, out var headerValue))
            return AuthorizationHeaderParseResult.NotBasic;

        if (!string.Equals(headerValue.Scheme, HttpBasicAuthenticationDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase))
            return AuthorizationHeaderParseResult.NotBasic;

        if (string.IsNullOrWhiteSpace(headerValue.Parameter))
            return AuthorizationHeaderParseResult.MissingCredentials;

        encodedCredentials = headerValue.Parameter;
        return AuthorizationHeaderParseResult.Basic;
    }

    private static Task<AuthenticateResult> FailTask(string failureMessage) => Task.FromResult(AuthenticateResult.Fail(failureMessage));

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
    internal static CredentialsDecodeResult DecodeCredentials(ReadOnlySpan<char> encodedCredentials, ArrayPool<byte> bytePool, ArrayPool<char> charPool, out string username, out string password)
    {
        byte[]? rentedBuffer = null;
        var maxDecodedLength = GetMaximumDecodedLength(encodedCredentials.Length);
        var credentialBytes = maxDecodedLength <= StackallocThreshold ? stackalloc byte[maxDecodedLength] : (rentedBuffer = bytePool.Rent(maxDecodedLength)).AsSpan(0, maxDecodedLength);

        try
        {
            if (!Convert.TryFromBase64Chars(encodedCredentials, credentialBytes, out var bytesWritten))
            {
                username = "";
                password = "";
                return CredentialsDecodeResult.InvalidBase64;
            }

            return TranscodeUtf8(credentialBytes[..bytesWritten], charPool, out username, out password);
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

    private static CredentialsDecodeResult TranscodeUtf8(ReadOnlySpan<byte> credentialBytes, ArrayPool<char> charPool, out string username, out string password)
    {
        char[]? rentedBuffer = null;
        username = "";
        password = "";

        // A UTF-8 sequence never produces more UTF-16 code units than it has bytes.
        var credentialChars = credentialBytes.Length <= StackallocThreshold ? stackalloc char[credentialBytes.Length] : (rentedBuffer = charPool.Rent(credentialBytes.Length)).AsSpan(0, credentialBytes.Length);

        try
        {
            // replaceInvalidSequences: false keeps malformed input from silently collapsing onto U+FFFD,
            // which would make unrelated byte sequences decode to the same credentials.
            if (Utf8.ToUtf16(credentialBytes, credentialChars, out _, out var charsWritten, replaceInvalidSequences: false) is not OperationStatus.Done)
                return CredentialsDecodeResult.InvalidEncoding;

            var credentials = credentialChars[..charsWritten];

            // RFC 7617 forbids control characters (CTL in RFC 5234: U+0000-U+001F and U+007F) in both the user-id and the password.
            // They are valid UTF-8, so strict transcoding lets them through. The separator is not a control character,
            // so checking the whole payload covers both components.
            if (ContainsControlCharacter(credentials))
                return CredentialsDecodeResult.ControlCharacter;

            // RFC 7617 defines userid as *<TEXT excluding ":">, so an empty user-id is well-formed.
            // Whether it is acceptable is the credential validator's decision, not the parser's.
            var separatorIndex = credentials.IndexOf(':');
            if (separatorIndex < 0)
                return CredentialsDecodeResult.InvalidFormat;

            username = new string(credentials[..separatorIndex]);
            password = new string(credentials[(separatorIndex + 1)..]);
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

    internal enum AuthorizationHeaderParseResult
    {
        Basic,
        NotBasic,
        MissingCredentials,
    }

    internal enum CredentialsDecodeResult
    {
        Success,
        InvalidBase64,
        InvalidEncoding,
        ControlCharacter,
        InvalidFormat,
    }
}
