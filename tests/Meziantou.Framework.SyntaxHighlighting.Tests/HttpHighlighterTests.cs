namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HttpHighlighterTests
{

    [Fact]
    public void RequestLine_GetSimple()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_GetWithPath()
    {
        AssertHighlighter("http",
"""
GET /api/users/42 HTTP/1.1
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/api/users/42</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_GetWithQuery()
    {
        AssertHighlighter("http",
"""
GET /search?q=hello&page=2 HTTP/1.1
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/search?q=hello&amp;page=2</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_Post()
    {
        AssertHighlighter("http",
"""
POST /api/users HTTP/1.1
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/api/users</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_Put()
    {
        AssertHighlighter("http",
"""
PUT /api/users/42 HTTP/1.1
""",
"""
<span class="hljs-keyword">PUT</span> <span class="hljs-string">/api/users/42</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_Delete()
    {
        AssertHighlighter("http",
"""
DELETE /api/users/42 HTTP/1.1
""",
"""
<span class="hljs-keyword">DELETE</span> <span class="hljs-string">/api/users/42</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_Patch()
    {
        AssertHighlighter("http",
"""
PATCH /api/users/42 HTTP/1.1
""",
"""
<span class="hljs-keyword">PATCH</span> <span class="hljs-string">/api/users/42</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_Head()
    {
        AssertHighlighter("http",
"""
HEAD /api/users HTTP/1.1
""",
"""
<span class="hljs-keyword">HEAD</span> <span class="hljs-string">/api/users</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_Options()
    {
        AssertHighlighter("http",
"""
OPTIONS /api/users HTTP/1.1
""",
"""
<span class="hljs-keyword">OPTIONS</span> <span class="hljs-string">/api/users</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_TraceMethod()
    {
        AssertHighlighter("http",
"""
TRACE / HTTP/1.1
""",
"""
<span class="hljs-keyword">TRACE</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_Connect()
    {
        AssertHighlighter("http",
"""
CONNECT example.com:443 HTTP/1.1
""",
"""
<span class="hljs-keyword">CONNECT</span> <span class="hljs-string">example.com:443</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_AbsoluteUri()
    {
        AssertHighlighter("http",
"""
GET https://example.com/api/users HTTP/1.1
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">https://example.com/api/users</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_AsteriskOptions()
    {
        AssertHighlighter("http",
"""
OPTIONS * HTTP/1.1
""",
"""
<span class="hljs-keyword">OPTIONS</span> <span class="hljs-string">*</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void RequestLine_Http10()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.0
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.0</span>
""");
    }

    [Fact]
    public void RequestLine_Http2()
    {
        AssertHighlighter("http",
"""
GET / HTTP/2
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/2</span>
""");
    }

    [Fact]
    public void RequestLine_Http3()
    {
        AssertHighlighter("http",
"""
GET / HTTP/3
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/3</span>
""");
    }

    [Fact]
    public void StatusLine_Ok()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
""");
    }

    [Fact]
    public void StatusLine_Created()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 201 Created
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">201</span> Created
""");
    }

    [Fact]
    public void StatusLine_Accepted()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 202 Accepted
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">202</span> Accepted
""");
    }

    [Fact]
    public void StatusLine_NoContent()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 204 No Content
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">204</span> No Content
""");
    }

    [Fact]
    public void StatusLine_PartialContent()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 206 Partial Content
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">206</span> Partial Content
""");
    }

    [Fact]
    public void StatusLine_MovedPermanently()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 301 Moved Permanently
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">301</span> Moved Permanently
""");
    }

    [Fact]
    public void StatusLine_Found()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 302 Found
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">302</span> Found
""");
    }

    [Fact]
    public void StatusLine_NotModified()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 304 Not Modified
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">304</span> Not Modified
""");
    }

    [Fact]
    public void StatusLine_TemporaryRedirect()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 307 Temporary Redirect
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">307</span> Temporary Redirect
""");
    }

    [Fact]
    public void StatusLine_PermanentRedirect()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 308 Permanent Redirect
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">308</span> Permanent Redirect
""");
    }

    [Fact]
    public void StatusLine_BadRequest()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 400 Bad Request
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">400</span> Bad Request
""");
    }

    [Fact]
    public void StatusLine_Unauthorized()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 401 Unauthorized
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">401</span> Unauthorized
""");
    }

    [Fact]
    public void StatusLine_Forbidden()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 403 Forbidden
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">403</span> Forbidden
""");
    }

    [Fact]
    public void StatusLine_NotFound()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 404 Not Found
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">404</span> Not Found
""");
    }

    [Fact]
    public void StatusLine_MethodNotAllowed()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 405 Method Not Allowed
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">405</span> Method Not Allowed
""");
    }

    [Fact]
    public void StatusLine_NotAcceptable()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 406 Not Acceptable
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">406</span> Not Acceptable
""");
    }

    [Fact]
    public void StatusLine_Conflict()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 409 Conflict
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">409</span> Conflict
""");
    }

    [Fact]
    public void StatusLine_Gone()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 410 Gone
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">410</span> Gone
""");
    }

    [Fact]
    public void StatusLine_PreconditionFailed()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 412 Precondition Failed
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">412</span> Precondition Failed
""");
    }

    [Fact]
    public void StatusLine_PayloadTooLarge()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 413 Payload Too Large
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">413</span> Payload Too Large
""");
    }

    [Fact]
    public void StatusLine_UnprocessableEntity()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 422 Unprocessable Entity
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">422</span> Unprocessable Entity
""");
    }

    [Fact]
    public void StatusLine_TooManyRequests()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 429 Too Many Requests
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">429</span> Too Many Requests
""");
    }

    [Fact]
    public void StatusLine_InternalServerError()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 500 Internal Server Error
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">500</span> Internal Server Error
""");
    }

    [Fact]
    public void StatusLine_BadGateway()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 502 Bad Gateway
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">502</span> Bad Gateway
""");
    }

    [Fact]
    public void StatusLine_ServiceUnavailable()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 503 Service Unavailable
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">503</span> Service Unavailable
""");
    }

    [Fact]
    public void StatusLine_GatewayTimeout()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 504 Gateway Timeout
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">504</span> Gateway Timeout
""");
    }

    [Fact]
    public void StatusLine_SwitchingProtocols()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 101 Switching Protocols
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">101</span> Switching Protocols
""");
    }

    [Fact]
    public void StatusLine_EarlyHints()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 103 Early Hints
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">103</span> Early Hints
""");
    }

    [Fact]
    public void Header_Host()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Host: example.com
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>example.com
""");
    }

    [Fact]
    public void Header_UserAgent()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">User-Agent</span><span class="hljs-punctuation">: </span>Mozilla/5.0 (Windows NT 10.0; Win64; x64)
