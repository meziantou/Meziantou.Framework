using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Storage;

/// <summary>The contract of <see cref="ServiceWorkerBackend"/>, checked for each implementation.</summary>
public abstract class ServiceWorkerBackendTests : IDisposable
{
    private const string Origin = "https://example.com";

    private ServiceWorkerBackend? _backend;

    protected ServiceWorkerBackend Backend => _backend ??= CreateBackend();

    /// <summary>Creates a backend on the storage of the test.</summary>
    protected abstract ServiceWorkerBackend CreateBackend();

    /// <summary>Releases the backend (e.g. closes its file).</summary>
    protected virtual void DisposeBackend(ServiceWorkerBackend backend)
    {
    }

    /// <summary>Returns a backend that reads the storage of <see cref="Backend"/> (a new instance for a persistent storage).</summary>
    protected virtual ServiceWorkerBackend Reopen()
    {
        DisposeBackend(Backend);
        _backend = CreateBackend();
        return _backend;
    }

    public void Dispose()
    {
        if (_backend is not null)
        {
            DisposeBackend(_backend);
        }

        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    private static StoredServiceWorker Worker(string id, string state, string body) => new()
    {
        Id = id,
        ScriptUrl = "https://example.com/sw.js",
        Type = "classic",
        State = state,
        ClassicScriptsImported = true,
        EventTypesToHandle = ["fetch", "install"],
        UsedScriptUrls = ["https://example.com/import.js"],
        Scripts =
        [
            new StoredServiceWorkerScript { Url = "https://example.com/sw.js", Status = 200, StatusText = "OK", Headers = [new("Content-Type", "text/javascript")], Body = System.Text.Encoding.UTF8.GetBytes(body) },
            new StoredServiceWorkerScript { Url = "https://example.com/import.js", ResponseUrl = "https://example.com/import2.js", Headers = [new("Content-Type", "text/javascript")], Body = System.Text.Encoding.UTF8.GetBytes("// import") },
        ],
    };

    private static StoredServiceWorkerRegistration Registration(string scope, StoredServiceWorker? waiting = null, StoredServiceWorker? active = null) => new()
    {
        ScopeUrl = scope,
        UpdateViaCache = "none",
        LastUpdateCheckTime = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero),
        NavigationPreloadEnabled = true,
        NavigationPreloadHeaderValue = "hello",
        Waiting = waiting,
        Active = active,
    };

    [Fact]
    public void Registrations_AreEmptyByDefault()
    {
        Assert.Empty(Backend.GetRegistrations(Origin));
    }

    [Fact]
    public void Registrations_AreSavedWithTheirWorkers()
    {
        Backend.SaveRegistration(Origin, Registration("https://example.com/scope/", waiting: Worker("2", "installed", "// new"), active: Worker("1", "activated", "// old")));

        var registration = Assert.Single(Reopen().GetRegistrations(Origin));
        Assert.Equal("https://example.com/scope/", registration.ScopeUrl);
        Assert.Equal("none", registration.UpdateViaCache);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), registration.LastUpdateCheckTime);
        Assert.True(registration.NavigationPreloadEnabled);
        Assert.Equal("hello", registration.NavigationPreloadHeaderValue);

        var waiting = registration.Waiting!;
        Assert.Equal("2", waiting.Id);
        Assert.Equal("installed", waiting.State);
        Assert.True(waiting.ClassicScriptsImported);
        Assert.Equal(["fetch", "install"], waiting.EventTypesToHandle);
        Assert.Equal(["https://example.com/import.js"], waiting.UsedScriptUrls);
        Assert.Equal(["https://example.com/sw.js", "https://example.com/import.js"], waiting.Scripts.Select(script => script.Url));
        Assert.Equal("// new", System.Text.Encoding.UTF8.GetString(waiting.Scripts[0].Body.Span));
        Assert.Equal("OK", waiting.Scripts[0].StatusText);
        Assert.Equal([new("Content-Type", "text/javascript")], waiting.Scripts[0].Headers);
        Assert.Equal("https://example.com/import2.js", waiting.Scripts[1].ResponseUrl);

        var active = registration.Active!;
        Assert.Equal("1", active.Id);
        Assert.Equal("activated", active.State);
        Assert.Equal("// old", System.Text.Encoding.UTF8.GetString(active.Scripts[0].Body.Span));
    }

    [Fact]
    public void SaveRegistration_ReplacesTheRegistrationOfTheScope()
    {
        Backend.SaveRegistration(Origin, Registration("https://example.com/a/", waiting: Worker("1", "installed", "// 1")));
        Backend.SaveRegistration(Origin, Registration("https://example.com/b/"));
        Backend.SaveRegistration(Origin, Registration("https://example.com/a/", active: Worker("1", "activated", "// 1")));

        var registrations = Reopen().GetRegistrations(Origin);
        Assert.Equal(["https://example.com/a/", "https://example.com/b/"], registrations.Select(registration => registration.ScopeUrl));
        Assert.Null(registrations[0].Waiting);
        Assert.Equal("activated", registrations[0].Active!.State);
        Assert.Equal(2, registrations[0].Active!.Scripts.Count);
    }

    [Fact]
    public void Registrations_AreSeparatedByOrigin()
    {
        Backend.SaveRegistration(Origin, Registration("https://example.com/"));

        Assert.Empty(Reopen().GetRegistrations("https://other.example"));
        Assert.Single(Backend.GetRegistrations(Origin));
    }

    [Fact]
    public void DeleteRegistration_RemovesTheRegistration()
    {
        Backend.SaveRegistration(Origin, Registration("https://example.com/", active: Worker("1", "activated", "// 1")));

        Assert.False(Backend.DeleteRegistration(Origin, "https://example.com/other/"));
        Assert.True(Backend.DeleteRegistration(Origin, "https://example.com/"));
        Assert.False(Backend.DeleteRegistration(Origin, "https://example.com/"));
        Assert.Empty(Reopen().GetRegistrations(Origin));
    }
}

public sealed class InMemoryServiceWorkerBackendTests : ServiceWorkerBackendTests
{
    private readonly InMemoryServiceWorkerBackend _backend = new();

    protected override ServiceWorkerBackend CreateBackend() => _backend;

    // The registrations are kept by the instance
    protected override ServiceWorkerBackend Reopen() => Backend;
}

public sealed class SqliteServiceWorkerBackendTests : ServiceWorkerBackendTests
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "service-workers-" + Guid.NewGuid().ToString("N") + ".db");

    protected override ServiceWorkerBackend CreateBackend() => new SqliteServiceWorkerBackend(_path);

    protected override void DisposeBackend(ServiceWorkerBackend backend) => ((SqliteServiceWorkerBackend)backend).Dispose();

    protected override void Dispose(bool disposing)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);

        base.Dispose(disposing);
    }
}
