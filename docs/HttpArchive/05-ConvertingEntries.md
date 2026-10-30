---
title: Converting entries
description: Turn archived entries into HttpRequestMessage and HttpResponseMessage, and get the bytes of a body.
---

[ToHttpRequestMessage](cref:Meziantou.Framework.HttpArchive.HarEntryExtensions.ToHttpRequestMessage) and [ToHttpResponseMessage](cref:Meziantou.Framework.HttpArchive.HarEntryExtensions.ToHttpResponseMessage) rebuild the messages of an archived entry, so you can replay a
request with `HttpClient`, or serve a recorded response from a test handler:

<!-- ref:../../samples/Meziantou.Framework.Http.Samples/HttpArchiveExamples.cs;region=HarEntry_ConvertToHttpMessages;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HarDocument.Parse(File.ReadAllText("traffic.har"));
var entry = document.Log!.Entries![0];

// Throws InvalidOperationException when the archived entry has no request/response,
// or when the request declares no method or URL.
using var request = entry.ToHttpRequestMessage();
using var response = entry.ToHttpResponseMessage();
```
<!-- endref -->

Both methods exist on [HarEntry](cref:Meziantou.Framework.HttpArchive.HarEntry), and on [HarRequest](cref:Meziantou.Framework.HttpArchive.HarRequest) and [HarResponse](cref:Meziantou.Framework.HttpArchive.HarResponse) directly. The messages are new objects: dispose
of them as usual.

## Requests

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarEntryExtensionsTests.cs;region=HarEntry_ConvertRequest;format=md-fenced-code-block;dedent=true -->
```csharp
var entry = new HarEntry
{
    Request = new HarRequest
    {
        Method = "POST",
        Url = "https://example.com/api/items",
        HttpVersion = "HTTP/2",
        Headers =
        [
            new HarHeader { Name = "Accept", Value = "application/json" },
            new HarHeader { Name = "Content-Length", Value = "999" },
        ],
        PostData = new HarPostData { MimeType = "application/json", Text = """{"name":"pen"}""" },
    },
};

using var request = entry.ToHttpRequestMessage();

Assert.Equal(HttpMethod.Post, request.Method);
Assert.Equal(new Uri("https://example.com/api/items"), request.RequestUri);
Assert.Equal(new Version(2, 0), request.Version);
Assert.Equal(["application/json"], request.Headers.GetValues("Accept"));

// The body comes from postData; its MIME type becomes the Content-Type header
Assert.Equal("""{"name":"pen"}""", await request.Content!.ReadAsStringAsync());
Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);

// The recorded Content-Length is not replayed: it is computed from the body
Assert.Equal(14, request.Content.Headers.ContentLength);
```
<!-- endref -->

- The method, the URL and the HTTP version come from the request. `HTTP/1.0`, `HTTP/1.1`, `HTTP/2`, `HTTP/3` and their
  variants (`http/2.0`, `h2`, `h3`, ...) map to the matching `Version`; any other value gives HTTP/1.1.
- The headers are copied. Entity headers, such as `Content-Type`, go to the content.
- `Content-Length` and `Content-Encoding` are not copied: they describe the bytes as they were sent, and the content
  holds the decoded body. The recorded values stay available in the archive.
- When the archive has no `Content-Type` header, the MIME type of the body is used.
- When the archive lists cookies but has no `Cookie` header, a `Cookie` header is built from the list.

### Bodies

The body comes from `PostData.Text`, as UTF-8. When the archive stored a form as [Params](cref:Meziantou.Framework.HttpArchive.HarPostData.Params) instead, an
`application/x-www-form-urlencoded` body is rebuilt from them:

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarEntryExtensionsTests.cs;region=HarRequest_FormParams;format=md-fenced-code-block;dedent=true -->
```csharp
var harRequest = new HarRequest
{
    Method = "POST",
    Url = "https://example.com/login",
    PostData = new HarPostData
    {
        MimeType = "application/x-www-form-urlencoded",
        Params =
        [
            new HarPostDataParameter { Name = "user", Value = "someone%40example.com" },
            new HarPostDataParameter { Name = "remember", Value = "on" },
        ],
    },
};

using var request = harRequest.ToHttpRequestMessage();