""");
    }

    [Fact]
    public void Header_Accept()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Accept: text/html,application/xhtml+xml,application/xml;q=0.9
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Accept</span><span class="hljs-punctuation">: </span>text/html,application/xhtml+xml,application/xml;q=0.9
""");
    }

    [Fact]
    public void Header_AcceptLanguage()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Accept-Language: en-US,en;q=0.9,fr;q=0.5
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Accept-Language</span><span class="hljs-punctuation">: </span>en-US,en;q=0.9,fr;q=0.5
""");
    }

    [Fact]
    public void Header_AcceptEncoding()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Accept-Encoding: gzip, deflate, br
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Accept-Encoding</span><span class="hljs-punctuation">: </span>gzip, deflate, br
""");
    }

    [Fact]
    public void Header_Connection()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Connection: keep-alive
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Connection</span><span class="hljs-punctuation">: </span>keep-alive
""");
    }

    [Fact]
    public void Header_CacheControl()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Cache-Control: no-cache, no-store, must-revalidate
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Cache-Control</span><span class="hljs-punctuation">: </span>no-cache, no-store, must-revalidate
""");
    }

    [Fact]
    public void Header_Pragma()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Pragma: no-cache
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Pragma</span><span class="hljs-punctuation">: </span>no-cache
""");
    }

    [Fact]
    public void Header_AuthorizationBearer()
    {
        AssertHighlighter("http",
"""
GET /api HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.abc123
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/api</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Authorization</span><span class="hljs-punctuation">: </span>Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.abc123
""");
    }

    [Fact]
    public void Header_AuthorizationBasic()
    {
        AssertHighlighter("http",
"""
GET /api HTTP/1.1
Authorization: Basic dXNlcjpwYXNzd29yZA==
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/api</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Authorization</span><span class="hljs-punctuation">: </span>Basic dXNlcjpwYXNzd29yZA==
""");
    }

    [Fact]
    public void Header_ContentType()
    {
        AssertHighlighter("http",
"""
POST /api HTTP/1.1
Content-Type: application/json
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/api</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>application/json
""");
    }

    [Fact]
    public void Header_ContentLength()
    {
        AssertHighlighter("http",
"""
POST /api HTTP/1.1
Content-Length: 42
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/api</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>42
""");
    }

    [Fact]
    public void Header_Cookie()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Cookie: sessionid=abc123; csrftoken=xyz789
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Cookie</span><span class="hljs-punctuation">: </span>sessionid=abc123; csrftoken=xyz789
""");
    }

    [Fact]
    public void Header_SetCookie()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Set-Cookie: sessionid=abc123; Path=/; HttpOnly; Secure; SameSite=Lax
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Set-Cookie</span><span class="hljs-punctuation">: </span>sessionid=abc123; Path=/; HttpOnly; Secure; SameSite=Lax
""");
    }

    [Fact]
    public void Header_Location()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 302 Found
Location: https://example.com/login
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">302</span> Found
<span class="hljs-attribute">Location</span><span class="hljs-punctuation">: </span>https://example.com/login
""");
    }

    [Fact]
    public void Header_ETag()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
