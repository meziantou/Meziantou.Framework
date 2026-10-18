using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>html/cross-origin-opener-policy</c>, <c>html/cross-origin-embedder-policy</c> and <c>html/document-isolation-policy</c> tests.</summary>
internal static partial class WptPythonHandlers
{
    private const string CorpImage = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABAQMAAAAl21bKAAAAA1BMVEUAAACnej3aAAAAAXRSTlMAQObYZgAAAApJREFUCNdjYAAAAAIAAeIhvDMAAAAASUVORK5CYII=";

    private static readonly Dictionary<string, Handler> CrossOriginIsolationHandlers = new(StringComparer.Ordinal)
    {
        ["/html/cross-origin-opener-policy/resources/coop-coep.py"] = CoopCoep,

        // A redirect to the query of the URL
        ["/html/cross-origin-opener-policy/resources/redirect.py"] = (request, response, _) =>
        {
            response.Status = 302;
            response.Set("Location", request.Query);
        },

        // A document sandboxed by its CSP, which opens a popup to coop-coep.py with the same parameters (the handler sets
        // the headers even when the parameters are empty: it compares byte strings with strings)
        ["/html/cross-origin-opener-policy/resources/csp-sandbox.py"] = (request, response, _) =>
        {
            response.Set("Cross-Origin-Opener-Policy", request.Get("coop") ?? "");
            response.Set("Cross-Origin-Embedder-Policy", request.Get("coep") ?? "");
            response.Set("Content-Security-Policy", "sandbox " + request.Get("sandbox") + ";");
            response.Content = """

                <!doctype html>
                <meta charset=utf-8>
                <script src="/common/get-host-info.sub.js"></script>
                <script src="/html/cross-origin-opener-policy/resources/fully-loaded.js"></script>
                <script>
                  const params = new URL(location).searchParams;
                  params.delete("sandbox");
                  const navigate = params.get("navigate");
                  if (navigate) {
                    fullyLoaded().then(() => {
                      self.location = navigate;
                    });
                  } else {
                    window.open(`/html/cross-origin-opener-policy/resources/coop-coep.py?${params}`);
                  }
                </script>

                """;
        },

        // A 1x1 PNG image, with the Cross-Origin-Resource-Policy header when corp-cross-origin is set
        ["/html/cross-origin-embedder-policy/resources/corp-image.py"] = (request, response, _) =>
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Methods", "OPTIONS, GET, POST");
            response.Set("Access-Control-Allow-Headers", "Content-Type");
            if (request.Method == "OPTIONS")
                return;

            response.Set("Cache-Control", request.Get("revalidate") == "true" ? "max-age=0, must-revalidate" : "max-age=3600");
            if (request.Header("If-None-Match") == "some-etag")
            {
                response.Status = 304;
                return;
            }

            if (!string.IsNullOrEmpty(request.Get("corp-cross-origin")))
            {
                response.Set("Cross-Origin-Resource-Policy", "cross-origin");
            }

            response.Set("Etag", "some-etag");
            Respond(response, "image/png", Convert.FromBase64String(CorpImage));
        },

        // An empty document with a Cross-Origin-Embedder-Policy header per value parameter
        ["/html/cross-origin-embedder-policy/resources/empty-coep.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            foreach (var (name, value) in request.GetParameters)
            {
                if (name == "value")
                {
                    response.Add("Cross-Origin-Embedder-Policy", value);
                }
            }
        },

        // The collector of the coep reports: a POST request appends the reports of its body to the key (or to the UUID of the
        // endpoint parameter), a GET request takes them (as a JSON array)
        ["/html/cross-origin-embedder-policy/resources/report.py"] = (request, response, stash) =>
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Methods", "OPTIONS, GET, POST");
            response.Set("Access-Control-Allow-Headers", "Content-Type");
            response.Set("Cache-Control", "no-cache, no-store, must-revalidate");
            if (request.Method == "OPTIONS")
                return;

            var key = request.Get("endpoint") switch
            {
                "endpoint" => "01234567-0123-0123-0123-0123456789AB",
                "report-only-endpoint" => "01234567-0123-0123-0123-0123456789CD",
                _ => null,
            };
            key = request.Get("key") ?? key;
            if (key is null)
            {
                response.Status = 400;
                response.Content = "invalid endpoint";
                return;
            }

