using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The format detection reads any input without throwing, gives the same format for the bytes and for a stream of them, and
/// restores the position of the stream.
/// </summary>
public static partial class EmailFuzzTargets
{
    private static readonly string[] DetectTokens =
    [
        "!BDN", "SM", "SO", BinaryToken([0x78, 0x9F, 0x3E, 0x22]), BinaryToken([0x20, 0x00, 0x00, 0x00]), BinaryToken([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
        BinaryToken([0xEF, 0xBB, 0xBF]), "From ", "// <!-- <mdb:mork:z v=\"1.4\"/> -->", "BEGIN:VCARD", "BEGIN:VCALENDAR", "From: ", "Subject: ", "Received: ", "X-Custom: ", "\r\n", "\n\t", "123\n",
    ];

    private static IReadOnlyList<FuzzTarget> DetectTargets { get; } =
    [
        new("email-detect", Detect) { Seeds = GetDetectSeeds, Dictionary = DetectTokens },
    ];

    private static void Detect(ReadOnlySpan<byte> data)
    {
        var format = EmailFormatDetector.Detect(data);
        using var stream = new MemoryStream();
        stream.Write([1, 2, 3]);
        stream.Write(data);
        stream.Position = 3;
        FuzzAssert.Equal(format, EmailFormatDetector.Detect(stream), "The format of the stream differs from the format of the bytes");
        FuzzAssert.Equal(3L, stream.Position, "The position of the stream is not restored");
    }

    private static IEnumerable<FuzzSeed> GetDetectSeeds()
    {
        // The files of the other targets, except the Outlook data files, which are detected from their header
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("TestData/", StringComparison.Ordinal) && !name.StartsWith(TestDataPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name["TestData/".Length..].Replace('/', '-'), memory.ToArray());
        }

        yield return new FuzzSeed("pst-header", (byte[])[.. "!BDN"u8, 0, 0, 0, 0, (byte)'S', (byte)'M', 23, 0]);
        yield return FuzzSeed.FromText("emlx", "12\nFrom: a@b.c\n\nx");
        yield return FuzzSeed.FromText("vcard", "BEGIN:VCARD\r\nEND:VCARD\r\n");
    }
}
