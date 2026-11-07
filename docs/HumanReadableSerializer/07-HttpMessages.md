---
title: HTTP messages
description: Write HttpRequestMessage and HttpResponseMessage values with their headers and content, and hide the headers that change.
---

`HttpRequestMessage`, `HttpResponseMessage`, `HttpContent` and HTTP headers have converters. [AddHttpConverters](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpExtensions.AddHttpConverters) writes
the messages in a compact form, with the members in a fixed order and without the values that change on every call:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_HttpResponse;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions()
    .AddHttpConverters(new HumanReadableHttpOptions
    {
        ResponseMessageOptions = { RequestMessageFormat = HttpRequestMessageFormat.MethodAndUri },
    })
    .AddJsonFormatter(new JsonFormatterOptions { OrderProperties = true });

var text = HumanReadableSerializer.Serialize(httpResponseMessage, options);

Assert.Equal("""
    StatusCode: 201 (Created)
    Headers:
      Location: https://example.com/users/42
    Content:
      Headers:
        Content-Type: application/json; charset=utf-8
      Value:
        {
          "id": 42,
          "name": "Ada"
        }
    RequestMessage: POST https://example.com/users
    """, text);
```
<!-- endref -->

- The status code is written with its number and its name.
- The content is written with its headers and its value. A JSON, XML, HTML or form value is formatted when the
  [formatter of its media type](06-JsonXmlAndHtml.md) is registered.
- Empty header collections, `IsSuccessStatusCode` and `ReasonPhrase` are not written.
- [OmitProtocolVersion](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpMessageOptions.OmitProtocolVersion) (`true` by default) skips the HTTP version when it is 1.1.
- The response headers `Date` and `Alt-Svc` are excluded by default, as they change on every response.

## Options

[HumanReadableHttpOptions](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpOptions) has a [RequestMessageOptions](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpOptions.RequestMessageOptions) and a [ResponseMessageOptions](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpOptions.ResponseMessageOptions):

| Option | Applies to | Description |
| --- | --- | --- |
| [ExcludedHeaderNames](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpMessageOptions.ExcludedHeaderNames) | Requests, responses | Names of the headers to skip, ignoring case, in the message and in its content |
| [HeaderValueTransformer](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpMessageOptions.HeaderValueTransformer) | Requests, responses | [HttpHeaderValueFormatter](cref:Meziantou.Framework.HumanReadable.Converters.HttpHeaderValueFormatter) instances that rewrite header values |
| [OmitProtocolVersion](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpMessageOptions.OmitProtocolVersion) | Requests, responses | Skips the HTTP version when it is 1.1, and the default version policy of requests |
| [RequestMessageFormat](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpResponseMessageOptions.RequestMessageFormat) | Responses | How the `RequestMessage` of a response is written: [NotSerialized](cref:Meziantou.Framework.HumanReadable.Converters.HttpRequestMessageFormat.NotSerialized) (the default), [MethodAndUri](cref:Meziantou.Framework.HumanReadable.Converters.HttpRequestMessageFormat.MethodAndUri), or [Full](cref:Meziantou.Framework.HumanReadable.Converters.HttpRequestMessageFormat.Full) |
| [RedactContentSecurityPolicyNonce](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpResponseMessageOptions.RedactContentSecurityPolicyNonce) | Responses | Replaces the nonces of the `Content-Security-Policy` header with `[redacted]` |

## Hide or rewrite headers

Headers such as request ids, cookies or tokens change on every call. Exclude them, or rewrite their value:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_HttpHeaderValueFormatter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class RedactCookieFormatter : HttpHeaderValueFormatter
{
    public override string FormatHeaderValue(string headerName, string headerValue)
    {
        return string.Equals(headerName, "Set-Cookie", StringComparison.OrdinalIgnoreCase) ? "[redacted]" : headerValue;
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_HttpHeaders;format=md-fenced-code-block;dedent=true -->
```csharp
var httpOptions = new HumanReadableHttpOptions();
httpOptions.ResponseMessageOptions.ExcludedHeaderNames.Add("X-Request-Id");
httpOptions.ResponseMessageOptions.HeaderValueTransformer.Add(new RedactCookieFormatter());

var options = new HumanReadableSerializerOptions().AddHttpConverters(httpOptions);

Assert.Equal("""
    StatusCode: 200 (OK)
    Headers:
      Set-Cookie: [redacted]
    Content:
      Headers:
        Content-Type: text/plain; charset=utf-8
      Value: Hello
    """, HumanReadableSerializer.Serialize(httpResponseMessage, options));
```
<!-- endref -->

[AddHttpConverters](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpExtensions.AddHttpConverters) copies the options. Calling it again replaces the previous configuration, so you can change the
HTTP options of a snapshot library that already called it.

## Content

The content is written with its headers and its value. A content that can be read as text is written as text, through
the formatter of its media type when there is one; other content, such as an image, is written as Base64. The parts of
a multipart content are written as a list.
