using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The expected results of the web-platform-tests (expectations/&lt;directory&gt;.json). Subtests not listed are expected
/// to pass. Set the environment variable <c>WPT_UPDATE_EXPECTATIONS=1</c> to rewrite the files from the current results.
/// </summary>
internal static class WptExpectations
{
    private static readonly Lock LockObject = new();
    private static readonly Dictionary<string, JsonObject> Files = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };

    public static bool UpdateMode => Environment.GetEnvironmentVariable("WPT_UPDATE_EXPECTATIONS") is "1" or "true";

    /// <summary>The name of the expectation file: the top-level directory, or the first two for the large ones (css/selectors → css-selectors).</summary>
    private static string GetFileName(WptTest test) => GetFileName(test.RelativePath);

    private static string GetFileName(string relativePath)
    {
        var segments = relativePath.Split('/');
        return segments[0] is "css" or "html" && segments.Length > 2 ? segments[0] + "-" + segments[1] : segments[0];
    }

    private static JsonObject GetFile(string name)
    {
        if (!Files.TryGetValue(name, out var file))
        {
            var path = Path.Combine(WptPaths.Expectations, name + ".json");
            file = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : [];
            Files[name] = file;
        }

        return file;
    }

    public static void Check(WptTest test, WptResult result)
    {
        if (UpdateMode)
        {
            Update(test, result);
            return;
        }

        if (GetDifferences(test, result) is { } differences)
        {
            Assert.Fail(differences);
        }
    }

    /// <summary>Returns the differences between the result of a test and its expectations, or null when it is the expected one.</summary>
    public static string? GetDifferences(WptTest test, WptResult result)
    {
        JsonObject? expected;
        lock (LockObject)
        {
            expected = GetFile(GetFileName(test))[test.Id]?.AsObject();
        }

        var expectedHarness = expected?["harness"]?.GetValue<string>() ?? "OK";
        var expectedTotal = expected?["total"]?.GetValue<int>();
        var expectedFailures = expected?["failures"]?.AsObject();
        var errors = new StringBuilder();
        if (expected is null)
        {
            errors.AppendLine("The test has no expectations. Run the tests with WPT_UPDATE_EXPECTATIONS=1 to add them.");
        }

        if (result.HarnessStatus != expectedHarness)
        {
            errors.AppendLine($"Harness status: expected {expectedHarness}, actual {result.HarnessStatus} ({result.HarnessMessage})");
        }

        if (expectedTotal is not null && expectedTotal != result.Subtests.Count)
        {
            errors.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Subtest count: expected {expectedTotal}, actual {result.Subtests.Count}");
        }

        foreach (var subtest in result.Subtests.DistinctBy(s => s.Name))
        {
            var expectedStatus = expectedFailures?[subtest.Name]?.GetValue<string>() ?? "PASS";
            if (subtest.Status != expectedStatus)
            {
                errors.AppendLine($"{(subtest.Status == "PASS" ? "UNEXPECTED PASS" : "REGRESSION")}: {subtest.Name}: expected {expectedStatus}, actual {subtest.Status} {subtest.Message}");
            }
        }

        if (errors.Length == 0)
            return null;

        errors.AppendLine().AppendLine("Log:");
        foreach (var line in result.Log)
        {
            errors.AppendLine(line);
        }

        return errors.ToString();
    }

    /// <summary>Returns the expected status of a reftest (<c>{ "reftest": "FAIL" }</c>), or null when it has no expectations.</summary>
    public static string? GetExpectedStatus(WptReftest test)
    {
        lock (LockObject)
        {
            return GetFile(GetFileName(test.RelativePath))[test.Id]?["reftest"]?.GetValue<string>();
        }
    }

    /// <summary>Checks the result of a reftest: its status (<c>PASS</c>, <c>FAIL</c>, <c>ERROR</c>, <c>TIMEOUT</c>) is the one of the expectations.</summary>
    public static void Check(WptReftest test, WptReftestResult result)
    {
        if (UpdateMode)
        {
            Write(test.RelativePath, test.Id, new JsonObject { ["reftest"] = result.Status });
            return;
        }

        var expected = GetExpectedStatus(test);
        if (expected == result.Status)
            return;

        var errors = new StringBuilder();
        if (expected is null)
        {
            errors.AppendLine("The test has no expectations. Run the tests with WPT_UPDATE_EXPECTATIONS=1 to add them.");
        }

        errors.AppendLine($"{(result.Status == "PASS" ? "UNEXPECTED PASS" : "REGRESSION")}: expected {expected ?? "PASS"}, actual {result.Status}");
        if (result.Message is not null)
        {
            errors.AppendLine(result.Message);
        }

        errors.AppendLine().AppendLine("Log:");
        foreach (var line in result.Log)
        {
            errors.AppendLine(line);
        }

        Assert.Fail(errors.ToString());
    }

    private static void Update(WptTest test, WptResult result)
    {
        var entry = new JsonObject();
        if (result.HarnessStatus != "OK")
        {
            entry["harness"] = result.HarnessStatus;
        }

        entry["total"] = result.Subtests.Count;
        var failures = new JsonObject();
        foreach (var subtest in result.Subtests.DistinctBy(s => s.Name).Where(s => s.Status != "PASS").OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            failures[subtest.Name] = subtest.Status;
        }

        if (failures.Count > 0)
        {
            entry["failures"] = failures;
        }

        Write(test.RelativePath, test.Id, entry);
    }

    /// <summary>The files whose entries changed since they were written, and the time of their last write.</summary>
    private static readonly HashSet<string> DirtyFiles = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LastWrites = new(StringComparer.Ordinal);
    private static bool s_flushRegistered;

    // Kept alive until the process exits
    private static Timer? s_flushTimer;
    private static IDisposable? s_terminationRegistration;

    /// <summary>
    /// Updates the entry of a test. A file is written at most once per second while the tests run (rewriting a large file after
    /// each of its tests is quadratic), and the files that changed since are written by a timer, every second, and when the
    /// process exits: a process that is killed (e.g. by the hang dump of the test platform, which gives no notice) loses at
    /// most the results of its last second, and leaves the marker of an interrupted update (see <see cref="GetInterruptedUpdates"/>).
    /// </summary>
    private static void Write(string relativePath, string id, JsonObject entry)
    {
        lock (LockObject)
        {
            var name = GetFileName(relativePath);
            GetFile(name)[id] = entry;
            DirtyFiles.Add(name);
            if (!s_flushRegistered)
            {
                s_flushRegistered = true;
                BeginUpdate();
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    s_flushTimer?.Dispose();
                    s_terminationRegistration?.Dispose();
                    Flush();
                    EndUpdate();
                };
                s_flushTimer = new Timer(static _ => Flush(), state: null, dueTime: 1000, period: 1000);
            }

            var now = Environment.TickCount64;
            if (!LastWrites.TryGetValue(name, out var lastWrite) || now - lastWrite > 1000)
            {
                WriteFile(name);
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Interrupted updates: a run that updates the expectations writes a marker file, which it deletes when the process
    // exits normally. A marker whose process is gone is the trace of an update that was killed (a crash, a hang dump...)
    // or canceled: the expectations of the tests that did not run are stale, which the test run must not hide.
    // ----------------------------------------------------------------------------------------------------------------

    private const string MarkerPrefix = ".incomplete-update-";
    private static string? s_markerPath;
    private static volatile bool s_canceled;

    private static string CurrentFilter => Environment.GetEnvironmentVariable("WPT_FILTER") ?? "";

    private sealed record UpdateMarker(int ProcessId, long ProcessStartTime, string Started, string Filter, bool Canceled);

    private static void BeginUpdate()
    {
        foreach (var (path, marker) in ReadMarkers())
        {
            if (!IsRunning(marker) && IsReplacedByThisRun(marker))
            {
                File.Delete(path);
            }
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Directory.CreateDirectory(WptPaths.Expectations);
        s_markerPath = Path.Combine(WptPaths.Expectations, MarkerPrefix + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteMarker(new UpdateMarker(Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks, DateTimeOffset.UtcNow.ToString("u", System.Globalization.CultureInfo.InvariantCulture), CurrentFilter, Canceled: false));

        // A canceled run (Ctrl+C, SIGTERM) exits normally, but it did not run all the tests: its marker is kept
        Console.CancelKeyPress += (_, _) => s_canceled = true;
        if (!OperatingSystem.IsWindows())
        {
            s_terminationRegistration = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, static _ => s_canceled = true);
        }
    }

    private static void EndUpdate()
    {
        if (s_markerPath is not { } path)
            return;

        try
        {
            if (!s_canceled)
            {
                File.Delete(path);
            }
            else if (JsonSerializer.Deserialize<UpdateMarker>(File.ReadAllText(path), JsonOptions) is { } marker)
            {
                WriteMarker(marker with { Canceled = true });
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
    }

    /// <summary>Whether this run updates the expectations of the tests of an interrupted update again: the same filter, or all the tests.</summary>
    private static bool IsReplacedByThisRun(UpdateMarker marker) => UpdateMode && (CurrentFilter.Length == 0 || marker.Filter == CurrentFilter);

    private static void WriteMarker(UpdateMarker marker) => File.WriteAllText(s_markerPath!, JsonSerializer.Serialize(marker, JsonOptions) + "\n");

    private static List<(string Path, UpdateMarker Marker)> ReadMarkers()
    {
        var result = new List<(string, UpdateMarker)>();
        if (!Directory.Exists(WptPaths.Expectations))
            return result;

        foreach (var path in Directory.EnumerateFiles(WptPaths.Expectations, MarkerPrefix + "*"))
        {
            try
            {
                if (JsonSerializer.Deserialize<UpdateMarker>(File.ReadAllText(path), JsonOptions) is { } marker)
                {
                    result.Add((path, marker));
                }
            }
            catch (IOException)
            {
                // Being written or deleted by its process
            }
            catch (JsonException)
            {
                result.Add((path, new UpdateMarker(0, 0, "an unknown date", "", Canceled: false)));
            }
        }

        return result;
    }

    /// <summary>Whether the process that wrote the marker still runs (the start time tells it from another process with the same ID).</summary>
    private static bool IsRunning(UpdateMarker marker)
    {
        if (marker.Canceled || marker.ProcessId == 0)
            return false;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(marker.ProcessId);
            return Math.Abs(process.StartTime.ToUniversalTime().Ticks - marker.ProcessStartTime) < TimeSpan.TicksPerSecond * 5;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process exists but cannot be inspected
            return true;
        }
    }

    /// <summary>
    /// Returns a description of the updates of the expectations that did not complete: their process was killed (e.g. the
    /// hang dump of the test platform kills the test host when no test completes for 10 minutes) or canceled before the end, so
    /// the expectations of the tests that it did not run were not updated. The update of this process and the ones that it
    /// runs again are not listed.
    /// </summary>
    public static IReadOnlyList<string> GetInterruptedUpdates()
    {
        var result = new List<string>();
        foreach (var (path, marker) in ReadMarkers())
        {
            if (marker.ProcessId == Environment.ProcessId || IsRunning(marker) || IsReplacedByThisRun(marker))
                continue;

            var filter = marker.Filter.Length == 0 ? "all the tests" : $"WPT_FILTER='{marker.Filter}'";
            result.Add($"The update of the expectations started on {marker.Started} ({filter}, process {marker.ProcessId}) was {(marker.Canceled ? "canceled" : "killed")} before the end: the expectations may be partially updated. Run the same update again, or delete '{path}'.");
        }

        return result;
    }

    /// <summary>Writes the files whose entries changed.</summary>
    public static void Flush()
    {
        lock (LockObject)
        {
            foreach (var name in DirtyFiles.ToList())
            {
                WriteFile(name);
            }
        }
    }

    private static void WriteFile(string name)
    {
        var file = GetFile(name);
        var sorted = new JsonObject();
        foreach (var (key, value) in file.OrderBy(p => p.Key, StringComparer.Ordinal).ToList())
        {
            file.Remove(key);
            sorted[key] = value;
        }

        Files[name] = sorted;
        Directory.CreateDirectory(WptPaths.Expectations);
        File.WriteAllText(Path.Combine(WptPaths.Expectations, name + ".json"), sorted.ToJsonString(JsonOptions) + "\n");
        DirtyFiles.Remove(name);
        LastWrites[name] = Environment.TickCount64;
    }
}
