using System.Text;
using System.Text.Json;
using Xunit;

namespace Meziantou.Framework.HttpArchive.Tests;

public sealed class HarDocumentTests
{
    private const string MinimalHar = """
        {
            "log": {
                "version": "1.2",
                "creator": {
                    "name": "TestApp",
                    "version": "1.0"
                },
                "entries": []
            }
        }
        """;

    private const string CompleteHar = """
        {
            "log": {
                "version": "1.2",
                "creator": {
                    "name": "WebInspector",
                    "version": "537.36"
                },
                "browser": {
                    "name": "Chrome",
                    "version": "120.0"
                },
                "pages": [
                    {
                        "startedDateTime": "2024-01-15T10:00:00.000Z",
                        "id": "page_1",
                        "title": "Test Page",
                        "pageTimings": {
                            "onContentLoad": 1500,
                            "onLoad": 2500
                        }
                    }
                ],
                "entries": [
                    {
                        "pageref": "page_1",
                        "startedDateTime": "2024-01-15T10:00:00.500Z",
                        "time": 150.5,
                        "request": {
                            "method": "GET",
                            "url": "https://example.com/api/data",
                            "httpVersion": "HTTP/1.1",
                            "cookies": [
                                {
                                    "name": "session",
                                    "value": "abc123",
                                    "path": "/",
                                    "domain": "example.com",
                                    "httpOnly": true,
                                    "secure": true
                                }
                            ],
                            "headers": [
                                { "name": "Host", "value": "example.com" },
                                { "name": "Accept", "value": "application/json" }
                            ],
                            "queryString": [
                                { "name": "page", "value": "1" }
                            ],
                            "headersSize": 200,
                            "bodySize": 0
                        },
                        "response": {
                            "status": 200,
                            "statusText": "OK",
                            "httpVersion": "HTTP/1.1",
                            "cookies": [],
                            "headers": [
                                { "name": "Content-Type", "value": "application/json" }
                            ],
                            "content": {
                                "size": 42,
                                "compression": 0,
                                "mimeType": "application/json",
                                "text": "{\"key\":\"value\"}"
                            },
                            "redirectURL": "",
                            "headersSize": 150,
                            "bodySize": 42
                        },
                        "cache": {},
                        "timings": {
                            "blocked": 0.5,
                            "dns": 10.0,
                            "connect": 25.0,
                            "send": 1.0,
                            "wait": 100.0,
                            "receive": 14.0,
                            "ssl": 12.0
                        },
                        "serverIPAddress": "93.184.216.34",
                        "connection": "443"
                    }
                ]
            }
        }
        """;

    [Fact]
    public void ParseMinimalHar()
    {
        var doc = HarDocument.Parse(MinimalHar);

        Assert.Equal("1.2", doc.Log.Version);
        Assert.Equal("TestApp", doc.Log.Creator.Name);
        Assert.Equal("1.0", doc.Log.Creator.Version);
        Assert.Null(doc.Log.Browser);
        Assert.Null(doc.Log.Pages);
        Assert.Empty(doc.Log.Entries);
    }

