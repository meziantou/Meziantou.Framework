using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the tests of the media elements: the slow, broken and stalled media resources.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> MediaHandlers = new(StringComparer.Ordinal)
    {
        // html/semantics/embedded-content/media-elements/loading-the-media-resource: a resource that is not a media resource, after 100 ms
        ["/html/semantics/embedded-content/media-elements/loading-the-media-resource/resources/delayed-broken-video.py"] = (_, response, _) =>
        {
            response.Delay = TimeSpan.FromMilliseconds(100);
            response.Set("Content-Type", "text/plain");
            response.Content = "FAIL";
        },

        // the-video-element/timeout_on_seek.py: movie_300 (the extension parameter) served as the range of the Range header
        // (206) or entirely (200), with the Content-Length of the range, but whose bytes from 50% to 95% of the file are never
        // sent: a range that starts before them is sent up to them, then the connection is closed (the body is incomplete), and
        // a range that starts in them gets its headers only
        ["/html/semantics/embedded-content/the-video-element/timeout_on_seek.py"] = (request, response, _) =>
        {
            var extension = request.Get("extension") ?? "";
            var file = ReadWptFile("media/movie_300." + extension);
            var range = request.Header("Range");
            var (start, last) = (0, file.Length - 1);
            if (range is not null && Regex.Match(range, @"bytes=(\d*)-(\d*)") is { Success: true } match)
            {
                start = match.Groups[1].Value.Trim().Length > 0 ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                last = match.Groups[2].Value.Trim().Length > 0 ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : file.Length - 1;
            }

            response.Status = range is not null ? 206 : 200;
            response.Set("Accept-Ranges", "bytes");
            response.Set("Content-Type", "video/" + extension);
            if (range is not null)
            {
                response.Set("Content-Range", string.Create(CultureInfo.InvariantCulture, $"bytes {start}-{last}/{file.Length}"));
            }

            response.Set("Content-Length", (last - start + 1).ToString(CultureInfo.InvariantCulture));
            var gapStart = (int)(file.Length * 0.5);
            var gapLast = (int)(file.Length * 0.95);
            if (gapStart < start && start < gapLast)
            {
                response.Chunks.Add((TimeSpan.Zero, []));
                response.ChunksError = true;
                return;
            }

            var sentLast = start < gapStart ? Math.Min(last, gapStart) : last;
            response.Chunks.Add((TimeSpan.Zero, file[start..(sentLast + 1)]));
            response.ChunksError = sentLast < last;
        },

        // fetch/range/resources/video-with-aged-proxy-cache.py: an audio file served as the range of the Range header (206),
        // a 304 response to a conditional request without Range, or the whole file (200) with the headers of an aged response
        // of a proxy cache (Age of 24 hours, Expires and Last-Modified in the past, an ETag)
        ["/fetch/range/resources/video-with-aged-proxy-cache.py"] = (request, response, _) =>
        {
            var file = File.ReadAllBytes(Path.Combine(WptPaths.Wpt, "media", "sine440.mp3"));
            const string EntityTag = "\"sine440-v1\"";
            response.Set("Content-Type", "audio/mp3");
            if (request.Header("Range") is { } range && Regex.Match(range, @"^bytes=(\d*)-(\d*)$") is { Success: true } match)
            {
                var start = match.Groups[1].Value.Length > 0 ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                var end = match.Groups[2].Value.Length > 0 ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : file.Length;
                response.Status = 206;
                response.Set("Content-Range", string.Create(CultureInfo.InvariantCulture, $"bytes {start}-{end}/{file.Length}"));
                response.Set("Accept-Ranges", "bytes");
                response.Set("ETag", EntityTag);
                end = Math.Min(end + 1, file.Length);
                response.Body = file[start..end];
                return;
            }

            // The handler checks the If-Not-Match header (sic), not If-None-Match
            if (request.Header("If-Modified-Since") is not null || request.Header("If-Not-Match") is not null)
            {
                response.Status = 304;
                return;
            }

            response.Set("Age", "86400");
            response.Set("Expires", "Wed, 21 Oct 2015 07:28:00 GMT");
            response.Set("Last-Modified", "Wed, 21 Oct 2015 07:28:00 GMT");
            response.Set("ETag", EntityTag);
            response.Body = file;
        },

        // fetch/range/resources/video-with-range.py: an audio file served as the range of the Range header (206), or
        // entirely (200); the rewrites parameter replaces the range or the status of the responses of given ranges
        ["/fetch/range/resources/video-with-range.py"] = (request, response, _) =>
        {
            var file = File.ReadAllBytes(Path.Combine(WptPaths.Wpt, "media", "sine440.mp3"));
            var rangeHeader = request.Header("Range");
            string? start = null;
            string? end = null;
            if (rangeHeader is not null && Regex.Match(rangeHeader, @"^bytes=(\d*)-(\d*)$") is { Success: true } match)
            {
                start = match.Groups[1].Value;
                end = match.Groups[2].Value;
            }

            var status = rangeHeader is null ? 200 : 206;
            var responseRangeOverridden = false;
            using var rewrites = JsonDocument.Parse(request.Get("rewrites") ?? "[]");
            foreach (var rewrite in rewrites.RootElement.EnumerateArray())
            {
                var requestRange = rewrite.GetProperty("request");
                var requestStart = requestRange[0].ToString();
                var requestEnd = requestRange[1].ToString();
                if ((start == requestStart || requestStart == "*") && (end == requestEnd || requestEnd == "*"))
                {
                    if (rewrite.TryGetProperty("response", out var responseRange))
                    {
                        start = responseRange[0].ToString();
                        end = responseRange[1].ToString();
                        responseRangeOverridden = true;
                    }

                    if (rewrite.TryGetProperty("status", out var rewrittenStatus))
                    {
                        status = rewrittenStatus.GetInt32();
                    }
                }
            }

            if (status == 200 && !responseRangeOverridden)
            {
                // By default, a server ignoring the range request must return the entire resource
                start = null;
                end = null;
            }

            var first = string.IsNullOrEmpty(start) ? 0 : int.Parse(start, CultureInfo.InvariantCulture);
            var last = string.IsNullOrEmpty(end) ? file.Length : int.Parse(end, CultureInfo.InvariantCulture);
            response.Status = status;
            if (status == 206)
            {
                response.Add("Content-Range", string.Create(CultureInfo.InvariantCulture, $"bytes {first}-{last - 1}/{file.Length}"));
                response.Add("Accept-Ranges", "bytes");
            }

            response.Add("Content-Type", "audio/mp3");
            response.Add("Content-Length", (last - first).ToString(CultureInfo.InvariantCulture));
            response.Add("Cache-Control", "no-cache");

            // file.seek(start); file.read(end): at most "end" bytes from the start
            first = Math.Clamp(first, 0, file.Length);
            response.Body = file[first..Math.Min(file.Length, first + Math.Max(last, 0))];
        },

        // html/semantics/embedded-content/media-elements/track/track-element/cors: a WebVTT file (or a redirect) that records
        // the Origin header and the cookie of each request in the stash
        ["/html/semantics/embedded-content/media-elements/track/track-element/cors/support/cors-tester.py"] = (request, response, stash) =>
        {
            if (request.Method != "GET" || request.Get("id") is not { } id)
            {
                response.Status = 400;
                return;
            }

            if (request.Has("read"))
            {
                if (stash.Take(id) is not { } log)
                {
                    response.Status = 404;
                    response.Content = "Tried to read data not yet set";
                    return;
                }

                response.Set("Content-Type", "text/plain");
                response.Content = log;
                return;
            }

            if (request.Has("cleanup"))
            {
                stash.Take(id);
                response.Content = "OK";
                return;
            }

            if (request.Has("delete-cookie"))
            {
                response.Add("Set-Cookie", PythonDeleteCookie(id));
                response.Set("Content-Type", "text/plain");
                response.Content = "OK";
                return;
            }

            if (request.Get("origin") is { } origin)
            {
                response.Set("Access-Control-Allow-Origin", origin);
                response.Set("Access-Control-Allow-Credentials", "true");
            }

            var cookie = ParseRequestCookies(request).FirstOrDefault(cookie => cookie.Key == id).Value ?? "no";
            var line = "cors = " + (request.Header("Origin") ?? "no") + " | cookie = " + cookie;
            if (stash.Take(id) is { } data)
            {
                line = data + "\n" + line;
            }

            stash.Put(id, line);
            if (request.Get("redirect") is { } redirect)
            {
                response.Status = 302;
                response.Set("Location", redirect);
            }
            else
            {
                response.Content = "WEBVTT\n\n00:00:00.000 --> 00:00:10.000\nTest";
            }
        },

        // css/selectors/media: a video whose first 4096 bytes are sent at once (not enough to play), and the rest when a POST
        // request with the same key resumes it (the key stays in the stash for 5 seconds); the handler gives up after 30 seconds
        ["/css/selectors/media/support/stall-resume.py"] = (request, response, stash) =>
        {
            const int FirstSize = 4096;
            var key = request.Get("key") ?? "";
            if (request.Method == "POST")
            {
                if (request.PollingElapsed < TimeSpan.FromSeconds(5))
                {
                    stash.Put(key, "resume");
                    response.RetryAfter = TimeSpan.FromSeconds(5) - request.PollingElapsed;
                    return;
                }

                stash.Take(key);
                response.Content = "put " + key + " into stash";
                return;
            }

            var file = File.ReadAllBytes(Path.Combine(WptPaths.Wpt, "media", "movie_300.webm"));
            response.Set("Content-Type", "video/webm");
            response.Set("Content-Length", file.Length.ToString(CultureInfo.InvariantCulture));
            response.Body = file[..FirstSize];
            response.Chunks.Add((TimeSpan.Zero, file[FirstSize..]));
            response.ChunksCondition = () => stash.Contains(key);
            response.ChunksTimeout = TimeSpan.FromSeconds(30);
        },
    };
}
