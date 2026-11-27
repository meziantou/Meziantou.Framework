using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;
using Microsoft.Extensions.Primitives;
using static Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationHandler;

namespace Meziantou.AspNetCore.Authentication.HttpBasic.Fuzz;

/// <summary>
/// The Authorization header is read like <see cref="AuthenticationHeaderValue.TryParse(string?, out AuthenticationHeaderValue?)"/>
/// followed by the checks of the scheme and of the parameter, and the credentials are decoded like a reference made of
/// <see cref="Convert.FromBase64String(string)"/> and a strict <see cref="UTF8Encoding"/>, with every pooled buffer returned cleared.
/// </summary>
public static partial class HttpBasicFuzzTargets
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly string[] Tokens =
    [
        "Basic", "basic", "BASIC", "Bearer", "Negotiate", " ", "\t", "\r\n", ",", "\"", "=", "==", ":", "+", "/", "\0",
        "dXNlcjpwYXNz", "YWJj", "\u00A0", "\u0131", "\u00FF",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("httpbasic-authorization-header", AuthorizationHeader) { Seeds = GetAuthorizationHeaderSeeds, Dictionary = Tokens },
        new("httpbasic-credentials", Credentials) { Seeds = GetCredentialsSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// The first byte selects whether the text is one header value, or several values separated by NUL characters.
    /// The parsed scheme and credentials are those of the reference parser.
    /// </summary>
    private static void AuthorizationHeader(ReadOnlySpan<byte> data)
    {
        var multipleValues = (FuzzInput.ReadByte(ref data) & 1) is not 0;
        var text = FuzzText.Decode(data);
        var values = multipleValues ? new StringValues(text.Split('\0')) : new StringValues(text);

        var expectedResult = AuthorizationHeaderParseResult.NotBasic;
        var expectedCredentials = "";
        if (AuthenticationHeaderValue.TryParse(values, out var headerValue) && string.Equals(headerValue.Scheme, HttpBasicAuthenticationDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(headerValue.Parameter))
            {
                expectedResult = AuthorizationHeaderParseResult.MissingCredentials;
            }
            else
            {
                expectedResult = AuthorizationHeaderParseResult.Basic;
                expectedCredentials = headerValue.Parameter;
            }
        }

        var result = ParseAuthorizationHeader(values, out var encodedCredentials);
        FuzzAssert.Equal(expectedResult, result, "The parse result differs from AuthenticationHeaderValue");
        FuzzAssert.Equal(expectedCredentials, encodedCredentials.ToString(), "The credentials differ from AuthenticationHeaderValue");
    }

    /// <summary>
    /// The first byte selects whether the text is the Base64 payload, or the remaining bytes are the plaintext credentials
    /// to encode (so that invalid UTF-8 is reachable). The decoded username and password are those of the reference
    /// decoder, and the pooled buffers are returned cleared.
    /// </summary>
    private static void Credentials(ReadOnlySpan<byte> data)
    {
        var encodePlaintext = (FuzzInput.ReadByte(ref data) & 1) is not 0;
        var encodedCredentials = encodePlaintext ? Convert.ToBase64String(data) : FuzzText.Decode(data);

        var (expectedResult, expectedUsername, expectedPassword) = ReferenceDecode(encodedCredentials);

        var bytePool = new ClearedArrayPool<byte>();
        var charPool = new ClearedArrayPool<char>();
        var result = DecodeCredentials(encodedCredentials, bytePool, charPool, out var username, out var password);

        FuzzAssert.Equal(expectedResult, result, "The decode result differs from the reference decoder");
        FuzzAssert.Equal(expectedUsername, username, "The username differs from the reference decoder");
        FuzzAssert.Equal(expectedPassword, password, "The password differs from the reference decoder");
        bytePool.AssertAllReturnedCleared();
        charPool.AssertAllReturnedCleared();
    }

    private static (CredentialsDecodeResult Result, string Username, string Password) ReferenceDecode(string encodedCredentials)
    {
        var bytes = new byte[encodedCredentials.Length];
        if (!Convert.TryFromBase64String(encodedCredentials, bytes, out var bytesWritten))
            return (CredentialsDecodeResult.InvalidBase64, "", "");

        string credentials;
        try
        {
            credentials = StrictUtf8.GetString(bytes, 0, bytesWritten);
        }
        catch (DecoderFallbackException)
        {
            return (CredentialsDecodeResult.InvalidEncoding, "", "");
        }

        foreach (var c in credentials)
        {
            if (c is <= '\u001F' or '\u007F')
                return (CredentialsDecodeResult.ControlCharacter, "", "");
        }

        var separatorIndex = credentials.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex < 0)
            return (CredentialsDecodeResult.InvalidFormat, "", "");

        return (CredentialsDecodeResult.Success, credentials[..separatorIndex], credentials[(separatorIndex + 1)..]);
    }

    private static IEnumerable<FuzzSeed> GetAuthorizationHeaderSeeds()
    {
        var index = 0;
        foreach (var value in GetTestStrings())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index}"), [0], value);
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index}-multiple"), [1], value + "\0Basic YWJj");
            index++;
        }

        yield return FuzzSeed.FromText("basic", [0], "Basic dXNlcjpwYXNz");
        yield return FuzzSeed.FromText("basic-tab", [0], "basic\tdXNlcjpwYXNz==");
        yield return FuzzSeed.FromText("basic-quoted", [0], "Basic \"dXNlcjpwYXNz\"");
        yield return FuzzSeed.FromText("basic-list", [0], "Basic dXNlcjpwYXNz, Bearer token");
        yield return FuzzSeed.FromText("bearer", [0], "Bearer token");
    }

    private static IEnumerable<FuzzSeed> GetCredentialsSeeds()
    {
        var index = 0;
        foreach (var value in GetTestStrings())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index}"), [0], value);
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"test-{index}-plaintext"), (byte[])[1, .. Encoding.UTF8.GetBytes(value)]);
            index++;
        }

        yield return FuzzSeed.FromText("valid", [0], "dXNlcjpwYXNz");
        yield return FuzzSeed.FromText("invalid-base64", [0], "!!!not-base64!!!");
        yield return new FuzzSeed("long", (byte[])[1, .. Encoding.UTF8.GetBytes(new string('u', 300) + ":" + new string('p', 300))]);
        yield return new FuzzSeed("invalid-utf8", (byte[])[1, .. "user:pass"u8, 0xFF]);
        yield return new FuzzSeed("control-character", (byte[])[1, .. "user:pa\tss"u8]);
        yield return new FuzzSeed("no-separator", (byte[])[1, .. "userpass"u8]);
    }

    private static IEnumerable<string> GetTestStrings()
    {
        var source = FuzzResources.ReadText(typeof(HttpBasicFuzzTargets).Assembly, "HttpBasicAuthenticationTests.cs");
        return StringRegex().Matches(source).Select(match => Regex.Unescape(match.Groups["value"].Value)).Distinct(StringComparer.Ordinal);
    }

    [GeneratedRegex("""(?<![$@"])"(?<value>(?:[^"\\\r\n]|\\.)*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringRegex();

    // Hands out new zeroed arrays, so anything left in a returned array was written by the decoder.
    private sealed class ClearedArrayPool<T> : ArrayPool<T>
        where T : IEquatable<T>
    {
        private readonly List<T[]> _rented = [];
        private readonly List<T[]> _returned = [];

        public override T[] Rent(int minimumLength)
        {
            var array = new T[minimumLength];
            _rented.Add(array);
            return array;
        }

        public override void Return(T[] array, bool clearArray = false)
        {
            _returned.Add(array);
        }

        public void AssertAllReturnedCleared()
        {
            FuzzAssert.Equal(_rented.Count, _returned.Count, "A rented buffer is not returned");
            for (var i = 0; i < _rented.Count; i++)
            {
                FuzzAssert.True(ReferenceEquals(_rented[i], _returned[i]), "A buffer is returned out of order");
                FuzzAssert.True(_returned[i].AsSpan().IndexOfAnyExcept(default(T)!) < 0, "A returned buffer is not cleared");
            }
        }
    }
}
