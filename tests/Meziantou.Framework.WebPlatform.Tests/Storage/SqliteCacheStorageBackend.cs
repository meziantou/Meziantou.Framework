using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.WebPlatform.Tests.Storage;

/// <summary>
/// A <see cref="CacheStorageBackend"/> that stores the caches in a SQLite file: a table of the caches (in creation order)
/// and a table of the entries (in insertion order), indexed by the URL of the requests without query. The requests and the
/// metadata of the responses are JSON documents, the bodies are BLOBs. The names are stored as UTF-16 BLOBs (they can
/// contain lone surrogates, which a TEXT column cannot store).
/// </summary>
internal sealed class SqliteCacheStorageBackend : CacheStorageBackend, IDisposable
{
    private readonly Lock _lock = new();
    private readonly SqliteConnection _connection;

    public SqliteCacheStorageBackend(string path)
    {
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _connection.Open();
        SqliteIndexedDBBackend.Execute(_connection, null, """
            CREATE TABLE IF NOT EXISTS caches (id INTEGER PRIMARY KEY AUTOINCREMENT, origin BLOB NOT NULL, name BLOB NOT NULL, UNIQUE (origin, name));
            CREATE TABLE IF NOT EXISTS entries (id INTEGER PRIMARY KEY AUTOINCREMENT, cache_id INTEGER NOT NULL, url TEXT NOT NULL, request TEXT NOT NULL, response TEXT NOT NULL, body BLOB NULL);
            CREATE INDEX IF NOT EXISTS entries_by_url ON entries (cache_id, url);
            """);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _connection.Dispose();
        }
    }

    public override IReadOnlyList<string> GetCacheNames(string origin)
    {
        lock (_lock)
        {
            return SqliteIndexedDBBackend.Query(_connection, null, "SELECT name FROM caches WHERE origin = @origin ORDER BY id", reader => FromBlob((byte[])reader.GetValue(0)), ("@origin", ToBlob(origin)));
        }
    }

    public override bool CreateCache(string origin, string name)
    {
        lock (_lock)
        {
            if (FindCache(origin, name) is not null)
                return false;

            SqliteIndexedDBBackend.Execute(_connection, null, "INSERT INTO caches (origin, name) VALUES (@origin, @name)", ("@origin", ToBlob(origin)), ("@name", ToBlob(name)));
            return true;
        }
    }

    public override bool DeleteCache(string origin, string name)
    {
        lock (_lock)
        {
            if (FindCache(origin, name) is not { } id)
                return false;

            using var transaction = _connection.BeginTransaction();
            SqliteIndexedDBBackend.Execute(_connection, transaction, "DELETE FROM entries WHERE cache_id = @id", ("@id", id));
            SqliteIndexedDBBackend.Execute(_connection, transaction, "DELETE FROM caches WHERE id = @id", ("@id", id));
            transaction.Commit();
            return true;
        }
    }

    public override IReadOnlyList<CacheEntry> GetEntries(string origin, string name, string? urlWithoutQuery)
    {
        lock (_lock)
        {
            var id = FindCache(origin, name) ?? throw new InvalidOperationException($"The cache '{name}' does not exist");
            var sql = "SELECT id, request, response, body FROM entries WHERE cache_id = @id" + (urlWithoutQuery is null ? "" : " AND url = @url") + " ORDER BY id";
            return SqliteIndexedDBBackend.Query(_connection, null, sql, reader => new CacheEntry
            {
                Id = reader.GetInt64(0),
                Request = JsonSerializer.Deserialize<StoredRequest>(reader.GetString(1))!.ToCachedRequest(),
                Response = JsonSerializer.Deserialize<StoredResponse>(reader.GetString(2))!.ToCachedResponse(reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3)),
            }, ("@id", id), ("@url", urlWithoutQuery));
        }
    }

    public override void ApplyBatch(string origin, string name, IReadOnlyList<long> removedEntryIds, IReadOnlyList<CacheEntry> addedEntries)
    {
        lock (_lock)
        {
            var id = FindCache(origin, name) ?? throw new InvalidOperationException($"The cache '{name}' does not exist");
            using var transaction = _connection.BeginTransaction();
            foreach (var entryId in removedEntryIds)
            {
                SqliteIndexedDBBackend.Execute(_connection, transaction, "DELETE FROM entries WHERE id = @id AND cache_id = @cache", ("@id", entryId), ("@cache", id));
            }

            foreach (var entry in addedEntries)
            {
                SqliteIndexedDBBackend.Execute(_connection, transaction, "INSERT INTO entries (cache_id, url, request, response, body) VALUES (@cache, @url, @request, @response, @body)",
                    ("@cache", id),
                    ("@url", entry.Request.UrlWithoutQuery),
                    ("@request", JsonSerializer.Serialize(StoredRequest.From(entry.Request))),
                    ("@response", JsonSerializer.Serialize(StoredResponse.From(entry.Response))),
                    ("@body", entry.Response.Body?.ToArray()));
            }

            transaction.Commit();
        }
    }

    private long? FindCache(string origin, string name)
        => SqliteIndexedDBBackend.Query(_connection, null, "SELECT id FROM caches WHERE origin = @origin AND name = @name", reader => (long?)reader.GetInt64(0), ("@origin", ToBlob(origin)), ("@name", ToBlob(name))).FirstOrDefault();

    // The UTF-16 code units, little-endian (Encoding.Unicode would replace the lone surrogates)
    internal static byte[] ToBlob(string value)
    {
        var bytes = new byte[value.Length * 2];
        for (var i = 0; i < value.Length; i++)
        {
            bytes[2 * i] = (byte)value[i];
            bytes[(2 * i) + 1] = (byte)(value[i] >> 8);
        }

        return bytes;
    }

    private static string FromBlob(byte[] bytes)
    {
        var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)(bytes[2 * i] | (bytes[(2 * i) + 1] << 8));
        }

        return new string(chars);
    }

    private sealed record StoredRequest(string Method, string Url, List<string[]> Headers, string Mode, string Credentials, string Cache, string Redirect, string Integrity, string Referrer, string ReferrerPolicy)
    {
        public static StoredRequest From(CachedRequest request)
            => new(request.Method, request.Url, [.. request.Headers.Select(header => new[] { header.Key, header.Value })], request.Mode, request.Credentials, request.Cache, request.Redirect, request.Integrity, request.Referrer, request.ReferrerPolicy);

        public CachedRequest ToCachedRequest() => new()
        {
            Method = Method,
            Url = Url,
            Headers = [.. Headers.Select(header => new KeyValuePair<string, string>(header[0], header[1]))],
            Mode = Mode,
            Credentials = Credentials,
            Cache = Cache,
            Redirect = Redirect,
            Integrity = Integrity,
            Referrer = Referrer,
            ReferrerPolicy = ReferrerPolicy,
        };
    }

    private sealed record StoredResponse(string Type, int Status, string StatusText, List<string[]> Headers, List<string> UrlList, bool RequestIncludesCredentials, string? CrossOriginResourcePolicy)
    {
        public static StoredResponse From(CachedResponse response)
            => new(response.Type, response.Status, response.StatusText, [.. response.Headers.Select(header => new[] { header.Key, header.Value })], [.. response.UrlList], response.RequestIncludesCredentials, response.CrossOriginResourcePolicy);

        public CachedResponse ToCachedResponse(byte[]? body) => new()
        {
            Type = Type,
            Status = Status,
            StatusText = StatusText,
            Headers = [.. Headers.Select(header => new KeyValuePair<string, string>(header[0], header[1]))],
            UrlList = UrlList,
            RequestIncludesCredentials = RequestIncludesCredentials,
            CrossOriginResourcePolicy = CrossOriginResourcePolicy,
            Body = body,
        };
    }
}
