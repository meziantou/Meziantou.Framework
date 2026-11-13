using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the Signature-based SRI tests (<c>subresource-integrity/signatures</c>).</summary>
internal static partial class WptPythonHandlers
{
    private const string SignatureRfcKeyInput = "keyid=\"JrQLj5P/89iXES9+vFgrIy29clF9CC/oPPsw3c5D0bs=\"; tag=\"sri\"";

    private static readonly Dictionary<string, Handler> SignaturesHandlers = new(StringComparer.Ordinal)
    {
        // resource.py: a response whose Unencoded-Digest, Signature, Signature-Input and Content-Type headers, status and body
        // are the ones of the query, which reflects the Accept-Signature header of the request
        ["/subresource-integrity/signatures/resource.py"] = (request, response, _) =>
        {
            if (request.Get("digest") is { Length: > 0 } digest)
            {
                response.Set("unencoded-digest", digest);
            }

            if (request.Get("signature") is { Length: > 0 } signature)
            {
                response.Set("signature", signature);
            }

            if (request.Get("signatureInput") is { Length: > 0 } signatureInput)
            {
                response.Set("signature-input", signatureInput);
            }

            if (request.Get("cors") is { Length: > 0 })
            {
                response.Set("access-control-allow-origin", "*");
            }

            response.Set("content-type", request.Get("type") ?? "text/plain");
            response.Set("accept-signature", request.Header("accept-signature") ?? "");
            response.Set("access-control-expose-headers", "accept-signature");
            response.Status = int.Parse(request.Get("status") ?? "200", CultureInfo.InvariantCulture);
            response.Body = Encoding.Latin1.GetBytes(request.Get("body") ?? "");
        },

        // query-resource.py: {"hello": "world"} signed with the @query of the request
        ["/subresource-integrity/signatures/query-resource.py"] = (request, response, _) =>
        {
            response.Body = "{\"hello\": \"world\"}"u8.ToArray();
            response.Set("content-type", "application/json");
            response.Set("access-control-allow-origin", "*");
            response.Set("unencoded-digest", "sha-256=:X48E9qOokqqrvdts8nOJRJN3OWDUoyWxBf7kbu9DBPE=:");
            response.Set("signature-input", "signature=(\"unencoded-digest\";sf \"@query\";req); " + SignatureRfcKeyInput);
            var signature = request.Get("test") switch
            {
                null => "nddn9dgy3sPXvxoBcV5jxwCgejS3FOIuzXRmQ05V321MnArHDd1BydFm5Na3Q0gUNQFsBJus4+x8+VkTIjlFBA==",
                "" => "F4fftMcvtwmghk6n3J86MpYhDT9fPHInd69SXdKb8SB6cWQJMfaKxM0FmO1fJa/pnB2ThO/Sp077+vLURnDEBw==",
                "a" => "QYLmBgierby9E6Q9ZG92jT28+AF73cFHVgBeX/05hqFIt+MG8niYq3G3YWgxPigS7O1i2Vbxu7eQU1JYxht0Dw==",
                "/" => "6RY+KehKbGFhE9RItvGuYvs4uOTD7hTE23w5O/oTsvdM6kuU1gD7Y3x3KIjCxTjiFij+8xlFAyYYLsv3LaQSDg==",
                "Ã¼" => "TMyMXXE6Pw0wBzeuBpaEOr9RI3WaZLNB7Rhu1euibMqTcp35y7JM3bWZICb7keSGZBvWbidbxrWfWFSsw+J0CA==",
                var other => other,
            };
            response.Set("signature", "signature=:" + signature + ":");
        },

        // accept-signature-script.py: a script that reports whether the Accept-Signature header of the request is the one of
        // the header parameter
        ["/subresource-integrity/signatures/accept-signature-script.py"] = (request, response, _) =>
        {
            var matched = (request.Header("accept-signature") ?? "") == (request.Get("header") ?? "");
            response.Status = 200;
            response.Set("content-type", "application/json");
            response.Set("access-control-allow-origin", "*");
            response.Set("signature-input", "signature=(\"unencoded-digest\";sf); " + SignatureRfcKeyInput);
            response.Body = Encoding.ASCII.GetBytes("document.currentScript.setAttribute(`matched`, " + (matched ? "true" : "false") + ");");
            response.Set("unencoded-digest", "sha-256=:" + (matched ? "dq6r7uJehA7JvZk7hczA4TM0uQ5Ad9WkKKihnuQ+B3c=" : "es+3YnsBqgi4mkbDZd3Vghz6PsqpNeg5CEJn7WOKzJI=") + ":");
            response.Set("signature", "signature=:" + (matched ? "93PZphf5q5GJ0esZxDk/RJTG5WcExWsRAYSPgXdiQDQVyOH33qgwi0nvon9kQj7jdtoLg7uEOceGv/DBTAbRDQ==" : "y91SB5QNcqsZBd0XOnuf83W1FOgTWYOP+0gJZ+Lj3JahopKDedZDne9LsJ1KmV4JnjpF8LF5jJzbOO5snLidAg==") + ":");
        },
    };
}
