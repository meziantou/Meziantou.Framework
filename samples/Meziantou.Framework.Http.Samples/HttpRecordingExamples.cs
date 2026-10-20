#pragma warning disable CA2234 // Pass system uri objects instead of strings
using Meziantou.Framework.Http.Recording;

namespace Meziantou.Framework.Http.Samples;

/// <summary>The examples of src/Meziantou.Framework.Http.Recording/readme.md that send real requests.</summary>
public static class HttpRecordingExamples
{
    public static async Task AutoRecordAndReplay()
    {
        #region HttpRecordingHandler_AutoRecordAndReplay
        var store = new HarHttpRecordingStore("http-recordings.har");
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Auto,
            Sanitizers = { new HeaderRemovalSanitizer("Authorization", "Cookie") },
        };

        using var innerHandler = new SocketsHttpHandler();
        await using var recordingHandler = new HttpRecordingHandler(innerHandler, store, options);
        using var httpClient = new HttpClient(recordingHandler);

        // The first run calls the real endpoint and records the response.
        var response1 = await httpClient.GetAsync("https://api.example.com/data");

        // Persist all recordings at the end of the test/session.
        await recordingHandler.SaveAsync();
        #endregion
    }

    public static async Task ReplayOnly()
    {
        #region HttpRecordingHandler_ReplayOnly
        var store = new JsonHttpRecordingStore("http-recordings.json");
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            MissBehavior = HttpRecordingMissBehavior.Throw,
        };

        using var recordingHandler = new HttpRecordingHandler(store, options);
        using var httpClient = new HttpClient(recordingHandler);

        var response = await httpClient.GetAsync("https://api.example.com/data");
        #endregion
    }
}