ETag: "abc-123-def"
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">ETag</span><span class="hljs-punctuation">: </span>&quot;abc-123-def&quot;
""");
    }

    [Fact]
    public void Header_LastModified()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Last-Modified: Wed, 21 Oct 2026 07:28:00 GMT
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Last-Modified</span><span class="hljs-punctuation">: </span>Wed, 21 Oct 2026 07:28:00 GMT
""");
    }

    [Fact]
    public void Header_ExpiresHeader()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Expires: Thu, 01 Dec 2026 16:00:00 GMT
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Expires</span><span class="hljs-punctuation">: </span>Thu, 01 Dec 2026 16:00:00 GMT
""");
    }

    [Fact]
    public void Header_Referer()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Referer: https://example.com/previous-page
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Referer</span><span class="hljs-punctuation">: </span>https://example.com/previous-page
""");
    }

    [Fact]
    public void Header_XForwardedFor()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
X-Forwarded-For: 203.0.113.10, 70.41.3.18
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">X-Forwarded-For</span><span class="hljs-punctuation">: </span>203.0.113.10, 70.41.3.18
""");
    }

    [Fact]
    public void Header_XCustomHeader()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
X-Request-ID: 11111111-2222-3333-4444-555555555555
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">X-Request-ID</span><span class="hljs-punctuation">: </span>11111111-2222-3333-4444-555555555555
""");
    }

    [Fact]
    public void Security_Hsts()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Strict-Transport-Security: max-age=31536000; includeSubDomains; preload
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Strict-Transport-Security</span><span class="hljs-punctuation">: </span>max-age=31536000; includeSubDomains; preload
""");
    }

    [Fact]
    public void Security_Csp()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; img-src *
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Content-Security-Policy</span><span class="hljs-punctuation">: </span>default-src &#x27;self&#x27;; script-src &#x27;self&#x27; &#x27;unsafe-inline&#x27;; img-src *
""");
    }

    [Fact]
    public void Security_XFrameOptions()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
