using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The formats of the Clear Key key system (https://w3c.github.io/encrypted-media/#clear-key): the Initialization Data
/// (<c>keyids</c>, <c>cenc</c> and <c>webm</c>, https://www.w3.org/TR/eme-initdata-registry/), the license request, the
/// license (a JSON Web Key Set), the license release and its acknowledgement. Every input comes from the page or the
/// media data: the sizes and the counts are bounded, and an invalid input is rejected as a whole.
/// </summary>
internal static class ClearKey
{
    public const string KeySystem = "org.w3.clearkey";

    /// <summary>The largest Initialization Data, as in Chromium (WPT generate-request-disallowed-input).</summary>
    public const int MaxInitDataLength = 64 * 1024;

    /// <summary>The largest response given to <c>update()</c>, as in Chromium (WPT update-disallowed-input).</summary>
    public const int MaxResponseLength = 64 * 1024;

    /// <summary>The bounds of the length of a key ID (https://www.w3.org/TR/eme-initdata-keyids/: Chromium accepts 1 to 512 bytes).</summary>
    public const int MinKeyIdLength = 1;

    public const int MaxKeyIdLength = 512;

    /// <summary>The length of a key: Clear Key decrypts with AES-128.</summary>
    public const int KeyLength = 16;

    /// <summary>The most key IDs of an Initialization Data, and the most keys of a license.</summary>
    public const int MaxKeyCount = 1024;

    /// <summary>
    /// The largest record of a persistent session in the storage: the keys that several <c>update()</c> calls add (at most
    /// <see cref="MaxKeyCount"/> keys whose IDs have <see cref="MaxKeyIdLength"/> bytes) do not fit in a response.
    /// </summary>
    public const int MaxStoredSessionLength = 1024 * 1024;

    private const int MaxJsonDepth = 8;

    /// <summary>The SystemID of the Common PSSH box (https://www.w3.org/TR/eme-initdata-cenc/#common-system), whose version 1 lists the key IDs.</summary>
    private static ReadOnlySpan<byte> CommonSystemId => [0x10, 0x77, 0xEF, 0xEC, 0xC0, 0xB2, 0x4D, 0x02, 0xAC, 0xE3, 0x3C, 0x1E, 0x52, 0xE2, 0xFB, 0x4B];

    /// <summary>The Initialization Data Types that the Clear Key implementation supports, in the order of preference.</summary>
    public static IReadOnlyList<string> InitDataTypes { get; } = ["cenc", "keyids", "webm"];

    public static bool IsSupportedInitDataType(string initDataType) => initDataType is "cenc" or "keyids" or "webm";

    /// <summary>
    /// Extracts the key IDs of an Initialization Data (the sanitized init data of <c>generateRequest()</c>): null when the
    /// data is not valid for its type (a <c>TypeError</c>), an empty list when it is valid but gives no key ID to Clear Key
    /// (e.g. <c>pssh</c> boxes of other key systems: a <c>NotSupportedError</c>). The key IDs are in the order of the data,
    /// without duplicates.
    /// </summary>
    public static List<byte[]>? ExtractKeyIds(string initDataType, ReadOnlySpan<byte> initData)
    {
        if (initData.IsEmpty || initData.Length > MaxInitDataLength)
            return null;

        return initDataType switch
        {
            "keyids" => ParseKeyIdsInitData(initData),
            "cenc" => ParseCencInitData(initData),
            "webm" => initData.Length <= MaxKeyIdLength ? [initData.ToArray()] : null,
            _ => null,
        };
    }

    /// <summary>https://www.w3.org/TR/eme-initdata-keyids/: a JSON object whose <c>kids</c> member lists the base64url-encoded key IDs.</summary>
    private static List<byte[]>? ParseKeyIdsInitData(ReadOnlySpan<byte> initData)
    {
        if (!TryParseJsonObject(initData, out var document))
            return null;

        using (document)
        {
            if (!document.RootElement.TryGetProperty("kids", out var kids) || ReadKeyIds(kids) is not { Count: > 0 } keyIds)
                return null;

            return keyIds;
        }
    }

    /// <summary>
    /// https://www.w3.org/TR/eme-initdata-cenc/: one or more <c>pssh</c> boxes. Every box must be well formed; the key IDs
    /// are the ones of the version 1 boxes of the Common SystemID.
    /// </summary>
    private static List<byte[]>? ParseCencInitData(ReadOnlySpan<byte> initData)
    {
        var keyIds = new List<byte[]>();
        var offset = 0;
        while (offset < initData.Length)
        {
            var remaining = initData[offset..];
            if (remaining.Length < 8)
                return null;

            long size = BinaryPrimitives.ReadUInt32BigEndian(remaining);
            var headerSize = 8;
            if (size == 1)
            {
                if (remaining.Length < 16)
                    return null;

                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(remaining[8..]), long.MaxValue);
                headerSize = 16;
            }

            if (!remaining.Slice(4, 4).SequenceEqual("pssh"u8) || size < headerSize + 4 + 16 + 4 || size > remaining.Length)
                return null;

            var box = remaining[headerSize..(int)size];
            var version = box[0];
            var systemId = box.Slice(4, 16);
            var position = 20;
            var boxKeyIds = new List<byte[]>();
            if (version > 0)
            {
                if (box.Length < position + 4)
                    return null;

                var count = BinaryPrimitives.ReadUInt32BigEndian(box[position..]);
                position += 4;
                if (count > MaxKeyCount || box.Length - position < (long)count * 16)
                    return null;

                for (var i = 0; i < count; i++)
                {
                    boxKeyIds.Add(box.Slice(position, 16).ToArray());
                    position += 16;
                }
            }

            if (box.Length < position + 4)
                return null;

            var dataSize = BinaryPrimitives.ReadUInt32BigEndian(box[position..]);
            position += 4;

            // The box ends with its data
            if (box.Length - position != dataSize)
                return null;

            if (version == 1 && systemId.SequenceEqual(CommonSystemId))
            {
                foreach (var keyId in boxKeyIds)
                {
                    AddKeyId(keyIds, keyId);
                }
            }

            offset += (int)size;
        }

        return keyIds.Count > MaxKeyCount ? null : keyIds;
    }

    private static void AddKeyId(List<byte[]> keyIds, byte[] keyId)
    {
        if (!keyIds.Exists(existing => existing.AsSpan().SequenceEqual(keyId)))
        {
            keyIds.Add(keyId);
        }
    }

    /// <summary>The license request (https://w3c.github.io/encrypted-media/#clear-key-request-format): <c>{"kids":[...],"type":"..."}</c> in UTF-8.</summary>
    public static byte[] CreateLicenseRequest(IReadOnlyList<byte[]> keyIds, MediaKeySessionType sessionType)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteKeyIds(writer, keyIds);
            writer.WriteString("type", sessionType == MediaKeySessionType.PersistentLicense ? "persistent-license" : "temporary");
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The license release (https://w3c.github.io/encrypted-media/#clear-key-release-format): <c>{"kids":[...]}</c> in UTF-8.</summary>
    public static byte[] CreateLicenseRelease(IReadOnlyList<byte[]> keyIds)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteKeyIds(writer, keyIds);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteKeyIds(Utf8JsonWriter writer, IReadOnlyList<byte[]> keyIds)
    {
        writer.WriteStartArray("kids");
        foreach (var keyId in keyIds)
        {
            writer.WriteStringValue(Base64Url.EncodeToString(keyId));
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Parses a response given to <c>update()</c>: a license (https://w3c.github.io/encrypted-media/#clear-key-license-format,
    /// a JSON Web Key Set with at least one valid symmetric key) or the acknowledgement of a license release
    /// (https://w3c.github.io/encrypted-media/#clear-key-release-ack-format). Returns null when the response is invalid.
    /// </summary>
    public static ClearKeyResponse? ParseResponse(ReadOnlySpan<byte> response) => ParseResponse(response, MaxResponseLength);

    /// <summary>
    /// Serializes the record of a persistent session: its keys (a <c>persistent-license</c> license), or its record of license
    /// destruction (the key IDs of a license release).
    /// </summary>
    public static byte[] SerializeStoredSession(IReadOnlyList<ClearKeyKey>? keys, IReadOnlyList<byte[]>? releasedKeyIds)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "persistent-license");
            if (keys is not null)
            {
                writer.WriteStartArray("keys");
                foreach (var key in keys)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kty", "oct");
                    writer.WriteString("kid", Base64Url.EncodeToString(key.KeyId));
                    writer.WriteString("k", Base64Url.EncodeToString(key.Key));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            if (releasedKeyIds is not null)
            {
                WriteKeyIds(writer, releasedKeyIds);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Parses the record of a persistent session (written by <see cref="SerializeStoredSession"/>; the storage is not trusted):
    /// it has the format of a response, up to <see cref="MaxStoredSessionLength"/> bytes. Returns null when it is invalid.
    /// </summary>
    public static ClearKeyResponse? ParseStoredSession(ReadOnlySpan<byte> record) => ParseResponse(record, MaxStoredSessionLength);

    private static ClearKeyResponse? ParseResponse(ReadOnlySpan<byte> response, int maxLength)
    {
        if (response.IsEmpty || response.Length > maxLength || !TryParseJsonObject(response, out var document))
            return null;

        using (document)
        {
            var root = document.RootElement;
            if (root.TryGetProperty("keys", out var keys))
            {
                var sessionType = MediaKeySessionType.Temporary;
                if (root.TryGetProperty("type", out var type))
                {
                    switch (type.ValueKind == JsonValueKind.String ? type.GetString() : null)
                    {
                        case "temporary":
                            break;
                        case "persistent-license":
                            sessionType = MediaKeySessionType.PersistentLicense;
                            break;
                        default:
                            return null;
                    }
                }

                if (keys.ValueKind != JsonValueKind.Array || keys.GetArrayLength() is 0 or > MaxKeyCount)
                    return null;

                var result = new List<ClearKeyKey>();
                foreach (var jwk in keys.EnumerateArray())
                {
                    if (jwk.ValueKind != JsonValueKind.Object
                        || !jwk.TryGetProperty("kty", out var kty) || kty.ValueKind != JsonValueKind.String || kty.GetString() != "oct"
                        || !jwk.TryGetProperty("kid", out var kid) || DecodeBase64Url(kid, MinKeyIdLength, MaxKeyIdLength) is not { } keyId
                        || !jwk.TryGetProperty("k", out var k) || DecodeBase64Url(k, KeyLength, KeyLength) is not { } key)
                    {
                        return null;
                    }

                    // A key ID given twice: the last key replaces the previous one
                    result.RemoveAll(existing => existing.KeyId.AsSpan().SequenceEqual(keyId));
                    result.Add(new ClearKeyKey(keyId, key));
                }

                return new ClearKeyResponse(result, sessionType, ReleasedKeyIds: null);
            }

            if (root.TryGetProperty("kids", out var kids) && ReadKeyIds(kids) is { } released)
                return new ClearKeyResponse(Keys: null, MediaKeySessionType.Temporary, released);

            return null;
        }
    }

    /// <summary>The key IDs of a <c>kids</c> member (an array of base64url strings), or null when it is invalid.</summary>
    private static List<byte[]>? ReadKeyIds(JsonElement kids)
    {
        if (kids.ValueKind != JsonValueKind.Array || kids.GetArrayLength() > MaxKeyCount)
            return null;

        var keyIds = new List<byte[]>();
        foreach (var kid in kids.EnumerateArray())
        {
            if (DecodeBase64Url(kid, MinKeyIdLength, MaxKeyIdLength) is not { } keyId)
                return null;

            AddKeyId(keyIds, keyId);
        }

        return keyIds;
    }

    /// <summary>Decodes a base64url string without padding (https://www.rfc-editor.org/rfc/rfc7515#appendix-C), or returns null when it is invalid or its length is out of the bounds.</summary>
    private static byte[]? DecodeBase64Url(JsonElement value, int minLength, int maxLength)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length == 0 || text.Length > (maxLength * 4 / 3) + 4)
            return null;

        foreach (var c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return null;
        }

        // A length of 1 modulo 4 cannot be the encoding of bytes
        if (text.Length % 4 == 1)
            return null;

        var bytes = new byte[Base64Url.GetMaxDecodedLength(text.Length)];
        if (Base64Url.DecodeFromChars(text, bytes, out _, out var written) != OperationStatus.Done || written < minLength || written > maxLength)
            return null;

        return bytes[..written];
    }

    private static bool TryParseJsonObject(ReadOnlySpan<byte> json, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            // https://infra.spec.whatwg.org/#parse-json-bytes-to-a-javascript-value: the bytes are UTF-8 decoded (the invalid
            // sequences are replaced, the byte order mark is removed); the JSON reader would only reject them in the strings it reads
            var text = System.Text.Encoding.UTF8.GetString(json);
            document = JsonDocument.Parse(text is [(char)0xFEFF, ..] ? text.AsMemory(1) : text.AsMemory(), new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            document = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The order of the key IDs of a <c>MediaKeyStatusMap</c> (https://w3c.github.io/encrypted-media/#mediakeystatusmap-interface):
    /// the octets are compared lexicographically, and a key ID that is a prefix of another one comes first.
    /// </summary>
    public static int CompareKeyIds(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
}

/// <summary>A key of a Clear Key license.</summary>
internal sealed record ClearKeyKey(byte[] KeyId, byte[] Key);

/// <summary>A response given to <c>update()</c>: the keys of a license and its session type, or the key IDs of a license release acknowledgement.</summary>
internal sealed record ClearKeyResponse(IReadOnlyList<ClearKeyKey>? Keys, MediaKeySessionType SessionType, IReadOnlyList<byte[]>? ReleasedKeyIds);
