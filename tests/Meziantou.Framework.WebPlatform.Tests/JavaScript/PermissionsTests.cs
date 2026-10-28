using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The Permissions API (https://w3c.github.io/permissions/): navigator.permissions, PermissionStatus and the permission store.</summary>
public sealed class PermissionsTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, PermissionStore? permissions = null, string url = "https://example.com/")
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            Permissions = permissions,
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    private string Run(Document document)
    {
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public void Query_ReturnsTheStateOfThePermissionStore()
    {
        var permissions = new PermissionStore();
        permissions.SetState("notifications", PermissionState.Granted);
        permissions.SetState("camera", "https://example.com", PermissionState.Denied);
        permissions.SetState("microphone", "https://other.example", PermissionState.Denied);
        var document = Load("""
            <script>
              Promise.all(['geolocation', 'notifications', 'camera', 'microphone'].map(name => navigator.permissions.query({ name })))
                .then(statuses => document.title = statuses.map(status => status.name + '=' + status.state).join(' '));
            </script>
            """, permissions);

        Assert.Equal("geolocation=prompt notifications=granted camera=denied microphone=prompt", Run(document));
    }

    [Fact]
    public void Query_RejectsTheUnsupportedPermissions()
    {
        var document = Load("""
            <script>
              navigator.permissions.query({ name: 'unknown' }).then(() => document.title = 'resolved', e => document.title = e.name);
            </script>
            """);

        Assert.Equal("TypeError", Run(document));
    }

    [Fact]
    public void Query_InANonSecureContext_IsDenied()
    {
        var permissions = new PermissionStore();
        permissions.SetState("geolocation", PermissionState.Granted);
        var document = Load("""
            <script>
              navigator.permissions.query({ name: 'geolocation' }).then(status => document.title = status.state);
            </script>
            """, permissions, url: "http://example.com/");

        Assert.Equal("denied", Run(document));
    }

    [Fact]
    public void SetState_FiresTheChangeEvent()
    {
        var permissions = new PermissionStore();
        var document = Load("""
            <script>
              navigator.permissions.query({ name: 'geolocation' }).then(status => {
                status.onchange = () => document.title += 'onchange:' + status.state + ';';
                status.addEventListener('change', () => document.title += 'listener:' + status.state + ';');
              });
            </script>
            """, permissions);

        permissions.SetState("geolocation", PermissionState.Granted);
        Assert.Equal("onchange:granted;listener:granted;", Run(document));

        // The state of another permission or the same state does not fire the event
        permissions.SetState("camera", PermissionState.Denied);
        permissions.SetState("geolocation", PermissionState.Granted);
        Assert.Equal("onchange:granted;listener:granted;", Run(document));
    }

    [Fact]
    public async Task Query_FromDotNet()
    {
        var permissions = new PermissionStore();
        permissions.SetState("persistent-storage", PermissionState.Granted);
        var document = Load("<body>", permissions);

        var task = document.DefaultView!.Navigator.Permissions.Query(new PermissionDescriptor { Name = "persistent-storage" });
        _contexts.EventLoop.RunUntilIdle();
        var status = await task;
        Assert.Equal("persistent-storage", status.Name);
        Assert.Equal(PermissionState.Granted, status.State);
    }
}
