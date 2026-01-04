using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

/// <summary>Comprehensive tests for RFC 7234 cache invalidation on unsafe methods (Section 4.4).</summary>
public sealed class UnsafeMethodInvalidationTests
{
    [Fact]
    public async Task WhenPostSucceedsThenInvalidatesTargetUri()
    {
        await using var context = new HttpTestContext();
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
                Content-Length: 18
                Content-Type: text/plain; charset=utf-8
              Value: cached-before-post
            """);

        // POST invalidates cache
        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/resource", """
            StatusCode: 201 (Created)
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: post-result
            """);

        // Subsequent GET must fetch fresh
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 18
                Content-Type: text/plain; charset=utf-8
              Value: fetched-after-post
            """);
    }

    [Fact]
    public async Task WhenPutSucceedsThenInvalidatesTargetUri()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "put-result");
        context.AddResponse(HttpStatusCode.OK, "updated", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: put-result
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: updated
            """);
    }

    [Fact]
    public async Task WhenDeleteSucceedsThenInvalidatesTargetUri()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "exists", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.NoContent);
        context.AddResponse(HttpStatusCode.NotFound, "deleted", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: exists
            """);

        await context.SnapshotResponse(HttpMethod.Delete, "http://example.com/resource", """
            StatusCode: 204 (NoContent)
            Content:
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 404 (NotFound)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: deleted
            """);
    }

    [Fact]
    public async Task WhenPatchSucceedsThenInvalidatesTargetUri()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "before-patch", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "patch-result");
        context.AddResponse(HttpStatusCode.OK, "after-patch", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: before-patch
            """);

        await context.SnapshotResponse(HttpMethod.Patch, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: patch-result
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: after-patch
            """);
    }

    [Fact]
    public async Task WhenPostWithLocationHeaderThenInvalidatesBothUris()
    {
        await using var context = new HttpTestContext();
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
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: cached-target
            """);

        await context.SnapshotResponse("http://example.com/new-resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: cached-location
            """);

        // POST with Location header should invalidate both
        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/collection", """
            StatusCode: 201 (Created)
            Headers:
              Location: http://example.com/new-resource
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: created
            """);

        // Both should be invalidated
        await context.SnapshotResponse("http://example.com/collection", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: target-refreshed
            """);

        await context.SnapshotResponse("http://example.com/new-resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 18
                Content-Type: text/plain; charset=utf-8
              Value: location-refreshed
            """);
    }

    [Fact]
    public async Task WhenPostWithContentLocationHeaderThenInvalidatesBothUris()
    {
        await using var context = new HttpTestContext();
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
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: cached-target
            """);

        await context.SnapshotResponse("http://example.com/content-resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 18
                Content-Type: text/plain; charset=utf-8
              Value: cached-content-loc
            """);

        // POST with Content-Location should invalidate both
        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/action", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 7
                Content-Location: http://example.com/content-resource
                Content-Type: text/plain; charset=utf-8
              Value: created
            """);

        // Both should be invalidated
        await context.SnapshotResponse("http://example.com/action", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: target-refreshed
            """);

        await context.SnapshotResponse("http://example.com/content-resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: content-refreshed
            """);
    }

    [Fact]
    public async Task WhenUnsafeMethodFailsThenDoesNotInvalidate()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.BadRequest, "bad-request");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/resource", """
            StatusCode: 400 (BadRequest)
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: bad-request
            """);

        // Cache should still be valid
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenUnsafeMethodReturns5xxThenDoesNotInvalidate()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.InternalServerError, "error");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/resource", """
            StatusCode: 500 (InternalServerError)
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: error
            """);

        // Cache should still be valid
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenMultipleUrisNeedInvalidationThenAllAreInvalidated()
    {
        await using var context = new HttpTestContext();
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
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: target-cached
            """);

        await context.SnapshotResponse("http://example.com/location", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: location-cached
            """);

        await context.SnapshotResponse("http://example.com/content", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: content-cached
            """);

        // POST with both Location and Content-Location
        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/target", """
            StatusCode: 201 (Created)
            Headers:
              Location: http://example.com/location
            Content:
              Headers:
                Content-Length: 6
                Content-Location: http://example.com/content
                Content-Type: text/plain; charset=utf-8
              Value: result
            """);

        // All three should be invalidated
        await context.SnapshotResponse("http://example.com/target", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: target-new
            """);

        await context.SnapshotResponse("http://example.com/location", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: location-new
            """);

        await context.SnapshotResponse("http://example.com/content", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: content-new
            """);
    }
}