X-Frame-Options: DENY
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">X-Frame-Options</span><span class="hljs-punctuation">: </span>DENY
""");
    }

    [Fact]
    public void Security_XContentTypeOptions()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
X-Content-Type-Options: nosniff
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">X-Content-Type-Options</span><span class="hljs-punctuation">: </span>nosniff
""");
    }

    [Fact]
    public void Security_ReferrerPolicy()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Referrer-Policy: strict-origin-when-cross-origin
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Referrer-Policy</span><span class="hljs-punctuation">: </span>strict-origin-when-cross-origin
""");
    }

    [Fact]
    public void Security_PermissionsPolicy()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Permissions-Policy: geolocation=(), camera=(self), microphone=()
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Permissions-Policy</span><span class="hljs-punctuation">: </span>geolocation=(), camera=(self), microphone=()
""");
    }

    [Fact]
    public void Cors_AllowOrigin()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Access-Control-Allow-Origin: https://app.example.com
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Access-Control-Allow-Origin</span><span class="hljs-punctuation">: </span>https://app.example.com
""");
    }

    [Fact]
    public void Cors_AllowMethods()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 204 No Content
Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">204</span> No Content
<span class="hljs-attribute">Access-Control-Allow-Methods</span><span class="hljs-punctuation">: </span>GET, POST, PUT, DELETE, OPTIONS
""");
    }

    [Fact]
    public void Cors_AllowHeaders()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 204 No Content