// The parameters are joined as recorded: they are already percent-encoded
Assert.Equal("user=someone%40example.com&remember=on", await request.Content!.ReadAsStringAsync());
```
<!-- endref -->

- [Text](cref:Meziantou.Framework.HttpArchive.HarPostData.Text) wins when the archive has both.
- A multipart body cannot be rebuilt from its parameters, because the archive does not hold the original boundary: it
  converts to an empty body.
- A request without [PostData](cref:Meziantou.Framework.HttpArchive.HarRequest.PostData) has no content, unless the archive recorded entity headers for it; it then gets an
  empty content that holds them.
- [ToHttpRequestMessage](cref:Meziantou.Framework.HttpArchive.HarEntryExtensions.ToHttpRequestMessage) does not decode a body stored as base64: call [TryGetRawData](cref:Meziantou.Framework.HttpArchive.HarPostDataExtensions.TryGetRawData) to get its bytes (see below).

## Responses

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarEntryExtensionsTests.cs;region=HarEntry_ConvertResponse;format=md-fenced-code-block;dedent=true -->
```csharp
var entry = new HarEntry
{
    Response = new HarResponse
    {
        Status = 200,
        StatusText = "OK",
        HttpVersion = "HTTP/1.1",
        Headers = [new HarHeader { Name = "Cache-Control", Value = "max-age=60" }],
        Cookies = [new HarCookie { Name = "theme", Value = "dark", Path = "/" }],
        Content = new HarContent
        {
            MimeType = "image/png",
            Encoding = "base64",
            Text = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]),
        },
    },
};

using var response = entry.ToHttpResponseMessage();

Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.Equal("max-age=60", response.Headers.CacheControl!.ToString());
Assert.Equal(["theme=dark; Path=/"], response.Headers.GetValues("Set-Cookie"));

// The base64 text is decoded
Assert.Equal([0x89, 0x50, 0x4E, 0x47], await response.Content.ReadAsByteArrayAsync());
Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
```
<!-- endref -->

- The status code, the reason phrase, the HTTP version and the headers are copied, with the same rules as for
  requests.
- When the archive lists cookies but has no `Set-Cookie` header, one `Set-Cookie` header is built for each cookie,
  with its path, domain, expiration, [HttpOnly](cref:Meziantou.Framework.HttpArchive.HarCookie.HttpOnly) and [Secure](cref:Meziantou.Framework.HttpArchive.HarCookie.Secure) attributes.
- The body comes from `Content.Text`. A body whose [Encoding](cref:Meziantou.Framework.HttpArchive.HarContent.Encoding) is `base64` is decoded; when the text is not valid
  base64, it is used as UTF-8 text instead. A response without text has an empty content.

## Get the bytes of a body

[TryGetRawData](cref:Meziantou.Framework.HttpArchive.HarPostDataExtensions.TryGetRawData) returns the bytes of a response body ([HarContent](cref:Meziantou.Framework.HttpArchive.HarContent)) or of a request body ([HarPostData](cref:Meziantou.Framework.HttpArchive.HarPostData)):

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarContentExtensionsTests.cs;region=HarContent_TryGetRawData;format=md-fenced-code-block;dedent=true -->
```csharp
var text = new HarContent { MimeType = "text/plain", Text = "hello" };
Assert.True(text.TryGetRawData(out var textBytes));
Assert.Equal("hello"u8.ToArray(), textBytes);

var binary = new HarContent { MimeType = "image/png", Encoding = "base64", Text = "iVBORw==" };
Assert.True(binary.TryGetRawData(out var binaryBytes));
Assert.Equal([0x89, 0x50, 0x4E, 0x47], binaryBytes);

// No text, invalid base64 or an unknown encoding: no bytes
var notCaptured = new HarContent { MimeType = "image/png", Size = 1024 };
Assert.False(notCaptured.TryGetRawData(out _));
var unknownEncoding = new HarContent { Encoding = "quoted-printable", Text = "=41" };
Assert.False(unknownEncoding.TryGetRawData(out _));
```
<!-- endref -->

- Text without an encoding is returned as UTF-8 bytes, and text whose encoding is `base64` is decoded.
- It returns `false` when the archive has no text (many tools leave out the bodies they do not capture), when the
  base64 is invalid, and when the encoding is not `base64`. It never returns the undecoded text as the body.

The HAR format has no encoding field for request bodies. `HarPostData.TryGetRawData` reads it from the
`x-meziantou-encoding` custom field ([HarPostDataExtensions.DefaultEncodingExtensionName](cref:Meziantou.Framework.HttpArchive.HarPostDataExtensions.DefaultEncodingExtensionName)), and you can pass the name
of another field:

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarEntryExtensionsTests.cs;region=HarPostData_TryGetRawData;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HarDocument.Parse("""
    {
      "log": {
        "entries": [
          {
            "request": {
              "method": "POST",
              "url": "https://example.com/upload",
              "postData": { "mimeType": "application/octet-stream", "text": "AAF/gP8=", "x-meziantou-encoding": "base64" }
            }
          }
        ]
      }
    }
    """);

var postData = document.Log!.Entries![0].Request!.PostData;

Assert.True(postData.TryGetRawData(out var bytes));
Assert.Equal([0x00, 0x01, 0x7F, 0x80, 0xFF], bytes);
```
<!-- endref -->
