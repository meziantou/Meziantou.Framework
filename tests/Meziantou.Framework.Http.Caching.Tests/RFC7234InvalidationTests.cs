using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

/// <summary>
/// Comprehensive tests for RFC 7234 cache invalidation on unsafe methods (Section 4.4).
/// </summary>
public sealed class UnsafeMethodInvalidationTests
{
    [Fact]
    public async Task WhenPostSucceedsThenInvalidatesTargetUri()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-before-post", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.Created, "post-result");
        context.AddResponse(HttpStatusCode.OK, "fetched-after-post", ("Cache-Control", "max-age=3600"));

        // Cache GET response
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 18
              Value: cached-before-post
            """);

        // POST invalidates cache
        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/resource", """
            StatusCode: 201 (Created)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: post-result
            """);

        // Subsequent GET must fetch fresh
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 19
              Value: fetched-after-post
            """);
    }

    [Fact]
    public async Task WhenPutSucceedsThenInvalidatesTargetUri()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "put-result");
        context.AddResponse(HttpStatusCode.OK, "updated", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 10
              Value: put-result
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: updated
            """);
    }

    [Fact]
    public async Task WhenDeleteSucceedsThenInvalidatesTargetUri()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "exists", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.NoContent);
        context.AddResponse(HttpStatusCode.NotFound, "deleted", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: exists
            """);

        await context.SnapshotResponse(HttpMethod.Delete, "http://example.com/resource", """
            StatusCode: 204 (No Content)
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 404 (Not Found)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: deleted
            """);
    }

    [Fact]
    public async Task WhenPatchSucceedsThenInvalidatesTargetUri()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "before-patch", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "patch-result");
        context.AddResponse(HttpStatusCode.OK, "after-patch", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: before-patch
            """);

        await context.SnapshotResponse(HttpMethod.Patch, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: patch-result
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: after-patch
            """);
    }

    [Fact]
    public async Task WhenPostWithLocationHeaderThenInvalidatesBothUris()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-target", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "cached-location", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.Created, "created", ("Location", "http://example.com/new-resource"));
        context.AddResponse(HttpStatusCode.OK, "target-refreshed", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "location-refreshed", ("Cache-Control", "max-age=3600"));

        // Cache both URIs
        await context.SnapshotResponse("http://example.com/collection", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: cached-target
            """);

        await context.SnapshotResponse("http://example.com/new-resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: cached-location
            """);

        // POST with Location header should invalidate both
        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/collection", """
            StatusCode: 201 (Created)
            Headers:
              Location: http://example.com/new-resource
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: created
            """);

        // Both should be invalidated
        await context.SnapshotResponse("http://example.com/collection", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 17
              Value: target-refreshed
            """);

        await context.SnapshotResponse("http://example.com/new-resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 19
              Value: location-refreshed
            """);
    }

    [Fact]
    public async Task WhenPostWithContentLocationHeaderThenInvalidatesBothUris()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-target", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "cached-content-loc", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "created", ("Content-Location", "http://example.com/content-resource"));
        context.AddResponse(HttpStatusCode.OK, "target-refreshed", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "content-refreshed", ("Cache-Control", "max-age=3600"));

        // Cache both URIs
        await context.SnapshotResponse("http://example.com/action", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: cached-target
            """);

        await context.SnapshotResponse("http://example.com/content-resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 18
              Value: cached-content-loc
            """);

        // POST with Content-Location should invalidate both
        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/action", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Location: http://example.com/content-resource
                Content-Length: 7
              Value: created
            """);

        // Both should be invalidated
        await context.SnapshotResponse("http://example.com/action", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 17
              Value: target-refreshed
            """);

        await context.SnapshotResponse("http://example.com/content-resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 17
              Value: content-refreshed
            """);
    }

    [Fact]
    public async Task WhenUnsafeMethodFailsThenDoesNotInvalidate()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.BadRequest, "bad-request");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);

        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/resource", """
            StatusCode: 400 (Bad Request)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: bad-request
            """);

        // Cache should still be valid
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenUnsafeMethodReturns5xxThenDoesNotInvalidate()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.InternalServerError, "error");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/resource", """
            StatusCode: 500 (Internal Server Error)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: error
            """);

        // Cache should still be valid
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenMultipleUrisNeedInvalidationThenAllAreInvalidated()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "target-cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "location-cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "content-cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.Created, "result", 
            ("Location", "http://example.com/location"),
            ("Content-Location", "http://example.com/content"));
        context.AddResponse(HttpStatusCode.OK, "target-new", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "location-new", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "content-new", ("Cache-Control", "max-age=3600"));

        // Cache all three URIs
        await context.SnapshotResponse("http://example.com/target", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: target-cached
            """);

        await context.SnapshotResponse("http://example.com/location", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: location-cached
            """);

        await context.SnapshotResponse("http://example.com/content", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: content-cached
            """);

        // POST with both Location and Content-Location
        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/target", """
            StatusCode: 201 (Created)
            Headers:
              Location: http://example.com/location
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Location: http://example.com/content
                Content-Length: 6
              Value: result
            """);

        // All three should be invalidated
        await context.SnapshotResponse("http://example.com/target", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 10
              Value: target-new
            """);

        await context.SnapshotResponse("http://example.com/location", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: location-new
            """);

        await context.SnapshotResponse("http://example.com/content", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: content-new
            """);
    }
}
