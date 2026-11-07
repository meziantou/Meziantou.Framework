using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.WebPlatform.Tests.Storage;

/// <summary>
/// A <see cref="ServiceWorkerBackend"/> that stores the registrations in a SQLite file: a table of the registrations (in
/// insertion order) whose metadata are JSON documents, and a table of the scripts of their service workers, whose bodies
/// are BLOBs.
/// </summary>
internal sealed class SqliteServiceWorkerBackend : ServiceWorkerBackend, IDisposable
{
    private readonly Lock _lock = new();
    private readonly SqliteConnection _connection;

    public SqliteServiceWorkerBackend(string path)
    {
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _connection.Open();
        SqliteIndexedDBBackend.Execute(_connection, null, """
            CREATE TABLE IF NOT EXISTS registrations (id INTEGER PRIMARY KEY AUTOINCREMENT, origin TEXT NOT NULL, scope TEXT NOT NULL, registration TEXT NOT NULL, UNIQUE (origin, scope));
            CREATE TABLE IF NOT EXISTS scripts (registration_id INTEGER NOT NULL, worker TEXT NOT NULL, url TEXT NOT NULL, response TEXT NOT NULL, body BLOB NOT NULL, PRIMARY KEY (registration_id, worker, url));
            """);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _connection.Dispose();
        }
    }

    public override IReadOnlyList<StoredServiceWorkerRegistration> GetRegistrations(string origin)
    {
        lock (_lock)
        {
            var registrations = SqliteIndexedDBBackend.Query(_connection, null, "SELECT id, registration FROM registrations WHERE origin = @origin ORDER BY id", reader => (Id: reader.GetInt64(0), Json: reader.GetString(1)), ("@origin", origin));
            return [.. registrations.Select(registration => JsonSerializer.Deserialize<RegistrationRow>(registration.Json)!.ToRegistration(worker => GetScripts(registration.Id, worker)))];
        }
    }

    public override void SaveRegistration(string origin, StoredServiceWorkerRegistration registration)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();
            var json = JsonSerializer.Serialize(RegistrationRow.From(registration));
            var id = FindRegistration(origin, registration.ScopeUrl, transaction);
            if (id is { } existing)
            {
                SqliteIndexedDBBackend.Execute(_connection, transaction, "UPDATE registrations SET registration = @registration WHERE id = @id", ("@registration", json), ("@id", existing));
                SqliteIndexedDBBackend.Execute(_connection, transaction, "DELETE FROM scripts WHERE registration_id = @id", ("@id", existing));
            }
            else
            {
                SqliteIndexedDBBackend.Execute(_connection, transaction, "INSERT INTO registrations (origin, scope, registration) VALUES (@origin, @scope, @registration)", ("@origin", origin), ("@scope", registration.ScopeUrl), ("@registration", json));
                id = FindRegistration(origin, registration.ScopeUrl, transaction);
            }

            foreach (var worker in new[] { registration.Waiting, registration.Active })
            {
                if (worker is null)
                    continue;

                foreach (var script in worker.Scripts)
                {
                    SqliteIndexedDBBackend.Execute(_connection, transaction, "INSERT OR REPLACE INTO scripts (registration_id, worker, url, response, body) VALUES (@id, @worker, @url, @response, @body)",
                        ("@id", id),
                        ("@worker", worker.Id),
                        ("@url", script.Url),
                        ("@response", JsonSerializer.Serialize(ScriptRow.From(script))),
                        ("@body", script.Body.ToArray()));
                }
            }

            transaction.Commit();
        }
    }

    public override bool DeleteRegistration(string origin, string scopeUrl)
    {
        lock (_lock)
        {
            if (FindRegistration(origin, scopeUrl, transaction: null) is not { } id)
                return false;

            using var transaction = _connection.BeginTransaction();
            SqliteIndexedDBBackend.Execute(_connection, transaction, "DELETE FROM scripts WHERE registration_id = @id", ("@id", id));
            SqliteIndexedDBBackend.Execute(_connection, transaction, "DELETE FROM registrations WHERE id = @id", ("@id", id));
            transaction.Commit();
            return true;
        }
    }

    private long? FindRegistration(string origin, string scopeUrl, SqliteTransaction? transaction)
        => SqliteIndexedDBBackend.Query(_connection, transaction, "SELECT id FROM registrations WHERE origin = @origin AND scope = @scope", reader => (long?)reader.GetInt64(0), ("@origin", origin), ("@scope", scopeUrl)).FirstOrDefault();

    private List<StoredServiceWorkerScript> GetScripts(long registrationId, string workerId)
        => SqliteIndexedDBBackend.Query(_connection, null, "SELECT response, body FROM scripts WHERE registration_id = @id AND worker = @worker ORDER BY rowid", reader => JsonSerializer.Deserialize<ScriptRow>(reader.GetString(0))!.ToScript((byte[])reader.GetValue(1)), ("@id", registrationId), ("@worker", workerId));

    private sealed record RegistrationRow(string ScopeUrl, string UpdateViaCache, DateTimeOffset? LastUpdateCheckTime, bool NavigationPreloadEnabled, string NavigationPreloadHeaderValue, WorkerRow? Waiting, WorkerRow? Active)
    {
        public static RegistrationRow From(StoredServiceWorkerRegistration registration)
            => new(registration.ScopeUrl, registration.UpdateViaCache, registration.LastUpdateCheckTime, registration.NavigationPreloadEnabled, registration.NavigationPreloadHeaderValue, WorkerRow.From(registration.Waiting), WorkerRow.From(registration.Active));

        public StoredServiceWorkerRegistration ToRegistration(Func<string, List<StoredServiceWorkerScript>> getScripts) => new()
        {
            ScopeUrl = ScopeUrl,
            UpdateViaCache = UpdateViaCache,
            LastUpdateCheckTime = LastUpdateCheckTime,
            NavigationPreloadEnabled = NavigationPreloadEnabled,
            NavigationPreloadHeaderValue = NavigationPreloadHeaderValue,
            Waiting = Waiting?.ToWorker(getScripts),
            Active = Active?.ToWorker(getScripts),
        };
    }

    private sealed record WorkerRow(string Id, string ScriptUrl, string Type, string State, bool ClassicScriptsImported, List<string> EventTypesToHandle, List<string> UsedScriptUrls, List<StoredServiceWorkerRouterRule>? RouterRules)
    {
        public static WorkerRow? From(StoredServiceWorker? worker)
            => worker is null ? null : new(worker.Id, worker.ScriptUrl, worker.Type, worker.State, worker.ClassicScriptsImported, [.. worker.EventTypesToHandle], [.. worker.UsedScriptUrls], [.. worker.RouterRules]);

        public StoredServiceWorker ToWorker(Func<string, List<StoredServiceWorkerScript>> getScripts) => new()
        {
            Id = Id,
            ScriptUrl = ScriptUrl,
            Type = Type,
            State = State,
            ClassicScriptsImported = ClassicScriptsImported,
            EventTypesToHandle = EventTypesToHandle,
            UsedScriptUrls = UsedScriptUrls,
            RouterRules = RouterRules ?? [],
            Scripts = getScripts(Id),
        };
    }

    private sealed record ScriptRow(string Url, string? ResponseUrl, int Status, string StatusText, List<string[]> Headers)
    {
        public static ScriptRow From(StoredServiceWorkerScript script)
            => new(script.Url, script.ResponseUrl, script.Status, script.StatusText, [.. script.Headers.Select(header => new[] { header.Key, header.Value })]);

        public StoredServiceWorkerScript ToScript(byte[] body) => new()
        {
            Url = Url,
            ResponseUrl = ResponseUrl,
            Status = Status,
            StatusText = StatusText,
            Headers = [.. Headers.Select(header => KeyValuePair.Create(header[0], header[1]))],
            Body = body,
        };
    }
}