Access-Control-Allow-Headers: Content-Type, Authorization, X-Request-ID
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">204</span> No Content
<span class="hljs-attribute">Access-Control-Allow-Headers</span><span class="hljs-punctuation">: </span>Content-Type, Authorization, X-Request-ID
""");
    }

    [Fact]
    public void Cors_AllowCredentials()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 204 No Content
Access-Control-Allow-Credentials: true
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">204</span> No Content
<span class="hljs-attribute">Access-Control-Allow-Credentials</span><span class="hljs-punctuation">: </span>true
""");
    }

    [Fact]
    public void Cors_ExposeHeaders()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Access-Control-Expose-Headers: X-Total-Count, X-Page
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Access-Control-Expose-Headers</span><span class="hljs-punctuation">: </span>X-Total-Count, X-Page
""");
    }

    [Fact]
    public void Cors_PreflightRequest()
    {
        AssertHighlighter("http",
"""
OPTIONS /api/users HTTP/1.1
Host: api.example.com
Origin: https://app.example.com
Access-Control-Request-Method: POST
Access-Control-Request-Headers: Content-Type, Authorization
""",
"""
<span class="hljs-keyword">OPTIONS</span> <span class="hljs-string">/api/users</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>api.example.com
<span class="hljs-attribute">Origin</span><span class="hljs-punctuation">: </span>https://app.example.com
<span class="hljs-attribute">Access-Control-Request-Method</span><span class="hljs-punctuation">: </span>POST
<span class="hljs-attribute">Access-Control-Request-Headers</span><span class="hljs-punctuation">: </span>Content-Type, Authorization
""");
    }

    [Fact]
    public void Conditional_IfMatch()
    {
        AssertHighlighter("http",
"""
PUT /api/resource HTTP/1.1
If-Match: "abc-123"
""",
"""
<span class="hljs-keyword">PUT</span> <span class="hljs-string">/api/resource</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">If-Match</span><span class="hljs-punctuation">: </span>&quot;abc-123&quot;
""");
    }

    [Fact]
    public void Conditional_IfNoneMatch()
    {
        AssertHighlighter("http",
"""
GET /api/resource HTTP/1.1
If-None-Match: "abc-123"
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/api/resource</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">If-None-Match</span><span class="hljs-punctuation">: </span>&quot;abc-123&quot;
""");
    }

    [Fact]
    public void Conditional_IfModifiedSince()
    {
        AssertHighlighter("http",
"""
GET /api/resource HTTP/1.1
If-Modified-Since: Wed, 21 Oct 2026 07:28:00 GMT
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/api/resource</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">If-Modified-Since</span><span class="hljs-punctuation">: </span>Wed, 21 Oct 2026 07:28:00 GMT
""");
    }

    [Fact]
    public void Conditional_IfUnmodifiedSince()
    {
        AssertHighlighter("http",
"""
PUT /api/resource HTTP/1.1
If-Unmodified-Since: Wed, 21 Oct 2026 07:28:00 GMT
""",
"""
<span class="hljs-keyword">PUT</span> <span class="hljs-string">/api/resource</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">If-Unmodified-Since</span><span class="hljs-punctuation">: </span>Wed, 21 Oct 2026 07:28:00 GMT
""");
    }

    [Fact]
    public void RangeRequest_BytesRange()
    {
        AssertHighlighter("http",
"""
GET /video.mp4 HTTP/1.1
Range: bytes=0-1023
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/video.mp4</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Range</span><span class="hljs-punctuation">: </span>bytes=0-1023
""");
    }

    [Fact]
    public void RangeRequest_BytesMultiRange()
    {
        AssertHighlighter("http",
"""
GET /file HTTP/1.1
Range: bytes=0-499, 500-999, -100
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/file</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Range</span><span class="hljs-punctuation">: </span>bytes=0-499, 500-999, -100
""");
    }

    [Fact]
    public void RangeRequest_ContentRange()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 206 Partial Content
Content-Range: bytes 0-1023/146515
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">206</span> Partial Content
<span class="hljs-attribute">Content-Range</span><span class="hljs-punctuation">: </span>bytes 0-1023/146515
""");
    }

    [Fact]
    public void RangeRequest_AcceptRanges()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Accept-Ranges: bytes
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Accept-Ranges</span><span class="hljs-punctuation">: </span>bytes
""");
    }

    [Fact]
    public void ChunkedAndCompression_TransferEncoding()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Transfer-Encoding: chunked
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Transfer-Encoding</span><span class="hljs-punctuation">: </span>chunked
""");
    }

    [Fact]
    public void ChunkedAndCompression_ContentEncoding()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Content-Encoding: gzip
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Content-Encoding</span><span class="hljs-punctuation">: </span>gzip
""");
    }

    [Fact]
    public void ChunkedAndCompression_VaryHeader()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Vary: Accept-Encoding, Accept-Language
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Vary</span><span class="hljs-punctuation">: </span>Accept-Encoding, Accept-Language
""");
    }

    [Fact]
    public void Body_JsonRequest()
    {
        AssertHighlighter("http",
"""
POST /api/users HTTP/1.1
Host: api.example.com
Content-Type: application/json
Content-Length: 50

{"name":"alice","email":"alice@example.com"}
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/api/users</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>api.example.com
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>application/json
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>50

<span class="language-json"><span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;name&quot;</span><span class="hljs-punctuation">:</span><span class="hljs-string">&quot;alice&quot;</span><span class="hljs-punctuation">,</span><span class="hljs-attr">&quot;email&quot;</span><span class="hljs-punctuation">:</span><span class="hljs-string">&quot;alice@example.com&quot;</span><span class="hljs-punctuation">}</span></span>
""");
    }

    [Fact]
    public void Body_JsonResponse()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Content-Type: application/json
Content-Length: 38

{"id":1,"name":"alice","active":true}
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>application/json
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>38

<span class="language-json"><span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;id&quot;</span><span class="hljs-punctuation">:</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span><span class="hljs-attr">&quot;name&quot;</span><span class="hljs-punctuation">:</span><span class="hljs-string">&quot;alice&quot;</span><span class="hljs-punctuation">,</span><span class="hljs-attr">&quot;active&quot;</span><span class="hljs-punctuation">:</span><span class="hljs-literal"><span class="hljs-keyword">true</span></span><span class="hljs-punctuation">}</span></span>
""");
    }

    [Fact]
    public void Body_FormUrlEncoded()
    {
        AssertHighlighter("http",
"""
POST /login HTTP/1.1
Content-Type: application/x-www-form-urlencoded
Content-Length: 27

username=alice&password=hunter2
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/login</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>application/x-www-form-urlencoded
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>27

