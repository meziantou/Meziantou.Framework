using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>element-timing</c>, <c>largest-contentful-paint</c> and <c>paint-timing</c> tests.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> PaintTimingHandlers = new(StringComparer.Ordinal)
    {
        // An image of 100x100 with the Timing-Allow-Origin headers of the tao parameter
        ["/element-timing/resources/TAOImage.py"] = (request, response, _) =>
        {
            var origin = request.Get("origin");
            if (!string.IsNullOrEmpty(origin))
            {
                response.Set("Access-Control-Allow-Origin", origin);
            }

            switch (request.Get("tao"))
            {
                case "wildcard":
                    response.Set("Timing-Allow-Origin", "*");
                    break;
                case "null":
                    response.Set("Timing-Allow-Origin", "null");
                    break;
                case "origin":
                    response.Set("Timing-Allow-Origin", origin);
                    break;
                case "space":
                    response.Set("Timing-Allow-Origin", origin + " *");
                    break;
                case "multi":
                    response.Set("Timing-Allow-Origin", origin);
                    response.Add("Timing-Allow-Origin", "*");
                    break;
                case "multi_wildcard":
                    response.Set("Timing-Allow-Origin", "*");
                    response.Add("Timing-Allow-Origin", "*");
                    break;
                case "match_origin":
                    response.Set("Timing-Allow-Origin", origin);
                    response.Add("Timing-Allow-Origin", "fake");
                    break;
                case "match_wildcard":
                    response.Set("Timing-Allow-Origin", "fake");
                    response.Add("Timing-Allow-Origin", "*");
                    break;
                case "uppercase":
                    response.Set("Timing-Allow-Origin", origin?.ToUpperInvariant());
                    break;
            }

            response.Set("Cache-Control", "no-cache, must-revalidate");
            response.Body = ReadWptFile("element-timing/resources/square100.png");
        },

        // An image of the resources directory, delayed by the sleep parameter (milliseconds)
        ["/element-timing/resources/slow-image.py"] = (request, response, _) =>
        {
            response.Delay = Milliseconds(request.Get("sleep"), 0);
            response.Set("Content-Type", "image");
            response.Set("Cache-control", "no-cache, must-revalidate");
            response.Body = ReadWptFile("element-timing/resources/" + request.Get("name"));
        },

        // An image of the resources directory whose first numInitial bytes are sent first, then the others after the sleep
        // parameter (milliseconds): an image is available once its whole body is received
        ["/element-timing/resources/progressive-image.py"] = (request, response, _) =>
        {
            var body = ReadWptFile("element-timing/resources/" + request.Get("name"));
            var numInitial = Math.Clamp(int.Parse(request.Get("numInitial") ?? "0", CultureInfo.InvariantCulture), 0, body.Length);
            response.Set("Content-Type", "image");
            response.Set("Cache-control", "no-cache, must-revalidate");
            response.Body = body[..numInitial];
            response.Chunks.Add((Milliseconds(request.Get("sleep"), 0), body[numInitial..]));
        },

        // A chain of redirect_count redirects to final_resource, with the Timing-Allow-Origin header tao{i} and the origin{i} of each step
        ["/element-timing/resources/multiple-redirects.py"] = (request, response, _) =>
        {
            var step = int.TryParse(request.Get("step"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 1;
            var redirectCount = int.Parse(request.Get("redirect_count") ?? "1", CultureInfo.InvariantCulture);
            var finalResource = request.Get("final_resource") ?? "";
            var location = request.Get("origin" + step.ToString(CultureInfo.InvariantCulture)) ?? "";
            if (step == redirectCount)
            {
                location += finalResource;
            }
            else
            {
                location += "/element-timing/resources/multiple-redirects.py?redirect_count=" + redirectCount.ToString(CultureInfo.InvariantCulture) + "&final_resource=" + finalResource;
                for (var i = 1; i <= redirectCount; i++)
                {
                    var index = i.ToString(CultureInfo.InvariantCulture);
                    if (request.Get("tao" + index) is { } tao)
                    {
                        location += "&tao" + index + "=" + tao;
                    }

                    if (request.Get("origin" + index) is { } origin)
                    {
                        location += "&origin" + index + "=" + origin;
                    }
                }

                location += "&step=" + (step + 1).ToString(CultureInfo.InvariantCulture);
            }

            if (request.Get("tao" + step.ToString(CultureInfo.InvariantCulture)) is { Length: > 0 } taoValue)
            {
                response.Set("timing-allow-origin", taoValue);
            }

            response.Status = 302;
            response.Set("Location", location);
        },
    };
}