            var path = request.Path[..(request.Path.LastIndexOf('/', StringComparison.Ordinal) + 1)];
            switch (request.Method)
            {
                case "POST":
                    lock (stash)
                    {
                        var reports = stash.TakeValue(key, path) as System.Text.Json.Nodes.JsonArray ?? [];
                        if (System.Text.Json.Nodes.JsonNode.Parse(request.Body) is System.Text.Json.Nodes.JsonArray newReports)
                        {
                            foreach (var report in newReports.ToArray())
                            {
                                newReports.Remove(report);
                                reports.Add(report);
                            }
                        }

                        stash.Put(key, reports, path);
                    }

                    response.Content = "done";
                    return;
                case "GET":
                    response.Set("Content-Type", "application/json");
                    response.Content = (stash.TakeValue(key, path) as System.Text.Json.Nodes.JsonArray)?.ToJsonString() ?? "[]";
                    return;
                default:
                    response.Status = 400;
                    response.Content = "invalid method";
                    return;
            }
        },

        // A shared worker that fetches the URLs it receives, with a Cross-Origin-Embedder-Policy header per value parameter
        ["/html/cross-origin-embedder-policy/resources/shared-worker-fetch.js.py"] = (request, response, _) => SharedWorkerFetch(request, response, "Cross-Origin-Embedder-Policy"),
        ["/html/document-isolation-policy/resources/shared-worker-fetch.js.py"] = (request, response, _) => SharedWorkerFetch(request, response, "Document-Isolation-Policy"),

        // A redirect to a response with the Cross-Origin-Embedder-Policy (and Cross-Origin-Resource-Policy) header
        ["/html/cross-origin-embedder-policy/credentialless/resources/redirect_none_to_credentialless.py"] = (request, response, _) =>
        {
            response.Status = 302;
            response.Set("Location", request.Get("redirectTo") + "&pipe=|header(cross-origin-embedder-policy, credentialless)|header(cross-origin-resource-policy, cross-origin)");
        },
        ["/html/document-isolation-policy/resources/redirect_none_to_credentialless.py"] = (request, response, _) =>
        {
            response.Status = 302;
            response.Set("Location", request.Get("redirectTo") + "&pipe=|header(document-isolation-policy, isolate-and-credentialless)");
        },
    };

    /// <summary>html/cross-origin-opener-policy/resources/coop-coep.py: a document with the given opener and embedder policies, which reports its opener to the test.</summary>
    // html/cross-origin-embedder-policy/resources/shared-worker-fetch.js.py (and its document-isolation-policy copy)
    private static void SharedWorkerFetch(WptHandlerRequest request, WptHandlerResponse response, string policyHeader)
    {
        response.Set("Content-Type", "text/javascript");
        foreach (var (name, value) in request.GetParameters)
        {
            if (name == "value")
            {
                response.Add(policyHeader, value);
            }
        }

        response.Content = """

            'use strict';

            onconnect = (event) => {
              const port = event.ports[0];

              port.onmessage = (event) => {
                fetch(event.data, { mode: 'no-cors' })
                  .then(
                    () => port.postMessage('success'),
                    () => port.postMessage('failure')
                  );
              };

              port.postMessage('ready');
            };
            """;
    }

    private static void CoopCoep(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        string? Get(string name)
        {
            return request.Method == "POST" ? GetPost(request, name) : request.Get(name);
        }

        var coop = Get("coop") ?? "";
        var coep = Get("coep") ?? "";
        if (coop.Length > 0)
        {
            response.Set("Cross-Origin-Opener-Policy", coop);
        }

        if (Get("coop-report-only") is { } coopReportOnly)
        {
            response.Set("Cross-Origin-Opener-Policy-Report-Only", coopReportOnly);
        }

        if (coep.Length > 0)
        {
            response.Set("Cross-Origin-Embedder-Policy", coep);
        }

        if (Get("coep-report-only") is { } coepReportOnly)
        {
            response.Set("Cross-Origin-Embedder-Policy-Report-Only", coepReportOnly);
        }

        if (Get("cache") is not null)
        {
            response.Set("Cache-Control", "max-age=3600");
        }

        if (Get("redirect") is { } redirect)
        {
            response.Status = 302;
            response.Set("Location", redirect);
            return;
        }

        // The parameters visible to the script of the response (json.dumps)
        var parameters = new StringBuilder("{");
        foreach (var key in new[] { "navHistory", "avoidBackAndForth", "navigate", "channel", "responseToken", "iframeToken" })
        {
            if (parameters.Length > 1)
            {
                parameters.Append(", ");
            }

            var value = Get(key);
            parameters.Append(JsonSerializer.Serialize(key)).Append(": ").Append(string.IsNullOrEmpty(value) ? (value is null ? "null" : "\"\"") : JsonSerializer.Serialize(DecodeUtf8(value)));
        }

        parameters.Append('}');
        response.Content = """

            <!doctype html>
            <meta charset=utf-8>
            <script src="/common/get-host-info.sub.js"></script>
            <script src="/html/cross-origin-opener-policy/resources/fully-loaded.js"></script>
            <body>
            <script>
              const params =
            """ + parameters + """
            ;
              const navHistory = params.navHistory;
              const avoidBackAndForth = params.avoidBackAndForth;
              const navigate = params.navigate;
              if (navHistory !== null) {
                fullyLoaded().then(() => {
                  history.go(Number(navHistory));
                });
              } else if (navigate !== null && (history.length === 1 || !avoidBackAndForth)) {
                fullyLoaded().then(() => {
                  self.location = navigate;
                });
              } else {
                let openerDOMAccessAllowed = false;
                try {
                  openerDOMAccessAllowed = !!self.opener.document.URL;
                } catch(ex) {
                }
                // Handle the response from the frame, closing the popup once the
                // test completes.
                addEventListener("message", event => {
                  if (event.data == "close") {
                    close();
                  }
                });
                iframe = document.createElement("iframe");
                iframe.onload = () => {
                  const payload = { name: self.name, opener: !!self.opener, openerDOMAccess: openerDOMAccessAllowed };
                  iframe.contentWindow.postMessage(payload, "*");
                };
                const channelName = params.channel;
                const responseToken = params.responseToken;
                const iframeToken = params.iframeToken;
                iframe.src = `${get_host_info().HTTPS_ORIGIN}/html/cross-origin-opener-policy/resources/postback.html` +
                             `?channel=${encodeURIComponent(channelName)}` +
                             `&responseToken=${responseToken}` +
                             `&iframeToken=${iframeToken}`;
                document.body.appendChild(iframe);
              }
            </script>
            </body>

            """;
    }
}