    [Fact]
    public void ParseCompleteHar()
    {
        var doc = HarDocument.Parse(CompleteHar);

        Assert.Equal("1.2", doc.Log.Version);
        Assert.Equal("WebInspector", doc.Log.Creator.Name);
        Assert.Equal("Chrome", doc.Log.Browser?.Name);
        Assert.Equal("120.0", doc.Log.Browser?.Version);

        Assert.NotNull(doc.Log.Pages);
        var page = Assert.Single(doc.Log.Pages);
        Assert.Equal("page_1", page.Id);
        Assert.Equal("Test Page", page.Title);
        Assert.Equal(1500, page.PageTimings.OnContentLoad);
        Assert.Equal(2500, page.PageTimings.OnLoad);

        var entry = Assert.Single(doc.Log.Entries);
        Assert.Equal("page_1", entry.Pageref);
        Assert.Equal(150.5, entry.Time);
        Assert.Equal("93.184.216.34", entry.ServerIPAddress);
        Assert.Equal("443", entry.Connection);

        Assert.Equal("GET", entry.Request.Method);
        Assert.Equal("https://example.com/api/data", entry.Request.Url);
        Assert.Equal("HTTP/1.1", entry.Request.HttpVersion);
        Assert.Equal(200, entry.Request.HeadersSize);
        Assert.Equal(2, entry.Request.Headers.Count);
        var cookie = Assert.Single(entry.Request.Cookies);
        Assert.Equal("session", cookie.Name);
        Assert.Equal("abc123", cookie.Value);
        Assert.Equal("/", cookie.Path);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        var queryParam = Assert.Single(entry.Request.QueryString);
        Assert.Equal("page", queryParam.Name);
        Assert.Equal("1", queryParam.Value);

        Assert.Equal(200, entry.Response.Status);
        Assert.Equal("OK", entry.Response.StatusText);
        Assert.Equal(42, entry.Response.Content.Size);
        Assert.Equal(0, entry.Response.Content.Compression);
        Assert.Equal("application/json", entry.Response.Content.MimeType);
        Assert.Equal("{\"key\":\"value\"}", entry.Response.Content.Text);

        Assert.Equal(0.5, entry.Timings.Blocked);
        Assert.Equal(10.0, entry.Timings.Dns);
        Assert.Equal(25.0, entry.Timings.Connect);
        Assert.Equal(1.0, entry.Timings.Send);
        Assert.Equal(100.0, entry.Timings.Wait);
        Assert.Equal(14.0, entry.Timings.Receive);
        Assert.Equal(12.0, entry.Timings.Ssl);
    }

    [Fact]
    public void RoundTrip()
    {
        var doc = HarDocument.Parse(CompleteHar);
        var json = doc.ToJsonString();
        var doc2 = HarDocument.Parse(json);

        Assert.Equal(doc.Log.Version, doc2.Log.Version);
        Assert.Equal(doc.Log.Creator.Name, doc2.Log.Creator.Name);
        Assert.Equal(doc.Log.Browser?.Name, doc2.Log.Browser?.Name);
        Assert.Equal(doc.Log.Entries.Count, doc2.Log.Entries.Count);

        var entry1 = doc.Log.Entries[0];
        var entry2 = doc2.Log.Entries[0];
        Assert.Equal(entry1.Request.Url, entry2.Request.Url);
        Assert.Equal(entry1.Response.Status, entry2.Response.Status);
        Assert.Equal(entry1.Response.Content.Text, entry2.Response.Content.Text);
        Assert.Equal(entry1.Timings.Wait, entry2.Timings.Wait);
    }

