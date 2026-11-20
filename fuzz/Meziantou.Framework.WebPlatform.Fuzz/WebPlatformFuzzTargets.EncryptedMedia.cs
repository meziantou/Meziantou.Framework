using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// Encrypted Media Extensions: the Clear Key formats that a page or the media data provides (the Initialization Data of
// generateRequest() and of the encrypted event, the responses of update()) and the decryption of the samples of the Common
// Encryption schemes (cenc, cbcs) with the encryption that the media backend reads from the media data
public static partial class WebPlatformFuzzTargets
{
    private static readonly string[] ClearKeyInitDataTypes = ["keyids", "cenc", "webm"];

    /// <summary>The tokens of the Clear Key targets: the members of the JSON formats, and the type of a pssh box.</summary>
    private static string[] ClearKeyTokens =>
    [
        "\"kids\"", "\"keys\"", "\"kty\"", "\"oct\"", "\"kid\"", "\"k\"", "\"type\"", "\"temporary\"", "\"persistent-license\"", "\"alg\"", "\"A128KW\"",
        "{", "}", "[", "]", ":", ",", "\"AQEBAQEBAQEBAQEBAQEBAQ\"", "pssh",
    ];

    /// <summary>
    /// Extracts the key IDs of an Initialization Data (the first byte selects the type: <c>keyids</c>, <c>cenc</c> or
    /// <c>webm</c>): the key IDs have 1 to 512 bytes, are not duplicated and are bounded in number, the result does not depend
    /// on the run, and the license request generated from them is a <c>keyids</c> Initialization Data with the same key IDs.
    /// </summary>
    public static void ClearKeyInitData(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var initDataType = ClearKeyInitDataTypes[data[0] % ClearKeyInitDataTypes.Length];
        var initData = data[1..];
        var keyIds = ClearKey.ExtractKeyIds(initDataType, initData);
        var again = ClearKey.ExtractKeyIds(initDataType, initData);
        FuzzAssert.Equal(keyIds is null, again is null, "ExtractKeyIds is not deterministic");
        if (keyIds is null)
            return;

        FuzzAssert.True(initData.Length <= ClearKey.MaxInitDataLength, "An Initialization Data longer than the limit is accepted");
        FuzzAssert.True(keyIds.Count <= ClearKey.MaxKeyCount, "Too many key IDs");
        for (var i = 0; i < keyIds.Count; i++)
        {
            FuzzAssert.True(keyIds[i].Length is >= ClearKey.MinKeyIdLength and <= ClearKey.MaxKeyIdLength, "A key ID has an invalid length");
            FuzzAssert.True(keyIds[i].AsSpan().SequenceEqual(again![i]), "ExtractKeyIds is not deterministic");
            for (var j = 0; j < i; j++)
            {
                FuzzAssert.False(keyIds[i].AsSpan().SequenceEqual(keyIds[j]), "A key ID is duplicated");
            }
        }

        if (keyIds.Count == 0)
            return;

        // The license request lists the key IDs as a keyids Initialization Data does
        var request = ClearKey.CreateLicenseRequest(keyIds, MediaKeySessionType.Temporary);
        var parsed = ClearKey.ExtractKeyIds("keyids", request);
        FuzzAssert.True(parsed is not null && parsed.Count == keyIds.Count && parsed.Zip(keyIds).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)), "The license request does not round-trip");
    }

    /// <summary>
    /// Parses a response of <c>update()</c>: the keys of a license have 16 bytes and a key ID of 1 to 512 bytes, without
    /// duplicated key ID, and a license (or a license release acknowledgement) serialized again is parsed to the same keys.
    /// </summary>
    public static void ClearKeyResponse(ReadOnlySpan<byte> data)
    {
        var response = ClearKey.ParseResponse(data);
        if (response is null)
            return;

        FuzzAssert.True(data.Length <= ClearKey.MaxResponseLength, "A response longer than the limit is accepted");
        if (response.Keys is { } keys)
        {
            FuzzAssert.True(keys.Count is > 0 and <= ClearKey.MaxKeyCount, "A license has no key or too many keys");
            var json = new StringBuilder("{\"keys\":[");
            for (var i = 0; i < keys.Count; i++)
            {
                FuzzAssert.Equal(ClearKey.KeyLength, keys[i].Key.Length, "A key has an invalid length");
                FuzzAssert.True(keys[i].KeyId.Length is >= ClearKey.MinKeyIdLength and <= ClearKey.MaxKeyIdLength, "A key ID has an invalid length");
                for (var j = 0; j < i; j++)
                {
                    FuzzAssert.False(keys[i].KeyId.AsSpan().SequenceEqual(keys[j].KeyId), "A key ID is duplicated");
                }

                json.Append(i == 0 ? "" : ",").Append("{\"kty\":\"oct\",\"kid\":\"").Append(Base64Url.EncodeToString(keys[i].KeyId)).Append("\",\"k\":\"").Append(Base64Url.EncodeToString(keys[i].Key)).Append("\"}");
            }

            json.Append("],\"type\":\"").Append(response.SessionType == MediaKeySessionType.PersistentLicense ? "persistent-license" : "temporary").Append("\"}");
            var parsed = ClearKey.ParseResponse(Encoding.UTF8.GetBytes(json.ToString()));
            FuzzAssert.True(parsed?.Keys is { } parsedKeys && parsed.SessionType == response.SessionType && parsedKeys.Count == keys.Count && parsedKeys.Zip(keys).All(pair => pair.First.KeyId.AsSpan().SequenceEqual(pair.Second.KeyId) && pair.First.Key.AsSpan().SequenceEqual(pair.Second.Key)), "The license does not round-trip");
        }
        else
        {
            var released = response.ReleasedKeyIds;
            FuzzAssert.True(released is not null, "A response is neither a license nor a release acknowledgement");
            var parsed = ClearKey.ParseResponse(ClearKey.CreateLicenseRelease(released));
            FuzzAssert.True(parsed?.ReleasedKeyIds is { } parsedKeyIds && parsedKeyIds.Count == released.Count && parsedKeyIds.Zip(released).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)), "The license release does not round-trip");
        }
    }

    /// <summary>
    /// Parses the record of a persistent session in the storage (not trusted): the record of a session is at most
    /// <see cref="ClearKey.MaxStoredSessionLength"/> bytes, and the keys or the released key IDs it has, stored again, are read
    /// back the same (load() reads what update() saves, also beyond the limit of a response).
    /// </summary>
    public static void ClearKeyStoredSession(ReadOnlySpan<byte> data)
    {
        if (ClearKey.ParseStoredSession(data) is not { } session)
            return;

        FuzzAssert.True(data.Length <= ClearKey.MaxStoredSessionLength, "A record longer than the limit is accepted");
        var record = ClearKey.SerializeStoredSession(session.Keys, session.Keys is null ? session.ReleasedKeyIds : null);
        FuzzAssert.True(record.Length <= ClearKey.MaxStoredSessionLength, "A stored record cannot be read back");
        var parsed = ClearKey.ParseStoredSession(record);
        if (session.Keys is { } keys)
        {
            FuzzAssert.True(parsed?.Keys is { } parsedKeys && parsedKeys.Count == keys.Count && parsedKeys.Zip(keys).All(pair => pair.First.KeyId.AsSpan().SequenceEqual(pair.Second.KeyId) && pair.First.Key.AsSpan().SequenceEqual(pair.Second.Key)), "The keys of a stored session do not round-trip");
        }
        else
        {
            FuzzAssert.True(parsed?.ReleasedKeyIds is { } parsedKeyIds && session.ReleasedKeyIds is { } released && parsedKeyIds.Count == released.Count, "The record of license destruction does not round-trip");
        }
    }

    private static IEnumerable<FuzzSeed> GetClearKeyStoredSessionSeeds()
    {
        // The records that update() and remove() save: a session of 1024 keys whose IDs have 512 bytes (larger than a response), a small one, and a record of license destruction
        var largest = Enumerable.Range(0, ClearKey.MaxKeyCount).Select(i =>
        {
            var keyId = new byte[ClearKey.MaxKeyIdLength];
            keyId[0] = (byte)i;
            keyId[1] = (byte)(i >> 8);
            return new ClearKeyKey(keyId, new byte[ClearKey.KeyLength]);
        }).ToList();
        yield return new FuzzSeed("clearkey-stored-session-largest", ClearKey.SerializeStoredSession(largest, releasedKeyIds: null));
        yield return new FuzzSeed("clearkey-stored-session-small", ClearKey.SerializeStoredSession([new ClearKeyKey(new byte[16], new byte[16])], releasedKeyIds: null));
        yield return new FuzzSeed("clearkey-stored-session-released", ClearKey.SerializeStoredSession(keys: null, [new byte[16], [1, 2, 3]]));
    }

    /// <summary>
    /// Decrypts a sample. The input is the scheme and the pattern (1 byte), the IV size (1 byte), the subsample count (1 byte),
    /// the IV, the subsamples (2 bytes of clear bytes, 2 bytes of protected bytes), then the sample. The decryption fails only
    /// when the subsamples do not cover the sample; the clear bytes are kept; cenc decrypts with a key stream, so decrypting
    /// twice gives the sample back; cbcs keeps the bytes after the last complete block of each protected range.
    /// </summary>
    public static void CencDecrypt(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
            return;

        var scheme = (data[0] & 1) == 0 ? MediaEncryptionScheme.Cenc : MediaEncryptionScheme.Cbcs;
        var crypt = (data[0] >> 1) & 0x7;
        var skip = data[0] >> 4;
        var ivSize = scheme == MediaEncryptionScheme.Cbcs || (data[1] & 1) != 0 ? 16 : 8;
        var subsampleCount = data[2] % 8;
        var position = 3;
        if (data.Length < position + ivSize + (subsampleCount * 4))
            return;

        var iv = data.Slice(position, ivSize).ToArray();
        position += ivSize;
        var subsamples = new List<MediaSubsample>();
        for (var i = 0; i < subsampleCount; i++)
        {
            subsamples.Add(new MediaSubsample(BinaryPrimitives.ReadUInt16BigEndian(data[position..]), BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..])));
            position += 4;
        }

        var sample = data[position..].ToArray();
        var encryption = new MediaSampleEncryption(scheme, new byte[16], iv, subsamples) { CryptByteBlock = crypt, SkipByteBlock = skip };
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var decrypted = new byte[sample.Length];
        var coversSample = subsamples.Count == 0 || subsamples.Sum(subsample => (long)subsample.ClearBytes + subsample.ProtectedBytes) == sample.Length;

        // A cbcs pattern without encrypted blocks (0:N) is invalid
        var validPattern = scheme == MediaEncryptionScheme.Cenc || crypt != 0 || skip == 0;
        bool result;
        try
        {
            result = MediaSampleDecryptor.TryDecrypt(encryption, key, sample, decrypted);
        }
        catch (Exception ex)
        {
            // Malformed media is reported by the result: no exception escapes
            FuzzAssert.True(false, "TryDecrypt threw " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        FuzzAssert.Equal(coversSample && validPattern, result, "The decryption fails if and only if the subsamples do not cover the sample or the pattern is invalid");
        if (!result)
            return;

        // The decryption in place gives the same bytes
        var inPlace = sample.ToArray();
        FuzzAssert.True(MediaSampleDecryptor.TryDecrypt(encryption, key, inPlace, inPlace) && inPlace.AsSpan().SequenceEqual(decrypted), "The decryption in place differs");

        var ranges = subsamples.Count == 0 ? [new MediaSubsample(0, sample.Length)] : subsamples;
        var offset = 0;
        foreach (var range in ranges)
        {
            FuzzAssert.True(decrypted.AsSpan(offset, range.ClearBytes).SequenceEqual(sample.AsSpan(offset, range.ClearBytes)), "A clear byte was modified");
            offset += range.ClearBytes;
            if (scheme == MediaEncryptionScheme.Cbcs)
            {
                var partial = range.ProtectedBytes % 16;
                var end = offset + range.ProtectedBytes;
                FuzzAssert.True(decrypted.AsSpan(end - partial, partial).SequenceEqual(sample.AsSpan(end - partial, partial)), "The partial block of a cbcs range was modified");
            }

            offset += range.ProtectedBytes;
        }

        if (scheme == MediaEncryptionScheme.Cenc)
        {
            var twice = decrypted.ToArray();
            FuzzAssert.True(MediaSampleDecryptor.TryDecrypt(encryption, key, twice, twice) && twice.AsSpan().SequenceEqual(sample), "The cenc key stream is not an involution");
        }
    }

    private static IEnumerable<FuzzSeed> GetClearKeyInitDataSeeds()
    {
        byte[] commonPssh = [0x00, 0x00, 0x00, 0x44, .. "pssh"u8, 0x01, 0x00, 0x00, 0x00, 0x10, 0x77, 0xEF, 0xEC, 0xC0, 0xB2, 0x4D, 0x02, 0xAC, 0xE3, 0x3C, 0x1E, 0x52, 0xE2, 0xFB, 0x4B, 0x00, 0x00, 0x00, 0x02, .. Enumerable.Repeat((byte)1, 16), .. Enumerable.Repeat((byte)2, 16), 0x00, 0x00, 0x00, 0x00];
        byte[] otherPssh = [0x00, 0x00, 0x00, 0x24, .. "pssh"u8, 0x00, 0x00, 0x00, 0x00, .. Enumerable.Repeat((byte)0x55, 16), 0x00, 0x00, 0x00, 0x04, 1, 2, 3, 4];
        byte[][] seeds =
        [
            [0, .. """{"kids":["AQEBAQEBAQEBAQEBAQEBAQ"]}"""u8],
            [0, .. """{"kids":["AQI","AwQ","AQI"],"type":"temporary"}"""u8],
            [0, .. """{"kids":["AQI="]}"""u8],
            [0, .. """{"kids":[]}"""u8],
            [1, .. commonPssh],
            [1, .. otherPssh, .. commonPssh],
            [1, 0x00, 0x00, 0xFF, 0xFF, .. "pssh"u8, 0x00, 0x00, 0x00, 0x00, 0x10, 0x77, 0xEF, 0xEC, 0xC0, 0xB2, 0x4D, 0x02, 0xAC, 0xE3, 0x3C, 0x1E, 0x52, 0xE2, 0xFB, 0x4B, 0x00, 0x00, 0x00, 0x00],
            [2, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("clearkey-initdata-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetClearKeyResponseSeeds()
    {
        string[] seeds =
        [
            """{"keys":[{"kty":"oct","kid":"AQEBAQEBAQEBAQEBAQEBAQ","k":"AgICAgICAgICAgICAgICAg"}]}""",
            """{"keys":[{"kty":"oct","alg":"A128KW","kid":"MDEyMzQ1Njc4OTAxMjM0NQ","k":"MDEyMzQ1Njc4OTAxMjM0NQ"}],"type":"persistent-license"}""",
            """{"keys":[{"kty":"oct","kid":"AQ","k":"AQ"}]}""",
            """{"keys":[{"kty":"RSA","kid":"AQI","k":"AAAAAAAAAAAAAAAAAAAAAA"}]}""",
            """{"kids":["AQEBAQEBAQEBAQEBAQEBAQ","AgI"]}""",
            "\u0000\u0011\"3DUfw",
            """{"keys":[{"kty":"oct","k":"MDEyMzQ1Njc4OTAxMjM0NQ","kid":"MDEyMzQ1Njc4O\u0000TAxMjM0NQ"}]}""",
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("clearkey-response-" + i.ToString(CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(seeds[i]));
        }
    }

    private static IEnumerable<FuzzSeed> GetCencDecryptSeeds()
    {
        // The vectors of the unit tests (NIST SP 800-38A CTR and CBC), with and without subsamples and patterns
        var counter = Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
        var ctr = Convert.FromHexString("874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff");
        var iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        var cbc = Convert.FromHexString("7649abac8119b246cee98e9b12e9197d5086cb9b507219ee95db113a917678b2");
        byte[][] seeds =
        [
            [0, 1, 0, .. counter, .. ctr],
            [0, 1, 2, .. counter, 0, 1, 0, 10, 0, 2, 0, 22, 0xAA, .. ctr[..10], 0xBB, 0xCC, .. ctr[10..]],
            [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 5, 0, 11, .. new byte[16]],
            [0x13, 0, 0, .. iv, .. cbc, 1, 2, 3],
            [0x93, 0, 1, .. iv, 0, 3, 0, 32, 9, 9, 9, .. cbc],
            [0x01, 0, 0, .. iv, .. cbc],
            [0x11, 0, 0, .. iv, .. cbc],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("cenc-decrypt-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
