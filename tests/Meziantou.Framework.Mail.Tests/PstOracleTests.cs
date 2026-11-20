using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.Mail.Tests;

/// <summary>Compares the content the library reads with expected.json, the output of pst-extractor (TestData/Pst/Oracle/dump.mjs).</summary>
public sealed class PstOracleTests
{
    private static readonly Lazy<JsonObject> Expected = new(() => (JsonObject)JsonNode.Parse(File.ReadAllText(GetPath("expected.json")))!);

    public static TheoryData<string> Files => [.. Expected.Value.Select(item => item.Key).Order(StringComparer.Ordinal)];

    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", fileName);

    [Theory]
    [MemberData(nameof(Files))]
    public void Content_MatchesOracle(string fileName)
    {
        using var file = PstFile.Open(GetPath(fileName));
        var actual = Dump(file);
        var expected = Expected.Value[fileName]!;
        var differences = new List<string>();
        Compare(expected, actual, "$", differences);
        Assert.Empty(differences);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Validate_NoIssue(string fileName)
    {
        using var file = PstFile.Open(GetPath(fileName));
        Assert.Empty(file.Validate().Select(issue => issue.Message));
    }

    /// <summary>Writes the content of a file in the shape of dump.mjs.</summary>
    internal static JsonObject Dump(PstFile file)
    {
        return new JsonObject
        {
            ["storeDisplayName"] = Text(file.Store.DisplayName),
            ["rootFolder"] = DumpFolder(file.RootFolder),
        };
    }

    private static JsonObject DumpFolder(PstFolder folder)
    {
        var items = new JsonArray();

        // pst-extractor does not list the items of search folders
        if (!folder.IsSearchFolder && folder.ContentCount > 0)
        {
            foreach (var item in folder.EnumerateItems())
            {
                items.Add(DumpItem(item, 0));
            }
        }

        var folders = new JsonArray();
        foreach (var child in folder.Folders)
        {
            folders.Add(DumpFolder(child));
        }

        return new JsonObject
        {
            ["displayName"] = Text(folder.Parent is null ? null : folder.DisplayName),
            ["containerClass"] = Text(folder.ContainerClass),
            ["contentCount"] = folder.ContentCount,
            ["unreadCount"] = folder.UnreadCount,
            ["items"] = items,
            ["folders"] = folders,
        };
    }

    private static JsonObject DumpItem(PstMessage item, int depth)
    {
        var recipients = new JsonArray();
        foreach (var recipient in item.Recipients)
        {
            recipients.Add(new JsonObject
            {
                ["displayName"] = Text(recipient.DisplayName),
                ["emailAddress"] = Text(recipient.EmailAddress),
                ["recipientType"] = (int)recipient.RecipientType,
            });
        }

        var attachments = new JsonArray();
        foreach (var attachment in item.Attachments)
        {
            var entry = new JsonObject
            {
                ["filename"] = Text(attachment.FileName),
                ["longFilename"] = Text(attachment.LongFileName),
                ["mimeTag"] = Text(attachment.MimeType),
                ["attachMethod"] = (int)attachment.Method,
            };

            if (attachment.Method == PstAttachmentMethod.EmbeddedMessage)
            {
                var embedded = attachment.GetEmbeddedMessage();
                entry["embeddedMessage"] = embedded is not null && depth < 5 ? DumpItem(embedded, depth + 1) : null;
            }
            else
            {
                var content = attachment.GetContent();
                entry["contentSha256"] = attachment.Properties.Find(new PstPropertyTag(0x3701, PstPropertyType.Binary)) is null ? null : Convert.ToHexStringLower(SHA256.HashData(content));
                entry["contentLength"] = attachment.Properties.Find(new PstPropertyTag(0x3701, PstPropertyType.Binary)) is null ? null : content.Length;
            }

            attachments.Add(entry);
        }

        var result = new JsonObject
        {
            ["messageClass"] = Text(item.MessageClass),
            ["subject"] = Text(item.Subject),
            ["senderName"] = Text(item.SenderName),
            ["senderEmailAddress"] = Text(item.SenderEmailAddress),
            ["displayTo"] = Text(item.DisplayTo),
            ["importance"] = (int)item.Importance,
            ["clientSubmitTime"] = Date(item.ClientSubmitTime),
            ["messageDeliveryTime"] = Date(item.MessageDeliveryTime),
            ["internetMessageId"] = Text(item.InternetMessageId),
            ["bodySha256"] = TextHash(item.Body),
            ["bodyHtmlSha256"] = TextHash(item.BodyHtml),
            ["recipients"] = recipients,
            ["attachments"] = attachments,
        };

        switch (item)
        {
            case PstContact contact:
                result["contact"] = new JsonObject
                {
                    ["givenName"] = Text(contact.GivenName),
                    ["surname"] = Text(contact.Surname),
                    ["companyName"] = Text(contact.CompanyName),
                    ["email1EmailAddress"] = Text(contact.Email1Address),
                    ["businessTelephoneNumber"] = Text(contact.BusinessTelephoneNumber),
                    ["mobileTelephoneNumber"] = Text(contact.MobileTelephoneNumber),
                };
                break;

            case PstAppointment appointment:
                result["appointment"] = new JsonObject
                {
                    ["startTime"] = Date(appointment.Start),
                    ["endTime"] = Date(appointment.End),
                    ["location"] = Text(appointment.Location),
                    ["isRecurring"] = appointment.IsRecurring,
                    ["busyStatus"] = (int)appointment.BusyStatus,
                };
                break;

            case PstTask task:
                result["task"] = new JsonObject
                {
                    ["taskStatus"] = (int)task.Status,
                    ["percentComplete"] = task.PercentComplete,
                    ["taskOwner"] = Text(task.Owner),
                };
                break;
        }

        return result;
    }

    private static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string? TextHash(string? value) => string.IsNullOrEmpty(value) ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string? Date(DateTime? value) => value is null ? null : value.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Lists the differences between the expected and the actual JSON. The keys that only the expected JSON has are reported.</summary>
    private static void Compare(JsonNode? expected, JsonNode? actual, string path, List<string> differences)
    {
        switch (expected)
        {
            case JsonObject expectedObject when actual is JsonObject actualObject:
                foreach (var (key, value) in expectedObject)
                {
                    if (!actualObject.ContainsKey(key))
                    {
                        differences.Add($"{path}.{key}: missing");
                        continue;
                    }

                    Compare(value, actualObject[key], $"{path}.{key}", differences);
                }

                break;

            case JsonArray expectedArray when actual is JsonArray actualArray:
                if (expectedArray.Count != actualArray.Count)
                {
                    differences.Add($"{path}: {expectedArray.Count} elements expected, {actualArray.Count} found");
                }

                for (var i = 0; i < Math.Min(expectedArray.Count, actualArray.Count); i++)
                {
                    Compare(expectedArray[i], actualArray[i], $"{path}[{i}]", differences);
                }

                break;

            // pst-extractor does not read some embedded messages, such as the exceptions of a recurring appointment
            case null when path.EndsWith(".embeddedMessage", StringComparison.Ordinal) && actual is JsonObject:
                break;

            default:
                if (!JsonNode.DeepEquals(expected, actual))
                {
                    differences.Add($"{path}: expected {expected?.ToJsonString() ?? "null"}, found {actual?.ToJsonString() ?? "null"}");
                }

                break;
        }
    }
}