    [Fact]
    public async Task ParseAsync_FromStream()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(CompleteHar));
        var doc = await HarDocument.ParseAsync(stream);

        Assert.Equal("1.2", doc.Log.Version);
        Assert.Single(doc.Log.Entries);
    }

    [Fact]
    public void Parse_FromStream()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(CompleteHar));
        var doc = HarDocument.Parse(stream);

        Assert.Equal("1.2", doc.Log.Version);
        Assert.Single(doc.Log.Entries);
    }

    [Fact]
    public void ToJsonString_Indented()
    {
        var doc = HarDocument.Parse(MinimalHar);
        var json = doc.ToJsonString(indented: true);

        Assert.Contains("\n", json, StringComparison.Ordinal);
        Assert.Contains("  ", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJsonString_NotIndented()
    {
        var doc = HarDocument.Parse(MinimalHar);
        var json = doc.ToJsonString(indented: false);

        Assert.DoesNotContain("\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteToAsync_Stream()
    {
        var doc = HarDocument.Parse(MinimalHar);
        using var stream = new MemoryStream();
        await doc.WriteToAsync(stream);
        stream.Position = 0;

        var doc2 = HarDocument.Parse(stream);
        Assert.Equal("1.2", doc2.Log.Version);
        Assert.Equal("TestApp", doc2.Log.Creator.Name);
    }

    [Fact]
    public void WriteTo_Stream()
    {
        var doc = HarDocument.Parse(MinimalHar);
        using var stream = new MemoryStream();
        doc.WriteTo(stream);
        stream.Position = 0;

        var doc2 = HarDocument.Parse(stream);
        Assert.Equal("1.2", doc2.Log.Version);
    }

    [Fact]
    public void NegativeOneTimingValues_Preserved()
    {
        const string har = """
            {
                "log": {
                    "version": "1.2",
                    "creator": { "name": "test", "version": "1.0" },
                    "entries": [
                        {
                            "startedDateTime": "2024-01-15T10:00:00.000Z",
                            "time": 50,
                            "request": {
                                "method": "GET",
                                "url": "https://example.com",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "queryString": [],
                                "headersSize": -1,
                                "bodySize": -1
                            },
                            "response": {
                                "status": 200,
                                "statusText": "OK",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "content": { "size": 0, "mimeType": "text/html" },
                                "redirectURL": "",
                                "headersSize": -1,
                                "bodySize": -1
                            },
                            "cache": {},
                            "timings": {
                                "blocked": -1,
                                "dns": -1,
                                "connect": -1,
                                "send": 1,
                                "wait": 40,
                                "receive": 9,
                                "ssl": -1
                            }
                        }
                    ]
                }
            }
            """;

        var doc = HarDocument.Parse(har);
        var timings = doc.Log.Entries[0].Timings;

        Assert.Equal(-1, timings.Blocked);
        Assert.Equal(-1, timings.Dns);
        Assert.Equal(-1, timings.Connect);
        Assert.Equal(1, timings.Send);
        Assert.Equal(40, timings.Wait);
        Assert.Equal(9, timings.Receive);
        Assert.Equal(-1, timings.Ssl);

        Assert.Equal(-1, doc.Log.Entries[0].Request.HeadersSize);
        Assert.Equal(-1, doc.Log.Entries[0].Request.BodySize);
    }

    [Fact]
    public void RedirectURL_Casing()
    {
        const string har = """
            {
                "log": {
                    "version": "1.2",
                    "creator": { "name": "test", "version": "1.0" },
                    "entries": [
                        {
                            "startedDateTime": "2024-01-15T10:00:00.000Z",
                            "time": 50,
                            "request": {
                                "method": "GET",
                                "url": "https://example.com",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "queryString": [],
                                "headersSize": 0,
                                "bodySize": 0
                            },
                            "response": {
                                "status": 301,
                                "statusText": "Moved Permanently",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "content": { "size": 0, "mimeType": "" },
                                "redirectURL": "https://www.example.com",
                                "headersSize": 0,
                                "bodySize": 0
                            },
                            "cache": {},
                            "timings": {
                                "send": 1,
                                "wait": 40,
                                "receive": 9
                            }
                        }
                    ]
                }
            }
            """;

        var doc = HarDocument.Parse(har);
        Assert.Equal("https://www.example.com", doc.Log.Entries[0].Response.RedirectUrl);

        var json = doc.ToJsonString();
        Assert.Contains("\"redirectURL\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownProperties_Preserved()
    {
        const string har = """
            {
                "log": {
                    "version": "1.2",
                    "creator": { "name": "test", "version": "1.0" },
                    "entries": [
                        {
                            "startedDateTime": "2024-01-15T10:00:00.000Z",
                            "time": 50,
                            "_priority": "High",
                            "_resourceType": "xhr",
                            "request": {
                                "method": "GET",
                                "url": "https://example.com",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "queryString": [],
                                "headersSize": 0,
                                "bodySize": 0
                            },
                            "response": {
                                "status": 200,
                                "statusText": "OK",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "content": { "size": 0, "mimeType": "text/html", "_transferSize": 1234 },
                                "redirectURL": "",
                                "headersSize": 0,
                                "bodySize": 0
                            },
                            "cache": {},
                            "timings": { "send": 1, "wait": 40, "receive": 9 }
                        }
                    ]
                }
            }
            """;

        var doc = HarDocument.Parse(har);
        var entry = doc.Log.Entries[0];

        Assert.NotNull(entry.ExtensionData);
        Assert.True(entry.ExtensionData.ContainsKey("_priority"));
        Assert.Equal("High", entry.ExtensionData["_priority"].GetString());
        Assert.Equal("xhr", entry.ExtensionData["_resourceType"].GetString());

        Assert.NotNull(entry.Response.Content.ExtensionData);
        Assert.Equal(1234, entry.Response.Content.ExtensionData["_transferSize"].GetInt32());

        var json = doc.ToJsonString();
        Assert.Contains("_priority", json, StringComparison.Ordinal);
        Assert.Contains("_transferSize", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PostData_Parsed()
    {
        const string har = """
            {
                "log": {
                    "version": "1.2",
                    "creator": { "name": "test", "version": "1.0" },
                    "entries": [
                        {
                            "startedDateTime": "2024-01-15T10:00:00.000Z",
                            "time": 50,
                            "request": {
                                "method": "POST",
                                "url": "https://example.com/api",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "queryString": [],
                                "postData": {
                                    "mimeType": "application/json",
                                    "text": "{\"name\":\"test\"}"
                                },
                                "headersSize": 0,
                                "bodySize": 15
                            },
                            "response": {
                                "status": 200,
                                "statusText": "OK",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "content": { "size": 0, "mimeType": "" },
                                "redirectURL": "",
                                "headersSize": 0,
                                "bodySize": 0
                            },
                            "cache": {},
                            "timings": { "send": 1, "wait": 40, "receive": 9 }
                        }
                    ]
                }
            }
            """;

        var doc = HarDocument.Parse(har);
        var postData = doc.Log.Entries[0].Request.PostData;

        Assert.NotNull(postData);
        Assert.Equal("application/json", postData.MimeType);
        Assert.Equal("{\"name\":\"test\"}", postData.Text);
    }

    [Fact]
    public void CacheEntries_Parsed()
    {
        const string har = """
            {
                "log": {
                    "version": "1.2",
                    "creator": { "name": "test", "version": "1.0" },
                    "entries": [
                        {
                            "startedDateTime": "2024-01-15T10:00:00.000Z",
                            "time": 50,
                            "request": {
                                "method": "GET",
                                "url": "https://example.com",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "queryString": [],
                                "headersSize": 0,
                                "bodySize": 0
                            },
                            "response": {
                                "status": 200,
                                "statusText": "OK",
                                "httpVersion": "HTTP/1.1",
                                "cookies": [],
                                "headers": [],
                                "content": { "size": 0, "mimeType": "" },
                                "redirectURL": "",
                                "headersSize": 0,
                                "bodySize": 0
                            },
                            "cache": {
                                "afterRequest": {
                                    "expires": "2024-02-15T10:00:00.000Z",
                                    "lastAccess": "2024-01-15T10:00:00.000Z",
                                    "eTag": "\"abc123\"",
                                    "hitCount": 5
                                }
                            },
                            "timings": { "send": 1, "wait": 40, "receive": 9 }
                        }
                    ]
                }
            }
            """;

        var doc = HarDocument.Parse(har);
        var cache = doc.Log.Entries[0].Cache;

        Assert.Null(cache.BeforeRequest);
        Assert.NotNull(cache.AfterRequest);
        Assert.Equal("\"abc123\"", cache.AfterRequest.ETag);
        Assert.Equal(5, cache.AfterRequest.HitCount);
    }
}
