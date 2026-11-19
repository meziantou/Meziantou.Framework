using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The NEL header of Network Error Logging (https://w3c.github.io/network-error-logging/#nel-response-header)
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The tokens of the NEL header target: the JSON syntax and the members of a policy.</summary>
    private static string[] NetworkErrorLoggingTokens =>
    [
        "{", "}", "[", "]", ":", ",", "\"", "\\\"", "\\u", "true", "false", "null", "0", "1", "1.0", "0.5", "-1", "1e400", "10886400",
        "\"report_to\"", "\"max_age\"", "\"include_subdomains\"", "\"success_fraction\"", "\"failure_fraction\"", "\"request_headers\"",
        "\"response_headers\"", "\"nel-group\"", "\"X-Header\"",
    ];

    /// <summary>
    /// Parses the value of a NEL header, and checks the properties of a valid policy: its <c>max_age</c> is not negative,
    /// its fractions are between 0 and 1, its header names are lowercase, and serializing it as a NEL header and parsing
    /// that header gives the same policy. An invalid header is ignored (null), never an exception.
    /// </summary>
    public static void NetworkErrorLoggingHeader(string input)
    {
        if (NetworkErrorLoggingPolicyHeader.Parse(input) is not { } policy)
            return;

        FuzzAssert.True(policy.MaxAge >= 0, $"The NEL header {FuzzAssert.Format(input)} has the max_age {policy.MaxAge}");
        FuzzAssert.True(policy.SuccessFraction is >= 0 and <= 1, $"The NEL header {FuzzAssert.Format(input)} has the success_fraction {policy.SuccessFraction}");
        FuzzAssert.True(policy.FailureFraction is >= 0 and <= 1, $"The NEL header {FuzzAssert.Format(input)} has the failure_fraction {policy.FailureFraction}");
        foreach (var name in policy.RequestHeaders.Concat(policy.ResponseHeaders))
        {
            FuzzAssert.True(!name.Any(c => c is >= 'A' and <= 'Z'), $"The NEL header {FuzzAssert.Format(input)} has the header name {FuzzAssert.Format(name)}");
        }

        // A string with a lone surrogate (a JSON escape) cannot be written by Utf8JsonWriter: the round trip is not checked
        if (!IsWellFormedUtf16(policy.ReportTo) || !policy.RequestHeaders.Concat(policy.ResponseHeaders).All(IsWellFormedUtf16))
            return;

        var serialized = SerializeNetworkErrorLoggingPolicy(policy);
        var reparsed = NetworkErrorLoggingPolicyHeader.Parse(serialized);
        FuzzAssert.True(reparsed is not null, $"The serialization {FuzzAssert.Format(serialized)} of the NEL header {FuzzAssert.Format(input)} is not valid");
        FuzzAssert.Equal(policy.ReportTo, reparsed.ReportTo, $"The report_to of the serialization of {FuzzAssert.Format(input)}");
        FuzzAssert.Equal(policy.MaxAge, reparsed.MaxAge, $"The max_age of the serialization of {FuzzAssert.Format(input)}");
        FuzzAssert.Equal(policy.IncludeSubdomains, reparsed.IncludeSubdomains, $"The include_subdomains of the serialization of {FuzzAssert.Format(input)}");
        FuzzAssert.Equal(policy.SuccessFraction, reparsed.SuccessFraction, $"The success_fraction of the serialization of {FuzzAssert.Format(input)}");
        FuzzAssert.Equal(policy.FailureFraction, reparsed.FailureFraction, $"The failure_fraction of the serialization of {FuzzAssert.Format(input)}");
        FuzzAssert.True(policy.RequestHeaders.SequenceEqual(reparsed.RequestHeaders, StringComparer.Ordinal), $"The request_headers of the serialization of {FuzzAssert.Format(input)}");
        FuzzAssert.True(policy.ResponseHeaders.SequenceEqual(reparsed.ResponseHeaders, StringComparer.Ordinal), $"The response_headers of the serialization of {FuzzAssert.Format(input)}");
    }

    private static bool IsWellFormedUtf16(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string SerializeNetworkErrorLoggingPolicy(NetworkErrorLoggingPolicyHeader policy)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("report_to", policy.ReportTo);
            writer.WriteNumber("max_age", policy.MaxAge);
            writer.WriteBoolean("include_subdomains", policy.IncludeSubdomains);
            writer.WriteNumber("success_fraction", policy.SuccessFraction);
            writer.WriteNumber("failure_fraction", policy.FailureFraction);
            writer.WriteStartArray("request_headers");
            foreach (var name in policy.RequestHeaders)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("response_headers");
            foreach (var name in policy.ResponseHeaders)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static IEnumerable<FuzzSeed> GetNetworkErrorLoggingSeeds()
    {
        string[] inputs =
        [
            """{"report_to": "nel-group", "max_age": 10886400, "success_fraction": 1.0}""",
            """{"report_to": "nel-group", "max_age": 10886400, "include_subdomains": true, "success_fraction": 1.0}""",
            """{"report_to": "nel-group", "max_age": 10886400, "success_fraction": 0.0}""",
            """{"max_age": 0}""",
            """{"report_to": "group", "max_age": 60, "failure_fraction": 0.5, "request_headers": ["X-Test"], "response_headers": ["ETag"]}""",
            """{"report_to": "group", "max_age": 60, "success_fraction": 2}, {"report_to": "other", "max_age": 1}""",
            """{"report_to": "group", "max_age": -1}""",
            """{"report_to": 1, "max_age": 60}""",
            """{"max_age": 60}""",
            "[]",
            "{",
        ];
        return inputs.Select((input, index) => new FuzzSeed($"nel-{index}", Encoding.UTF8.GetBytes(input)));
    }
}
