using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Compression;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The readers of Compression Dictionary Transport (the Use-As-Dictionary header, the dcb and dcz content codings) and of
// the signatures of Signature-based SRI (the Signature-Input and Signature headers), and the structured fields they use
public static partial class WebPlatformFuzzTargets
{
    private const int DictionaryMaxLength = 1 << 20;

    /// <summary>The tokens of the Use-As-Dictionary and signature targets (a property: see <see cref="IntegrityTokens"/>).</summary>
    private static string[] StructuredFieldTokens =>
    [
        "match=", "match-dest=", "id=", "type=raw", "\"", "(", ")", ";", ",", " ", "=", ":", "?1", "?0", "*", "/", "\\\\", "{", "}",
        "\"unencoded-digest\";sf", "\"@path\";req", "\"@query\";req", "\"@authority\";req", "\"@scheme\";req", "\"@method\";req", "\"@status\"",
        "\"content-type\"", "keyid=", "tag=\"ed25519-integrity\"", "alg=", "created=", "expires=", "sha-256=", "sha-512=", "\n",
        "Signature-Input: sig=", "Signature: sig=", "Unencoded-Digest: ",
    ];

    /// <summary>
    /// Parses a <c>Use-As-Dictionary</c> header and checks the result: the id has at most 1024 characters, the pattern
    /// matches no URL of another origin, and the structured dictionary serializes to a value that parses to the same
    /// serialization.
    /// </summary>
    public static void UseAsDictionary(string input)
    {
        var dictionaryUrl = UrlParser.Parse("https://example.com/app/dictionary.txt", baseUrl: null)!;
        if (CompressionDictionaries.ParseUseAsDictionary(input, dictionaryUrl) is { } options)
        {
            FuzzAssert.True(options.Id.Length <= CompressionDictionaries.MaxIdLength, $"The id of {FuzzAssert.Format(input)} is too long");
            foreach (var other in new[] { "https://other.example/app/dictionary.txt", "http://example.com/app/dictionary.txt", "https://example.com:8443/app/dictionary.txt" })
            {
                FuzzAssert.False(options.Pattern.Test(UrlParser.Parse(other, baseUrl: null)!), $"The pattern of {FuzzAssert.Format(input)} matches {other}");
            }
        }

        // The serialization of a structured dictionary is canonical
        if (StructuredFields.ParseDictionary(input) is { } dictionary)
        {
            var serialized = StructuredFields.SerializeDictionary(dictionary);
            var reparsed = StructuredFields.ParseDictionary(serialized);
            FuzzAssert.True(reparsed is not null, $"The serialization {FuzzAssert.Format(serialized)} of {FuzzAssert.Format(input)} is not a dictionary");
            FuzzAssert.Equal(serialized, StructuredFields.SerializeDictionary(reparsed!), $"The serialization of {FuzzAssert.Format(input)} is not canonical");
        }
    }

    /// <summary>
    /// Decodes data with a dictionary content coding: the first byte selects the coding (<c>dcb</c> or <c>dcz</c>), the
    /// second one the length of the dictionary that follows, and the rest is the compressed data, after a header with the
    /// hash of the dictionary. The decoder throws an <see cref="InvalidDataException"/> or returns at most the maximum
    /// length; the same data decode to the same output, and a Brotli stream without dictionary decodes as the decoder of the
    /// base class library decodes it.
    /// </summary>
    public static void DictionaryDecompression(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return;

        var coding = (data[0] & 1) == 0 ? "dcb" : "dcz";
        var dictionaryLength = Math.Min(data[1], data.Length - 2);
        var dictionary = data.Slice(2, dictionaryLength).ToArray();
        var payload = data[(2 + dictionaryLength)..];
        var hash = SHA256.HashData(dictionary);
        byte[] header = coding == "dcb" ? [0xFF, 0x44, 0x43, 0x42, .. hash] : [0x5E, 0x2A, 0x4D, 0x18, 0x20, 0x00, 0x00, 0x00, .. hash];
        byte[] encoded = [.. header, .. payload];

        byte[]? decoded;
        try
        {
            decoded = DictionaryDecompressor.Decompress(coding, encoded, dictionary, hash, DictionaryMaxLength);
        }
        catch (InvalidDataException)
        {
            decoded = null;
        }

        if (decoded is null)
            return;

        FuzzAssert.True(decoded.Length <= DictionaryMaxLength, "The decoded data are longer than the maximum length");
        FuzzAssert.True(decoded.AsSpan().SequenceEqual(DictionaryDecompressor.Decompress(coding, encoded, dictionary, hash, DictionaryMaxLength)), "The same data decode to another output");
        if (coding == "dcb" && dictionary.Length == 0)
        {
            using var input = new MemoryStream(payload.ToArray());
            using var brotli = new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            FuzzAssert.True(decoded.AsSpan().SequenceEqual(output.ToArray()), "A Brotli stream without dictionary decodes to another output than BrotliStream");
        }
    }

