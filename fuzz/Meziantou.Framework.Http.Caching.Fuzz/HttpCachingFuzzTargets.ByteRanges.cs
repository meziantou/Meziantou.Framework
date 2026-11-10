using System.Net;
using System.Net.Http.Headers;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Http.Caching.Fuzz;

public static partial class HttpCachingFuzzTargets
{
    private static readonly string[] ByteRangeTokens = ["bytes=", "bytes", "items=", "=", "-", ",", ", ", " ", "0", "1", "9", "16", "17", "100", "9223372036854775807", "99999999999999999999"];

    /// <summary>
    /// The first two bytes are the length of a stored representation, and the rest a <c>Range</c> header. Nothing throws; the
    /// satisfiable ranges are within the representation, in the order of the header, at most
    /// <see cref="ByteRanges.MaximumRangeCount"/> of them, and add up to no more than the representation when there are several;
    /// a header is unsatisfiable only when none of its ranges is. The response built from them is a 206 whose body is the
    /// single range that its <c>Content-Range</c> describes or a <c>multipart/byteranges</c> body with one part per range, a
    /// 416 whose <c>Content-Range</c> gives the length, or the unchanged 200 response.
    /// </summary>
    private static void ByteRangesTarget(ReadOnlySpan<byte> data)
    {
        var reader = new InputReader(data);
        var length = reader.ReadByte() | (reader.ReadByte() << 8);
        var text = FuzzText.Decode(data[Math.Min(2, data.Length)..]);
        if (!RangeHeaderValue.TryParse(text, out var range))
            return;

        var ranges = new List<ByteRange>();
        var evaluation = ByteRanges.Evaluate(range, length, ranges);
        switch (evaluation)
        {
            case RangeEvaluation.Ignore:
                FuzzAssert.Equal(0, ranges.Count, "Ranges are returned for a header that is ignored");
                break;

            case RangeEvaluation.Unsatisfiable:
                FuzzAssert.Equal(0, ranges.Count, "Ranges are returned for an unsatisfiable header");
                foreach (var item in range.Ranges)
                {
                    FuzzAssert.False(IsSatisfiable(item, length), $"The range {item} of an unsatisfiable header is satisfiable for a length of {length}");
                }

                break;

            case RangeEvaluation.Satisfiable:
                FuzzAssert.True(ranges.Count is > 0 and <= ByteRanges.MaximumRangeCount, $"{ranges.Count} ranges are satisfiable");
                long total = 0;
                foreach (var resolved in ranges)
                {
                    FuzzAssert.True(resolved.First >= 0 && resolved.First <= resolved.Last && resolved.Last < length, $"The range {resolved} is not within a representation of {length} bytes");
                    total += resolved.Length;
                }

                FuzzAssert.Equal(range.Ranges.Count(item => IsSatisfiable(item, length)), ranges.Count, "The number of satisfiable ranges disagrees with the header");
                if (ranges.Count > 1)
                {
                    FuzzAssert.True(total <= length, $"The ranges add up to {total} bytes, more than the representation of {length} bytes");
                }

                break;
        }

        var content = new byte[length];
        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(i % 251);
        }

        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        ByteRanges.Apply(response, content, range);
        var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        switch (evaluation)
        {
            case RangeEvaluation.Ignore:
                FuzzAssert.Equal(HttpStatusCode.OK, response.StatusCode, "An ignored range changes the status");
                FuzzAssert.True(body.AsSpan().SequenceEqual(content), "An ignored range changes the body");
                break;

            case RangeEvaluation.Unsatisfiable:
                FuzzAssert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode, "An unsatisfiable range is not answered with a 416 response");
                FuzzAssert.Equal(length, response.Content.Headers.ContentRange?.Length, "The Content-Range of the 416 response does not give the length");
                FuzzAssert.Equal(0, body.Length, "A 416 response has a body");
                break;

            case RangeEvaluation.Satisfiable when ranges.Count is 1:
                FuzzAssert.Equal(HttpStatusCode.PartialContent, response.StatusCode, "A satisfiable range is not answered with a 206 response");
                var contentRange = response.Content.Headers.ContentRange;
                FuzzAssert.True(contentRange is not null && contentRange.From == ranges[0].First && contentRange.To == ranges[0].Last && contentRange.Length == length, $"The Content-Range {contentRange} does not describe the range {ranges[0]}");
                FuzzAssert.True(body.AsSpan().SequenceEqual(content.AsSpan((int)ranges[0].First, (int)ranges[0].Length)), "The body of the 206 response is not the range");
                FuzzAssert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType, "The 206 response lost the content type");
                break;

            case RangeEvaluation.Satisfiable:
                FuzzAssert.Equal(HttpStatusCode.PartialContent, response.StatusCode, "Satisfiable ranges are not answered with a 206 response");
                FuzzAssert.Equal("multipart/byteranges", response.Content.Headers.ContentType?.MediaType, "Several ranges are not answered with a multipart/byteranges body");
                var multipart = (MultipartContent)response.Content;
                FuzzAssert.Equal(ranges.Count, multipart.Count(), "The multipart/byteranges body does not have a part per range");
                break;
        }
    }

    /// <summary>RFC 9110 Section 14.1.2, written apart from <see cref="ByteRanges"/>.</summary>
    private static bool IsSatisfiable(RangeItemHeaderValue item, long length)
    {
        if (item.From is { } from)
            return from < length;

        return item.To is > 0 && length > 0;
    }

    private static IEnumerable<FuzzSeed> GetByteRangesSeeds()
    {
        yield return ByteRangesSeed("single", 17, "bytes=5-11");
        yield return ByteRangesSeed("suffix", 17, "bytes=-4");
        yield return ByteRangesSeed("suffix-longer-than-length", 17, "bytes=-100");
        yield return ByteRangesSeed("open-ended", 17, "bytes=13-");
        yield return ByteRangesSeed("clamped", 17, "bytes=13-100");
        yield return ByteRangesSeed("unsatisfiable", 17, "bytes=100-200");
        yield return ByteRangesSeed("zero-suffix", 17, "bytes=-0");
        yield return ByteRangesSeed("empty-representation", 0, "bytes=0-1");
        yield return ByteRangesSeed("multiple", 17, "bytes=0-3, 13-16");
        yield return ByteRangesSeed("overlapping", 17, "bytes=0-15, 1-16");
        yield return ByteRangesSeed("some-unsatisfiable", 17, "bytes=0-1, 100-200");
        yield return ByteRangesSeed("other-unit", 17, "items=0-1");
        yield return ByteRangesSeed("too-many", 1000, "bytes=" + string.Join(", ", Enumerable.Range(0, 20).Select(i => string.Create(CultureInfo.InvariantCulture, $"{i * 10}-{i * 10 + 1}"))));
    }

    private static FuzzSeed ByteRangesSeed(string name, int length, string range)
    {
        byte[] data = [(byte)length, (byte)(length >> 8), .. Encoding.UTF8.GetBytes(range)];
        return new FuzzSeed(name, data);
    }
}
