using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The reader of offline address books reads any input or throws an <see cref="InvalidEmailDataException"/>. An address book
/// written and read back has the same schemas and the same values, except the empty values the format does not write, and its
/// checksum is valid.
/// </summary>
public static partial class EmailFuzzTargets
{
    private const string OabSeedPrefix = "TestData/Oab/";

    private static readonly string[] OabTokens =
    [
        BinaryToken([0x20, 0x00, 0x00, 0x00]), BinaryToken([0x1F, 0x00, 0x00, 0x68]), BinaryToken([0x1E, 0x00, 0x03, 0x30]), BinaryToken([0x1F, 0x00, 0x01, 0x30]), BinaryToken([0x03, 0x10, 0x05, 0x68]),
        BinaryToken([0x02, 0x01, 0x22, 0x3A]), BinaryToken([0x1F, 0x10, 0x0F, 0x80]), BinaryToken([0x0D, 0x00, 0xD8, 0x8C]), BinaryToken([0x02, 0x11, 0x70, 0x3A]), BinaryToken([0x0B, 0x00, 0x40, 0x3A]),
        BinaryToken([0x81]), BinaryToken([0x82]), BinaryToken([0x84, 0xFF, 0xFF, 0xFF, 0xFF]), BinaryToken([0xF8]), BinaryToken([0x00]),
    ];

    private static IReadOnlyList<FuzzTarget> OabTargets { get; } =
    [
        new("oab-read", OabRead) { Seeds = GetOabSeeds, Dictionary = OabTokens },
    ];

    private static void OabRead(ReadOnlySpan<byte> data)
    {
        OabFile file;
        try
        {
            file = OabFile.Load(data, verifyChecksum: false);
        }
        catch (InvalidEmailDataException)
        {
            return;
        }

        foreach (var record in file.Records)
        {
            _ = VCard.FromOab(record).ToString(VCardVersion.V40);
        }

        var copy = OabFile.Load(file.ToByteArray(), verifyChecksum: true);
        FuzzAssert.Equal(DescribeOab(file), DescribeOab(copy), "An offline address book written and read back differs");
    }

    private static string DescribeOab(OabFile file)
    {
        var builder = new StringBuilder();
        builder.AppendJoin(",", file.HeaderProperties).Append('\n').AppendJoin(",", file.RecordProperties).Append('\n');
        foreach (var record in new[] { file.Header }.Concat(file.Records))
        {
            foreach (var (tag, value) in record.Values.OrderBy(pair => pair.Key.Value))
            {
                // The format does not write the empty values
                var text = value switch
                {
                    string s => s,
                    byte[] b => Convert.ToHexString(b),
                    string[] items => string.Join("|", items.Where(item => item.Length > 0)),
                    byte[][] items => string.Join("|", items.Where(item => item.Length > 0).Select(Convert.ToHexString)),
                    int[] items => items.Length == 0 ? "" : "#" + string.Join("|", items),
                    _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                };

                if (!string.IsNullOrEmpty(text))
                {
                    builder.Append(tag).Append('=').Append(text).Append(';');
                }
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static IEnumerable<FuzzSeed> GetOabSeeds()
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(OabSeedPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[OabSeedPrefix.Length..], memory.ToArray());
        }

        var book = OabFile.Create();
        book.Header.Values[MapiPropertyTag.FromValue(0x6800001F)] = "\\Global Address List";
        var record = new OabRecord();
        record.Values[MapiPropertyTag.FromValue(0x3001001F)] = "Zoë";
        record.Values[MapiPropertyTag.FromValue(0x800F101F)] = new[] { "SMTP:zoe@example.com" };
        record.Values[MapiPropertyTag.FromValue(0x3A220102)] = new byte[] { 1, 2, 3 };
        record.Values[MapiPropertyTag.FromValue(0x0FFE0003)] = 300;
        record.Values[MapiPropertyTag.FromValue(0x3A40000B)] = true;
        book.Records.Add(record);
        yield return new FuzzSeed("writer", book.ToByteArray());
    }
}
