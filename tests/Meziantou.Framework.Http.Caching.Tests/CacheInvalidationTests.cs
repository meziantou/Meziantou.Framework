using System.Net;

namespace Meziantou.Framework.Http.Caching.Tests;

public class CacheInvalidationTests
{
    [Fact]
    public async Task WhenUnsafeMethodSucceedsThenCacheIsInvalidated()
    {
        await using var context = new HttpTestContext();
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
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: original-content
            """);

        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/test", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: post-response
            """);

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              X-Version: 2
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: updated-content
            """);
    }

    [Fact]
    public async Task WhenPutMethodSucceedsThenCacheIsInvalidated()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "before-put", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "put-response");
        context.AddResponse(HttpStatusCode.OK, "after-put", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: before-put
            """);

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/test", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: put-response
            """);

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: after-put
            """);
    }

    [Fact]
    public async Task WhenDeleteMethodSucceedsThenCacheIsInvalidated()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "before-delete", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.NoContent);
        context.AddResponse(HttpStatusCode.OK, "after-delete", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: before-delete
            """);

        await context.SnapshotResponse(HttpMethod.Delete, "http://example.com/test", """
            StatusCode: 204 (NoContent)
            Content:
            """);

        await context.SnapshotResponse("http://example.com/test", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: after-delete
            """);
    }
}
