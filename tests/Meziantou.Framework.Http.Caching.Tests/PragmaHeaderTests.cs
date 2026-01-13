using System.Net;

namespace Meziantou.Framework.Http.Caching.Tests;

public class PragmaHeaderTests
{
    [Fact]
    public async Task WhenRequestHasPragmaNoCacheWithoutCacheControlThenValidationIsRequired()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "default-content", ("Cache-Control", "max-age=600"), ("ETag", "\"abc\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              ETag: "abc"
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: default-content
            """);
        
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
        request2.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
              ETag: "abc"
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: default-content
            """);
    }

    [Fact]
    public async Task WhenRequestHasBothCacheControlAndPragmaThenCacheControlTakesPrecedence()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "default-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: default-content
            """);
        
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
        request2.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        request2.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: default-content
            """);
    }
}
