using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.Http.Caching;

/// <summary>Answers range requests (RFC 9110 Section 14) from a stored complete response.</summary>
internal static class ByteRanges
{
    /// <summary>The number of ranges above which a request is answered with the complete response.</summary>
    /// <remarks>RFC 9110 Section 14.2: a server may ignore a Range header with many ranges, which costs more to answer than the complete response.</remarks>
    internal const int MaximumRangeCount = 16;

    /// <summary>Resolves the ranges of a <c>Range</c> header against a representation of <paramref name="length"/> bytes.</summary>
    /// <returns>
    /// <see cref="RangeEvaluation.Ignore"/> when the response must be the complete representation, <see cref="RangeEvaluation.Satisfiable"/>
    /// when <paramref name="ranges"/> holds the satisfiable ranges in the order of the request, and <see cref="RangeEvaluation.Unsatisfiable"/>
    /// when none of them is.
    /// </returns>
    public static RangeEvaluation Evaluate(RangeHeaderValue range, long length, List<ByteRange> ranges)
    {
        // RFC 9110 Section 14.2: a server must ignore a Range header with a range unit it does not understand
        if (!string.Equals(range.Unit, "bytes", StringComparison.OrdinalIgnoreCase))
            return RangeEvaluation.Ignore;

        if (range.Ranges.Count is 0 or > MaximumRangeCount)
            return RangeEvaluation.Ignore;

        long totalLength = 0;
        foreach (var item in range.Ranges)
        {
            // RFC 9110 Section 14.1.1: a range whose last position is before its first one is invalid, which makes the
            // whole header invalid, so it is ignored.
            if (item.From is { } from && item.To is { } to && to < from)
            {
                ranges.Clear();
                return RangeEvaluation.Ignore;
            }

            if (!TryResolve(item.From, item.To, length, out var resolved))
                continue;

            ranges.Add(resolved);
            totalLength += resolved.Length;
        }

        if (ranges.Count is 0)
            return RangeEvaluation.Unsatisfiable;

        // RFC 9110 Section 14.2: overlapping ranges that add up to more than the representation cost more than the
        // complete response, which is sent instead.
        if (ranges.Count > 1 && totalLength > length)
        {
            ranges.Clear();
            return RangeEvaluation.Ignore;
        }

        return RangeEvaluation.Satisfiable;
    }

    /// <summary>RFC 9110 Section 14.1.2: resolves a range against the length of the representation, when it is satisfiable.</summary>
    private static bool TryResolve(long? from, long? to, long length, out ByteRange range)
    {
        range = default;
        if (from is { } first)
        {
            // int-range: satisfiable when the first position is within the representation. The last position is
            // clamped to the end of the representation.
            if (first >= length)
                return false;

            var last = to is { } requestedLast && requestedLast < length ? requestedLast : length - 1;
            range = new ByteRange(first, last);
            return true;
        }

        // suffix-range: the last N bytes, satisfiable when N is not zero and the representation is not empty
        if (to is not { } suffixLength || suffixLength is 0 || length is 0)
            return false;

        range = new ByteRange(suffixLength >= length ? 0 : length - suffixLength, length - 1);
        return true;
    }

    /// <summary>
    /// RFC 9110 Section 13.1.5: indicates whether the <c>If-Range</c> condition of the request holds for the stored response, in
    /// which case the range can be served. Otherwise, the complete response is.
    /// </summary>
    public static bool EvaluateIfRange(HttpRequestMessage request, CacheEntry entry)
    {
        if (!request.Headers.Contains("If-Range"))
            return true;

        // An If-Range that cannot be parsed cannot match
        var ifRange = request.Headers.IfRange;
        if (ifRange is null)
            return false;

        if (ifRange.EntityTag is { } entityTag)
        {
            // The strong comparison: neither tag can be weak
            if (entityTag.IsWeak || entry.ETag is null)
                return false;

            if (!EntityTagHeaderValue.TryParse(entry.ETag, out var storedTag) || storedTag.IsWeak)
                return false;

            return string.Equals(entityTag.Tag, storedTag.Tag, StringComparison.Ordinal);
        }

        // A date matches the Last-Modified of the stored response exactly, when it is a strong validator: RFC 9110
        // Section 8.8.2.2 considers it strong when the response was sent at least one second after it.
        if (ifRange.Date is { } date && entry.LastModified is { } lastModified)
        {
            return date == lastModified && entry.ResponseDate - lastModified >= TimeSpan.FromSeconds(1);
        }

        return false;
    }

    /// <summary>Turns a complete response, whose body is <paramref name="content"/>, into the answer to the ranges of <paramref name="range"/>.</summary>
    /// <remarks>The response is left unchanged when the range must be ignored.</remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The response owns its content, and the multipart content owns its parts")]
    public static void Apply(HttpResponseMessage response, byte[] content, RangeHeaderValue range)
    {
        var ranges = new List<ByteRange>();
        var length = content.LongLength;
        switch (Evaluate(range, length, ranges))
        {
            case RangeEvaluation.Unsatisfiable:
                // RFC 9110 Section 15.5.17: a 416 response carries the length of the representation
                response.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable;
                response.ReasonPhrase = null;
                ReplaceContent(response, new ByteArrayContent([]), copyContentHeaders: false);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(length);
                break;

            case RangeEvaluation.Satisfiable when ranges.Count is 1:
                {
                    var single = ranges[0];
                    response.StatusCode = HttpStatusCode.PartialContent;
                    response.ReasonPhrase = null;
                    ReplaceContent(response, new ByteArrayContent(content, checked((int)single.First), checked((int)single.Length)), copyContentHeaders: true);
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(single.First, single.Last, length);
                    break;
                }

            case RangeEvaluation.Satisfiable:
                {
                    // RFC 9110 Section 14.6: the parts of a multipart/byteranges body carry the content type of the representation.
                    // A content coding applies to the whole representation, so it cannot be split between parts: the complete
                    // response is sent instead.
                    if (response.Content.Headers.ContentEncoding.Count > 0)
                        return;

                    var contentType = response.Content.Headers.ContentType;
                    var multipart = new MultipartContent("byteranges");
                    foreach (var part in ranges)
                    {
                        multipart.Add(CreatePart(content, part, length, contentType));
                    }

                    response.StatusCode = HttpStatusCode.PartialContent;
                    response.ReasonPhrase = null;
                    ReplaceContent(response, multipart, copyContentHeaders: true);
                    break;
                }
        }
    }

    private static ByteArrayContent CreatePart(byte[] content, ByteRange range, long length, MediaTypeHeaderValue? contentType)
    {
        var partContent = new ByteArrayContent(content, checked((int)range.First), checked((int)range.Length));
        partContent.Headers.ContentType = contentType;
        partContent.Headers.ContentRange = new ContentRangeHeaderValue(range.First, range.Last, length);
        return partContent;
    }

    private static void ReplaceContent(HttpResponseMessage response, HttpContent newContent, bool copyContentHeaders)
    {
        var previous = response.Content;
        if (copyContentHeaders)
        {
            foreach (var header in previous.Headers)
            {
                // The length and the type are those of the new content
                if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Content-Range", StringComparison.OrdinalIgnoreCase) ||
                    (newContent is MultipartContent && string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        response.Content = newContent;
        previous.Dispose();
    }
}
