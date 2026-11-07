using Meziantou.Framework.HttpArchive;

namespace Meziantou.Framework.Http.Samples;

/// <summary>The examples of src/Meziantou.Framework.HttpArchive/readme.md that read a HAR file from the disk.</summary>
public static class HttpArchiveExamples
{
    public static async Task ParseFile()
    {
        #region HarDocument_ParseFile
        await using var stream = File.OpenRead("traffic.har");
        var document = await HarDocument.ParseAsync(stream);

        foreach (var entry in document.Log?.Entries ?? [])
        {
            Console.WriteLine($"{entry.Request?.Method} {entry.Request?.Url} -> {entry.Response?.Status}");
        }
        #endregion
    }

    public static void ConvertToHttpMessages()
    {
        #region HarEntry_ConvertToHttpMessages
        var document = HarDocument.Parse(File.ReadAllText("traffic.har"));
        var entry = document.Log!.Entries![0];

        // Throws InvalidOperationException when the archived entry has no request/response,
        // or when the request declares no method or URL.
        using var request = entry.ToHttpRequestMessage();
        using var response = entry.ToHttpResponseMessage();
        #endregion
    }
}
