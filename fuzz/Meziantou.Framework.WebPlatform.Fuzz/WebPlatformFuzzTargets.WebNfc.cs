using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The creation of the NDEF messages and records of Web NFC (https://w3c-cg.github.io/web-nfc/#create-web-nfc-message): the
// record types, the external and local types, the payloads and the nested messages
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The tokens of the NDEF message target: the JSON syntax, the members of the dictionaries and the record types.</summary>
    private static string[] NdefMessageTokens =>
    [
        "{", "}", "[", "]", ":", ",", "\"", "null", "1", "255",
        "\"records\"", "\"recordType\"", "\"mediaType\"", "\"id\"", "\"encoding\"", "\"lang\"", "\"data\"",
        "\"empty\"", "\"text\"", "\"url\"", "\"absolute-url\"", "\"mime\"", "\"unknown\"", "\"smart-poster\"",
        "\":t\"", "\":s\"", "\":act\"", "\":local\"", "\"example.com:type\"", "\"xn--hndvrker-9zan.dk:abc\"", "\"håndværker.dk:abc\"",
        "\"utf-8\"", "\"utf-16\"", "\"utf-16le\"", "\"utf-16be\"", "\"en\"", "\"application/json\"", "\"text/plain;charset=utf-8\"",
        "\"https://example.com/\"", "\"mailto:a@b\"",
    ];

    /// <summary>
    /// Creates an NDEF message from a JSON description of an NDEFMessageInit (a string, an array of bytes or a nested message
    /// as the data of a record), and checks the properties of the result: the creation fails only with the documented
    /// exceptions, the message has a record for each record of the description, with its record type, a smart poster starts
    /// with its url record, the records that can contain records return them or null, the other ones throw a
    /// <c>NotSupportedError</c>, and the attributes of the text and mime records are set.
    /// </summary>
    public static void NdefMessageTarget(ReadOnlySpan<byte> data)
    {
        // A JSON node decodes its strings when they are read: invalid UTF-8 would throw then, in the description of the message
        if (!System.Text.Unicode.Utf8.IsValid(data))
            return;

        JsonNode? json;
        try
        {
            json = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { MaxDepth = 256 });
        }
        catch (JsonException)
        {
            return;
        }

        if (ToNdefMessageInit(json) is not { } messageInit)
            return;

        NdefMessage message;
        try
        {
            message = new NdefMessage(messageInit);
        }
        catch (TypeErrorException)
        {
            return;
        }
        catch (DomException exception) when (exception.Name == "SyntaxError")
        {
            return;
        }

        FuzzAssert.Equal(messageInit.Records.Count, message.Records.Count, "The message does not have a record for each record of its description");
        for (var i = 0; i < message.Records.Count; i++)
        {
            FuzzAssert.Equal(messageInit.Records[i].RecordType, message.Records[i].RecordType, "The record type differs from the one of the description");
            CheckNdefRecord(message.Records[i], depth: 1);
        }
    }

    private static void CheckNdefRecord(NdefRecord record, int depth)
    {
        FuzzAssert.True(depth <= 32, "The NDEF messages are nested more than 32 times");
        switch (record.RecordType)
        {
            case "text":
                FuzzAssert.True(record.Encoding is "utf-8" or "utf-16" or "utf-16le" or "utf-16be", $"The encoding '{record.Encoding}' of a text record is invalid");
                FuzzAssert.True(record.Lang is { Length: <= 63 }, "The language of a text record is missing or too long");
                FuzzAssert.True(record.Data is not null, "A text record has no payload");
                break;

            case "mime":
                FuzzAssert.True(record.MediaType is not null, "A mime record has no media type");
                break;

            default:
                FuzzAssert.True(record.MediaType is null, "A record that is not a mime record has a media type");
                break;
        }

        IReadOnlyList<NdefRecord>? records;
        try
        {
            records = record.ToRecords();
        }
        catch (DomException exception) when (exception.Name == "NotSupportedError")
        {
            FuzzAssert.False(record.RecordType == "smart-poster" || record.RecordType.Contains(':', StringComparison.Ordinal), $"The records of a '{record.RecordType}' record cannot be read");
            return;
        }

        if (records is null)
        {
            FuzzAssert.True(record.Data is not null, "A record has neither a payload nor records");
            return;
        }

        FuzzAssert.True(record.Data is null, "A record has both a payload and records");
        FuzzAssert.True(records.Count > 0, "A record contains an empty message");
        if (record.RecordType == "smart-poster")
        {
            FuzzAssert.Equal("url", records[0].RecordType, "The first record of a smart poster is not its url record");
        }

        foreach (var child in records)
        {
            CheckNdefRecord(child, depth + 1);
        }
    }

    // The description of an NDEFMessageInit: an object whose records are objects with the members of NDEFRecordInit, whose
    // data is a string, an array of bytes or another message. Returns null for another JSON value.
    private static NdefMessageInit? ToNdefMessageInit(JsonNode? json)
    {
        if (json is not JsonObject message || message["records"] is not JsonArray records)
            return null;

        var result = new List<NdefRecordInit>(records.Count);
        foreach (var record in records)
        {
            if (record is not JsonObject recordObject || GetString(recordObject, "recordType") is not { } recordType)
                return null;

            result.Add(new NdefRecordInit
            {
                RecordType = recordType,
                MediaType = GetString(recordObject, "mediaType"),
                Id = GetString(recordObject, "id"),
                Encoding = GetString(recordObject, "encoding"),
                Lang = GetString(recordObject, "lang"),
                Data = recordObject["data"] switch
                {
                    JsonValue value when value.TryGetValue<string>(out var text) => text,
                    JsonArray bytes => bytes.Select(item => item is JsonValue value && value.TryGetValue<int>(out var number) ? (byte)number : (byte)0).ToArray(),
                    JsonObject nested => ToNdefMessageInit(nested) ?? (object)"",
                    _ => null,
                },
            });
        }

        return new NdefMessageInit { Records = result };

        static string? GetString(JsonObject value, string name) => value[name] is JsonValue member && member.TryGetValue<string>(out var text) ? text : null;
    }

    private static IEnumerable<FuzzSeed> GetNdefMessageSeeds()
    {
        string[] seeds =
        [
            """{"records":[{"recordType":"text","data":"Test text data.","id":"/test_path/test_id"}]}""",
            """{"records":[{"recordType":"text","data":[84,101,115,116],"encoding":"utf-16be","lang":"fr"}]}""",
            """{"records":[{"recordType":"empty"}]}""",
            """{"records":[{"recordType":"url","data":"https://w3c.github.io/web-nfc/"},{"recordType":"absolute-url","data":"https://w3c.github.io/web-nfc/"}]}""",
            """{"records":[{"recordType":"mime","mediaType":"application/json","data":[123,125]},{"recordType":"unknown","data":[1,2,3,4]}]}""",
            """{"records":[{"recordType":"foo.eXamPle.com:bAr*-","data":[1,2,3,4]}]}""",
            """{"records":[{"recordType":"example.com:foo","data":{"records":[{"recordType":":xyz","data":{"records":[{"recordType":"text","data":"a"}]}}]}}]}""",
            """{"records":[{"recordType":"smart-poster","id":"dummy_record_id","data":{"records":[{"recordType":"url","data":"https://w3c.github.io/web-nfc/"},{"recordType":"text","data":"Title","lang":"en"},{"recordType":":t","data":[105,109,97,103,101]},{"recordType":":s","data":[0,16,0,0]},{"recordType":":act","data":[3]},{"recordType":"mime","mediaType":"image/gif","data":[1,2,3,4]}]}}]}""",
            """{"records":[{"recordType":"smart-poster","data":{"records":[{"recordType":"text","data":"No URL"}]}}]}""",
            """{"records":[{"recordType":"håndværker.dk:abc","data":[1]}]}""",
            """{"records":[{"recordType":"example.com:hellö","data":[1]}]}""",
            """{"records":[{"recordType":":xyz","data":[1]}]}""",
            """{"records":[{"recordType":"TeXt","data":"a"}]}""",
            """{"records":[]}""",
        ];

        for (var i = 0; i < seeds.Length; i++)
        {
            yield return FuzzSeed.FromText("ndef-message-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
