using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>
/// The APIs whose behavior depends on the device, implemented by the callbacks of <see cref="BrowsingContextOptions"/>:
/// Web Share (https://w3c.github.io/web-share/), Vibration (https://w3c.github.io/vibration/), Geolocation
/// (https://w3c.github.io/geolocation/), Notifications (https://notifications.spec.whatwg.org/), Badging
/// (https://w3c.github.io/badging/), Battery Status (https://w3c.github.io/battery/), Screen Wake Lock
/// (https://w3c.github.io/screen-wake-lock/), Keyboard Lock (https://wicg.github.io/keyboard-lock/), Screen Orientation
/// (https://w3c.github.io/screen-orientation/), VirtualKeyboard (https://w3c.github.io/virtual-keyboard/), the viewport
/// segments (https://drafts.csswg.org/css-viewport/#viewport-segments), Keyboard Map (https://wicg.github.io/keyboard-map/),
/// Media Session (https://w3c.github.io/mediasession/), Page Lifecycle (https://wicg.github.io/page-lifecycle/), Compute
/// Pressure (https://w3c.github.io/compute-pressure/), Idle Detection (https://wicg.github.io/idle-detection/), Device
/// Posture (https://w3c.github.io/device-posture/), Network Information (https://wicg.github.io/netinfo/), Device Memory
/// (https://www.w3.org/TR/device-memory/), Global Privacy Control (https://w3c.github.io/gpc/), the early detection of
/// input events (https://wicg.github.io/is-input-pending/), the sensors (https://w3c.github.io/sensors/) and the device
/// orientation and motion events (https://w3c.github.io/deviceorientation/).
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
            ComputePressure = options.ComputePressure,
            IdleDetection = options.IdleDetection,
            DevicePosture = options.DevicePosture,
            WebAuthentication = options.WebAuthentication,
            UserAgentUI = options.UserAgentUI,
            Sensors = options.Sensors,
            ScreenOrientation = options.ScreenOrientation,
            VirtualKeyboard = options.VirtualKeyboard,
            ViewportSegments = options.ViewportSegments,
            KeyboardMap = options.KeyboardMap,
            MediaSession = options.MediaSession,
            DocumentLoader = options.DocumentLoader,
            PendingInput = options.PendingInput,
            ProtocolHandlers = options.ProtocolHandlers,
            UserPromptHandler = options.UserPromptHandler,
            TimeProvider = options.TimeProvider,
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
        var clock = new FakeTimeProvider();
        var provider = new SlowGeolocationProvider();
        var document = Load("<script>" + GeolocationScript + "</script>", new BrowsingContextOptions { Geolocation = provider, Permissions = GrantedPermissions("geolocation"), TimeProvider = clock });

        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError, { timeout: 50 })");
        Assert.Equal("", Run(document, "done()"));

        clock.Advance(TimeSpan.FromMilliseconds(50));
        await RunAsync(document);

        Assert.Equal("error 3 TIMEOUT", Run(document, "done()"));

        // The provider observes the cancellation on another thread, possibly after the error callback
        await provider.Canceled.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetCurrentPosition_ProviderCanceledByTheTimeoutBeforeItStarts_TimesOut(bool throwSynchronously)
    {
        var clock = new FakeTimeProvider();
        var provider = new LateGeolocationProvider(clock, throwSynchronously);
        var document = Load("<script>" + GeolocationScript + "</script>", new BrowsingContextOptions { Geolocation = provider, Permissions = GrantedPermissions("geolocation"), TimeProvider = clock });

        Run(document, "navigator.geolocation.getCurrentPosition(onPosition, onError, { timeout: 50 })");

        Assert.Equal("error 3 TIMEOUT", Run(document, "done()"));
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

    /// <summary>A provider that is called once the timeout elapsed, as on a busy machine, and observes the cancellation.</summary>
    private sealed class LateGeolocationProvider(FakeTimeProvider clock, bool throwSynchronously) : GeolocationProvider
    {
        public override ValueTask<GeolocationReading?> GetPositionAsync(GeolocationRequest request, CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromMilliseconds(50));
            if (throwSynchronously)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromCanceled<GeolocationReading?>(cancellationToken);
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

        Assert.Equal("show,title,body,tag,https://example.com/icon.png,,fr,rtl,42,true,1000,,false,0,2", Run(document));
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

    // Compute Pressure

    private const string PressureScript = """
        window.log = [];
        window.observe = (options) => {
          window.observer = new PressureObserver(records => log.push(records.map(r => r.source + ':' + r.state).join(',')));
          return observer.observe('cpu', options).then(() => log.push('observing'), e => log.push(e.name));
        };
        window.done = () => document.title = log.join('|');
        """;

    [Fact]
    public void PressureObserver_WithoutProvider_ObservesTheCpuWithoutRecords()
    {
        var document = Load("<script>" + PressureScript + "observe();</script>");

        Assert.Equal("cpu|observing", Run(document, "log.unshift(PressureObserver.knownSources.join()); done()"));
    }

    [Fact]
    public void PressureObserver_ReadsTheProvider()
    {
        #region WebPlatform_ComputePressure
        var pressure = new PressureSourceProvider();
        pressure.SetSample(PressureSource.Cpu, new PressureSample(PressureState.Nominal));

        var options = new BrowsingContextOptions
        {
            ComputePressure = new ComputePressureOptions { Provider = pressure },
        };
        #endregion

        var document = Load("<script>" + PressureScript + "observe();</script>", options);
        Run(document);

        // A new sample is delivered when the state changes: the same state is not delivered again
        pressure.SetSample(PressureSource.Cpu, new PressureSample(PressureState.Critical));
        Run(document);
        pressure.SetSample(PressureSource.Cpu, new PressureSample(PressureState.Critical));
        Run(document);

        Assert.Equal("observing|cpu:nominal|cpu:critical", Run(document, "done()"));
        Assert.Equal("0", Run(document, "observer.disconnect(); document.title = observer.takeRecords().length"));
    }

    [Fact]
    public void PressureObserver_UnsupportedSource_IsRejectedWithNotSupportedError()
    {
        var pressure = new PressureSourceProvider();
        pressure.SetSupported(PressureSource.Cpu, supported: false);
        var document = Load("<script>" + PressureScript + "observe();</script>", new BrowsingContextOptions { ComputePressure = new ComputePressureOptions { Provider = pressure } });

        Assert.Equal("NotSupportedError", Run(document, "done()"));
    }

    [Fact]
    public void PressureObserver_DisconnectBeforeObserveCompletes_RejectsWithAbortError()
    {
        var document = Load("<script>" + PressureScript + "observe(); observer.disconnect();</script>");

        Assert.Equal("AbortError", Run(document, "done()"));
    }

    [Fact]
    public void PressureObserver_SampleInterval_DeliversTheSampleAtEachInterval()
    {
        var clock = new FakeTimeProvider();
        var pressure = new PressureSourceProvider();
        pressure.SetSample(PressureSource.Cpu, new PressureSample(PressureState.Fair));
        var document = Load("<script>" + PressureScript + "observe({ sampleInterval: 1000 });</script>", new BrowsingContextOptions
        {
            ComputePressure = new ComputePressureOptions { Provider = pressure },
            TimeProvider = clock,
        });
        Run(document);

        clock.Advance(TimeSpan.FromMilliseconds(1000));
        Run(document);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Run(document);

        Assert.Equal("observing|cpu:fair|cpu:fair", Run(document, "observer.disconnect(); done()"));
    }

    [Fact]
    public void PressureObserver_InvalidSampleInterval_IsRejectedWithTypeError()
    {
        var document = Load("<script>" + PressureScript + "observe({ sampleInterval: -1 }).catch(e => log.push(e.name));</script>");

        Assert.Equal("TypeError", Run(document, "done()"));
    }

    // Idle Detection

    private const string IdleScript = """
        window.log = [];
        window.start = (options) => {
          window.detector = new IdleDetector();
          detector.onchange = () => log.push(detector.userState + ',' + detector.screenState);
          return detector.start(options).then(() => log.push('started'), e => log.push(e.name));
        };
        window.done = () => document.title = log.join('|');
        """;

    [Fact]
    public void IdleDetector_WithoutProvider_ReportsAnActiveUser()
    {
        var document = Load("<script>" + IdleScript + "start();</script>");

        Assert.Equal("started|active,unlocked", Run(document, "done()"));
    }

    [Fact]
    public void IdleDetector_ReadsTheProvider()
    {
        #region WebPlatform_IdleDetection
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var idleState = new IdleStateProvider
        {
            // The user interacted with the device now: they are idle once the threshold of a detector elapsed
            Status = new IdleStatus { LastInteractionTime = clock.GetUtcNow() },
        };

        var permissions = new PermissionStore();
        permissions.SetState("idle-detection", PermissionState.Granted);

        var options = new BrowsingContextOptions
        {
            Permissions = permissions,
            IdleDetection = new IdleDetectionOptions { Provider = idleState },
            TimeProvider = clock,
        };
        #endregion

        var document = Load("<script>" + IdleScript + "start({ threshold: 60000 });</script>", options);
        Run(document);

        clock.Advance(TimeSpan.FromSeconds(60));
        Run(document);
        idleState.Status = idleState.Status with { IsScreenLocked = true };
        Run(document);
        idleState.Status = new IdleStatus();
        Run(document);

        Assert.Equal("started|active,unlocked|idle,unlocked|idle,locked|active,unlocked", Run(document, "done()"));
    }

    [Fact]
    public void IdleDetector_DeniedPermission_IsRejectedWithNotAllowedError()
    {
        var permissions = new PermissionStore();
        permissions.SetState("idle-detection", PermissionState.Denied);
        var document = Load("<script>" + IdleScript + "start();</script>", new BrowsingContextOptions { Permissions = permissions });

        Assert.Equal("NotAllowedError", Run(document, "done()"));
    }

    [Theory]
    [InlineData("{ threshold: 59999 }", "TypeError")]
    [InlineData("{ threshold: -1 }", "TypeError")]
    [InlineData("{ threshold: 60000 }", "started")]
    public void IdleDetector_Threshold(string options, string expected)
    {
        var document = Load("<script>" + IdleScript + "start(" + options + ").catch(e => log.push(e.name));</script>");

        Assert.Equal(expected, Run(document, "document.title = log[0]"));
    }

    [Fact]
    public void IdleDetector_AbortedSignal_StopsTheDetector()
    {
        var idleState = new IdleStateProvider();
        var document = Load("<script>" + IdleScript + "const controller = new AbortController(); start({ signal: controller.signal }); controller.abort();</script>", new BrowsingContextOptions { IdleDetection = new IdleDetectionOptions { Provider = idleState } });

        idleState.Status = new IdleStatus { IsScreenLocked = true };
        Assert.Equal("AbortError", Run(document, "done()"));
    }

    [Fact]
    public void IdleDetector_RequestPermission_NeedsTransientActivation()
    {
        var document = Load("<script>window.log = []; window.request = () => IdleDetector.requestPermission().then(p => log.push(p), e => log.push(e.name));</script>", new BrowsingContextOptions
        {
            RequestPermission = (_, name) => name == "idle-detection" ? PermissionState.Granted : PermissionState.Denied,
        });

        Run(document, "request()");
        Activate(document);
        Assert.Equal("NotAllowedError|granted", Run(document, "request().then(() => document.title = log.join('|'))"));
    }

    // Device Posture

    [Fact]
    public void DevicePosture_WithoutProvider_IsContinuous()
    {
        var document = Load("<script>document.title = [navigator.devicePosture.type, matchMedia('(device-posture: continuous)').matches].join();</script>");

        Assert.Equal("continuous,true", Run(document));
    }

    [Fact]
    public void DevicePosture_ReadsTheProvider()
    {
        #region WebPlatform_DevicePosture
        var posture = new DevicePostureProvider();
        var options = new BrowsingContextOptions
        {
            DevicePosture = new DevicePostureOptions { Provider = posture },
        };

        // Fires the change event of navigator.devicePosture, and updates the device-posture media feature
        posture.Posture = DevicePostureType.Folded;
        #endregion

        posture.Posture = DevicePostureType.Continuous;
        var document = Load("<script>window.log = []; navigator.devicePosture.onchange = () => log.push(navigator.devicePosture.type);</script>", options);

        posture.Posture = DevicePostureType.Folded;
        Run(document);

        Assert.Equal("folded|true", Run(document, "log.push(matchMedia('(device-posture: folded)').matches); document.title = log.join('|')"));
    }

    [Fact]
    public void DevicePosture_HiddenDocument_FiresTheEventWhenItBecomesVisible()
    {
        var posture = new DevicePostureProvider();
        var document = Load("<script>window.log = []; navigator.devicePosture.onchange = () => log.push(navigator.devicePosture.type);</script>", new BrowsingContextOptions { DevicePosture = new DevicePostureOptions { Provider = posture } });

        document.BrowsingContext!.IsVisible = false;
        Run(document);
        posture.Posture = DevicePostureType.Folded;
        Assert.Equal("", Run(document, "document.title = log.join('|')"));

        document.BrowsingContext.IsVisible = true;
        Run(document);
        Assert.Equal("folded", Run(document, "document.title = log.join('|')"));
    }

    // Web Authentication

    private const string WebAuthnScript = """
        window.create = (publicKey) => navigator.credentials.create({ publicKey: Object.assign({
          rp: { name: 'Example' },
          user: { id: new Uint8Array([1, 2, 3]), name: 'john', displayName: 'John' },
          challenge: new Uint8Array([0xff]),
          pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
        }, publicKey) });
        """;

    [Fact]
    public void WebAuthentication_CreateAndGet()
    {
        #region WebPlatform_WebAuthentication
        var authenticators = new VirtualAuthenticatorEnvironment();
        var authenticator = authenticators.AddAuthenticator(new VirtualAuthenticatorOptions
        {
            Protocol = VirtualAuthenticatorProtocol.Ctap21,
            Transport = "internal",
            HasResidentKey = true,
            HasUserVerification = true,
            IsUserVerified = true,
        });

        var options = new BrowsingContextOptions
        {
            WebAuthentication = new WebAuthenticationOptions { Authenticators = authenticators },
        };
        #endregion

        var document = Load("<script>" + WebAuthnScript + "</script>", options);
        Assert.Equal("public-key|platform|webauthn.create|true", Run(document, """
            create({ authenticatorSelection: { residentKey: 'required' }, extensions: { credProps: true } }).then(c => {
              window.credential = c;
              const clientData = JSON.parse(new TextDecoder().decode(c.response.clientDataJSON));
              document.title = [c.type, c.authenticatorAttachment, clientData.type, c.getClientExtensionResults().credProps.rk].join('|');
            }, e => document.title = e.name)
            """));

        var credentials = authenticator.GetCredentials();
        var stored = Assert.Single(credentials);
        Assert.Equal("example.com", stored.RpId);
        Assert.True(stored.IsResidentCredential);
        Assert.Equal([1, 2, 3], stored.UserHandle!.Value.ToArray());

        // A discoverable credential is found without allowCredentials, and its user handle is returned
        Assert.Equal("true|1,2,3|true", Run(document, """
            navigator.credentials.get({ publicKey: { challenge: new Uint8Array([1]) } }).then(a => {
              document.title = [a.id === credential.id, new Uint8Array(a.response.userHandle).join(','), a.response.signature.byteLength > 0].join('|');
            }, e => document.title = e.name)
            """));
        Assert.Equal(1u, authenticator.GetCredentials()[0].SignCount);
    }

    [Fact]
    public void WebAuthentication_WithoutAuthenticator_IsNotAllowed()
    {
        var document = Load("<script>" + WebAuthnScript + "</script>");

        Assert.Equal("NotAllowedError", Run(document, "create({}).then(() => document.title = 'created', e => document.title = e.name)"));
    }

    [Fact]
    public void WebAuthentication_ClientDataJson_IsTheSpecifiedSerialization()
    {
        var authenticators = new VirtualAuthenticatorEnvironment();
        authenticators.AddAuthenticator(new VirtualAuthenticatorOptions());
        var document = Load("<script>" + WebAuthnScript + "</script>", new BrowsingContextOptions { WebAuthentication = new WebAuthenticationOptions { Authenticators = authenticators } });

        Assert.Equal("""{"type":"webauthn.create","challenge":"_w","origin":"https://example.com","crossOrigin":false}""", Run(document, "create({}).then(c => document.title = new TextDecoder().decode(c.response.clientDataJSON), e => document.title = e.name)"));
    }

    [Fact]
    public void WebAuthentication_AddCredential_SignsTheAssertions()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var authenticators = new VirtualAuthenticatorEnvironment();
        var authenticator = authenticators.AddAuthenticator(new VirtualAuthenticatorOptions { Protocol = VirtualAuthenticatorProtocol.Ctap1U2f });
        authenticator.AddCredential(new VirtualCredential
        {
            CredentialId = new byte[] { 7, 8, 9 },
            RpId = "example.com",
            PrivateKey = key.ExportPkcs8PrivateKey(),
            SignCount = 41,
        });

        var document = Load("<script>window.log = [];</script>", new BrowsingContextOptions { WebAuthentication = new WebAuthenticationOptions { Authenticators = authenticators } });
        Run(document, """
            navigator.credentials.get({ publicKey: { challenge: new Uint8Array([1]), allowCredentials: [{ type: 'public-key', id: new Uint8Array([7, 8, 9]) }] } }).then(a => {
              window.assertion = a;
              document.title = a.id;
            }, e => document.title = e.name)
            """);

        Assert.Equal("BwgJ", document.Title);
        Assert.Equal(42u, authenticator.GetCredentials()[0].SignCount);
    }

    [Theory]
    [InlineData("1.2.840.21143491591", false)] // Unknown curve (fuzzing, issue 4912)
    [InlineData("1.2.840.21143491591", true)]
    [InlineData("1.3.132.0.34", false)] // P-384
    [InlineData("1.3.132.0.34", true)]
    public void WebAuthentication_AddCredential_RejectsEcdsaKeyOfAnotherCurve(string curve, bool inEcPrivateKey)
    {
        // The named curve is either the parameters of the AlgorithmIdentifier, or the parameters of the ECPrivateKey (RFC 5915)
        // when the AlgorithmIdentifier has none
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var privateKey = key.ExportParameters(includePrivateParameters: true).D!;
        var ecPrivateKey = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (ecPrivateKey.PushSequence())
        {
            ecPrivateKey.WriteInteger(1);
            ecPrivateKey.WriteOctetString(privateKey);
            if (inEcPrivateKey)
            {
                using (ecPrivateKey.PushSequence(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    ecPrivateKey.WriteObjectIdentifier(curve);
                }
            }
        }

        var pkcs8 = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (pkcs8.PushSequence())
        {
            pkcs8.WriteInteger(0);
            using (pkcs8.PushSequence())
            {
                pkcs8.WriteObjectIdentifier("1.2.840.10045.2.1");
                if (!inEcPrivateKey)
                {
                    pkcs8.WriteObjectIdentifier(curve);
                }
            }

            pkcs8.WriteOctetString(ecPrivateKey.Encode());
        }

        var authenticator = new VirtualAuthenticatorEnvironment().AddAuthenticator(new VirtualAuthenticatorOptions());
        Assert.Throws<ArgumentException>(() => authenticator.AddCredential(new VirtualCredential { CredentialId = new byte[] { 1 }, RpId = "example.com", PrivateKey = pkcs8.Encode(), SignCount = 0 }));
        Assert.Empty(authenticator.GetCredentials());
    }

    [Fact]
    public void WebAuthentication_ParseCreationOptionsFromJson_RejectsInvalidBase64Url()
    {
        var document = Load("<script></script>");

        Assert.Equal("EncodingError|4", Run(document, """
            const json = { rp: { name: 'Example' }, user: { id: 'dGVzdA', name: 'a', displayName: 'A' }, challenge: 'dGVzdA', pubKeyCredParams: [] };
            const options = PublicKeyCredential.parseCreationOptionsFromJSON(json);
            try { PublicKeyCredential.parseCreationOptionsFromJSON(Object.assign({}, json, { challenge: 'dGVzdA=' })); } catch (e) { document.title = e.name + '|' + options.challenge.byteLength; }
            """));
    }

    // Credential Management and the user agent UI

    private const string CredentialsScript = "window.go = p => p.then(c => document.title = c === null ? 'null' : [c.type, c.id, c.password ?? c.provider].join('|'), e => document.title = e.name);";

    [Fact]
    public void PasswordCredential_WithoutBackend_ResolvesNull()
    {
        var document = Load("<script>" + CredentialsScript + "</script>");

        Assert.Equal("null", Run(document, "go(navigator.credentials.get({ password: true }))"));
        Assert.Equal("undefined", Run(document, "navigator.credentials.store(new PasswordCredential({ id: 'john', password: 'secret', origin: location.origin })).then(r => document.title = String(r))"));
    }

    [Fact]
    public void PasswordCredential_StoreAndGet()
    {
        #region WebPlatform_UserAgentUI_Credentials
        var options = new BrowsingContextOptions
        {
            // The prompts of the user agent: the credential store and the credential chooser of the backend
            UserAgentUI = new InMemoryCredentialStore(),
        };
        #endregion

        var document = Load("<script>" + CredentialsScript + "</script>", options);
        Assert.Equal("null", Run(document, "go(navigator.credentials.get({ password: true }))"));
        Run(document, "navigator.credentials.store(new PasswordCredential({ id: 'john', password: 'secret', origin: location.origin }))");
        Assert.Equal("password|john|secret", Run(document, "go(navigator.credentials.get({ password: true }))"));

        // Only the requested types are returned
        Assert.Equal("null", Run(document, "go(navigator.credentials.get({ federated: { providers: ['https://idp.example'] } }))"));
    }

    [Fact]
    public void PasswordCredential_PreventSilentAccess_SilentRequestsReturnNull()
    {
        var backend = new InMemoryCredentialStore();
        var document = Load("<script>" + CredentialsScript + "</script>", new BrowsingContextOptions { UserAgentUI = backend });
        Run(document, "navigator.credentials.store(new PasswordCredential({ id: 'john', password: 'secret', origin: location.origin }))");
        Assert.Equal("password|john|secret", Run(document, "go(navigator.credentials.get({ password: true, mediation: 'silent' }))"));

        Run(document, "navigator.credentials.preventSilentAccess()");
        var prompts = backend.ChooserPrompts;
        Assert.Equal("null", Run(document, "go(navigator.credentials.get({ password: true, mediation: 'silent' }))"));
        Assert.Equal(prompts, backend.ChooserPrompts);

        // The user chooses a credential: the origin no longer requires user mediation
        Assert.Equal("password|john|secret", Run(document, "go(navigator.credentials.get({ password: true }))"));
        Assert.True(backend.LastPrompt!.RequiresUserMediation);
        Assert.Equal("password|john|secret", Run(document, "go(navigator.credentials.get({ password: true, mediation: 'silent' }))"));
    }

    [Fact]
    public void Credentials_Create_FromFormAndData()
    {
        var document = Load("""
            <form>
              <input name=user autocomplete=username value=john>
              <input name=pwd autocomplete=current-password value=old>
              <input name=pwd2 autocomplete=new-password value=new>
              <input name=display autocomplete=nickname value=Johnny>
            </form>
            <script>
            """ + CredentialsScript + "</script>");

        Assert.Equal("password|john|new", Run(document, "go(navigator.credentials.create({ password: document.forms[0] }))"));
        Assert.Equal("Johnny|https://example.com", Run(document, "document.title = (c => [c.name, c.iconURL || location.origin].join('|'))(new PasswordCredential(document.forms[0]))"));
        Assert.Equal("federated|john|https://idp.example", Run(document, "go(navigator.credentials.create({ federated: { id: 'john', provider: 'https://idp.example', origin: 'https://other.example' } }))"));
        Assert.Equal("NotSupportedError", Run(document, "go(navigator.credentials.create({ password: document.forms[0], federated: { id: 'john', provider: 'https://idp.example', origin: location.origin } }))"));
        Assert.Equal("TypeError", Run(document, "go(navigator.credentials.create({ password: { id: '', password: 'x', origin: location.origin } }))"));
    }

    [Fact]
    public void Credentials_Get_InvalidCombination_IsNotSupported()
    {
        var document = Load("<script>" + CredentialsScript + "</script>");

        Assert.Equal("NotSupportedError", Run(document, "go(navigator.credentials.get({ password: true, publicKey: { challenge: new Uint8Array(1) } }))"));
        Assert.Equal("TypeError", Run(document, "go(navigator.credentials.get({ password: true, mediation: 'conditional' }))"));
    }

    [Fact]
    public void Credentials_Get_AbortClosesThePrompt()
    {
        var backend = new PendingCredentialChooser();
        var document = Load("<script>" + CredentialsScript + "window.controller = new AbortController();</script>", new BrowsingContextOptions { UserAgentUI = backend });

        Run(document, "go(navigator.credentials.get({ password: true, signal: controller.signal }))");
        Assert.False(backend.CancellationToken.IsCancellationRequested);
        Assert.Equal("AbortError", Run(document, "controller.abort()"));
        Assert.True(backend.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Credentials_Get_AsynchronousAnswer()
    {
        var backend = new PendingCredentialChooser();
        var document = Load("<script>" + CredentialsScript + "</script>", new BrowsingContextOptions { UserAgentUI = backend });
        Run(document, "go(navigator.credentials.get({ password: true }))");

        _ = Task.Run(() => backend.Answer.SetResult(new PasswordCredential(new PasswordCredentialData { Id = "jane", Password = "pass", Origin = "https://example.com" })));
        Assert.Equal("password|jane|pass", await RunAsync(document));
    }

    private const string DigitalCredentialsScript = """
        window.request = (protocol, data) => ({ digital: { requests: [{ protocol, data }] } });
        window.go = p => p.then(c => document.title = [c.type, c.protocol, JSON.stringify(c.data)].join('|'), e => document.title = e.name);
        """;

    [Fact]
    public void DigitalCredential_WalletResponds()
    {
        var wallet = new SingleResponseWallet();
        var document = Load("<script>" + DigitalCredentialsScript + "</script>", new BrowsingContextOptions { UserAgentUI = wallet });
        Activate(document);

        Assert.Equal("digital|org-iso-mdoc|{\"protocol\":\"org-iso-mdoc\",\"request\":{\"a\":1}}", Run(document, "go(navigator.credentials.get(request('org-iso-mdoc', { a: 1 })))"));
        var prompt = Assert.IsType<DigitalCredentialPrompt>(wallet.LastPrompt);
        Assert.Equal(DigitalCredentialRequestKind.Presentation, prompt.Kind);
        Assert.Equal("https://example.com", prompt.Origin);
        Assert.Equal(new DigitalCredentialRequest("org-iso-mdoc", "{\"a\":1}"), Assert.Single(prompt.Requests));
    }

    [Fact]
    public void DigitalCredential_WithoutWallet_IsNotAllowed()
    {
        var document = Load("<script>" + DigitalCredentialsScript + "</script>");

        // Without user activation, then without wallet (the user cancels)
        Assert.Equal("NotAllowedError", Run(document, "go(navigator.credentials.get(request('openid4vp-v1-unsigned', {})))"));
        Activate(document);
        Assert.Equal("NotAllowedError", Run(document, "go(navigator.credentials.get(request('openid4vp-v1-unsigned', {})))"));
    }

    [Fact]
    public void DigitalCredential_ValidatesTheRequests()
    {
        var document = Load("<script>" + DigitalCredentialsScript + "</script>", new BrowsingContextOptions { UserAgentUI = new SingleResponseWallet() });
        Activate(document);

        // The protocols that the user agent does not allow are filtered out, and the data must be serializable to JSON
        Assert.Equal("TypeError", Run(document, "go(navigator.credentials.get(request('unknown', {})))"));
        Assert.Equal("TypeError", Run(document, "go(navigator.credentials.get(request('org-iso-mdoc', { n: 1n })))"));
        Assert.Equal("true|false|true", Run(document, "document.title = [DigitalCredential.userAgentAllowsProtocol('org-iso-mdoc'), DigitalCredential.userAgentAllowsProtocol('unknown'), DigitalCredential.userAgentAllowsProtocol('openid4vci-v1')].join('|')"));
    }

    [Fact]
    public void DigitalCredential_Create_IsAnIssuance()
    {
        var wallet = new SingleResponseWallet();
        var document = Load("<script>" + DigitalCredentialsScript + "</script>", new BrowsingContextOptions { UserAgentUI = wallet });
        Activate(document);

        Assert.Equal("digital|openid4vci-v1|{\"protocol\":\"openid4vci-v1\",\"request\":{}}", Run(document, "go(navigator.credentials.create(request('openid4vci-v1', {})))"));
        Assert.Equal(DigitalCredentialRequestKind.Issuance, wallet.LastPrompt!.Kind);
    }

    private sealed class PendingCredentialChooser : UserAgentUIBackend
    {
        public TaskCompletionSource<Credential?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CancellationToken { get; private set; }

        public override ValueTask<Credential?> ChooseCredentialAsync(CredentialChooserPrompt prompt, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return new(Answer.Task);
        }
    }

    // Payment Request

    private const string PaymentScript = """
        window.log = [];
        window.go = p => p.then(r => document.title = String(r), e => document.title = e.name);
        window.methods = [{ supportedMethods: 'https://pay.example/pay', data: { merchant: 'shop' } }];
        window.details = { total: { label: 'Total', amount: { currency: 'eur', value: '10.00' } } };
        window.construct = (m, d, o) => { try { new PaymentRequest(m, d, o); return 'ok'; } catch (e) { return e.name; } };
        """;

    [Fact]
    public void PaymentRequest_WithoutBackend_NoPaymentMethodIsSupported()
    {
        var document = Load("<script>" + PaymentScript + "</script>");

        Assert.Equal("false", Run(document, "go(new PaymentRequest(methods, details).canMakePayment())"));
        Assert.Equal("SecurityError", Run(document, "go(new PaymentRequest(methods, details).show())"));
        Activate(document);
        Assert.Equal("NotSupportedError", Run(document, "go(new PaymentRequest(methods, details).show())"));
    }

    [Fact]
    public void PaymentRequest_Constructor_ValidatesTheArguments()
    {
        var document = Load("<script>" + PaymentScript + "</script>");

        Assert.Equal("ok", Run(document, "document.title = construct([{ supportedMethods: 'basic-card' }, { supportedMethods: 'https://pay.example/pay?v=1' }], details)"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([], details)"));
        Assert.Equal("RangeError", Run(document, "document.title = construct([{ supportedMethods: 'Basic-Card' }], details)"));
        Assert.Equal("RangeError", Run(document, "document.title = construct([{ supportedMethods: 'http://pay.example/' }], details)"));
        Assert.Equal("RangeError", Run(document, "document.title = construct([{ supportedMethods: 'https://pay.example/' }, { supportedMethods: 'https://pay.example' }], details)"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([{ supportedMethods: 'basic-card', data: 'text' }], details)"));
        Assert.Equal("TypeError", Run(document, "document.title = construct(methods, { total: { label: '', amount: { currency: 'USD', value: '-1' } } })"));
        Assert.Equal("TypeError", Run(document, "document.title = construct(methods, { total: { label: '', amount: { currency: 'USD', value: '1.' } } })"));
        Assert.Equal("RangeError", Run(document, "document.title = construct(methods, { total: { label: '', amount: { currency: 'US', value: '1' } } })"));
        Assert.Equal("ok", Run(document, "document.title = construct(methods, { ...details, displayItems: [{ label: 'Discount', amount: { currency: 'EUR', value: '-1.50' } }] })"));
        Assert.Equal("TypeError", Run(document, "document.title = construct(methods, { ...details, shippingOptions: [{ id: 'a', label: '', amount: details.total.amount }, { id: 'a', label: '', amount: details.total.amount }] }, { requestShipping: true })"));
        Assert.Equal("b|delivery|null|null|order-1|true", Run(document, """
            const r = new PaymentRequest(methods, { ...details, id: 'order-1', shippingOptions: [{ id: 'a', label: '', amount: details.total.amount, selected: true }, { id: 'b', label: '', amount: details.total.amount, selected: true }] }, { requestShipping: true, shippingType: 'delivery' });
            const other = new PaymentRequest(methods, details);
            document.title = [r.shippingOption, r.shippingType, String(other.shippingType), String(other.shippingAddress), r.id, other.id.length > 0 && other.id !== new PaymentRequest(methods, details).id].join('|');
            """));
    }

    [Fact]
    public async Task PaymentRequest_Show_TheUserAcceptsThePayment()
    {
        #region WebPlatform_UserAgentUI_Payments
        var paymentSheet = new AcceptingPaymentSheet();
        var options = new BrowsingContextOptions
        {
            // The payment handlers and the payment sheet of the Payment Request API are the ones of the backend
            UserAgentUI = paymentSheet,
        };
        #endregion

        var document = Load("<script>" + PaymentScript + "</script>", options);
        Assert.Equal("true", await RunAsync(document, "go(new PaymentRequest([{ supportedMethods: 'basic-card' }, ...methods], details).canMakePayment())"));

        Activate(document);
        Assert.Equal("https://pay.example/pay|tok_12.00|John Doe|null|undefined", await RunAsync(document, """
            const r = new PaymentRequest(methods, details, { requestPayerName: true });
            go(r.show(Promise.resolve({ total: { label: 'Total', amount: { currency: 'eur', value: '12.00' } } }))
                .then(response => response.complete('success').then(result => [response.methodName, response.details.token, response.payerName, String(response.payerEmail), String(result)].join('|'))));
            """));
        Assert.Equal(PaymentComplete.Success, paymentSheet.Result);
        Assert.Equal("EUR", paymentSheet.LastPrompt!.Details.Total.Amount.Currency);
        Assert.Equal("{\"merchant\":\"shop\"}", paymentSheet.LastPrompt.Methods[0].Data);
    }

    [Fact]
    public async Task PaymentRequest_Show_TheSheetWaitsForTheUpdatesOfThePage()
    {
        var backend = new ShippingPaymentSheet();
        var document = Load("<script>" + PaymentScript + "</script>", new BrowsingContextOptions { UserAgentUI = backend });
        Activate(document);

        Assert.Equal("John|express|FR|address Paris//0,option express", await RunAsync(document, """
            const r = new PaymentRequest(methods, details, { requestShipping: true });
            r.onshippingaddresschange = e => {
                log.push('address ' + [r.shippingAddress.city, r.shippingAddress.recipient, r.shippingAddress.addressLine.length].join('/'));
                e.updateWith(new Promise(resolve => setTimeout(() => resolve({ shippingOptions: [
                    { id: 'standard', label: 'Standard', amount: { currency: 'EUR', value: '0' }, selected: true },
                    { id: 'express', label: 'Express', amount: { currency: 'EUR', value: '5' } }] }), 0)));
            };
            r.onshippingoptionchange = () => log.push('option ' + r.shippingOption);
            go(r.show().then(response => [response.shippingAddress.recipient, response.shippingOption, response.shippingAddress.country, log.join()].join('|')));
            """));
        Assert.Equal(["standard", "express"], backend.ShippingOptions);
    }

    [Fact]
    public void PaymentRequest_Show_InvalidUpdate_AbortsTheRequest()
    {
        var document = Load("<script>" + PaymentScript + "</script>", new BrowsingContextOptions { UserAgentUI = new PendingPaymentSheet() });

        Activate(document);
        Assert.Equal("AbortError", Run(document, "go(new PaymentRequest(methods, details).show(Promise.reject(new Error('rejected'))))"));
        Activate(document);
        Assert.Equal("TypeError", Run(document, "go(new PaymentRequest(methods, details).show(Promise.resolve({ total: { label: '', amount: { currency: 'EUR', value: '-1' } } })))"));
        Activate(document);
        Assert.Equal("RangeError", Run(document, "go(new PaymentRequest(methods, details).show(Promise.resolve({ displayItems: [{ label: '', amount: { currency: '€', value: '1' } }] })))"));
    }

    [Fact]
    public void PaymentRequest_Abort_ClosesTheSheet()
    {
        var backend = new PendingPaymentSheet();
        var document = Load("<script>" + PaymentScript + "</script>", new BrowsingContextOptions { UserAgentUI = backend });

        Assert.Equal("InvalidStateError", Run(document, "window.r = new PaymentRequest(methods, details); go(r.abort())"));
        Activate(document);
        Run(document, "go(r.show())");
        Assert.False(backend.CancellationToken.IsCancellationRequested);

        // Another payment sheet cannot be shown in the tab
        Activate(document);
        Assert.Equal("AbortError", Run(document, "go(new PaymentRequest(methods, details).show())"));
        Assert.Equal("InvalidStateError", Run(document, "go(r.canMakePayment())"));

        Assert.Equal("AbortError", Run(document, "r.abort()"));
        Assert.True(backend.CancellationToken.IsCancellationRequested);
        Activate(document);
        Assert.Equal("InvalidStateError", Run(document, "go(r.show())"));
    }

    [Fact]
    public async Task PaymentResponse_Retry_ShowsTheErrorsToTheUser()
    {
        var backend = new AcceptingPaymentSheet();
        var document = Load("<script>" + PaymentScript + "</script>", new BrowsingContextOptions { UserAgentUI = backend });
        Activate(document);

        Assert.Equal("Jane Doe|InvalidStateError", await RunAsync(document, """
            const r = new PaymentRequest(methods, details, { requestPayerName: true });
            go(r.show().then(response => response.retry({ error: 'Card declined', payer: { name: 'Unknown name' } }).then(() => {
                const name = response.payerName;
                return response.complete().then(() => response.retry()).catch(e => name + '|' + e.name);
            })));
            """));
        Assert.Equal("Card declined", backend.LastPrompt!.Errors!.Error);
        Assert.Equal("Unknown name", backend.LastPrompt.Errors.Payer!.Name);
    }

    [Fact]
    public void PaymentRequestUpdateEvent_UntrustedEvent_CannotUpdate()
    {
        var document = Load("<script>" + PaymentScript + "</script>");

        Assert.Equal("InvalidStateError|test|paymentmethodchange|null", Run(document, """
            const r = new PaymentRequest(methods, details);
            const e = new PaymentMethodChangeEvent('paymentmethodchange', { methodName: 'test' });
            r.onpaymentmethodchange = event => log.push(event.type);
            r.dispatchEvent(e);
            try { e.updateWith(Promise.resolve({})); } catch (error) { log.unshift(error.name); }
            document.title = [log[0], e.methodName, log[1], String(new PaymentMethodChangeEvent('x').methodDetails)].join('|');
            """));
    }

    [Theory]
    [InlineData("https://example.com/pay", true)]
    [InlineData(" \thttps://example.com/pay\n", true)]
    [InlineData("https://:@example.com/pay", true)]
    [InlineData("secure-payment-confirmation", true)]
    [InlineData("a-b0-c", true)]
    [InlineData("https://user@example.com/pay", false)]
    [InlineData("http://example.com/pay", false)]
    [InlineData("Basic-Card", false)]
    [InlineData("a--b", false)]
    [InlineData("a-0", false)]
    [InlineData("a-", false)]
    [InlineData("", false)]
    public void PaymentMethodIdentifier_IsValid(string identifier, bool expected)
    {
        Assert.Equal(expected, PaymentMethodIdentifier.IsValid(identifier));
    }

    [Theory]
    [InlineData("""{"default_applications": ["app.json", "https://pay.example/pay/app.json"], "supported_origins": ["https://bob.example", "https://bob.example/"], "other": 1}""", "https://pay.example/pay/app.json", "https://bob.example")]
    [InlineData("""{"supported_origins": ["https://bob.example:8443"]}""", "", "https://bob.example:8443")]
    [InlineData("""{"default_applications": ["first.json"], "default_applications": ["last.json"]}""", "https://pay.example/pay/last.json", "")]
    [InlineData("\uFEFF{}", "", "")]
    [InlineData("""{"default_applications": []}""", null, null)]
    [InlineData("""{"default_applications": "app.json"}""", null, null)]
    [InlineData("""{"default_applications": ["app.json", 1]}""", null, null)]
    [InlineData("""{"default_applications": ["http://pay.example/app.json"]}""", null, null)]
    [InlineData("""{"default_applications": ["https://"]}""", null, null)]
    [InlineData("""{"supported_origins": "*"}""", null, null)]
    [InlineData("""{"supported_origins": null}""", null, null)]
    [InlineData("""{"supported_origins": ["https://bob.example/pay"]}""", null, null)]
    [InlineData("""{"supported_origins": ["https://bob.example/?q"]}""", null, null)]
    [InlineData("""{"supported_origins": ["https://bob.example/#f"]}""", null, null)]
    [InlineData("""{"supported_origins": ["https://user:pass@bob.example"]}""", null, null)]
    [InlineData("""{"supported_origins": ["/relative"]}""", null, null)]
    [InlineData("[]", null, null)]
    [InlineData("not json", null, null)]
    public void PaymentMethodManifest_Parse(string content, string? defaultApplications, string? supportedOrigins)
    {
        var manifest = PaymentMethodManifest.Parse(System.Text.Encoding.UTF8.GetBytes(content), "https://pay.example/pay/manifest.json");

        Assert.Equal(defaultApplications, manifest is null ? null : string.Join(' ', manifest.DefaultApplications));
        Assert.Equal(supportedOrigins, manifest is null ? null : string.Join(' ', manifest.SupportedOrigins));
    }

    [Theory]
    [InlineData("<https://pay.example/manifest.json>; rel=\"payment-method-manifest\"", "https://pay.example/manifest.json")]
    [InlineData("<manifest.json>; rel=payment-method-manifest", "manifest.json")]
    [InlineData("<manifest.json>; rel=\"PAYMENT-METHOD-MANIFEST\"", "manifest.json")]
    [InlineData("<manifest.json>; rel=\"other payment-method-manifest\"; foo=bar", "manifest.json")]
    [InlineData("<other.json>; rel=\"other\"\n<first.json>; rel=\"payment-method-manifest\"\n<second.json>; rel=\"payment-method-manifest\"", "first.json")]
    [InlineData("<manifest.json>; rel=\"stylesheet\"", null)]
    [InlineData("<manifest.json>; rel=\"stylesheet\"; rel=\"payment-method-manifest\"", null)]
    [InlineData("manifest.json; rel=\"payment-method-manifest\"", null)]
    [InlineData("<manifest.json>; rel=\"payment-method-manifest", null)]
    [InlineData("<manifest.json> rel=\"payment-method-manifest\"", null)]
    public void LinkHeader_FindPaymentMethodManifest(string values, string? expected)
    {
        Assert.Equal(expected, LinkHeader.FindPaymentMethodManifest(values.Split('\n')));
    }

    // Secure Payment Confirmation

    private const string SecurePaymentConfirmationScript = """
        window.go = p => p.then(r => document.title = String(r), e => document.title = e.name);
        window.clientData = c => JSON.parse(new TextDecoder().decode(c.response.clientDataJSON));
        window.enroll = (selection) => navigator.credentials.create({ publicKey: {
          rp: { name: 'Bank' },
          user: { id: new Uint8Array([1]), name: 'john', displayName: 'John' },
          challenge: new Uint8Array([1]),
          pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
          authenticatorSelection: Object.assign({ authenticatorAttachment: 'platform', residentKey: 'required', userVerification: 'required' }, selection),
          extensions: { payment: { isPayment: true } },
        } });
        window.data = (credentialIds, extra) => Object.assign({
          credentialIds,
          challenge: new Uint8Array([9]),
          rpId: 'example.com',
          payeeOrigin: 'https://merchant.example/checkout',
          instrument: { displayName: 'Card', icon: 'data:image/png;base64,iVBORw0KGgo=' },
        }, extra);
        window.details = { total: { label: 'Total', amount: { currency: 'usd', value: '1.00' } } };
        window.spc = (credentialIds, extra) => new PaymentRequest([{ supportedMethods: 'secure-payment-confirmation', data: data(credentialIds, extra) }], details);
        window.construct = (methods) => { try { new PaymentRequest(methods, details); return 'ok'; } catch (e) { return e.name; } };
        """;

    private static VirtualAuthenticatorEnvironment CreatePlatformAuthenticator()
    {
        var authenticators = new VirtualAuthenticatorEnvironment();
        authenticators.AddAuthenticator(new VirtualAuthenticatorOptions { Protocol = VirtualAuthenticatorProtocol.Ctap21, Transport = "internal", HasResidentKey = true, HasUserVerification = true, IsUserVerified = true });
        return authenticators;
    }

    [Fact]
    public async Task SecurePaymentConfirmation_Show_AuthenticatesThePayment()
    {
        #region WebPlatform_UserAgentUI_SecurePaymentConfirmation
        var authenticators = new VirtualAuthenticatorEnvironment();
        authenticators.AddAuthenticator(new VirtualAuthenticatorOptions
        {
            Protocol = VirtualAuthenticatorProtocol.Ctap21,
            Transport = "internal",
            HasResidentKey = true,
            HasUserVerification = true,
            IsUserVerified = true,
        });

        var transactionDialog = new TransactionDialog(SecurePaymentConfirmationChoice.Accept);
        var options = new BrowsingContextOptions
        {
            // The credentials of the payments are the ones of the virtual authenticators
            WebAuthentication = new WebAuthenticationOptions { Authenticators = authenticators },

            // The transaction dialog of Secure Payment Confirmation is the one of the backend
            UserAgentUI = transactionDialog,
        };
        #endregion

        var document = Load("<script>" + SecurePaymentConfirmationScript + "</script>", options);
        Assert.Equal("webauthn.create|true|true", await RunAsync(document, """
            enroll().then(c => {
              window.credential = c;
              document.title = [clientData(c).type, clientData(c).payment.browserBoundPublicKey.length > 0, c.getClientExtensionResults().payment.browserBoundSignature.signature.byteLength > 0].join('|');
            }, e => document.title = e.name);
            """));

        Activate(document);
        Assert.Equal("secure-payment-confirmation|true|payment.get|example.com|https://example.com|https://merchant.example|USD|1.00|Card|true|true", await RunAsync(document, """
            spc([credential.rawId]).show().then(response => response.complete('success').then(() => {
              const c = response.details;
              const d = clientData(c);
              document.title = [response.methodName, c.id === credential.id, d.type, d.payment.rpId, d.payment.topOrigin, d.payment.payeeOrigin, d.payment.total.currency, d.payment.total.value,
                d.payment.instrument.displayName, d.payment.browserBoundPublicKey === clientData(credential).payment.browserBoundPublicKey, c.getClientExtensionResults().payment.browserBoundSignature.signature.byteLength > 0].join('|');
            }), e => document.title = e.name);
            """));

        var prompt = transactionDialog.LastPrompt!;
        Assert.Equal("https://merchant.example", prompt.PayeeOrigin);
        Assert.Equal("USD", prompt.Total.Currency);
        Assert.Equal(1, prompt.CredentialCount);
        Assert.Equal("https://example.com", prompt.Origin);
    }

    [Theory]
    [InlineData(SecurePaymentConfirmationChoice.Decline, "{}", "AbortError")]
    [InlineData(SecurePaymentConfirmationChoice.AuthenticateAnotherWay, "{}", "NotAllowedError")]
    [InlineData(SecurePaymentConfirmationChoice.OptOut, "{ showOptOut: true }", "OptOutError")]
    [InlineData(SecurePaymentConfirmationChoice.OptOut, "{}", "AbortError")]
    public async Task SecurePaymentConfirmation_Show_TheChoiceOfTheUser(SecurePaymentConfirmationChoice choice, string extra, string expected)
    {
        var options = new BrowsingContextOptions
        {
            WebAuthentication = new WebAuthenticationOptions { Authenticators = CreatePlatformAuthenticator() },
            UserAgentUI = new TransactionDialog(choice),
        };
        var document = Load("<script>" + SecurePaymentConfirmationScript + "</script>", options);
        await RunAsync(document, "enroll().then(c => window.credential = c)");

        Activate(document);
        Assert.Equal(expected, await RunAsync(document, $"go(spc([credential.rawId], {extra}).show())"));
    }

    [Fact]
    public async Task SecurePaymentConfirmation_Show_UnknownCredential_IsNotAllowed()
    {
        var transactionDialog = new TransactionDialog(SecurePaymentConfirmationChoice.Accept);
        var document = Load("<script>" + SecurePaymentConfirmationScript + "</script>", new BrowsingContextOptions { WebAuthentication = new WebAuthenticationOptions { Authenticators = CreatePlatformAuthenticator() }, UserAgentUI = transactionDialog });

        Assert.Equal("true", await RunAsync(document, "go(spc([new Uint8Array([1, 2])]).canMakePayment())"));
        Activate(document);
        Assert.Equal("NotAllowedError", await RunAsync(document, "go(spc([new Uint8Array([1, 2])]).show())"));
        Assert.Equal(0, transactionDialog.LastPrompt!.CredentialCount);

        // Without the backend, the user agent does not support Secure Payment Confirmation
        var other = Load("<script>" + SecurePaymentConfirmationScript + "</script>");
        Activate(other);
        Assert.Equal("NotSupportedError", await RunAsync(other, "go(spc([new Uint8Array([1, 2])]).show())"));
    }

    [Fact]
    public void SecurePaymentConfirmation_Constructor_ValidatesTheData()
    {
        var document = Load("<script>" + SecurePaymentConfirmationScript + "</script>");

        Assert.Equal("ok", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])]) }])"));
        Assert.Equal("RangeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])]) }, { supportedMethods: 'basic-card' }])"));
        Assert.Equal("RangeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([]) }])"));
        Assert.Equal("RangeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array()]) }])"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { challenge: null }) }])"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { rpId: 'not a domain' }) }])"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { payeeOrigin: undefined }) }])"));
        Assert.Equal("ok", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { payeeOrigin: undefined, payeeName: 'Merchant' }) }])"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { payeeOrigin: 'http://merchant.example' }) }])"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { paymentEntitiesLogos: [{ url: 'https://logo.example/a.png', label: '' }] }) }])"));

        // Only the relying party can give extensions
        Assert.Equal("ok", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { extensions: { credProps: true } }) }])"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { rpId: 'bank.example', extensions: { credProps: true } }) }])"));

        // The locales must match the language of the transaction dialog
        Assert.Equal("ok", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { locale: ['fr', 'en'] }) }])"));
        Assert.Equal("NotSupportedError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { locale: ['fr'] }) }])"));
        Assert.Equal("TypeError", Run(document, "document.title = construct([{ supportedMethods: 'secure-payment-confirmation', data: data([new Uint8Array([1])], { locale: ['-'] }) }])"));
    }

    [Fact]
    public async Task SecurePaymentConfirmation_PaymentExtension()
    {
        var document = Load("<script>" + SecurePaymentConfirmationScript + "</script>", new BrowsingContextOptions { WebAuthentication = new WebAuthenticationOptions { Authenticators = CreatePlatformAuthenticator() } });

        // A credential for the payments is a discoverable credential of a platform authenticator that verifies the user
        Assert.Equal("TypeError", await RunAsync(document, "go(enroll({ authenticatorAttachment: 'cross-platform' }))"));
        Assert.Equal("TypeError", await RunAsync(document, "go(enroll({ userVerification: 'preferred' }))"));
        Assert.Equal("[object PublicKeyCredential]", await RunAsync(document, "go(enroll({ residentKey: 'preferred' }).then(c => window.credential = c))"));

        // An authentication with the extension is only possible from the payment handler of Secure Payment Confirmation
        Assert.Equal("NotAllowedError", await RunAsync(document, """
            go(navigator.credentials.get({ publicKey: { challenge: new Uint8Array([1]), allowCredentials: [{ type: 'public-key', id: credential.rawId }], extensions: { payment: { isPayment: true } } } }));
            """));
    }

    [Fact]
    public async Task SecurePaymentConfirmation_Availability()
    {
        var options = new BrowsingContextOptions { WebAuthentication = new WebAuthenticationOptions { Authenticators = CreatePlatformAuthenticator() }, UserAgentUI = new TransactionDialog(SecurePaymentConfirmationChoice.Decline) };
        var document = Load("<script>" + SecurePaymentConfirmationScript + "</script>", options);
        Assert.Equal("available", await RunAsync(document, "go(PaymentRequest.securePaymentConfirmationAvailability())"));
        Assert.Equal("{\"browserBoundKeyHardware\":false}", await RunAsync(document, "go(PaymentRequest.getSecurePaymentConfirmationCapabilities().then(JSON.stringify))"));

        var withoutAuthenticator = Load("<script>" + SecurePaymentConfirmationScript + "</script>", new BrowsingContextOptions { UserAgentUI = new TransactionDialog(SecurePaymentConfirmationChoice.Decline) });
        Assert.Equal("unavailable-no-user-verifying-platform-authenticator", await RunAsync(withoutAuthenticator, "go(PaymentRequest.securePaymentConfirmationAvailability())"));

        var withoutBackend = Load("<script>" + SecurePaymentConfirmationScript + "</script>");
        Assert.Equal("unavailable-feature-not-enabled", await RunAsync(withoutBackend, "go(PaymentRequest.securePaymentConfirmationAvailability())"));
    }

    private sealed class PendingPaymentSheet : UserAgentUIBackend
    {
        public CancellationToken CancellationToken { get; private set; }

        public override ValueTask<bool> HasPaymentHandlerAsync(PaymentHandlerQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public override ValueTask<PaymentSheetResponse?> ShowPaymentSheetAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return new(new TaskCompletionSource<PaymentSheetResponse?>().Task);
        }
    }

    /// <summary>A payment sheet whose user chooses a shipping address, then the last shipping option offered by the page.</summary>
    private sealed class ShippingPaymentSheet : UserAgentUIBackend
    {
        public List<string> ShippingOptions { get; } = [];

        public override ValueTask<bool> HasPaymentHandlerAsync(PaymentHandlerQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public override async ValueTask<PaymentSheetResponse?> ShowPaymentSheetAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken)
        {
            var details = await prompt.ChangeShippingAddressAsync(new ContactAddress { Country = "FR", City = "Paris", Recipient = "John", AddressLine = ["1 rue de Rivoli"] }, cancellationToken);
            ShippingOptions.AddRange(details.ShippingOptions.Select(option => option.Id));
            await prompt.ChangeShippingOptionAsync(details.ShippingOptions[^1].Id, cancellationToken);
            return new PaymentSheetResponse { MethodName = prompt.Methods[0].Identifier };
        }
    }

    // Federated Credential Management (FedCM) and the Login Status API

    private const string FedCmScript = """
        window.go = p => p.then(c => document.title = [c.type, typeof c.token === 'string' ? c.token : JSON.stringify(c.token), c.isAutoSelected, c.configURL].join('|'), e => document.title = e.name + (e.name === 'IdentityCredentialError' ? '|' + e.error + '|' + e.url : ''));
        window.options = (mediation, extra) => ({ identity: { providers: [Object.assign({ configURL: 'https://idp.example/fedcm.json', clientId: 'rp', nonce: 'n' }, extra)] }, mediation: mediation ?? 'optional' });
        """;

    [Fact]
    public void IdentityCredential_WithoutBackend_IsRejected()
    {
        var loader = new IdentityProviderLoader();
        var document = Load("<script>" + FedCmScript + "</script>", new BrowsingContextOptions { DocumentLoader = loader });

        // The accounts are fetched, but the account chooser is dismissed
        Assert.Equal("NetworkError", Run(document, "go(navigator.credentials.get(options()))"));
        Assert.Contains(loader.Requests, request => request.Url == "https://idp.example/accounts");
        Assert.DoesNotContain(loader.Requests, request => request.Url == "https://idp.example/token");
    }

    [Fact]
    public void IdentityCredential_ChooseAccount_FetchesTheToken()
    {
        #region WebPlatform_UserAgentUI_FedCm
        var options = new BrowsingContextOptions
        {
            // The dialogs of FedCM: the account chooser of the backend chooses the first account
            UserAgentUI = new FirstAccountChooser(),
        };
        #endregion

        var loader = new IdentityProviderLoader();
        var backend = (FirstAccountChooser)options.UserAgentUI;
        var document = Load("<script>" + FedCmScript + "</script>", new BrowsingContextOptions { DocumentLoader = loader, UserAgentUI = backend });

        Assert.Equal("identity|token-for-1234|false|https://idp.example/fedcm.json", Run(document, "go(navigator.credentials.get(options()))"));
        var prompt = Assert.IsType<IdentityAccountChooserPrompt>(backend.Prompts.Single());
        Assert.Equal(["1234", "5678"], prompt.Accounts.Select(account => account.Id));
        Assert.Equal("John Doe", prompt.Accounts[0].Name);
        Assert.Equal("https://idp.example/fedcm.json", prompt.Accounts[0].ConfigUrl);
        Assert.False(prompt.Accounts[0].IsConnected);
        Assert.Equal("https://rp.example/privacy", prompt.Accounts[0].PrivacyPolicyUrl);
        Assert.Equal(IdentityCredentialRequestOptionsContext.Signin, prompt.RequestContext);

        // The well-known file and the config file are fetched without cookies, the token with the origin of the site
        var wellKnown = loader.Requests.Single(request => request.Url == "https://idp.example/.well-known/web-identity");
        Assert.DoesNotContain(wellKnown.Headers, header => header.Key is "Origin" or "Cookie" or "Referer");
        var token = loader.Requests.Single(request => request.Url == "https://idp.example/token");
        Assert.Equal("POST", token.Method);
        Assert.Equal("https://example.com", token.Origin);
        Assert.Equal("client_id=rp&nonce=n&account_id=1234&disclosure_text_shown=true&is_auto_selected=false&fields=name%2Cemail%2Cpicture&disclosure_shown_for=name%2Cemail%2Cpicture", Encoding.UTF8.GetString(token.Body!.Value.Span));
        Assert.Contains(token.Headers, header => header.Key == "Sec-Fetch-Dest" && header.Value == "webidentity");
    }

    [Fact]
    public void IdentityCredential_ReturningAccount_IsAutoReauthenticated()
    {
        var loader = new IdentityProviderLoader();
        var backend = new FirstAccountChooser();
        var document = Load("<script>" + FedCmScript + "</script>", new BrowsingContextOptions { DocumentLoader = loader, UserAgentUI = backend });

        Assert.Equal("identity|token-for-1234|false|https://idp.example/fedcm.json", Run(document, "go(navigator.credentials.get(options('required')))"));

        // The account is connected to the site: an optional or silent request selects it without the account chooser
        Assert.Equal("identity|token-for-1234|true|https://idp.example/fedcm.json", Run(document, "go(navigator.credentials.get(options('silent')))"));
        Assert.IsType<IdentityAutoReauthenticationPrompt>(backend.Prompts[^1]);
        Assert.Single(backend.Prompts, prompt => prompt is IdentityAccountChooserPrompt);

        // preventSilentAccess() requires the user mediation again
        Assert.Equal("NetworkError", Run(document, "navigator.credentials.preventSilentAccess().then(() => go(navigator.credentials.get(options('silent'))))"));

        // The account is disconnected: the silent request fails
        Assert.Equal("identity|token-for-1234|false|https://idp.example/fedcm.json", Run(document, "go(navigator.credentials.get(options()))"));
        Assert.Equal("disconnected", Run(document, "IdentityCredential.disconnect({ configURL: 'https://idp.example/fedcm.json', clientId: 'rp', accountHint: '1234' }).then(() => document.title = 'disconnected', e => document.title = e.name)"));
        Assert.Equal("NetworkError", Run(document, "go(navigator.credentials.get(options('silent')))"));
        Assert.Equal("NetworkError", Run(document, "IdentityCredential.disconnect({ configURL: 'https://idp.example/fedcm.json', clientId: 'rp', accountHint: '1234' }).then(() => document.title = 'disconnected', e => document.title = e.name)"));
    }

    [Fact]
    public void IdentityCredential_TokenError_IsShownAndRejected()
    {
        var loader = new IdentityProviderLoader { TokenResponse = """{ "error": { "error": "access_denied", "url": "/error" } }""" };
        var backend = new FirstAccountChooser();
        var document = Load("<script>" + FedCmScript + "</script>", new BrowsingContextOptions { DocumentLoader = loader, UserAgentUI = backend });

        Assert.Equal("IdentityCredentialError|access_denied|https://idp.example/error", Run(document, "go(navigator.credentials.get(options()))"));
        var error = Assert.IsType<IdentityCredentialErrorPrompt>(backend.Prompts[^1]);
        Assert.Equal("access_denied", error.Error.Error);
        Assert.Equal("invalid_request|", Run(document, "document.title = (e => [e.error, e.url].join('|'))(new IdentityCredentialError('message', { error: 'invalid_request' }))"));
    }

    [Fact]
    public void IdentityCredential_ContinueOn_ResolvesTheTokenOfTheContinuationPage()
    {
        var loader = new IdentityProviderLoader { TokenResponse = """{ "continue_on": "/continue" }""" };
        var backend = new FirstAccountChooser();
        var document = Load("<script>" + FedCmScript + "</script>", new BrowsingContextOptions { DocumentLoader = loader, UserAgentUI = backend });

        Assert.Equal("identity|{\"value\":42}|false|https://idp.example/fedcm.json", Run(document, "go(navigator.credentials.get(options()))"));
        Assert.Equal("NotAllowedError", Run(document, "IdentityProvider.resolve('token').then(() => document.title = 'resolved', e => document.title = e.name)"));
    }

    [Fact]
    public void IdentityCredential_ContinueOn_ThirdPartyFrameCannotResolve()
    {
        // The iframe of another origin in the continuation page cannot complete the sign-in with its own token and account
        var loader = new IdentityProviderLoader
        {
            TokenResponse = """{ "continue_on": "/continue" }""",
            ContinuationPage = """
                <iframe src="https://third-party.example/frame"></iframe>
                <script>onmessage = e => IdentityProvider.resolve({ frame: e.data });</script>
                """,
        };
        var document = Load("<script>" + FedCmScript + "</script>", new BrowsingContextOptions { DocumentLoader = loader, UserAgentUI = new FirstAccountChooser() });

        Assert.Equal("identity|{\"frame\":\"NotAllowedError\"}|false|https://idp.example/fedcm.json", Run(document, "go(navigator.credentials.get(options()))"));
    }

    [Fact]
    public void IdentityCredential_InvalidRequests_AreRejected()
    {
        var loader = new IdentityProviderLoader();
        var document = Load("<script>" + FedCmScript + "</script>", new BrowsingContextOptions { DocumentLoader = loader, UserAgentUI = new FirstAccountChooser() });

        Assert.Equal("TypeError", Run(document, "go(navigator.credentials.get({ identity: { providers: [] } }))"));
        Assert.Equal("InvalidStateError", Run(document, "go(navigator.credentials.get(options('optional', { clientId: '' })))"));
        Assert.Equal("NotSupportedError", Run(document, "go(navigator.credentials.get({ password: true, identity: options().identity }))"));
        Assert.Equal("NetworkError", Run(document, "go(navigator.credentials.get({ identity: { mode: 'active', providers: options().identity.providers } }))"));
    }

    [Fact]
    public void IdentityCredential_LoggedOutProvider_IsNotFetched()
    {
        var loader = new IdentityProviderLoader();
        var document = Load("<script>" + FedCmScript + "</script>", new BrowsingContextOptions { DocumentLoader = loader, UserAgentUI = new FirstAccountChooser() });

        // The login status of the origin of the document is set by navigator.login.setStatus()
        Assert.Equal("set", Run(document, "navigator.login.setStatus('logged-out').then(() => document.title = 'set')"));
        Assert.Equal("NetworkError", Run(document, "go(navigator.credentials.get({ identity: { providers: [{ configURL: location.origin + '/fedcm.json', clientId: 'rp' }] } }))"));
        Assert.DoesNotContain(loader.Requests, request => request.Url.StartsWith("https://example.com/", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityCredential_Abort_ClosesTheAccountChooser()
    {
        var loader = new IdentityProviderLoader();
        var backend = new PendingAccountChooser();
        var document = Load("<script>" + FedCmScript + "window.controller = new AbortController();</script>", new BrowsingContextOptions { DocumentLoader = loader, UserAgentUI = backend });

        Run(document, "go(navigator.credentials.get(Object.assign(options(), { signal: controller.signal })))");
        Assert.False(backend.CancellationToken.IsCancellationRequested);
        Assert.Equal("NotAllowedError", Run(document, "navigator.credentials.get(options()).catch(e => document.title = e.name)"));
        Assert.Equal("AbortError", Run(document, "controller.abort()"));
        Assert.True(backend.CancellationToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData("""{ "provider_urls": ["https://idp.example/fedcm.json"] }""", "https://idp.example/fedcm.json||")]
    [InlineData("""{ "accounts_endpoint": "/accounts", "login_url": "/login" }""", "|/accounts|/login")]
    [InlineData("""{ "provider_urls": ["https://a.example/1", "https://a.example/2"] }""", null)]
    [InlineData("""{ "provider_urls": "https://idp.example/fedcm.json" }""", null)]
    [InlineData("""null""", "||")]
    [InlineData("""[]""", null)]
    [InlineData("""{ "provider_urls": [1, true, null] """, null)]
    public void IdentityProviderResponses_ParseWellKnown(string json, string? expected)
    {
        var wellKnown = IdentityProviderResponses.ParseWellKnown(Encoding.UTF8.GetBytes(json));
        Assert.Equal(expected, wellKnown is null ? null : string.Join(',', wellKnown.ProviderUrls ?? []) + "|" + wellKnown.AccountsEndpoint + "|" + wellKnown.LoginUrl);
    }

    [Theory]
    [InlineData("""{ "accounts_endpoint": "a", "id_assertion_endpoint": "t", "login_url": "l" }""", "a||t|l||False|")]
    [InlineData("""{ "accounts_endpoint": "a", "client_metadata_endpoint": "m", "id_assertion_endpoint": "t", "login_url": "l", "disconnect_endpoint": "d", "supports_use_other_account": true, "account_label": "x", "branding": { "name": "IdP", "icons": [{ "url": "i.png", "size": 32 }] } }""", "a|m|t|l|d|True|x")]
    [InlineData("""{ "accounts_endpoint": 12, "id_assertion_endpoint": 1.5, "login_url": false, "supports_use_other_account": "" }""", "12||1.5|false||False|")]
    [InlineData("""{ "accounts_endpoint": "a", "login_url": "l" }""", null)]
    [InlineData("""{ "accounts_endpoint": "a", "id_assertion_endpoint": "t", "login_url": "l", "branding": { "icons": [{ "size": 1 }] } }""", null)]
    [InlineData("""{ "accounts_endpoint": "a", "id_assertion_endpoint": "t", "login_url": "l", "branding": 1 }""", null)]
    public void IdentityProviderResponses_ParseConfig(string json, string? expected)
    {
        var config = IdentityProviderResponses.ParseConfig(Encoding.UTF8.GetBytes(json));
        Assert.Equal(expected, config is null ? null : string.Join('|', config.AccountsEndpoint, config.ClientMetadataEndpoint, config.IdAssertionEndpoint, config.LoginUrl, config.DisconnectEndpoint, config.SupportsUseOtherAccount, config.AccountLabel));
    }

    [Theory]
    [InlineData("""{ "accounts": [{ "id": "1", "name": "John", "email": "j@idp.example", "given_name": "J", "picture": "p.png", "approved_clients": ["a", "b"], "login_hints": ["h"], "domain_hints": ["d"], "label_hints": ["l"] }] }""", "1:John:j@idp.example:J:p.png:a,b:h:d:l")]
    [InlineData("""{ "accounts": [{ "id": "1", "username": "u" }, { "id": 2, "tel": "+1" }] }""", "1::::::::;2::::::::")]
    [InlineData("""{ "accounts": [] }""", "")]
    [InlineData("""{ }""", "")]
    [InlineData("""{ "accounts": [{ "id": "1" }] }""", null)]
    [InlineData("""{ "accounts": [{ "name": "John" }] }""", null)]
    [InlineData("""{ "accounts": [{ "id": "1", "name": "John", "approved_clients": "a" }] }""", null)]
    [InlineData("""{ "accounts": [{ "id": "\uD800", "name": "\u00E9" }] }""", "\uFFFD:\u00E9:::::::")]
    public void IdentityProviderResponses_ParseAccounts(string json, string? expected)
    {
        var accounts = IdentityProviderResponses.ParseAccounts(Encoding.UTF8.GetBytes(json));
        Assert.Equal(expected, accounts is null ? null : string.Join(';', accounts.Select(account => string.Join(':', account.Id, account.Name, account.Email, account.GivenName, account.Picture, string.Join(',', account.ApprovedClients ?? []), string.Join(',', account.LoginHints ?? []), string.Join(',', account.DomainHints ?? []), string.Join(',', account.LabelHints ?? [])))));
    }

    [Theory]
    [InlineData("""{ "privacy_policy_url": "p", "terms_of_service_url": "t", "client_is_third_party_to_top_frame_origin": true }""", "p|t|True")]
    [InlineData("""{ }""", "||False")]
    [InlineData("""{ "client_is_third_party_to_top_frame_origin": 0 }""", "||False")]
    [InlineData("""1""", null)]
    public void IdentityProviderResponses_ParseClientMetadata(string json, string? expected)
    {
        var metadata = IdentityProviderResponses.ParseClientMetadata(Encoding.UTF8.GetBytes(json));
        Assert.Equal(expected, metadata is null ? null : string.Join('|', metadata.PrivacyPolicyUrl, metadata.TermsOfServiceUrl, metadata.ClientIsThirdPartyToTopFrameOrigin));
    }

    [Theory]
    [InlineData("""{ "token": "token" }""", "\"token\"||")]
    [InlineData("""{ "token": { "a": [1, null] } }""", "{ \"a\": [1, null] }||")]
    [InlineData("""{ "token": null }""", "null||")]
    [InlineData("""{ "continue_on": "/continue" }""", "|/continue|")]
    [InlineData("""{ "error": { "error": "access_denied", "url": "/error" } }""", "||access_denied,/error")]
    [InlineData("""{ "error": { "code": "access_denied" } }""", "||,")]
    [InlineData("""{ "error": "access_denied" }""", null)]
    [InlineData("""{ "token": "a" """, null)]
    public void IdentityProviderResponses_ParseAssertion(string json, string? expected)
    {
        var assertion = IdentityProviderResponses.ParseAssertion(Encoding.UTF8.GetBytes(json));
        Assert.Equal(expected, assertion is null ? null : assertion.TokenJson + "|" + assertion.ContinueOn + "|" + (assertion.Error is { } error ? error.Error + "," + error.Url : ""));
    }

    [Theory]
    [InlineData("""{ "account_id": "1234" }""", "1234")]
    [InlineData("""{ "account_id": 1e21 }""", "1e+21")]
    [InlineData("""{ "account_id": [1, [2, 3]] }""", "1,2,3")]
    [InlineData("""{ }""", null)]
    [InlineData("\uFEFF{ \"account_id\": \"1\" }", "1")]
    public void IdentityProviderResponses_ParseDisconnect(string json, string? expected)
    {
        Assert.Equal(expected, IdentityProviderResponses.ParseDisconnect(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>An identity provider at https://idp.example (cross-site with the documents of the tests, https://example.com).</summary>
    private sealed class IdentityProviderLoader : DocumentLoader
    {
        public List<DocumentRequest> Requests { get; } = [];

        public string? TokenResponse { get; init; }

        public string? ContinuationPage { get; init; }

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            var path = request.Url.Split('?')[0];
            var resource = path switch
            {
                "https://idp.example/.well-known/web-identity" => Json("""{ "provider_urls": ["https://idp.example/fedcm.json"] }"""),
                "https://idp.example/fedcm.json" => Json("""{ "accounts_endpoint": "/accounts", "client_metadata_endpoint": "/metadata", "id_assertion_endpoint": "/token", "disconnect_endpoint": "/disconnect", "login_url": "/login" }"""),
                "https://idp.example/accounts" => Json("""{ "accounts": [{ "id": "1234", "name": "John Doe", "email": "john@idp.example" }, { "id": "5678", "name": "Jane Doe", "email": "jane@idp.example" }] }"""),
                "https://idp.example/metadata" => Json("""{ "privacy_policy_url": "https://rp.example/privacy" }"""),
                "https://idp.example/token" => Json(TokenResponse ?? "{ \"token\": \"token-for-" + GetField(request, "account_id") + "\" }", cors: true),
                "https://idp.example/disconnect" => Json("{ \"account_id\": \"" + GetField(request, "account_hint") + "\" }", cors: true),
                "https://idp.example/continue" => new DocumentResource(ContinuationPage ?? "<script>IdentityProvider.resolve({ value: 42 })</script>") { Headers = [new("Content-Type", "text/html")] },
                "https://third-party.example/frame" => new DocumentResource("<script>IdentityProvider.resolve('stolen', { accountId: '5678' }).then(() => parent.postMessage('resolved', '*'), e => parent.postMessage(e.name, '*'));</script>") { Headers = [new("Content-Type", "text/html")] },
                _ => null,
            };
            return ValueTask.FromResult(resource);

            static DocumentResource Json(string json, bool cors = false) => new(json)
            {
                Headers = cors
                    ? [new("Content-Type", "application/json"), new("Access-Control-Allow-Origin", "https://example.com"), new("Access-Control-Allow-Credentials", "true")]
                    : [new("Content-Type", "application/json")],
            };

            static string? GetField(DocumentRequest request, string name)
                => request.Body is { } body ? System.Web.HttpUtility.ParseQueryString(Encoding.UTF8.GetString(body.Span))[name] : null;
        }
    }

    private sealed class PendingAccountChooser : UserAgentUIBackend
    {
        public CancellationToken CancellationToken { get; private set; }

        public override ValueTask<IdentityAccountChoice?> ChooseIdentityAccountAsync(IdentityAccountChooserPrompt prompt, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return new(new TaskCompletionSource<IdentityAccountChoice?>().Task);
        }
    }

    // Screen Orientation

    private const string OrientationScript = """
        window.log = [];
        screen.orientation.onchange = () => log.push('change ' + screen.orientation.type + ' ' + screen.orientation.angle);
        window.go = p => p.then(() => log.push('locked ' + screen.orientation.type), e => log.push(e.name));
        window.fullscreen = () => document.documentElement.requestFullscreen();
        window.done = () => document.title = log.join('|');
        """;

    /// <summary>The clock of the screen orientation tests: a lock completes once the screen has rotated, after a frame.</summary>
    private readonly FakeTimeProvider _clock = new();

    private Document LoadWithScreen(ScreenOrientationProvider screen)
        => Load("<script>" + OrientationScript + "</script>", new BrowsingContextOptions
        {
            ScreenOrientation = new ScreenOrientationOptions { Provider = screen },
            TimeProvider = _clock,
        });

    /// <summary>Runs the script, then lets the screen rotate.</summary>
    private string RunFrame(Document document, string? script = null)
    {
        Run(document, script);
        _clock.Advance(TimeSpan.FromMilliseconds(20));
        return Run(document);
    }

    [Fact]
    public void ScreenOrientation_WithoutProvider_IsADesktopScreenThatCannotBeLocked()
    {
        var document = Load("<script>" + OrientationScript + "</script>");

        Assert.Equal("landscape-primary,0", Run(document, "document.title = [screen.orientation.type, screen.orientation.angle].join()"));
        Assert.Equal("NotSupportedError", Run(document, "go(screen.orientation.lock('portrait')).then(done)"));
    }

    [Fact]
    public void ScreenOrientation_Lock_RotatesTheScreen()
    {
        #region WebPlatform_ScreenOrientation
        // The screen of a phone, held in portrait: lock() rotates it to the orientation of the lock
        var screen = new ScreenOrientationProvider(OrientationType.PortraitPrimary);
        var options = new BrowsingContextOptions
        {
            ScreenOrientation = new ScreenOrientationOptions { Provider = screen },
        };
        #endregion

        var document = Load("<script>" + OrientationScript + "</script>", new BrowsingContextOptions { ScreenOrientation = options.ScreenOrientation, TimeProvider = _clock });
        Assert.Equal("portrait-primary,0", Run(document, "document.title = [screen.orientation.type, screen.orientation.angle].join()"));

        Activate(document);
        Run(document, "fullscreen().then(() => go(screen.orientation.lock('landscape')))");
        Assert.Equal(OrientationLockType.Landscape, screen.ActiveLock);
        Assert.Equal("", Run(document, "done()"));

        RunFrame(document);
        Assert.Equal("change landscape-primary 90|locked landscape-primary", Run(document, "done()"));

        RunFrame(document, "screen.orientation.unlock()");
        Assert.Equal("change landscape-primary 90|locked landscape-primary|change portrait-primary 0", Run(document, "done()"));
        Assert.Null(screen.ActiveLock);
    }

    [Fact]
    public void ScreenOrientation_LockWithoutFullscreen_IsRejectedWithNotAllowedError()
    {
        var document = LoadWithScreen(new ScreenOrientationProvider());

        Assert.Equal("NotAllowedError", Run(document, "go(screen.orientation.lock('portrait')).then(done)"));
    }

    [Fact]
    public void ScreenOrientation_ExitFullscreen_UnlocksTheScreen()
    {
        var screen = new ScreenOrientationProvider();
        var document = LoadWithScreen(screen);
        Activate(document);
        RunFrame(document, "fullscreen().then(() => go(screen.orientation.lock('portrait-secondary')))");
        Assert.Equal(OrientationLockType.PortraitSecondary, screen.ActiveLock);

        RunFrame(document, "document.exitFullscreen()");

        Assert.Null(screen.ActiveLock);
        Assert.Equal("change portrait-secondary 270|locked portrait-secondary|change landscape-primary 0", Run(document, "done()"));
    }

    [Fact]
    public void ScreenOrientation_DeviceRotation_FiresTheChangeEvent()
    {
        var screen = new ScreenOrientationProvider();
        var document = LoadWithScreen(screen);

        screen.DeviceOrientation = OrientationType.LandscapeSecondary;
        Run(document);

        Assert.Equal("change landscape-secondary 180", Run(document, "done()"));
    }

    [Fact]
    public void ScreenOrientation_Override_EmulatesTheNaturalOrientationAndTheType()
    {
        var screen = new ScreenOrientationProvider();
        var document = LoadWithScreen(screen);

        screen.SetOrientationOverride(OrientationType.PortraitPrimary, OrientationType.LandscapeSecondary);
        Run(document);

        Assert.Equal(OrientationType.PortraitPrimary, screen.NaturalOrientation);
        Assert.Equal(270, screen.CurrentAngle);
        Assert.Equal("change landscape-secondary 270", Run(document, "done()"));

        screen.ClearOrientationOverride();
        Run(document);

        Assert.Equal(OrientationType.LandscapePrimary, screen.NaturalOrientation);
        Assert.Equal("change landscape-secondary 270|change landscape-primary 0", Run(document, "done()"));
    }

    [Fact]
    public void ScreenOrientation_SecondLock_AbortsThePendingOne()
    {
        var document = LoadWithScreen(new ScreenOrientationProvider());
        Activate(document);

        RunFrame(document, "fullscreen().then(() => { go(screen.orientation.lock('portrait')); go(screen.orientation.lock('landscape')); })");

        Assert.Equal("AbortError|locked landscape-primary", Run(document, "done()"));
    }

    [Fact]
    public void ScreenOrientation_RemovedIframe_AbortsTheLockRequest()
    {
        var document = LoadWithScreen(new ScreenOrientationProvider());
        Activate(document);

        RunFrame(document, """
            const frame = document.createElement('iframe');
            document.body.append(frame);
            const orientation = frame.contentWindow.screen.orientation;
            fullscreen().then(() => {
              const promise = orientation.lock('portrait');
              frame.remove();
              return promise;
            }).catch(e => { log.push(e.name); done(); });
            """);

        Assert.Equal("AbortError", Run(document, "done()"));
    }

    // VirtualKeyboard

    private const string VirtualKeyboardScript = """
        window.log = [];
        navigator.virtualKeyboard.ongeometrychange = e => {
          const r = navigator.virtualKeyboard.boundingRect;
          log.push([e.type, e.bubbles, r.x, r.y, r.width, r.height].join(' '));
        };
        window.done = () => document.title = log.join('|');
        """;

    [Fact]
    public void VirtualKeyboard_Show_CallsTheHost()
    {
        #region WebPlatform_VirtualKeyboard
        var keyboard = new VirtualKeyboardProvider();
        var options = new BrowsingContextOptions
        {
            VirtualKeyboard = new VirtualKeyboardOptions
            {
                // The system shows its keyboard, then reports its rectangle (relative to the viewport)
                Show = callbackContext => keyboard.BoundingRect = new VirtualKeyboardRect(0, 400, 800, 200),
                Hide = callbackContext => keyboard.BoundingRect = default,
                Provider = keyboard,
            },
        };
        #endregion

        var document = Load("""<div contenteditable virtualkeyboardpolicy="manual">text</div><script>""" + VirtualKeyboardScript + "</script>", options);
        Activate(document);

        Run(document, "document.querySelector('div').focus(); navigator.virtualKeyboard.show()");
        Assert.Equal("geometrychange false 0 400 800 200", Run(document, "done()"));

        Run(document, "navigator.virtualKeyboard.hide()");
        Assert.Equal("geometrychange false 0 400 800 200|geometrychange false 0 0 0 0", Run(document, "done()"));
    }

    [Fact]
    public void VirtualKeyboard_Show_RequiresTheManualPolicy()
    {
        var shown = 0;
        var document = Load("""<div contenteditable>text</div><input><script>""" + VirtualKeyboardScript + "</script>", new BrowsingContextOptions
        {
            VirtualKeyboard = new VirtualKeyboardOptions { Show = _ => shown++ },
        });

        Run(document, "document.querySelector('div').focus(); navigator.virtualKeyboard.show()");
        Activate(document);
        Run(document, "navigator.virtualKeyboard.show()");
        Assert.Equal(0, shown);

        Run(document, "document.querySelector('div').virtualKeyboardPolicy = 'MANUAL'; navigator.virtualKeyboard.show()");
        Assert.Equal(1, shown);

        Run(document, "document.querySelector('div').setAttribute('inputmode', 'none'); navigator.virtualKeyboard.show()");
        Assert.Equal(1, shown);
    }

    [Fact]
    public void VirtualKeyboard_OverlaysContent_SetsTheKeyboardInsets()
    {
        var keyboard = new VirtualKeyboardProvider();
        var document = Load("""
            <style>div { margin-top: env(keyboard-inset-top, 1px); height: env(keyboard-inset-height, 1px); }</style>
            <div></div>
            <script>
            window.insets = () => { const style = getComputedStyle(document.querySelector('div')); document.title = style.marginTop + ' ' + style.height; };
            </script>
            """, new BrowsingContextOptions { VirtualKeyboard = new VirtualKeyboardOptions { Provider = keyboard } });

        keyboard.BoundingRect = new VirtualKeyboardRect(0, 300, 800, 300);
        Assert.Equal("0px 0px", Run(document, "insets()"));

        Assert.Equal("300px 300px", Run(document, "navigator.virtualKeyboard.overlaysContent = true; insets()"));
    }

    // Viewport segments

    [Fact]
    public void ViewportSegments_DisplayFeatures_SplitTheViewport()
    {
        #region WebPlatform_ViewportSegments
        // A foldable device: a vertical hinge of 20 pixels splits the viewport in two segments, side by side
        var displayFeatures = new DisplayFeaturesProvider();
        var options = new BrowsingContextOptions
        {
            ViewportSegments = new ViewportSegmentsOptions { Provider = displayFeatures },
        };
        displayFeatures.DisplayFeatures = [new DisplayFeature(DisplayFeatureOrientation.Vertical, Offset: 390, MaskLength: 20)];
        #endregion

        var document = Load("""
            <style>div { width: env(viewport-segment-width 1 0, 1px); margin-left: env(viewport-segment-left 1 0, 1px); }</style>
            <div></div>
            <iframe srcdoc="<p>"></iframe>
            <script>
            window.describe = () => {
              const style = getComputedStyle(document.querySelector('div'));
              document.title = [
                viewport.segments.map(s => [s.x, s.y, s.width, s.height].join(' ')).join(','),
                matchMedia('(horizontal-viewport-segments: 2) and (vertical-viewport-segments: 1)').matches,
                style.width, style.marginLeft,
                frames[0].viewport.segments,
              ].join('|');
            };
            </script>
            """, options);

        Assert.Equal("0 0 390 600,410 0 390 600|true|390px|410px|", Run(document, "describe()"));

        displayFeatures.DisplayFeatures = [];
        Assert.Equal("0 0 800 600|false|1px|1px|", Run(document, "describe()"));
    }

    // Keyboard Map

    private const string LayoutMapScript = "window.go = () => navigator.keyboard.getLayoutMap().then(map => document.title = [map.get('KeyQ'), map.get('KeyA'), map.get('Digit1'), map.size, map.has('IntlRo'), map instanceof KeyboardLayoutMap].join(), e => document.title = e.name);";

    [Fact]
    public void KeyboardMap_DefaultLayout_IsUsQwerty()
    {
        var document = Load("<script>" + LayoutMapScript + "</script>");

        Assert.Equal("q,a,1,47,false,true", Run(document, "go()"));
    }

    [Fact]
    public void KeyboardMap_ReadsTheProvider()
    {
        #region WebPlatform_KeyboardMap
        var keyboard = new KeyboardLayoutProvider
        {
            // The key produced by each key of the keyboard, by code: setting the layout fires the layoutchange event
            Layout = new Dictionary<string, string>(KeyboardLayoutProvider.UsQwerty) { ["KeyQ"] = "a", ["KeyA"] = "q", ["Digit1"] = "&" },
        };

        var options = new BrowsingContextOptions
        {
            KeyboardMap = new KeyboardMapOptions { Provider = keyboard },
        };
        #endregion

        var document = Load("<script>" + LayoutMapScript + "</script>", options);

        Assert.Equal("a,q,&,47,false,true", Run(document, "go()"));

        Run(document, "navigator.keyboard.onlayoutchange = () => go()");
        keyboard.Layout = KeyboardLayoutProvider.UsQwerty;
        Assert.Equal("q,a,1,47,false,true", Run(document));
    }

    [Fact]
    public void KeyboardMap_ParallelCalls_ReturnTheSamePromise()
    {
        var document = Load("<script>const p = navigator.keyboard.getLayoutMap(); const same = p === navigator.keyboard.getLayoutMap(); p.then(() => document.title = same + ',' + (p === navigator.keyboard.getLayoutMap()));</script>");

        Assert.Equal("true,false", Run(document));
    }

    [Fact]
    public void KeyboardMap_DisallowedIframe_IsRejectedWithSecurityError()
    {
        var document = Load("""<iframe allow="keyboard-map 'none'" srcdoc="<p>"></iframe><script>""" + LayoutMapScript + "</script>");

        Assert.Equal("SecurityError", Run(document, "document.querySelector('iframe').contentWindow.navigator.keyboard.getLayoutMap().catch(e => document.title = e.name)"));
    }

    // Media Session

    [Fact]
    public void MediaSession_NotifiesTheHostAndRunsTheActions()
    {
        #region WebPlatform_MediaSession
        string? nowPlaying = null;
        MediaSession? session = null;
        var options = new BrowsingContextOptions
        {
            MediaSession = new MediaSessionOptions
            {
                // Called when the page changes its metadata, its playback state, its position or its action handlers
                SessionChanged = (callbackContext, mediaSession) =>
                {
                    session = mediaSession;
                    nowPlaying = mediaSession.Metadata is { } metadata ? $"{metadata.Title} - {metadata.Artist} ({mediaSession.PlaybackState})" : null;
                },
            },
        };
        #endregion

        var document = Load("""
            <script>
              navigator.mediaSession.metadata = new MediaMetadata({ title: 'Song', artist: 'Band', artwork: [{ src: 'cover.png', sizes: '96x96' }] });
              navigator.mediaSession.playbackState = 'playing';
              navigator.mediaSession.setActionHandler('pause', details => document.title = details.action);
              navigator.mediaSession.setPositionState({ duration: 60, position: 10 });
            </script>
            """, options);

        Assert.Equal("Song - Band (Playing)", nowPlaying);
        Assert.Equal("https://example.com/cover.png", Assert.Single(session!.Metadata!.Artwork).Src);
        Assert.Equal([MediaSessionAction.Pause], session.SupportedActions);
        Assert.Equal(10, session.PositionState!.Position);
        Assert.Equal(1, session.PositionState.PlaybackRate);

        Run(document, "navigator.mediaSession.metadata.title = 'Other song'");
        Assert.Equal("Other song - Band (Playing)", nowPlaying);

        Assert.False(session.HandleAction(new MediaSessionActionDetails { Action = MediaSessionAction.Play }));
        Assert.True(session.HandleAction(new MediaSessionActionDetails { Action = MediaSessionAction.Pause }));
        Assert.Equal("pause", Run(document));
    }

    [Fact]
    public void MediaSession_SetPositionState_ValidatesTheState()
    {
        var document = Load("<script></script>");

        Assert.Equal("TypeError,TypeError,TypeError,TypeError,ok", Run(document, """
            const results = [];
            for (const state of [{ position: 1 }, { duration: -1 }, { duration: 10, position: 20 }, { duration: 10, playbackRate: 0 }, {}]) {
              try { navigator.mediaSession.setPositionState(state); results.push('ok'); } catch (e) { results.push(e.name); }
            }
            document.title = results.join();
            """));
    }

    [Fact]
    public void MediaMetadata_Artwork_IsFrozen()
    {
        var document = Load("<script></script>");

        Assert.Equal("true,true,true,http://example.com/a", Run(document, """
            const metadata = new MediaMetadata({ artwork: [{ src: 'http://example.com/a' }] });
            document.title = [Object.isFrozen(metadata.artwork), Object.isFrozen(metadata.artwork[0]), metadata.artwork === metadata.artwork, metadata.artwork[0].src].join();
            """));
    }

    // Page Lifecycle

    [Fact]
    public void PageLifecycle_FreezeDefersTheTasks()
    {
        var document = Load("""
            <script>
              window.log = [];
              document.onfreeze = () => log.push('freeze');
              document.onresume = () => log.push('resume');
              window.onmessage = e => log.push(e.data);
              window.done = () => document.title = log.join();
            </script>
            """);

        #region WebPlatform_PageLifecycle
        // Freezes the tab: the freeze event is fired, then the tasks and the timers of the documents wait
        document.BrowsingContext!.IsFrozen = true;
        #endregion

        Run(document);
        Run(document, "postMessage('message', '*')");
        Assert.Equal("freeze", Run(document, "done()"));

        document.BrowsingContext.IsFrozen = false;
        Run(document);
        Assert.Equal("freeze,resume,message", Run(document, "done()"));
    }

    [Fact]
    public void ProtocolHandlers_RegisteredHandlerHandlesTheNavigation()
    {
        #region WebPlatform_ProtocolHandlers
        // The handlers registered by navigator.registerProtocolHandler() when the user accepts them
        var protocolHandlers = new ProtocolHandlerOptions
        {
            RequestRegistration = registration => registration.Scheme == "web+soup",
        };
        var options = new BrowsingContextOptions { ProtocolHandlers = protocolHandlers };
        #endregion

        var loader = new RecordingLoader();
        loader.Resources["https://example.com/soup?url=web%2Bsoup%3Achicken-kiwi"] = new DocumentResource("<title>soup</title>");
        var document = Load("", new BrowsingContextOptions { ProtocolHandlers = options.ProtocolHandlers, DocumentLoader = loader });

        Run(document, "navigator.registerProtocolHandler('web+soup', 'soup?url=%s'); navigator.registerProtocolHandler('web+other', 'other?url=%s')");
        Assert.Equal([KeyValuePair.Create("web+soup", "https://example.com/soup?url=%s")], protocolHandlers.Registry.Handlers);

        var browsingContext = document.BrowsingContext!;
        Run(document, "location.href = 'web+soup:chicken-kiwi'");
        Assert.Equal("https://example.com/soup?url=web%2Bsoup%3Achicken-kiwi", browsingContext.Document.Url);
        Assert.Equal("soup", browsingContext.Document.Title);

        Run(browsingContext.Document, "navigator.unregisterProtocolHandler('web+soup', 'soup?url=%s')");
        Assert.Empty(protocolHandlers.Registry.Handlers);
    }

    [Fact]
    public void ProtocolHandlers_NavigationToARegisteredSchemeAsksToLeaveThePageOnce()
    {
        var prompts = new List<UserPromptType>();
        var protocolHandlers = new ProtocolHandlerOptions { RequestRegistration = _ => true };
        var loader = new RecordingLoader();
        loader.Resources["https://example.com/soup?url=web%2Bsoup%3Achicken"] = new DocumentResource("<title>soup</title>");
        var document = Load("<script>addEventListener('beforeunload', e => e.preventDefault());</script>", new BrowsingContextOptions
        {
            ProtocolHandlers = protocolHandlers,
            DocumentLoader = loader,
            UserPromptHandler = (_, prompt) =>
            {
                prompts.Add(prompt.Type);
                return UserPromptResult.Accept();
            },
        });

        Run(document, "navigator.registerProtocolHandler('web+soup', 'soup?url=%s')");
        Activate(document);
        var browsingContext = document.BrowsingContext!;
        Run(document, "location.href = 'web+soup:chicken'");

        Assert.Equal([UserPromptType.BeforeUnload], prompts);
        Assert.Equal("soup", browsingContext.Document.Title);
    }

    [Fact]
    public void ProtocolHandlers_AutomationModeAcceptsOrRejectsTheRegistrations()
    {
        var protocolHandlers = new ProtocolHandlerOptions();
        var document = Load("", new BrowsingContextOptions { ProtocolHandlers = protocolHandlers });

        Run(document, "navigator.registerProtocolHandler('web+none', 'none?url=%s')");
        document.ProtocolHandlerAutomationMode = ProtocolHandlerAutomationMode.AutoReject;
        Run(document, "navigator.registerProtocolHandler('web+rejected', 'rejected?url=%s')");
        document.ProtocolHandlerAutomationMode = ProtocolHandlerAutomationMode.AutoAccept;
        Run(document, "navigator.registerProtocolHandler('web+accepted', 'accepted?url=%s')");

        Assert.Equal([KeyValuePair.Create("web+accepted", "https://example.com/accepted?url=%s")], protocolHandlers.Registry.Handlers);
    }

    [Fact]
    public async Task PageLifecycle_Discard_ReloadsTheDocument()
    {
        var loader = new RecordingLoader();
        loader.Resources["https://example.com/"] = new DocumentResource("<script>document.title = document.wasDiscarded;</script>");
        var document = Load("<script>document.title = document.wasDiscarded;</script>", new BrowsingContextOptions { DocumentLoader = loader });
        Assert.Equal("false", document.Title);

        var browsingContext = document.BrowsingContext!;
        browsingContext.Discard();
        await RunAsync(document);

        Assert.NotSame(document, browsingContext.Document);
        Assert.Equal("true", browsingContext.Document.Title);
        Assert.True(browsingContext.Document.WasDiscarded);
    }

    [Fact]
    public void IsInputPending_WithoutHandler_ReturnsFalse()
    {
        var document = Load("<p>");

        Assert.Equal("false,false,true", Run(document, "document.title = [navigator.scheduling.isInputPending(), navigator.scheduling.isInputPending({ includeContinuous: true }), navigator.scheduling === navigator.scheduling].join()"));
    }

    [Fact]
    public void IsInputPending_CallsTheHost()
    {
        #region WebPlatform_PendingInput
        var pendingInput = new Queue<string>(["pointermove", "click"]);
        var options = new BrowsingContextOptions
        {
            PendingInput = new PendingInputOptions
            {
                // The input events that the host has not dispatched yet; the continuous events (e.g. pointermove) are
                // only considered when includeContinuous is true
                IsInputPending = (callbackContext, includeContinuous) => pendingInput.Any(type => includeContinuous || type is not ("pointermove" or "mousemove" or "wheel" or "touchmove")),
            },
        };
        #endregion

        var document = Load("<p>", options);

        Assert.Equal("true,true", Run(document, "document.title = [navigator.scheduling.isInputPending(), navigator.scheduling.isInputPending({ includeContinuous: true })].join()"));

        pendingInput.Dequeue();
        pendingInput.Dequeue();
        pendingInput.Enqueue("pointermove");
        Assert.Equal("false,true", Run(document, "document.title = [navigator.scheduling.isInputPending(), navigator.scheduling.isInputPending({ includeContinuous: true })].join()"));
    }

    [Fact]
    public void IsInputPending_NotFullyActive_ReturnsFalse()
    {
        var document = Load("<iframe srcdoc='<p>'></iframe>", new BrowsingContextOptions { PendingInput = new PendingInputOptions { IsInputPending = (_, _) => true } });

        Assert.Equal("true,false", Run(document, """
            const iframe = document.querySelector('iframe');
            const scheduling = iframe.contentWindow.navigator.scheduling;
            const before = scheduling.isInputPending();
            iframe.remove();
            document.title = [before, scheduling.isInputPending()].join();
            """));
    }

    // Sensors

    private const string SensorScript = """
        window.log = [];
        window.startSensor = sensor => {
          window.sensor = sensor;
          sensor.onactivate = () => log.push('activate');
          sensor.onerror = e => log.push(e.error.name);
          sensor.onreading = () => log.push(window.describe(sensor));
          sensor.start();
        };
        window.describe = s => [s.x, s.y, s.z].join(',');
        window.done = () => document.title = log.join('|');
        """;

    private static PermissionStore GrantedSensorPermissions()
    {
        var permissions = new PermissionStore();
        foreach (var name in (ReadOnlySpan<string>)["accelerometer", "gyroscope", "magnetometer", "ambient-light-sensor"])
        {
            permissions.SetState(name, PermissionState.Granted);
        }

        return permissions;
    }

    [Fact]
    public void Sensor_WithoutDevice_FiresNotReadableError()
    {
        var document = Load("<script>" + SensorScript + "startSensor(new Accelerometer());</script>", new BrowsingContextOptions { Permissions = GrantedSensorPermissions() });

        Assert.Equal("NotReadableError", Run(document, "done()"));
        Assert.Equal("false", Run(document, "document.title = sensor.activated"));
    }

    [Fact]
    public void Sensor_WithoutPermission_FiresNotAllowedError()
    {
        var provider = new DeviceSensorProvider();
        provider.AddSensor(DeviceSensorType.Accelerometer);
        var document = Load("<script>" + SensorScript + "startSensor(new Accelerometer());</script>", new BrowsingContextOptions { Sensors = new DeviceSensorsOptions { Provider = provider } });

        Assert.Equal("NotAllowedError", Run(document, "done()"));
    }

    [Fact]
    public void Accelerometer_ReadsTheSensorOfTheDevice()
    {
        #region WebPlatform_Sensors
        var sensors = new DeviceSensorProvider();

        // The accelerometer of the device: Update() makes a new reading available to the sensor objects of the documents
        var accelerometer = sensors.AddSensor(DeviceSensorType.Accelerometer);

        var permissions = new PermissionStore();
        permissions.SetState("accelerometer", PermissionState.Granted);
        var options = new BrowsingContextOptions
        {
            Sensors = new DeviceSensorsOptions { Provider = sensors },
            Permissions = permissions,
        };
        #endregion

        var document = Load("<script>" + SensorScript + "startSensor(new Accelerometer());</script>", options);
        Run(document);

        accelerometer.Update(DeviceSensorReading.FromXyz(1.12345, 2.16, -9.81));
        Run(document);
        Assert.Equal("activate|1.1,2.2,-9.8", Run(document, "done()"));
        Assert.Equal("true,true", Run(document, "document.title = [sensor.hasReading, sensor.timestamp > 0].join()"));

        Assert.Equal("false,null,null", Run(document, "sensor.stop(); document.title = [sensor.hasReading, String(sensor.x), String(sensor.timestamp)].join()"));
    }

    [Fact]
    public void Sensor_RequestedSamplingFrequency_IsTheHighestFrequencyWithinTheBounds()
    {
        var provider = new DeviceSensorProvider();
        var gyroscope = provider.AddSensor(DeviceSensorType.Gyroscope, maximumSamplingFrequency: 30);
        var document = Load("<script>" + SensorScript + "</script>", new BrowsingContextOptions { Sensors = new DeviceSensorsOptions { Provider = provider }, Permissions = GrantedSensorPermissions() });

        Assert.Equal(0, gyroscope.RequestedSamplingFrequency);
        Run(document, "window.slow = new Gyroscope({ frequency: 10 }); slow.start()");
        Assert.Equal(10, gyroscope.RequestedSamplingFrequency);

        Run(document, "window.fast = new Gyroscope({ frequency: 500 }); fast.start()");
        Assert.Equal(30, gyroscope.RequestedSamplingFrequency);

        Run(document, "fast.stop()");
        Assert.Equal(10, gyroscope.RequestedSamplingFrequency);

        Run(document, "slow.stop()");
        Assert.Equal(0, gyroscope.RequestedSamplingFrequency);
    }

    [Fact]
    public void AmbientLightSensor_RoundsTheIlluminanceAndDiscardsTheSmallChanges()
    {
        var clock = new FakeTimeProvider();
        var provider = new DeviceSensorProvider();
        var light = provider.AddSensor(DeviceSensorType.AmbientLight);
        var document = Load("<script>" + SensorScript + "window.describe = s => s.illuminance; startSensor(new AmbientLightSensor());</script>", new BrowsingContextOptions { Sensors = new DeviceSensorsOptions { Provider = provider }, Permissions = GrantedSensorPermissions(), TimeProvider = clock });
        Run(document);

        foreach (var illuminance in (ReadOnlySpan<double>)[127, 165, 24, 35, 49])
        {
            light.Update(DeviceSensorReading.FromIlluminance(illuminance));
            Run(document);

            // The readings are reported at most at the frequency of the sensor (5 Hz by default)
            clock.Advance(TimeSpan.FromSeconds(1));
            Run(document);
        }

        Assert.Equal("activate|150|0|50", Run(document, "done()"));
    }

    [Fact]
    public void Sensor_HiddenDocument_GetsTheReadingWhenItBecomesVisible()
    {
        var provider = new DeviceSensorProvider();
        var accelerometer = provider.AddSensor(DeviceSensorType.Accelerometer);
        var document = Load("<script>" + SensorScript + "startSensor(new Accelerometer());</script>", new BrowsingContextOptions { Sensors = new DeviceSensorsOptions { Provider = provider }, Permissions = GrantedSensorPermissions() });
        Run(document);

        document.BrowsingContext!.IsVisible = false;
        Run(document);
        accelerometer.Update(DeviceSensorReading.FromXyz(1, 2, 3));
        Assert.Equal("activate", Run(document, "done()"));

        document.BrowsingContext.IsVisible = true;
        Run(document);
        Assert.Equal("activate|1,2,3", Run(document, "done()"));
    }

    [Fact]
    public void Sensor_NotAllowedByPermissionsPolicy_ThrowsSecurityError()
    {
        var document = Load("""
            <iframe allow="gyroscope 'none'" srcdoc="<p>"></iframe>
            <script>
              window.onload = () => {
                try {
                  new (document.querySelector('iframe').contentWindow.Gyroscope)();
                  document.title = 'created';
                } catch (e) {
                  document.title = e.name;
                }
              };
            </script>
            """);

        Assert.Equal("SecurityError", Run(document));
    }

    [Fact]
    public void OrientationSensor_QuaternionAndRotationMatrix()
    {
        var provider = new DeviceSensorProvider();
        var orientation = provider.AddSensor(DeviceSensorType.RelativeOrientation);
        orientation.Update(DeviceSensorReading.FromEulerAngles(alpha: 0, beta: -180, gamma: 0));
        var document = Load("<script>" + SensorScript + "window.describe = s => s.quaternion.map(Math.round).join(); startSensor(new RelativeOrientationSensor());</script>", new BrowsingContextOptions { Sensors = new DeviceSensorsOptions { Provider = provider }, Permissions = GrantedSensorPermissions() });

        Assert.Equal("activate|-1,0,0,0", Run(document, "done()"));
        Assert.Equal("true,1,0,0,0,0,-1,0,0,0,0,-1,0,0,0,0,1", Run(document, """
            const matrix = new Float64Array(16);
            sensor.populateMatrix(matrix);
            document.title = [sensor.quaternion === sensor.quaternion, ...Array.from(matrix, Math.round)].join();
            """));
        Assert.Equal("TypeError", Run(document, "try { sensor.populateMatrix(new Float32Array(15)); } catch (e) { document.title = e.name; }"));
    }

    // Device orientation and motion events

    [Fact]
    public void DeviceOrientation_FiresTheEventsWithTheReadings()
    {
        #region WebPlatform_DeviceOrientation
        var sensors = new DeviceSensorProvider();

        // The deviceorientation event reads the relative orientation sensor, as Euler angles in degrees
        var orientation = sensors.AddSensor(DeviceSensorType.RelativeOrientation);
        #endregion

        var document = Load("""
            <script>
              window.log = [];
              window.ondeviceorientation = e => log.push([e.alpha, e.beta, e.gamma, e.absolute].join());
              window.done = () => document.title = log.join('|');
            </script>
            """, new BrowsingContextOptions { Sensors = new DeviceSensorsOptions { Provider = sensors }, Permissions = GrantedSensorPermissions() });

        orientation.Update(DeviceSensorReading.FromEulerAngles(alpha: 1.23, beta: 2.34, gamma: 3.46));
        Run(document);
        orientation.Update(DeviceSensorReading.FromEulerAngles(alpha: 1.21, beta: 2.33, gamma: 3.46));
        Run(document);
        orientation.Update(DeviceSensorReading.FromEulerAngles(alpha: 10, beta: 20, gamma: 30));
        Run(document);

        Assert.Equal("1.2,2.3,3.5,false|10,20,30,false", Run(document, "done()"));
    }

    [Fact]
    public void DeviceOrientation_WithoutSensor_FiresANullEvent()
    {
        var document = Load("<script>window.addEventListener('deviceorientation', e => document.title = [e.alpha, e.beta, e.gamma, e.absolute].join());</script>");

        Assert.Equal(",,,false", Run(document));
    }

    [Fact]
    public void DeviceMotion_FiresTheEventsWithTheReadings()
    {
        var clock = new FakeTimeProvider();
        var sensors = new DeviceSensorProvider();
        sensors.AddSensor(DeviceSensorType.Accelerometer).Update(DeviceSensorReading.FromXyz(1.23, 2, 3));
        sensors.AddSensor(DeviceSensorType.LinearAcceleration).Update(DeviceSensorReading.FromXyz(0, 0.5, 0));
        sensors.AddSensor(DeviceSensorType.Gyroscope).Update(DeviceSensorReading.FromXyz(Math.PI, 0, 0));
        var document = Load("""
            <script>
              window.addEventListener('devicemotion', e => document.title = [
                e.acceleration.x, e.acceleration.y, e.accelerationIncludingGravity.x, e.rotationRate.alpha, e.interval].join(), { once: true });
            </script>
            """, new BrowsingContextOptions { Sensors = new DeviceSensorsOptions { Provider = sensors }, Permissions = GrantedSensorPermissions(), TimeProvider = clock });

        clock.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal("0,0.5,1.2,180,16", Run(document));
    }

    [Fact]
    public async Task DeviceMotionEvent_RequestPermission()
    {
        var document = Load("<script>window.go = p => p.then(state => document.title = state, e => document.title = e.name);</script>");
        Assert.Equal("NotAllowedError", await RunAsync(document, "go(DeviceMotionEvent.requestPermission())"));

        var granted = Load("<script>window.go = p => p.then(state => document.title = state, e => document.title = e.name);</script>", new BrowsingContextOptions { Permissions = GrantedSensorPermissions() });
        Assert.Equal("granted", await RunAsync(granted, "go(DeviceOrientationEvent.requestPermission(true))"));
    }

    [Fact]
    public void CreateEvent_DeviceOrientationAndMotionEvents()
    {
        var document = Load("<script>document.title = [document.createEvent('DeviceOrientationEvent').alpha, document.createEvent('DeviceMotionEvent').interval].join();</script>");

        Assert.Equal(",0", Run(document));
    }

    [Fact]
    public void DisabledFeatures_HideTheApis()
    {
        var document = Load(
            "<script>document.title = ['share' in navigator, 'vibrate' in navigator, 'geolocation' in navigator, typeof Notification, 'setAppBadge' in navigator, 'getBattery' in navigator, typeof BatteryManager, 'wakeLock' in navigator, typeof WakeLockSentinel, 'keyboard' in navigator, typeof Keyboard, 'orientation' in screen, typeof ScreenOrientation, 'virtualKeyboard' in navigator, typeof VirtualKeyboard, 'scheduling' in navigator, typeof Scheduling, typeof Accelerometer, 'ondevicemotion' in window].join();</script>",
            new BrowsingContextOptions { Features = new WebPlatformFeatures { WebShareEnabled = false, VibrationEnabled = false, GeolocationEnabled = false, NotificationsEnabled = false, BadgingEnabled = false, BatteryStatusEnabled = false, WakeLockEnabled = false, KeyboardLockEnabled = false, KeyboardMapEnabled = false, ScreenOrientationEnabled = false, VirtualKeyboardEnabled = false, InputPendingEnabled = false, SensorsEnabled = false, DeviceOrientationEnabled = false } });

        Assert.Equal("false,false,false,undefined,false,false,undefined,false,undefined,false,undefined,false,undefined,false,undefined,false,undefined,undefined,false", Run(document));
    }
}

#region WebPlatform_UserAgentUIBackend
/// <summary>
/// A credential store of the user agent: <c>navigator.credentials.store()</c> saves the credentials without asking, and
/// <c>navigator.credentials.get()</c> returns the first stored credential that matches the request.
/// </summary>
internal sealed class InMemoryCredentialStore : UserAgentUIBackend
{
    private readonly List<Credential> _credentials = [];

    public int ChooserPrompts { get; private set; }

    public CredentialChooserPrompt? LastPrompt { get; private set; }

    public override ValueTask<bool> StoreCredentialAsync(CredentialStorePrompt prompt, CancellationToken cancellationToken)
    {
        _credentials.Add(prompt.Credential);
        return ValueTask.FromResult(true);
    }

    public override ValueTask<Credential?> ChooseCredentialAsync(CredentialChooserPrompt prompt, CancellationToken cancellationToken)
    {
        ChooserPrompts++;
        LastPrompt = prompt;
        return ValueTask.FromResult(_credentials.Find(prompt.Matches));
    }
}
#endregion

#region WebPlatform_DigitalCredentialWallet
/// <summary>A wallet that answers the first request of a digital credential request with the request itself.</summary>
internal sealed class SingleResponseWallet : UserAgentUIBackend
{
    public DigitalCredentialPrompt? LastPrompt { get; private set; }

    public override ValueTask<DigitalCredentialResponse?> RequestDigitalCredentialAsync(DigitalCredentialPrompt prompt, CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        var request = prompt.Requests[0];
        var response = $$"""{"protocol":"{{request.Protocol}}","request":{{request.Data}}}""";
        return ValueTask.FromResult<DigitalCredentialResponse?>(new DigitalCredentialResponse(request.Protocol, response));
    }
}
#endregion

#region WebPlatform_UserAgentUIBackend_Payments
/// <summary>
/// A payment sheet whose user accepts the payment with the first supported payment method, once the page updated the
/// details: the user agent has a payment handler for https://pay.example/pay.
/// </summary>
internal sealed class AcceptingPaymentSheet : UserAgentUIBackend
{
    public PaymentSheetPrompt? LastPrompt { get; private set; }

    public PaymentComplete? Result { get; private set; }

    public override ValueTask<bool> HasPaymentHandlerAsync(PaymentHandlerQuery query, CancellationToken cancellationToken)
        => ValueTask.FromResult(query.Method.Identifier == "https://pay.example/pay");

    public override async ValueTask<PaymentSheetResponse?> ShowPaymentSheetAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        var details = await prompt.WaitForUpdateAsync(cancellationToken);
        return new PaymentSheetResponse
        {
            MethodName = prompt.Methods[0].Identifier,
            Details = $$"""{"token": "tok_{{details.Total.Amount.Value}}"}""",
            PayerName = "John Doe",
        };
    }

    // The user fixes the errors shown by the sheet (prompt.Errors)
    public override ValueTask<PaymentSheetResponse?> RetryPaymentAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken)
        => ValueTask.FromResult<PaymentSheetResponse?>(new PaymentSheetResponse { MethodName = prompt.Response!.MethodName, PayerName = "Jane Doe" });

    public override ValueTask CompletePaymentAsync(PaymentSheetPrompt prompt, PaymentComplete result, string serializedData, CancellationToken cancellationToken)
    {
        Result = result;
        return ValueTask.CompletedTask;
    }
}
#endregion

#region WebPlatform_FedCmBackend
/// <summary>The dialogs of FedCM: the account chooser chooses the first account, and the other dialogs are recorded.</summary>
internal sealed class FirstAccountChooser : UserAgentUIBackend
{
    public List<IdentityCredentialPrompt> Prompts { get; } = [];

    public override ValueTask<IdentityAccountChoice?> ChooseIdentityAccountAsync(IdentityAccountChooserPrompt prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return ValueTask.FromResult<IdentityAccountChoice?>(IdentityAccountChoice.Select(prompt.Accounts[0]));
    }

    public override ValueTask ShowIdentityCredentialErrorAsync(IdentityCredentialErrorPrompt prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> ShowIdentityAutoReauthenticationAsync(IdentityAutoReauthenticationPrompt prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return ValueTask.FromResult(true);
    }
}
#endregion

#region WebPlatform_UserAgentUIBackend_SecurePaymentConfirmation
/// <summary>
/// A transaction dialog of Secure Payment Confirmation whose user always makes the same choice: the user agent supports the
/// secure-payment-confirmation payment method.
/// </summary>
internal sealed class TransactionDialog(SecurePaymentConfirmationChoice choice) : UserAgentUIBackend
{
    public SecurePaymentConfirmationPrompt? LastPrompt { get; private set; }

    public override ValueTask<bool> HasPaymentHandlerAsync(PaymentHandlerQuery query, CancellationToken cancellationToken)
        => ValueTask.FromResult(query.Method.Identifier == "secure-payment-confirmation");

    public override ValueTask<SecurePaymentConfirmationChoice> ShowSecurePaymentConfirmationAsync(SecurePaymentConfirmationPrompt prompt, CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        return ValueTask.FromResult(choice);
    }
}
#endregion