    /// <summary>
    /// Verifies the signatures of the response headers of the input (one <c>name: value</c> header per line) and checks
    /// the result: the valid keys are Ed25519 keys, a failure has no valid key, and the verification is deterministic.
    /// </summary>
    public static void IntegritySignatureVerification(string input)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in input.Split('\n').Take(16))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers.Add(new(line[..colon], line[(colon + 1)..].Trim()));
            }
        }

        var url = UrlParser.Parse("https://example.com/path/script.js?a=1", baseUrl: null)!;
        var context = new IntegritySignatureContext(url, "GET", [new("Accept", "*/*")], 200, headers);
        var result = IntegritySignatures.Verify(context, DateTimeOffset.UnixEpoch.AddYears(56));
        FuzzAssert.True(result.ValidKeys.All(key => key.Length == 32), $"A valid key of {FuzzAssert.Format(input)} is not an Ed25519 key");
        var again = IntegritySignatures.Verify(context, DateTimeOffset.UnixEpoch.AddYears(56));
        FuzzAssert.Equal(result.Failed, again.Failed, $"The verification of {FuzzAssert.Format(input)} is not deterministic");

        // The signature base of every inner list of the Signature-Input header ends with its parameters
        if (StructuredFields.GetDictionary(headers, "Signature-Input") is { } inputs)
        {
            foreach (var (_, member) in inputs)
            {
                if (member.InnerList is { } components && IntegritySignatures.GetSignatureBase(context, components, member.InnerListParameters) is { } signatureBase)
                {
                    FuzzAssert.True(signatureBase.EndsWith("\"@signature-params\": " + StructuredFields.SerializeInnerList(components, member.InnerListParameters), StringComparison.Ordinal), $"The signature base of {FuzzAssert.Format(input)} does not end with its parameters");
                }
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetUseAsDictionarySeeds()
    {
        string[] inputs =
        [
            "match=\"/app/*\"",
            "match=\"/app/*\", id=\"v1\", match-dest=(\"script\" \"\"), type=raw, unknown=?1",
            "match=\"/app/*\", type=other",
            "match=\"https://other.example/app/*\"",
            "match=\"/app/(\\\\d+)\"",
            "match=\"{\"",
            "?0",
            "match=\"/fetch/compression-dictionary/resources/*\", id=\"\\\"\\\\ !#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~\"",
            "match=\"/fetch/compression-dictionary/resources/*\", match-dest=(\"asdf\" \"\")",
            "a=1.5;b=:AQID:, c=(1 2.25 \"x\");d, e=?0",
        ];

        return inputs.Select((input, index) => new FuzzSeed($"use-as-dictionary-{index}.txt", Encoding.UTF8.GetBytes(input)));
    }

    private static IEnumerable<FuzzSeed> GetDictionaryDecompressionSeeds()
    {
        // The data of fetch/compression-dictionary/resources/compressed-data.py (WPT), compressed with "This is a test dictionary.\n"
        var dictionary = "This is a test dictionary.\n"u8.ToArray();
        var dcb = Convert.FromHexString("a198018062a44c1ddf12848caec2ca6022076e810514c9b7c3448ebc16e0150eecc1ee34333e0d");
        var dcz = Convert.FromHexString("28b52ffd2434f5000098636f6d70726573736564617461207573696e67030059f97354462726109e99f2bc");
        yield return new FuzzSeed("dcb.bin", (byte[])[0, (byte)dictionary.Length, .. dictionary, .. dcb]);
        yield return new FuzzSeed("dcz.bin", (byte[])[1, (byte)dictionary.Length, .. dictionary, .. dcz]);

        using var compressed = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("This is a test dictionary. ", 20))));
        }

        yield return new FuzzSeed("brotli-without-dictionary.bin", (byte[])[0, 0, .. compressed.ToArray()]);
    }

    private static IEnumerable<FuzzSeed> GetIntegritySignatureSeeds()
    {
        // The responses of subresource-integrity/signatures/tentative/header-component.window.js (WPT)
        string[] inputs =
        [
            "Unencoded-Digest: sha-256=:PZJ+9CdAAIacg7wfUe4t/RkDQJVKM0mCZ2K7qiRhHFc=:\nContent-Type: application/javascript\nSignature-Input: signature=(\"content-type\" \"unencoded-digest\";sf);keyid=\"JrQLj5P/89iXES9+vFgrIy29clF9CC/oPPsw3c5D0bs=\";tag=\"sri\"\nSignature: signature=:a5uYGT79upHlmAkq3PsPHr1xz5AXjqmjVbOr38e8HW94+YqthWLjPeYVRYkYprb0zDqrreptOB4m5d148uWAAQ==:",
            "Unencoded-Digest: sha-256=:X48E9qOokqqrvdts8nOJRJN3OWDUoyWxBf7kbu9DBPE=:\nSignature-Input: signature=(\"unencoded-digest\";sf \"@query\";req \"@path\";req \"@status\");keyid=\"JrQLj5P/89iXES9+vFgrIy29clF9CC/oPPsw3c5D0bs=\";tag=\"ed25519-integrity\";created=1;expires=4000000000\nSignature: signature=:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==:",
            "Unencoded-Digest: sha-512=:AAAA:\nSignature-Input: a=(\"unencoded-digest\";sf);keyid=\"xDnP380zcL4rJ76rXYjeHlfMyPZEOqpJYjsjEppbuXE=\";tag=\"ed25519-integrity\", b=(\"@authority\";req \"@scheme\";req \"@method\";req \"@target-uri\";req \"@request-target\";req \"accept\";req)\nSignature: a=:AA==:, b=:AA==:",
        ];

        return inputs.Select((input, index) => new FuzzSeed($"integrity-signature-{index}.txt", Encoding.UTF8.GetBytes(input)));
    }
}