<span class="language-urlencoded"><span class="hljs-attr">username</span><span class="hljs-punctuation">=</span><span class="hljs-string">alice</span><span class="hljs-punctuation">&amp;</span><span class="hljs-attr">password</span><span class="hljs-punctuation">=</span><span class="hljs-string">hunter2</span></span>
""");
    }

    [Fact]
    public void Body_XmlBody()
    {
        AssertHighlighter("http",
"""
POST /api HTTP/1.1
Content-Type: application/xml
Content-Length: 64

<?xml version="1.0"?>
<user><name>alice</name></user>
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/api</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>application/xml
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>64

<span class="language-xml"><span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">user</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">name</span>&gt;</span>alice<span class="hljs-tag">&lt;/<span class="hljs-name">name</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">user</span>&gt;</span></span>
""");
    }

    [Fact]
    public void Body_PlainText()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
Content-Type: text/plain; charset=utf-8
Content-Length: 13

Hello, world!
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>text/plain; charset=utf-8
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>13

Hello, world!
""");
    }

    [Fact]
    public void Body_MultipartFormBody()
    {
        AssertHighlighter("http",
"""
POST /upload HTTP/1.1
Host: example.com
Content-Type: multipart/form-data; boundary=----WebKitFormBoundary
Content-Length: 142

------WebKitFormBoundary
Content-Disposition: form-data; name="file"; filename="hi.txt"
Content-Type: text/plain

hello
------WebKitFormBoundary--
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/upload</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>example.com
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>multipart/form-data; boundary=----WebKitFormBoundary
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>142

------WebKitFormBoundary
Content-Disposition: form-data; name=&quot;file&quot;; filename=&quot;hi.txt&quot;
Content-Type: text/plain

hello
------WebKitFormBoundary--
""");
    }

    [Fact]
    public void Body_EmptyBody()
    {
        AssertHighlighter("http",
"""
POST /ping HTTP/1.1
Host: example.com
Content-Length: 0


""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/ping</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>example.com
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>0


""");
    }

    [Fact]
    public void ProtocolUpgrade_WebSocketRequest()
    {
        AssertHighlighter("http",
"""
GET /chat HTTP/1.1
Host: example.com
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==
Sec-WebSocket-Version: 13
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/chat</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>example.com
<span class="hljs-attribute">Upgrade</span><span class="hljs-punctuation">: </span>websocket
<span class="hljs-attribute">Connection</span><span class="hljs-punctuation">: </span>Upgrade
<span class="hljs-attribute">Sec-WebSocket-Key</span><span class="hljs-punctuation">: </span>dGhlIHNhbXBsZSBub25jZQ==
<span class="hljs-attribute">Sec-WebSocket-Version</span><span class="hljs-punctuation">: </span>13
""");
    }

    [Fact]
    public void ProtocolUpgrade_WebSocketResponse()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 101 Switching Protocols
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">101</span> Switching Protocols
<span class="hljs-attribute">Upgrade</span><span class="hljs-punctuation">: </span>websocket
<span class="hljs-attribute">Connection</span><span class="hljs-punctuation">: </span>Upgrade
<span class="hljs-attribute">Sec-WebSocket-Accept</span><span class="hljs-punctuation">: </span>s3pPLMBiTxaQ9kYGzzhZRbK+xOo=
""");
    }

    [Fact]
    public void ProtocolUpgrade_H2cUpgrade()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Host: example.com
Connection: Upgrade, HTTP2-Settings
Upgrade: h2c
HTTP2-Settings: AAMAAABkAARAAAAAAAIAAAAA
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>example.com
<span class="hljs-attribute">Connection</span><span class="hljs-punctuation">: </span>Upgrade, HTTP2-Settings
<span class="hljs-attribute">Upgrade</span><span class="hljs-punctuation">: </span>h2c
<span class="hljs-attribute">HTTP2-Settings</span><span class="hljs-punctuation">: </span>AAMAAABkAARAAAAAAAIAAAAA
""");
    }

    [Fact]
    public void Composite_FullJsonExchange()
    {
        AssertHighlighter("http",
"""
POST /api/users HTTP/1.1
Host: api.example.com
User-Agent: curl/8.0
Accept: application/json
Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig
Content-Type: application/json
Content-Length: 50

{"name":"alice","email":"alice@example.com"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: https://api.example.com/users/42
Content-Length: 58

{"id":42,"name":"alice","email":"alice@example.com"}
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/api/users</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>api.example.com
<span class="hljs-attribute">User-Agent</span><span class="hljs-punctuation">: </span>curl/8.0
<span class="hljs-attribute">Accept</span><span class="hljs-punctuation">: </span>application/json
<span class="hljs-attribute">Authorization</span><span class="hljs-punctuation">: </span>Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>application/json
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>50

<span class="language-json">{&quot;name&quot;:&quot;alice&quot;,&quot;email&quot;:&quot;alice@example.com&quot;}

HTTP/1.1 201 Created
Content-Type: application/json
Location: https://api.example.com/users/42
Content-Length: 58

{&quot;id&quot;:42,&quot;name&quot;:&quot;alice&quot;,&quot;email&quot;:&quot;alice@example.com&quot;}</span>
""");
    }

    [Fact]
    public void Composite_RedirectChain()
    {
        AssertHighlighter("http",
"""
GET /old HTTP/1.1
Host: example.com

HTTP/1.1 301 Moved Permanently
Location: https://example.com/new
Content-Length: 0


""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/old</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>example.com

HTTP/1.1 301 Moved Permanently
Location: https://example.com/new
Content-Length: 0


""");
    }

    [Fact]
    public void Composite_CachedResponse()
    {
        AssertHighlighter("http",
"""
GET /assets/app.css HTTP/1.1
Host: cdn.example.com
If-None-Match: "abc-123"

HTTP/1.1 304 Not Modified
ETag: "abc-123"
Cache-Control: public, max-age=31536000, immutable
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/assets/app.css</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>cdn.example.com
<span class="hljs-attribute">If-None-Match</span><span class="hljs-punctuation">: </span>&quot;abc-123&quot;

HTTP/1.1 304 Not Modified
ETag: &quot;abc-123&quot;
Cache-Control: public, max-age=31536000, immutable
""");
    }

    [Fact]
    public void Composite_OAuthDance()
    {
        AssertHighlighter("http",
"""
POST /oauth/token HTTP/1.1
Host: auth.example.com
Content-Type: application/x-www-form-urlencoded
Content-Length: 78

grant_type=authorization_code&code=abc&redirect_uri=https://app.example.com/cb
""",
"""
<span class="hljs-keyword">POST</span> <span class="hljs-string">/oauth/token</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>auth.example.com
<span class="hljs-attribute">Content-Type</span><span class="hljs-punctuation">: </span>application/x-www-form-urlencoded
<span class="hljs-attribute">Content-Length</span><span class="hljs-punctuation">: </span>78

<span class="language-urlencoded"><span class="hljs-attr">grant_type</span><span class="hljs-punctuation">=</span><span class="hljs-string">authorization_code</span><span class="hljs-punctuation">&amp;</span><span class="hljs-attr">code</span><span class="hljs-punctuation">=</span><span class="hljs-string">abc</span><span class="hljs-punctuation">&amp;</span><span class="hljs-attr">redirect_uri</span><span class="hljs-punctuation">=</span><span class="hljs-string">https://app.example.com/cb</span></span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("http",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_StatusOnly()
    {
        AssertHighlighter("http",
"""
HTTP/1.1 200 OK
""",
"""
<span class="hljs-meta">HTTP/1.1</span> <span class="hljs-number">200</span> OK
""");
    }

    [Fact]
    public void SpecialEdge_RequestLineOnly()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
""");
    }

    [Fact]
    public void SpecialEdge_TrailingBlankLine()
    {
        AssertHighlighter("http",
"""
GET / HTTP/1.1
Host: example.com


""",
"""
<span class="hljs-keyword">GET</span> <span class="hljs-string">/</span> <span class="hljs-meta">HTTP/1.1</span>
<span class="hljs-attribute">Host</span><span class="hljs-punctuation">: </span>example.com


""");
    }
}
