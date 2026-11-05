using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>clear-site-data</c> tests (the <c>Clear-Site-Data</c> header) and of the <c>gpc</c> tests (the <c>Sec-GPC</c> header).</summary>
internal static partial class WptPythonHandlers
{
    /// <summary>The document of <c>echo-clear-site-data.py</c>: it reports to its top-level window whether each datatype is empty.</summary>
    private const string EchoClearSiteDataContent = """

        <!DOCTYPE html>
        <html>
          <head>
            <title>Clear-Site-Data</title>
            <script src="test_utils.sub.js"></script>
          </head>
          <body>
            <script>
              var report = {};

              Promise.all(TestUtils.DATATYPES.map(function(datatype) {
                return datatype.isEmpty().then(function(isEmpty) {
                  report[datatype.name] = isEmpty;
                });
              })).then(function() {
                window.top.postMessage(report, "*");
              });
            </script>
          </body>
        </html>

        """;

    private static readonly Dictionary<string, Handler> ClearSiteDataHandlers = new(StringComparer.Ordinal)
    {
        // echo-clear-site-data.py: a document with the Clear-Site-Data header of the datatypes of the query, which reports the
        // empty datatypes to its top-level window
        ["/clear-site-data/support/echo-clear-site-data.py"] = (request, response, _) =>
        {
            response.Add("Clear-Site-Data", string.Join(',', request.GetParameters.Select(parameter => "\"" + parameter.Key + "\"")));
            response.Add("Content-Type", "text/html");
            response.Content = EchoClearSiteDataContent;
        },

        // clear-site-data-cookie.py: a document with a Set-Cookie header before or after the Clear-Site-Data header, which posts
        // its cookies to its opener
        ["/clear-site-data/support/clear-site-data-cookie.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            var clearSiteData = "\"" + request.Get("target", "*") + "\"";
            if (request.Get("location") == "after")
            {
                response.Add("Clear-Site-Data", clearSiteData);
                response.Add("Set-Cookie", "testSetWithClear=true");
            }
            else
            {
                response.Add("Set-Cookie", "testSetWithClear=true");
                response.Add("Clear-Site-Data", clearSiteData);
            }

            response.Content = "\n        <script>\n            window.opener.postMessage(document.cookie , \"*\");\n        </script>";
        },

        // controlled-endpoint.py: the response of the network, which the service worker of storage.https.html replaces
        ["/clear-site-data/support/controlled-endpoint.py"] = (_, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Content = "FROM_NETWORK";
        },

        // getGPC.py: a document, a script, a worker or an image that checks the Sec-GPC header of its request
        ["/gpc/support/getGPC.py"] = GetGpc,
    };

    private static void GetGpc(WptHandlerRequest request, WptHandlerResponse response, WptStash _)
    {
        var destination = request.Header("sec-fetch-dest") ?? "";
        var gpc = request.Header("sec-gpc") == "1" ? "true" : "false";
        var expected = request.Get("gpc") == "true" ? "true" : "false";
        var inFrame = request.Has("framed");
        var description = inFrame ? "framed " + destination : destination;
        var assertion = "assert_equals(" + gpc + ", " + expected + ", \"Expected Sec-GPC value (" + expected + ") is on the " + description + " fetch\");";
        var testName = "`Expected Sec-GPC value (" + expected + ") is on the " + description + " fetch`";
        var url = "getGPC.py?gpc=" + expected;
        switch (destination)
        {
            case "document" or "iframe":
                {
                    response.Set("Content-Type", "text/html");
                    var framedImage = destination == "iframe" || inFrame ? "framed " : "";
                    var content = "\n<!DOCTYPE html>\n<html>\n<title>Sec-GPC " + destination + "</title>\n<head>\n  <script src=\"/resources/testharness.js\"></script>\n</head>\n<body>\n  <div id=\"log\"></div>\n  <img id=\"imageTest\">\n  <script>\n"
                        + "    test(function(t) {\n      " + assertion + "\n    }, " + testName + ");\n"
                        + "    promise_test(function(t) {\n      const image = document.getElementById(\"imageTest\");\n      const testResult = new Promise((resolve, reject) => {\n        image.addEventListener('load', resolve);\n        image.addEventListener('error', reject);\n      });\n"
                        + "      image.src = \"" + url + "\";\n      return testResult;\n    }, `Expected Sec-GPC value (" + expected + ") is on the " + framedImage + "image fetch`);\n  </script>\n";
                    if (destination == "document")
                    {
                        content += "\n  <script>\n    const iframe = document.createElement(\"iframe\");\n    iframe.src = \"" + url + "\";\n    document.body.appendChild(iframe);\n    async function run() {\n      await Promise.all([\n"
                            + "        fetch_tests_from_window(iframe.contentWindow),\n        fetch_tests_from_worker(new Worker(\"" + url + "\")),\n        fetch_tests_from_worker(new SharedWorker(\"" + url + "\")),\n      ]);\n"
                            + "      let r = await navigator.serviceWorker.register(\n        \"" + url + "\",\n        {scope: \"./blank.html\"});\n      let sw = r.active || r.installing || r.waiting;\n      await fetch_tests_from_worker(sw);\n      await r.unregister();\n    }\n    run();\n  </script>\n  ";
                    }

                    response.Content = content + "\n  <script src=\"" + url + (destination == "iframe" || inFrame ? "&framed" : "") + "\"></script>\n</body>\n</html>\n";
                    break;
                }

            case "image":
                if (gpc == expected)
                {
                    response.Add("Content-Type", "image/png");
                    response.Body = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEUlEQVR42mP8nzaTAQQYYQwALssD/5ca+r8AAAAASUVORK5CYII=");
                }
                else
                {
                    response.Status = 400;
                }

                break;

            case "script":
                response.Set("Content-Type", "application/javascript");
                response.Content = "\ndebugger;\ntest(function(t) {\n  " + assertion + "\n}, " + testName + ");\n";
                break;

            case "worker" or "sharedworker" or "serviceworker":
                response.Set("Content-Type", "application/javascript");
                response.Content = "\nimportScripts(\"/resources/testharness.js\");\ntest(function(t) {\n  " + assertion + "\n}, " + testName + ");\ndone();\n";
                break;

            default:
                throw new NotSupportedException("Unexpected destination: " + destination);
        }
    }

    /// <summary>A black RGB PNG image (<c>generate_png</c> of <c>clear-site-data-cache.py</c>).</summary>
    private static byte[] CreateBlackPng(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        WritePngChunk(png, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (width * 3)];
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }

        WritePngChunk(png, "IDAT", compressed.ToArray());
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WritePngChunk(MemoryStream png, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        png.Write(buffer);
        var typeAndData = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
        {
            typeAndData[i] = (byte)type[i];
        }

        data.CopyTo(typeAndData, 4);
        png.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32(typeAndData));
        png.Write(buffer);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
