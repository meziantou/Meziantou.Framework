using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>
/// The APIs whose behavior depends on the device, implemented by the callbacks of <see cref="BrowsingContextOptions"/>:
/// Web Share (https://w3c.github.io/web-share/), Vibration (https://w3c.github.io/vibration/), Geolocation
/// (https://w3c.github.io/geolocation/), Notifications (https://notifications.spec.whatwg.org/), Badging
/// (https://w3c.github.io/badging/), Battery Status (https://w3c.github.io/battery/), Screen Wake Lock
/// (https://w3c.github.io/screen-wake-lock/), Keyboard Lock (https://wicg.github.io/keyboard-lock/), Network Information
/// (https://wicg.github.io/netinfo/), Device Memory (https://www.w3.org/TR/device-memory/) and Global Privacy Control
/// (https://w3c.github.io/gpc/).
/// </summary>
public sealed class HostDeviceApisTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, BrowsingContextOptions? options = null, string url = "https://example.com/")
    {
        options ??= new BrowsingContextOptions();
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            Permissions = options.Permissions,
            RequestPermission = options.RequestPermission,
            Share = options.Share,
            Vibrate = options.Vibrate,
            Geolocation = options.Geolocation,
            ShowNotification = options.ShowNotification,
            CloseNotification = options.CloseNotification,
            Badging = options.Badging,
            BatteryStatus = options.BatteryStatus,
            WakeLock = options.WakeLock,
            KeyboardLock = options.KeyboardLock,
            NetworkInformation = options.NetworkInformation,
            GlobalPrivacyControl = options.GlobalPrivacyControl,
            ClientHints = options.ClientHints,
            Features = options.Features,
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    private string Run(Document document, string? script = null)
    {
        if (script is not null)
        {
            TestScriptEngines.Evaluate(_engine, document.DefaultView!, script);
        }

        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document.Title;
    }

    private async Task<string> RunAsync(Document document, string? script = null)
    {
        if (script is not null)
        {
            TestScriptEngines.Evaluate(_engine, document.DefaultView!, script);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _contexts.EventLoop.RunUntilIdleAsync(timeout.Token);
        Assert.Empty(_errors);
        return document.Title;
    }

    /// <summary>Gives the window transient (and sticky) activation, as an activation triggering input event of the user does.</summary>
    private static void Activate(Document document) => Window.NotifyActivation(document);

    private const string ShareScript = "window.go = data => navigator.share(data).then(() => document.title = 'shared', e => document.title = e.name);";

    // Web Share

    [Fact]
    public void Share_WithoutHandler_IsRejectedWithAbortError()
    {
        var document = Load("<script>" + ShareScript + "</script>");
        Activate(document);

        Assert.Equal("AbortError", Run(document, "go({ title: 'title' })"));
    }

    [Fact]
    public void Share_CallsTheHandlerWithTheValidatedData()
    {
        #region WebPlatform_Share
        var shared = new List<string>();
        var options = new BrowsingContextOptions
        {
            // Plays the role of the share sheet of the operating system: true when the data is shared, false when the
            // user cancels (the promise of navigator.share() is rejected with an AbortError)
            Share = (callbackContext, request) =>
            {
                shared.Add($"{request.Title}|{request.Text}|{request.Url}|{request.Files.Count}");
                return ValueTask.FromResult(true);
            },
        };
        #endregion

        var document = Load("<script>" + ShareScript + "</script>", options);
        Activate(document);

        Assert.Equal("shared", Run(document, "go({ title: 'title', text: 'text', url: 'page?q=1', files: [new File(['x'], 'a.txt')] })"));
        Assert.Equal(["title|text|https://example.com/page?q=1|1"], shared);
        Assert.False(document.DefaultView!.HasTransientActivation);
    }

    [Fact]
    public void Share_CanceledByTheUser_IsRejectedWithAbortError()
    {
        var document = Load("<script>" + ShareScript + "</script>", new BrowsingContextOptions { Share = (_, _) => ValueTask.FromResult(false) });
        Activate(document);

        Assert.Equal("AbortError", Run(document, "go({ text: 'text' })"));
    }

    [Fact]
    public async Task Share_AsynchronousHandler_SettlesThePromiseWhenTheTaskCompletes()
    {
        var document = Load("<script>" + ShareScript + "</script>", new BrowsingContextOptions
        {
            Share = async (_, _) =>
            {
                await Task.Delay(10);
                return true;
            },
        });
        Activate(document);

        Assert.Equal("shared", await RunAsync(document, "go({ text: 'text' })"));
    }

    [Fact]
    public void Share_FailingHandler_IsRejectedWithDataError()
    {
        var document = Load("<script>" + ShareScript + "</script>", new BrowsingContextOptions { Share = (_, _) => ValueTask.FromException<bool>(new InvalidOperationException("failed")) });
        Activate(document);

        Assert.Equal("DataError", Run(document, "go({ text: 'text' })"));
    }

    [Fact]
    public void Share_WithoutUserActivation_IsRejectedWithNotAllowedError()
    {
        var calls = 0;
        var document = Load("<script>" + ShareScript + "go({ text: 'text' });</script>", new BrowsingContextOptions { Share = (_, _) => ValueTask.FromResult(++calls > 0) });

        Assert.Equal("NotAllowedError", Run(document));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Share_InvalidData_IsRejectedWithTypeError()
    {
        var document = Load("<script>" + ShareScript + "</script>", new BrowsingContextOptions { Share = (_, _) => ValueTask.FromResult(true) });
        Activate(document);

        Assert.Equal("TypeError", Run(document, "go({ url: 'data:text/plain,x' })"));
    }

    [Fact]
    public void Share_WhileAShareIsInProgress_IsRejectedWithInvalidStateError()
    {
        var pending = new TaskCompletionSource<bool>();
        var document = Load("<script>window.results = [];</script>", new BrowsingContextOptions { Share = (_, _) => new ValueTask<bool>(pending.Task) });
        Activate(document);
        TestScriptEngines.Evaluate(_engine, document.DefaultView!, "navigator.share({ text: 'a' }).then(() => results.push('first'))");
        Activate(document);

        Run(document, "navigator.share({ text: 'b' }).catch(e => document.title = e.name)");
        Assert.Equal("InvalidStateError", document.Title);
        pending.SetResult(true);
    }

    [Fact]
    public void CanShare_ValidatesTheData()
    {
        var document = Load("""
            <script>
              document.title = [
                navigator.canShare(), navigator.canShare({}), navigator.canShare({ url: 'data:text/plain,x' }), navigator.canShare({ url: 'https://[' }),
                navigator.canShare({ text: 'x' }), navigator.canShare({ url: 'page' }), navigator.canShare({ files: [] }), navigator.canShare({ files: [new File(['x'], 'a.txt')] }),
              ].join();
            </script>
            """);

        Assert.Equal("false,false,false,false,true,true,false,true", Run(document));
    }

    [Fact]
    public void Share_IsOnlyExposedToTheSecureContexts()
    {
        var document = Load("<script>document.title = ['share' in navigator, 'canShare' in navigator].join();</script>", url: "http://example.com/");

        Assert.Equal("false,false", Run(document));
    }

    // Vibration

    [Fact]
    public void Vibrate_WithoutStickyActivation_ReturnsFalse()
    {
        var patterns = new List<string>();
        var document = Load("<script>document.title = navigator.vibrate(100);</script>", new BrowsingContextOptions { Vibrate = (_, pattern) => patterns.Add(string.Join(',', pattern)) });

        Assert.Equal("false", Run(document));
        Assert.Empty(patterns);
    }

    [Fact]
    public void Vibrate_WithoutHandler_ReturnsTrue()
    {
        var document = Load("<p>");
        Activate(document);

        Assert.Equal("true", Run(document, "document.title = navigator.vibrate(100)"));
    }

    [Fact]
    public void Vibrate_NormalizesThePattern()
    {
        #region WebPlatform_Vibrate
        var patterns = new List<string>();
        var options = new BrowsingContextOptions
        {
            // The durations alternate between the vibrations and the pauses; an empty pattern cancels the vibration
            Vibrate = (callbackContext, pattern) => patterns.Add(string.Join(',', pattern.Select(duration => duration.TotalMilliseconds))),
        };
        #endregion

        var document = Load("<p>", options);
        Activate(document);

        Assert.Equal("true,true", Run(document, "document.title = [navigator.vibrate(50), navigator.vibrate([100, 200, 20000, 50])].join()"));
        Assert.Equal(["50", "100,200,10000"], patterns);
    }

    [Fact]
    public void Vibrate_PatternOfZeros_CancelsTheVibration()
    {
        var patterns = new List<string>();
        var document = Load("<p>", new BrowsingContextOptions { Vibrate = (_, pattern) => patterns.Add(string.Join(',', pattern.Select(duration => duration.TotalMilliseconds))) });
        Activate(document);

        Run(document, "navigator.vibrate(10000); navigator.vibrate([0, 100]); navigator.vibrate([]);");

        Assert.Equal(["10000", ""], patterns);
    }

    [Fact]
    public void Vibrate_DocumentHidden_CancelsTheVibration()
    {
        var patterns = new List<string>();
        var document = Load("<p>", new BrowsingContextOptions { Vibrate = (_, pattern) => patterns.Add(string.Join(',', pattern.Select(duration => duration.TotalMilliseconds))) });
        Activate(document);
        Run(document, "navigator.vibrate(10000)");

        document.BrowsingContext!.IsVisible = false;
        Run(document);
        Run(document, "document.title = navigator.vibrate(100)");

        Assert.Equal(["10000", ""], patterns);
        Assert.Equal("false", document.Title);
    }

    [Fact]
    public void Vibrate_InvalidPattern_ThrowsTypeError()
    {
        var document = Load("<script>try { navigator.vibrate(); } catch (e) { document.title = e.name; }</script>");

        Assert.Equal("TypeError", Run(document));
    }

    // Geolocation

    private const string GeolocationScript = """
        window.log = [];
        window.onPosition = p => log.push('position ' + p.coords.latitude + ',' + p.coords.longitude + ',' + p.coords.accuracy + ',' + p.coords.altitude + ',' + (p.timestamp > 0));
        window.onError = e => log.push('error ' + e.code + ' ' + (e.code === GeolocationPositionError.PERMISSION_DENIED ? 'PERMISSION_DENIED' : e.code === e.POSITION_UNAVAILABLE ? 'POSITION_UNAVAILABLE' : 'TIMEOUT'));
        window.done = () => document.title = log.join('|');
        """;

    private static PermissionStore GrantedPermissions(string name)
    {
        var permissions = new PermissionStore();
        permissions.SetState(name, PermissionState.Granted);
        return permissions;
    }

    [Fact]
    public void GetCurrentPosition_WithoutPermission_IsDenied()
    {
        var provider = new GeolocationProvider { Position = new GeolocationReading(1, 2, 3) };
        var document = Load("<script>" + GeolocationScript + "navigator.geolocation.getCurrentPosition(onPosition, onError);</script>", new BrowsingContextOptions { Geolocation = provider });

        Assert.Equal("error 1 PERMISSION_DENIED", Run(document, "done()"));
    }

    [Fact]
    public void GetCurrentPosition_ReturnsThePositionOfTheProvider()
    {
        #region WebPlatform_Geolocation
        var geolocation = new GeolocationProvider
        {
            // The position of the device; null when it is unavailable
            Position = new GeolocationReading(latitude: 48.8584, longitude: 2.2945, accuracy: 10) { Altitude = 35 },
        };

        var permissions = new PermissionStore();
        permissions.SetState("geolocation", PermissionState.Granted);

        var options = new BrowsingContextOptions
        {
            Geolocation = geolocation,
            Permissions = permissions,
        };
        #endregion

        var document = Load("<script>" + GeolocationScript + "navigator.geolocation.getCurrentPosition(onPosition, onError);</script>", options);

        Assert.Equal("position 48.8584,2.2945,10,35,true", Run(document, "done()"));
    }

    [Fact]
    public void GetCurrentPosition_PositionCanBeSerialized()
    {
        var document = Load("""
            <script>
              navigator.geolocation.getCurrentPosition(p => {
                const json = p.toJSON();
                document.title = JSON.stringify(json.coords) + ':' + (json.timestamp === p.timestamp) + ':' + (p.coords instanceof GeolocationCoordinates) + ':' + (p instanceof GeolocationPosition);
              });
            </script>
            """, new BrowsingContextOptions { Geolocation = new GeolocationProvider { Position = new GeolocationReading(1, 2, 3) { Speed = 0, Heading = 90 } }, Permissions = GrantedPermissions("geolocation") });

        Assert.Equal("""{"accuracy":3,"latitude":1,"longitude":2,"altitude":null,"altitudeAccuracy":null,"heading":null,"speed":0}:true:true:true""", Run(document));
    }

    [Fact]
    public void GetCurrentPosition_WithoutProvider_IsUnavailable()
    {
        var document = Load("<script>" + GeolocationScript + "navigator.geolocation.getCurrentPosition(onPosition, onError);</script>", new BrowsingContextOptions { Permissions = GrantedPermissions("geolocation") });

        Assert.Equal("error 2 POSITION_UNAVAILABLE", Run(document, "done()"));
    }

    [Fact]
    public void GetCurrentPosition_AsksThePermissionOnce()
    {
        var requests = new List<string>();
        var permissions = new PermissionStore();
        var document = Load("<script>" + GeolocationScript + "</script>", new BrowsingContextOptions
        {
            Permissions = permissions,
            Geolocation = new GeolocationProvider { Position = new GeolocationReading(1, 2, 3) },
            RequestPermission = (callbackContext, name) =>
            {
                requests.Add(name + " " + callbackContext.Document!.Url);
                return PermissionState.Granted;
            },
        });

        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError)");
        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError)");
        Run(document, "navigator.permissions.query({ name: 'geolocation' }).then(s => log.push(s.state))");

        Assert.Equal("position 1,2,3,null,true|position 1,2,3,null,true|granted", Run(document, "done()"));
        Assert.Equal(["geolocation https://example.com/"], requests);
        Assert.Equal(PermissionState.Granted, permissions.GetState("geolocation", "https://example.com"));
    }

    [Fact]
    public void GetCurrentPosition_PermissionDeniedByTheUser_IsNotAskedAgain()
    {
        var requests = 0;
        var document = Load("<script>" + GeolocationScript + "</script>", new BrowsingContextOptions
        {
            Geolocation = new GeolocationProvider { Position = new GeolocationReading(1, 2, 3) },
            RequestPermission = (_, _) =>
            {
                requests++;
                return PermissionState.Denied;
            },
        });

        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError)");
        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError)");

        Assert.Equal("error 1 PERMISSION_DENIED|error 1 PERMISSION_DENIED", Run(document, "done()"));
        Assert.Equal(1, requests);
    }

    [Fact]
    public void GetCurrentPosition_InsecureContext_IsDenied()
    {
        var document = Load("<script>" + GeolocationScript + "navigator.geolocation.getCurrentPosition(onPosition, onError);</script>", new BrowsingContextOptions
        {
            Geolocation = new GeolocationProvider { Position = new GeolocationReading(1, 2, 3) },
            Permissions = GrantedPermissions("geolocation"),
        }, url: "http://example.com/");

        Assert.Equal("error 1 PERMISSION_DENIED", Run(document, "done()"));
    }

    [Fact]
    public void GetCurrentPosition_TimeoutZero_FailsUnlessACachedPositionIsRecentEnough()
    {
        var provider = new GeolocationProvider { Position = new GeolocationReading(1, 2, 3) };
        var document = Load("<script>" + GeolocationScript + "</script>", new BrowsingContextOptions { Geolocation = provider, Permissions = GrantedPermissions("geolocation") });

        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError, { timeout: 0 })");
        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError)");
        provider.Position = new GeolocationReading(4, 5, 6);
        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError, { timeout: 0, maximumAge: 60000 })");

        Assert.Equal("error 3 TIMEOUT|position 1,2,3,null,true|position 1,2,3,null,true", Run(document, "done()"));
    }

    [Fact]
    public async Task GetCurrentPosition_SlowProvider_TimesOut()
    {
        var provider = new SlowGeolocationProvider();
        var document = Load("<script>" + GeolocationScript + "</script>", new BrowsingContextOptions { Geolocation = provider, Permissions = GrantedPermissions("geolocation") });

        await RunAsync(document, "navigator.geolocation.getCurrentPosition(onPosition, onError, { timeout: 50 })");

        Assert.Equal("error 3 TIMEOUT", Run(document, "done()"));

        // The provider observes the cancellation on another thread, possibly after the error callback
        await provider.Canceled.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetCurrentPosition_AsynchronousProvider()
    {
        var document = Load("<script>" + GeolocationScript + "</script>", new BrowsingContextOptions { Geolocation = new DelayedGeolocationProvider(), Permissions = GrantedPermissions("geolocation") });

        await RunAsync(document, "navigator.geolocation.getCurrentPosition(onPosition, onError)");

        Assert.Equal("position 7,8,9,null,true", Run(document, "done()"));
    }

    [Fact]
    public void WatchPosition_IsUpdatedWhenThePositionChanges()
    {
        var provider = new GeolocationProvider { Position = new GeolocationReading(1, 1, 1) };
        var document = Load("<script>" + GeolocationScript + "window.id = navigator.geolocation.watchPosition(onPosition, onError);</script>", new BrowsingContextOptions { Geolocation = provider, Permissions = GrantedPermissions("geolocation") });

        provider.Position = new GeolocationReading(2, 2, 2);
        Run(document);
        provider.Position = null;
        Run(document);
        Run(document, "navigator.geolocation.clearWatch(id)");
        provider.Position = new GeolocationReading(3, 3, 3);

        Assert.Equal("position 1,1,1,null,true|position 2,2,2,null,true|error 2 POSITION_UNAVAILABLE", Run(document, "done() + ':' + (id > 0)"));
    }

    [Fact]
    public void WatchPosition_HiddenDocument_GetsThePositionWhenItBecomesVisible()
    {
        var provider = new GeolocationProvider { Position = new GeolocationReading(1, 1, 1) };
        var document = Load("<script>" + GeolocationScript + "navigator.geolocation.watchPosition(onPosition, onError);</script>", new BrowsingContextOptions { Geolocation = provider, Permissions = GrantedPermissions("geolocation") });
        var context = document.BrowsingContext!;

        context.IsVisible = false;
        Run(document);
        provider.Position = new GeolocationReading(2, 2, 2);
        Run(document);
        Assert.Equal("position 1,1,1,null,true", Run(document, "done()"));

        context.IsVisible = true;
        Run(document);
        Assert.Equal("position 1,1,1,null,true|position 2,2,2,null,true", Run(document, "done()"));
    }

    private sealed class SlowGeolocationProvider : GeolocationProvider
    {
        private readonly TaskCompletionSource _canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Canceled => _canceled.Task;

        public override async ValueTask<GeolocationReading?> GetPositionAsync(GeolocationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _canceled.TrySetResult();
                throw;
            }

            return null;
        }
    }

    private sealed class DelayedGeolocationProvider : GeolocationProvider
    {
        public override async ValueTask<GeolocationReading?> GetPositionAsync(GeolocationRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            return new GeolocationReading(7, 8, 9);
        }
    }

    // Notifications

    [Fact]
    public void Notification_WithoutPermission_FiresError()
    {
        var shown = 0;
        var document = Load("""
            <script>
              const n = new Notification('title');
              n.onshow = () => document.title = 'show';
              n.onerror = () => document.title = 'error:' + Notification.permission;
            </script>
            """, new BrowsingContextOptions { ShowNotification = (_, _) => shown++ });

        Assert.Equal("error:default", Run(document));
        Assert.Equal(0, shown);
    }

    [Fact]
    public void Notification_RequestPermission_AsksTheUser()
    {
        var requests = 0;
        var permissions = new PermissionStore();
        var document = Load("""
            <script>
              Notification.requestPermission(p => document.title = 'callback:' + p)
                .then(p => Notification.requestPermission())
                .then(p => document.title += ',promise:' + p + ',' + Notification.permission);
            </script>
            """, new BrowsingContextOptions
        {
            Permissions = permissions,
            RequestPermission = (_, name) =>
            {
                requests++;
                return name == "notifications" ? PermissionState.Granted : PermissionState.Denied;
            },
        });

        Assert.Equal("callback:granted,promise:granted,granted", Run(document));
        Assert.Equal(1, requests);
        Assert.Equal(PermissionState.Granted, permissions.GetState("notifications", "https://example.com"));
    }

    [Fact]
    public void Notification_RequestPermissionWithoutHandler_IsDenied()
    {
        var document = Load("<script>Notification.requestPermission().then(p => document.title = p + ',' + Notification.permission);</script>");

        Assert.Equal("denied,default", Run(document));
    }

    [Fact]
    public void Notification_IsShownByTheHost()
    {
        #region WebPlatform_Notifications
        var notifications = new List<Notification>();
        var permissions = new PermissionStore();
        permissions.SetState("notifications", PermissionState.Granted);
        var options = new BrowsingContextOptions
        {
            Permissions = permissions,

            // Display the notification; call notification.Click() or notification.Dismiss() when the user interacts with it
            ShowNotification = (callbackContext, notification) => notifications.Add(notification),
            CloseNotification = (callbackContext, notification) => notifications.Remove(notification),
        };
        #endregion

        var document = Load("""
            <script>
              const n = new Notification('title', { body: 'body', tag: 'tag', icon: 'icon.png', lang: 'fr', dir: 'rtl', data: { value: 42 }, timestamp: 1000 });
              n.onshow = () => document.title = ['show', n.title, n.body, n.tag, n.icon, n.image, n.lang, n.dir, n.data.value, n.data === n.data, n.timestamp, n.silent, n.renotify, n.actions.length, Notification.maxActions].join();
            </script>
            """, options);

        Assert.Equal("show,title,body,tag,https://example.com/icon.png,,fr,rtl,42,true,1000,,false,0,0", Run(document));
        var notification = Assert.Single(notifications);
        Assert.Equal("title", notification.Title);
        Assert.Equal("body", notification.Body);
        Assert.True(notification.IsShown);
    }

    [Fact]
    public void Notification_ClickAndDismissFromTheHost()
    {
        var notifications = new List<Notification>();
        var closed = 0;
        var document = Load("""
            <script>
              window.log = [];
              const n = new Notification('title');
              n.onclick = e => log.push('click ' + e.cancelable);
              n.onclose = () => log.push('close');
            </script>
            """, new BrowsingContextOptions
        {
            Permissions = GrantedPermissions("notifications"),
            ShowNotification = (_, notification) => notifications.Add(notification),
            CloseNotification = (_, _) => closed++,
        });

        var notification = Assert.Single(notifications);
        notification.Click();
        notification.Dismiss();
        notification.Dismiss();
        Run(document);

        Assert.Equal("click true,close", Run(document, "document.title = log.join()"));
        Assert.False(notification.IsShown);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void Notification_CloseFromTheScript_RemovesItFromTheHost()
    {
        var closed = new List<string>();
        var document = Load("""
            <script>
              const n = new Notification('title');
              n.onshow = () => n.close();
              n.onclose = () => document.title = 'close';
            </script>
            """, new BrowsingContextOptions
        {
            Permissions = GrantedPermissions("notifications"),
            CloseNotification = (_, notification) => closed.Add(notification.Title),
        });

        Assert.Equal("close", Run(document));
        Assert.Equal(["title"], closed);
    }

    [Fact]
    public void Notification_SameTag_ReplacesTheNotification()
    {
        var events = new List<string>();
        var document = Load("""
            <script>
              const first = new Notification('first', { tag: 'tag' });
              first.onclose = () => document.title += 'first closed;';
              first.onshow = () => new Notification('second', { tag: 'tag' }).onshow = () => document.title += 'second shown;';
            </script>
            """, new BrowsingContextOptions
        {
            Permissions = GrantedPermissions("notifications"),
            ShowNotification = (_, notification) => events.Add("show " + notification.Title),
            CloseNotification = (_, notification) => events.Add("close " + notification.Title),
        });

        Assert.Equal("first closed;second shown;", Run(document));
        Assert.Equal(["show first", "close first", "show second"], events);
    }

    [Fact]
    public void Notification_InvalidOptions_ThrowTypeError()
    {
        var document = Load("""
            <script>
              const errors = [];
              try { new Notification('t', { actions: [{ action: 'a', title: 'A' }] }); } catch (e) { errors.push(e.name); }
              try { new Notification('t', { renotify: true }); } catch (e) { errors.push(e.name); }
              document.title = errors.join();
            </script>
            """);

        Assert.Equal("TypeError,TypeError", Run(document));
    }

    // Badging

    private const string BadgeScript = "window.go = p => p.then(() => document.title = 'ok', e => document.title = e.name);";

    [Fact]
    public void SetAppBadge_CallsTheHost()
    {
        #region WebPlatform_Badging
        var badges = new List<string>();
        var options = new BrowsingContextOptions
        {
            Badging = new BadgingOptions
            {
                // Shows the badge on the icon of the application: a number, a flag (setAppBadge() without argument), or nothing
                SetBadge = (callbackContext, badge) => badges.Add(badge.Kind switch
                {
                    AppBadgeKind.Count => badge.Count.ToString(CultureInfo.InvariantCulture),
                    AppBadgeKind.Flag => "flag",
                    _ => "nothing",
                }),
            },
        };
        #endregion

        var document = Load("<script>" + BadgeScript + "</script>", options);

        Assert.Equal("ok", Run(document, "go(navigator.setAppBadge(3).then(() => navigator.setAppBadge()).then(() => navigator.setAppBadge(0)).then(() => navigator.clearAppBadge()))"));
        Assert.Equal(["3", "flag", "nothing", "nothing"], badges);
    }

    [Fact]
    public void SetAppBadge_WithoutHandler_Resolves()
    {
        var document = Load("<script>" + BadgeScript + "</script>");

        Assert.Equal("ok", Run(document, "go(navigator.setAppBadge(1))"));
    }

    [Fact]
    public void SetAppBadge_InvalidValue_IsRejectedWithTypeError()
    {
        var document = Load("<script>" + BadgeScript + "</script>");

        Assert.Equal("TypeError", Run(document, "go(navigator.setAppBadge(-1))"));
    }

    [Fact]
    public void SetAppBadge_RequiresNotificationsPermission()
    {
        var badges = new List<AppBadge>();
        var document = Load("<script>" + BadgeScript + "</script>", new BrowsingContextOptions
        {
            Badging = new BadgingOptions { SetBadge = (_, badge) => badges.Add(badge), RequiresNotificationsPermission = true },
        });

        Assert.Equal("NotAllowedError", Run(document, "go(navigator.setAppBadge(1))"));
        Assert.Empty(badges);
    }

    [Fact]
    public void SetAppBadge_NotFullyActive_IsRejectedWithInvalidStateError()
    {
        var document = Load("""
            <iframe srcdoc="<p>"></iframe>
            <script>
              window.onload = () => {
                const frame = document.querySelector('iframe');
                const navigator = frame.contentWindow.navigator;
                frame.remove();
                navigator.setAppBadge(1).catch(e => document.title = e.name);
              };
            </script>
            """);

        Assert.Equal("InvalidStateError", Run(document));
    }

    // Battery Status

    private const string BatteryScript = """
        window.log = [];
        window.describe = b => [b.charging, b.chargingTime, b.dischargingTime, b.level].join(',');
        navigator.getBattery().then(b => {
          window.battery = b;
          log.push(describe(b));
          for (const type of ['chargingchange', 'chargingtimechange', 'dischargingtimechange', 'levelchange'])
            b.addEventListener(type, () => log.push(type));
        });
        window.done = () => document.title = log.join('|');
        """;

    [Fact]
    public void GetBattery_WithoutProvider_ReportsAFullBatteryThatIsPluggedIn()
    {
        var document = Load("<script>" + BatteryScript + "</script>");

        Assert.Equal("true,0,Infinity,1", Run(document, "done()"));
    }

    [Fact]
    public void GetBattery_ReadsTheProvider()
    {
        #region WebPlatform_BatteryStatus
        var battery = new BatteryStatusProvider
        {
            // The battery of the device: setting the status updates the BatteryManager objects, which fire their change events
            Status = new BatteryStatus { Charging = false, Level = 0.5, ChargingTime = null, DischargingTime = TimeSpan.FromHours(2) },
        };

        var options = new BrowsingContextOptions
        {
            BatteryStatus = new BatteryStatusOptions { Provider = battery },
        };
        #endregion

        var document = Load("<script>" + BatteryScript + "</script>", options);
        Run(document);

        battery.Status = new BatteryStatus { Charging = true, Level = 0.556789, ChargingTime = TimeSpan.FromMinutes(30), DischargingTime = null };
        Run(document);

        Assert.Equal("false,Infinity,7200,0.5|chargingchange|chargingtimechange|dischargingtimechange|levelchange", Run(document, "done()"));
        Assert.Equal("true,1800,Infinity,0.56", Run(document, "document.title = describe(battery)"));
    }

    [Fact]
    public void GetBattery_ReturnsTheSamePromise()
    {
        var document = Load("<script>document.title = navigator.getBattery() === navigator.getBattery();</script>");

        Assert.Equal("true", Run(document));
    }

    [Fact]
    public void GetBattery_HiddenDocument_FiresTheEventsWhenItBecomesVisible()
    {
        var battery = new BatteryStatusProvider();
        var document = Load("<script>" + BatteryScript + "</script>", new BrowsingContextOptions { BatteryStatus = new BatteryStatusOptions { Provider = battery } });
        Run(document);

        document.BrowsingContext!.IsVisible = false;
        Run(document);
        battery.Status = new BatteryStatus { Level = 0.8 };
        Run(document);
        Assert.Equal("true,0,Infinity,1", Run(document, "done()"));

        document.BrowsingContext.IsVisible = true;
        Run(document);
        Assert.Equal("true,0,Infinity,1|levelchange", Run(document, "done()"));
    }

    // Screen Wake Lock

    private const string WakeLockScript = """
        window.log = [];
        window.request = () => navigator.wakeLock.request('screen').then(lock => {
          lock.onrelease = () => log.push('release ' + lock.released);
          log.push('locked ' + lock.type);
          return lock;
        }, e => log.push(e.name));
        window.done = () => document.title = log.join('|');
        """;

    [Fact]
    public void WakeLock_WithoutPermission_IsRejectedWithNotAllowedError()
    {
        var document = Load("<script>" + WakeLockScript + "request();</script>");

        Assert.Equal("NotAllowedError", Run(document, "done()"));
    }

    [Fact]
    public void WakeLock_AcquiresAndReleasesTheScreenLock()
    {
        #region WebPlatform_WakeLock
        var screenOn = false;
        var permissions = new PermissionStore();
        permissions.SetState("screen-wake-lock", PermissionState.Granted);

        var options = new BrowsingContextOptions
        {
            Permissions = permissions,
            WakeLock = new WakeLockOptions
            {
                // Called when a document gets its first lock, and when its last lock is released
                Acquire = (callbackContext, type) => screenOn = true,
                Release = (callbackContext, type) => screenOn = false,
            },
        };
        #endregion

        var document = Load("<script>" + WakeLockScript + "</script>", options);
        Run(document, "Promise.all([request(), request()]).then(locks => window.locks = locks)");
        Assert.True(screenOn);

        Run(document, "locks[0].release()");
        Assert.True(screenOn);

        Run(document, "locks[1].release(); locks[1].release()");
        Assert.False(screenOn);
        Assert.Equal("locked screen|locked screen|release true|release true", Run(document, "done()"));
    }

    [Fact]
    public void WakeLock_HiddenDocument_ReleasesTheLocks()
    {
        var acquired = 0;
        var document = Load("<script>" + WakeLockScript + "</script>", new BrowsingContextOptions
        {
            Permissions = GrantedPermissions("screen-wake-lock"),
            WakeLock = new WakeLockOptions { Acquire = (_, _) => acquired++, Release = (_, _) => acquired-- },
        });
        Run(document, "request()");
        Assert.Equal(1, acquired);

        document.BrowsingContext!.IsVisible = false;
        Run(document, "request()");

        Assert.Equal(0, acquired);
        Assert.Equal("locked screen|release true|NotAllowedError", Run(document, "done()"));
    }

    [Fact]
    public void WakeLock_RemovedIframe_ReleasesTheLocks()
    {
        var acquired = 0;
        var document = Load("""<iframe srcdoc="<p>"></iframe>""", new BrowsingContextOptions
        {
            Permissions = GrantedPermissions("screen-wake-lock"),
            WakeLock = new WakeLockOptions { Acquire = (_, _) => acquired++, Release = (_, _) => acquired-- },
        });
        Run(document, "document.querySelector('iframe').contentWindow.navigator.wakeLock.request().then(lock => lock.onrelease = () => document.title = 'released')");
        Assert.Equal(1, acquired);

        Assert.Equal("released", Run(document, "document.querySelector('iframe').remove()"));
        Assert.Equal(0, acquired);
    }

    // Keyboard Lock

    private const string KeyboardScript = "window.go = p => p.then(() => document.title = 'locked', e => document.title = e.name);";

    [Fact]
    public void KeyboardLock_CallsTheHost()
    {
        #region WebPlatform_KeyboardLock
        IReadOnlyList<string>? reservedKeys = null;
        var options = new BrowsingContextOptions
        {
            KeyboardLock = new KeyboardLockOptions
            {
                // The keys to capture while the document is fullscreen (empty: all the keys)
                Lock = (callbackContext, keyCodes) => reservedKeys = keyCodes,
                Unlock = callbackContext => reservedKeys = null,
            },
        };
        #endregion

        var document = Load("<script>" + KeyboardScript + "</script>", options);

        Assert.Equal("locked", Run(document, "go(navigator.keyboard.lock(['KeyW', 'KeyA', 'KeyS', 'KeyD']))"));
        Assert.Equal(["KeyW", "KeyA", "KeyS", "KeyD"], reservedKeys);

        Run(document, "navigator.keyboard.unlock()");
        Assert.Null(reservedKeys);
    }

    [Fact]
    public void KeyboardLock_InvalidKeyCode_IsRejectedWithInvalidAccessError()
    {
        var document = Load("<script>" + KeyboardScript + "</script>");

        Assert.Equal("InvalidAccessError", Run(document, "go(navigator.keyboard.lock(['KeyA', 'NotAKey']))"));
    }

    [Fact]
    public void KeyboardLock_TwoParallelRequests_AbortsTheFirstOne()
    {
        var document = Load("<script>" + KeyboardScript + "</script>");

        Assert.Equal("AbortError|locked", Run(document, """
            Promise.allSettled([navigator.keyboard.lock(['KeyA']), navigator.keyboard.lock(['KeyB'])])
              .then(results => document.title = results.map(r => r.status === 'fulfilled' ? 'locked' : r.reason.name).join('|'))
            """));
    }

    [Fact]
    public void KeyboardLock_FromAnIframe_IsRejectedWithInvalidStateError()
    {
        var document = Load("""<iframe srcdoc="<p>"></iframe><script>""" + KeyboardScript + "</script>");

        Assert.Equal("InvalidStateError", Run(document, "go(document.querySelector('iframe').contentWindow.navigator.keyboard.lock())"));
    }

    private const string ConnectionScript = """
        const connection = navigator.connection;
        const describe = c => [c.type, c.effectiveType, c.downlink, c.downlinkMax, c.rtt, c.saveData].join();
        const events = [];
        connection.onchange = () => events.push(describe(connection));
        function done() { document.title = describe(connection) + '|' + events.join('|'); }
        """;

    [Fact]
    public void Connection_WithoutProvider_ReportsAnUnknownConnection()
    {
        var document = Load("<script>" + ConnectionScript + "</script>");

        Assert.Equal("unknown,4g,10,Infinity,50,false|", Run(document, "done()"));
        Assert.Equal("true", Run(document, "document.title = navigator.connection === navigator.connection"));
    }

    [Fact]
    public void Connection_WithoutProvider_ReadsTheClientHints()
    {
        var document = Load("<script>" + ConnectionScript + "</script>", new BrowsingContextOptions
        {
            ClientHints = new ClientHintsOptions { RoundTripTime = TimeSpan.FromMilliseconds(130), Downlink = 1.23, EffectiveConnectionType = "3g", SaveData = true },
        });

        Assert.Equal("unknown,3g,1.25,Infinity,150,true|", Run(document, "done()"));
    }

    [Fact]
    public void Connection_ReadsTheProvider()
    {
        #region WebPlatform_NetworkInformation
        var network = new NetworkInformationProvider
        {
            // The connection of the device: setting the status updates the NetworkInformation objects, which fire their change event
            Status = new NetworkConnectionStatus
            {
                Type = ConnectionType.Wifi,
                EffectiveType = EffectiveConnectionType.Cellular3G,
                Downlink = 1.5,
                DownlinkMax = 54,
                RoundTripTime = TimeSpan.FromMilliseconds(320),
            },
        };

        var options = new BrowsingContextOptions
        {
            NetworkInformation = new NetworkInformationOptions { Provider = network },
        };
        #endregion

        var document = Load("<script>" + ConnectionScript + "</script>", options);
        Run(document);

        // The values are rounded (multiples of 50 ms and 50 kbit/s, at most 3 s and 10 Mbit/s); an unchanged status fires no event
        network.Status = network.Status with { };
        network.Status = new NetworkConnectionStatus { Type = ConnectionType.Cellular, EffectiveType = EffectiveConnectionType.Slow2G, Downlink = 0.033, RoundTripTime = TimeSpan.FromSeconds(5) };
        Run(document);

        Assert.Equal("cellular,slow-2g,0.05,Infinity,3000,false|cellular,slow-2g,0.05,Infinity,3000,false", Run(document, "done()"));
    }

    [Fact]
    public async Task Connection_InAWorker_ReadsTheProvider()
    {
        var network = new NetworkInformationProvider { Status = new NetworkConnectionStatus { Type = ConnectionType.Ethernet } };
        var document = Load("""
            <script>
            const worker = new Worker(URL.createObjectURL(new Blob(["postMessage([navigator.connection.type, navigator.connection.effectiveType, typeof navigator.deviceMemory, navigator.globalPrivacyControl].join())"])));
            worker.onmessage = e => document.title = e.data;
            </script>
            """, new BrowsingContextOptions { NetworkInformation = new NetworkInformationOptions { Provider = network }, GlobalPrivacyControl = new GlobalPrivacyControlOptions { Enabled = true } });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (document.Title.Length == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await RunAsync(document);
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        Assert.Equal("ethernet,4g,number,true", document.Title);
    }

    [Fact]
    public void DeviceMemory_IsRoundedToAPowerOfTwo()
    {
        Assert.Equal("8", Run(Load("<script>document.title = navigator.deviceMemory</script>")));
        Assert.Equal("2", Run(Load("<script>document.title = navigator.deviceMemory</script>", new BrowsingContextOptions { ClientHints = new ClientHintsOptions { DeviceMemory = 3.5 } })));
        Assert.Equal("0.25", Run(Load("<script>document.title = navigator.deviceMemory</script>", new BrowsingContextOptions { ClientHints = new ClientHintsOptions { DeviceMemory = 0.1 } })));
        Assert.Equal("8", Run(Load("<script>document.title = navigator.deviceMemory</script>", new BrowsingContextOptions { ClientHints = new ClientHintsOptions { DeviceMemory = 64 } })));

        // [SecureContext]
        Assert.Equal("false", Run(Load("<script>document.title = 'deviceMemory' in navigator</script>", url: "http://example.com/")));
    }

    [Fact]
    public void GlobalPrivacyControl_ReadsTheHostPreference()
    {
        #region WebPlatform_GlobalPrivacyControl
        var enabled = true;
        var options = new BrowsingContextOptions
        {
            // The preference of the user, read at each request (the Sec-GPC header) and each read of navigator.globalPrivacyControl
            GlobalPrivacyControl = new GlobalPrivacyControlOptions { IsEnabled = _ => enabled },
        };
        #endregion

        var document = Load("<script>document.title = navigator.globalPrivacyControl</script>", options);
        Assert.Equal("true", Run(document));

        enabled = false;
        Assert.Equal("false", Run(document, "document.title = navigator.globalPrivacyControl"));
        Assert.Equal("false", Run(Load("<script>document.title = navigator.globalPrivacyControl</script>")));
    }

    [Fact]
    public void DisabledFeatures_HideTheNavigatorValues()
    {
        var document = Load(
            "<script>document.title = ['connection' in navigator, typeof NetworkInformation, 'deviceMemory' in navigator, 'globalPrivacyControl' in navigator].join();</script>",
            new BrowsingContextOptions { Features = new WebPlatformFeatures { NetworkInformationEnabled = false, DeviceMemoryEnabled = false, GlobalPrivacyControlEnabled = false } });

        Assert.Equal("false,undefined,false,false", Run(document));
    }

    [Fact]
    public void DisabledFeatures_HideTheApis()
    {
        var document = Load(
            "<script>document.title = ['share' in navigator, 'vibrate' in navigator, 'geolocation' in navigator, typeof Notification, 'setAppBadge' in navigator, 'getBattery' in navigator, typeof BatteryManager, 'wakeLock' in navigator, typeof WakeLockSentinel, 'keyboard' in navigator, typeof Keyboard].join();</script>",
            new BrowsingContextOptions { Features = new WebPlatformFeatures { WebShareEnabled = false, VibrationEnabled = false, GeolocationEnabled = false, NotificationsEnabled = false, BadgingEnabled = false, BatteryStatusEnabled = false, WakeLockEnabled = false, KeyboardLockEnabled = false } });

        Assert.Equal("false,false,false,undefined,false,false,undefined,false,undefined,false,undefined", Run(document));
    }
}
