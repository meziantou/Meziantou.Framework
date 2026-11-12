using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The readers and writers of Web Authentication: the base64url decoding of the JSON types and of the signal methods, the
// JSON-compatible serialization of the client data, the asymmetric key packages of the virtual authenticators (the
// credentials added by the host or by test_driver.add_credential()), and the authenticator data they sign
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The tokens of the webauthn target: the base64url alphabet, the padding, and the characters that the client data escapes.</summary>
    private static string[] WebAuthnTokens =>
    [
        "-", "_", "+", "/", "=", "==", "\"", "\\", "\u2028", "\0",
    ];

    /// <summary>
    /// Decodes the input as a base64url string and checks the round trips with the encoding (and the agreement with the
    /// base64url decoder of the BCL on the strict encodings), serializes the client data of the input and checks that it is
    /// the JSON of its fields, and adds the input as the private key of a credential of a virtual authenticator: an invalid
    /// key is rejected with an <see cref="ArgumentException"/>, a valid key signs authenticator data that its public key
    /// verifies.
    /// </summary>
    public static void WebAuthentication(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);

        // Base64url: the decoding of the encoding is the input
        var encoded = WebAuthenticationEncoding.Base64UrlEncode(data);
        FuzzAssert.True(WebAuthenticationEncoding.Base64UrlDecode(encoded) is { } decoded && decoded.AsSpan().SequenceEqual(data), $"The base64url encoding {FuzzAssert.Format(encoded)} does not decode to its input");
        if (WebAuthenticationEncoding.Base64UrlDecode(text) is { } bytes)
        {
            FuzzAssert.True(text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && text.Length % 4 != 1, $"The invalid base64url string {FuzzAssert.Format(text)} is decoded");
            var reencoded = WebAuthenticationEncoding.Base64UrlEncode(bytes);
            FuzzAssert.True(WebAuthenticationEncoding.Base64UrlDecode(reencoded) is { } again && again.AsSpan().SequenceEqual(bytes), $"The base64url string {FuzzAssert.Format(text)} does not round trip");
            if (reencoded == text)
            {
                FuzzAssert.True(System.Buffers.Text.Base64Url.DecodeFromChars(text).AsSpan().SequenceEqual(bytes), $"The base64url string {FuzzAssert.Format(text)} is decoded differently by the BCL");
            }
        }
        else
        {
            FuzzAssert.False(text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && text.Length % 4 != 1, $"The valid base64url string {FuzzAssert.Format(text)} is not decoded");
        }

        FuzzAssert.True(WebAuthenticationEncoding.Base64UrlDecode(text, allowPadding: true) is not null || WebAuthenticationEncoding.Base64UrlDecode(text) is null, $"The base64url string {FuzzAssert.Format(text)} is only decoded without the padding");

        // The client data is the JSON object of its fields, whatever the strings
        var clientData = WebAuthenticationEncoding.SerializeClientData(text, data, text, crossOrigin: data.Length % 2 == 0, data.Length % 3 == 0 ? text : null);
        using (var document = JsonDocument.Parse(clientData))
        {
            var root = document.RootElement;
            FuzzAssert.Equal(encoded, root.GetProperty("challenge").GetString(), "The challenge of the client data");
            FuzzAssert.Equal(data.Length % 2 == 0, root.GetProperty("crossOrigin").GetBoolean(), "The crossOrigin member of the client data");
            FuzzAssert.Equal(data.Length % 3 == 0, root.TryGetProperty("topOrigin", out _), "The topOrigin member of the client data");
            FuzzAssert.True(Encoding.UTF8.GetString(clientData).StartsWith("{\"type\":", StringComparison.Ordinal), "The client data does not start with its type");
            if (!text.Any(char.IsSurrogate))
            {
                FuzzAssert.Equal(text, root.GetProperty("type").GetString(), "The type of the client data");
                FuzzAssert.Equal(text, root.GetProperty("origin").GetString(), "The origin of the client data");
            }
        }

        // The asymmetric key package of a credential
        var authenticator = new VirtualAuthenticatorEnvironment().AddAuthenticator(new VirtualAuthenticatorOptions());
        try
        {
            authenticator.AddCredential(new VirtualCredential { CredentialId = new byte[] { 1 }, RpId = "example.com", PrivateKey = data.ToArray(), SignCount = 0 });
        }
        catch (ArgumentException)
        {
            return;
        }

        var stored = authenticator.Lookup([1], "example.com");
        FuzzAssert.True(stored is not null, "The credential added to the authenticator is not found");
        var authenticatorData = WebAuthenticationEncoding.EncodeAuthenticatorData(SHA256.HashData("example.com"u8), AuthenticatorDataFlags.UserPresent, 1, [], []);
        var signature = stored.Key.Sign(authenticatorData);
        var spki = stored.Key.ExportSubjectPublicKeyInfo();
        var verified = stored.Key.Algorithm switch
        {
            CredentialKey.ES256 => VerifyEcdsa(spki, authenticatorData, signature),
            CredentialKey.RS256 => VerifyRsa(spki, authenticatorData, signature),
            _ => Cryptography.OkpKeys.Verify("Ed25519", Cryptography.OkpKeys.ParseSubjectPublicKeyInfo("Ed25519", spki), authenticatorData, signature, []),
        };
        FuzzAssert.True(verified, $"The signature of the key {FuzzAssert.Format(Convert.ToHexString(data))} is not verified by its public key");

        // The key package of the credential is imported again
        var credentials = authenticator.GetCredentials();
        FuzzAssert.Equal(1, credentials.Count, "The number of credentials of the authenticator");
        authenticator.AddCredential(new VirtualCredential { CredentialId = new byte[] { 2 }, RpId = "example.com", PrivateKey = credentials[0].PrivateKey, SignCount = null });
    }

    private static bool VerifyEcdsa(byte[] spki, byte[] data, byte[] signature)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(spki, out _);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    private static bool VerifyRsa(byte[] spki, byte[] data, byte[] signature)
    {
        using var key = RSA.Create();
        key.ImportSubjectPublicKeyInfo(spki, out _);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static IEnumerable<FuzzSeed> GetWebAuthnSeeds()
    {
        // The base64url strings of the unit tests and of the WPT tests (webauthn/getcredential-prf.https.html and
        // webauthn/public-key-credential-*-from-json.https.window.js), and the key packages of each algorithm
        string[] texts = ["", "dGVzdA", "Zm9v", "Zm9v=", "_w", "not valid base64url", "BwgJ", "AA", "A", "\"\\\u0001\u2028"];
        foreach (var (text, index) in texts.Select((text, index) => (text, index)))
        {
            yield return FuzzSeed.FromText($"webauthn-text-{index}", text);
        }

        yield return new FuzzSeed("webauthn-eddsa", Cryptography.OkpKeys.EncodePrivateKeyInfo("Ed25519", new byte[32]));

        // The ES256 private key of webauthn/getcredential-timeout.https.html
        yield return new FuzzSeed("webauthn-es256",WebAuthenticationEncoding.Base64UrlDecode("MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg8_zMDQDYAxlU-Qhk1Dwkf0v18GZca1DMF3SaJ9HPdmShRANCAASNYX5lyVCOZLzFZzrIKmeZ2jwURmgsJYxGP__fWN_S-j5sN4tT15XEpN_7QZnt14YvI6uvAgO0uJEboFaZlOEB"));
    }
}
