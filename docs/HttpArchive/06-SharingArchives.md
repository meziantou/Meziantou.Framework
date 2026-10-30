---
title: Sharing archives
description: HAR files contain credentials; remove them before you share or commit a file.
---

An archive records the requests exactly as they were sent: `Authorization` headers, `Cookie` and `Set-Cookie` values,
bearer tokens, API keys in URLs, and complete request and response bodies. Treat a `.har` file as a secret: do not
attach one to a bug report, a support ticket or an issue, and do not commit one, before removing the sensitive data.

The library reads and writes what the file contains, and does not redact anything for you. To remove the headers and
cookies that usually carry credentials, edit the model before writing it:

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarDocumentTests.cs;region=HarDocument_RemoveCredentials;format=md-fenced-code-block;dedent=true -->
```csharp
string[] sensitiveHeaders = ["Authorization", "Cookie", "Set-Cookie"];
foreach (var entry in document.Log?.Entries ?? [])
{
    if (entry.Request is { } request)
    {
        request.Headers?.RemoveAll(header => sensitiveHeaders.Contains(header.Name, StringComparer.OrdinalIgnoreCase));
        request.Cookies?.Clear();
    }

    if (entry.Response is { } response)
    {
        response.Headers?.RemoveAll(header => sensitiveHeaders.Contains(header.Name, StringComparer.OrdinalIgnoreCase));
        response.Cookies?.Clear();
    }
}
```
<!-- endref -->

Then write the document as usual (see [Reading and writing](03-ReadingAndWriting.md)). Headers and cookies are not the
only places secrets hide; check the rest of the archive too:

- [HarRequest.Url](cref:Meziantou.Framework.HttpArchive.HarRequest.Url) and [HarRequest.QueryString](cref:Meziantou.Framework.HttpArchive.HarRequest.QueryString), for tokens and API keys passed in the query string.
- [HarRequest.PostData](cref:Meziantou.Framework.HttpArchive.HarRequest.PostData), for passwords and tokens sent in a form or a JSON body.
- `HarResponse.Content.Text`, for tokens returned by the server.
- The headers of your own API, such as `X-Api-Key`.
- `ExtensionData`, where tools store the fields they add.
