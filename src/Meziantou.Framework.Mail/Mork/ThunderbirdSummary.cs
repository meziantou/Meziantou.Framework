using System.Globalization;

namespace Meziantou.Framework.Mail;

/// <summary>Reads and writes the summary files of the folders of Thunderbird (<c>.msf</c>), the Mork databases next to the mbox files of the folders.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Mail.Tests/MboxTests.cs" ref:region="ThunderbirdSummary_Usage" ref:dedent="true">
/// var messages = ThunderbirdSummary.ReadMessages(path);
/// var first = messages[0];
/// Assert.Equal("First", first.Subject);
/// Assert.Equal("first@example.com", first.MessageId);
/// Assert.Equal(new DateTimeOffset(2025, 7, 1, 8, 52, 37, TimeSpan.Zero), first.Date);
/// Assert.True(first.Flags.HasFlag(ThunderbirdMessageFlags.Read));
///
/// // The position of the message in the mbox file of the folder
/// Assert.Equal(421, messages[1].Offset);
/// </code>
/// </example>
public static class ThunderbirdSummary
{
    /// <summary>The kind of the table of the messages.</summary>
    public const string MessagesTableKind = "ns:msg:db:table:kind:msgs";

    /// <summary>The scope of the rows of the messages.</summary>
    public const string MessagesRowScope = "ns:msg:db:row:scope:msgs:all";

    /// <summary>Returns the messages of a summary: the rows of its table of messages.</summary>
    /// <param name="document">The summary.</param>
    /// <returns>The messages.</returns>
    public static IEnumerable<ThunderbirdMessageSummary> ReadMessages(MorkDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var table = document.FindTable(MessagesTableKind);
        if (table is null)
        {
            yield break;
        }

        foreach (var row in table.Rows)
        {
            yield return new ThunderbirdMessageSummary
            {
                Key = long.TryParse(row.Id, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var key) ? key : 0,
                Subject = row["subject"],
                Sender = row["sender"],
                Recipients = row["recipients"],
                CcList = row["ccList"],
                MessageId = row["message-id"],
                References = row["references"],
                Date = ParseDate(row["date"]),
                DateReceived = ParseDate(row["dateReceived"]),
                Flags = (ThunderbirdMessageFlags)(ParseHex(row["flags"]) ?? 0),
                Size = ParseHex(row["size"]),
                LineCount = ParseHex(row["numLines"]),
                Offset = long.TryParse(row["storeToken"], NumberStyles.None, CultureInfo.InvariantCulture, out var token) ? token : ParseHex(row["msgOffset"]),
            };
        }
    }

    /// <summary>Returns the messages of a summary file.</summary>
    /// <param name="path">The path of the <c>.msf</c> file.</param>
    /// <returns>The messages.</returns>
    /// <exception cref="InvalidMailDataException">The file is not a Mork database.</exception>
    public static IReadOnlyList<ThunderbirdMessageSummary> ReadMessages(string path) => [.. ReadMessages(MorkDocument.Load(path))];

    /// <summary>Creates a summary of messages: a Mork database with a table of messages, with the columns Thunderbird reads. Thunderbird rebuilds a summary that does not match its folder.</summary>
    /// <param name="messages">The messages.</param>
    /// <returns>The summary.</returns>
    public static MorkDocument Create(IEnumerable<ThunderbirdMessageSummary> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var document = new MorkDocument();
        var table = document.GetOrAddTable(MessagesRowScope, "1", MessagesTableKind);
        foreach (var message in messages)
        {
            var row = document.GetOrAddRow(MessagesRowScope, Hex(message.Key));
            row["subject"] = message.Subject;
            row["sender"] = message.Sender;
            row["recipients"] = message.Recipients;
            row["ccList"] = message.CcList;
            row["message-id"] = message.MessageId;
            row["references"] = message.References;
            row["date"] = message.Date is { } date ? Hex(date.ToUnixTimeSeconds()) : null;
            row["dateReceived"] = message.DateReceived is { } received ? Hex(received.ToUnixTimeSeconds()) : null;
            row["flags"] = Hex((long)message.Flags);
            row["size"] = message.Size is { } size ? Hex(size) : null;
            row["numLines"] = message.LineCount is { } lines ? Hex(lines) : null;
            row["storeToken"] = message.Offset?.ToString(CultureInfo.InvariantCulture);
            table.Rows.Add(row);
        }

        return document;
    }

    private static long? ParseHex(string? value) => long.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateTimeOffset? ParseDate(string? value) => ParseHex(value) is long seconds and > -62135596800 and < 253402300800 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private static string Hex(long value) => value.ToString("x", CultureInfo.InvariantCulture);
}
