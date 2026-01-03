using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class CacheInvalidationTests
{
    [Fact]
    public async Task WhenUnsafeMethodSucceedsThenCacheIsInvalidated()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original-content", ("Cache-Control", "max-age=600"), ("X-Version", "1"));
        context.AddResponse(HttpStatusCode.OK, "post-response");
        context.AddResponse(HttpStatusCode.OK, "updated-content", ("Cache-Control", "max-age=600"), ("X-Version", "2"));

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              X-Version: 1
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 16
              Value: original-content
            """);

        var request2 = new HttpRequestMessage(HttpMethod.Post, "http://example.com/test");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: post-response
            """);

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              X-Version: 2
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: updated-content
            """);
    }

    [Fact]
    public async Task WhenPutMethodSucceedsThenCacheIsInvalidated()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "before-put", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "put-response");
        context.AddResponse(HttpStatusCode.OK, "after-put", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 10
              Value: before-put
            """);

        var request2 = new HttpRequestMessage(HttpMethod.Put, "http://example.com/test");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: put-response
            """);

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: after-put
            """);
    }

    [Fact]
    public async Task WhenDeleteMethodSucceedsThenCacheIsInvalidated()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "before-delete", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.NoContent);
        context.AddResponse(HttpStatusCode.OK, "after-delete", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: before-delete
            """);

        var request2 = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/test");
        await context.SnapshotResponse(request2, """
            StatusCode: 204 (NoContent)
            Content:
              Headers:
                Content-Length: 0
              Value:
            """);

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: after-delete
            """);
    }
}
