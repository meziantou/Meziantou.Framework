---
title: Permissions and devices
description: Answer the permission prompts, and decide what the clipboard, sharing, vibration, geolocation, camera and microphone, encrypted media, notifications, badging, battery, wake lock, keyboard lock, keyboard layout, media session, screen orientation, virtual keyboard, viewport segments, compute pressure, idle detection, device posture, Web Authentication, network information, device memory, Global Privacy Control, sensor and protocol handler APIs do in your pages.
---

Some web APIs depend on the device or on the user: a browser asks the user for a permission, shows a share sheet, reads
the GPS, or displays a notification. A browsing context has no user nor device, so these APIs call the callbacks of
[BrowsingContextOptions](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions). Each callback has a default
that works without a host, so the pages run as in a browser whose user declines everything. Set a callback to simulate
the device in a test, or to connect the API to your application.

| Option | API | Without the option |
| --- | --- | --- |
| [Permissions](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Permissions), [RequestPermission](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.RequestPermission) | `navigator.permissions.query()` and the permission prompts | Every permission is `prompt`, and the prompts are denied |
| [Clipboard](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Clipboard), [ClipboardPermission](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ClipboardPermission) | `navigator.clipboard`, copy, cut and paste | An in-memory clipboard, and every page may use it |
| [Share](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Share) | `navigator.share()` | There is no share target: the promise is rejected with an `AbortError` |
| [Vibrate](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Vibrate) | `navigator.vibrate()` | Nothing vibrates |
| [Geolocation](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Geolocation) | `navigator.geolocation` | The position is unavailable |
| [MediaCapture](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaCapture) | `navigator.mediaDevices.getUserMedia()`, `enumerateDevices()`, `getDisplayMedia()` | There is no camera, microphone nor screen: `getUserMedia()` fails with a `NotFoundError` |
| [MediaKeysStorage](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaKeysStorage) | The `persistent-license` sessions of `navigator.requestMediaKeySystemAccess()` (Clear Key) | The sessions are stored in memory, for the browsing contexts of the group |
| [ShowNotification](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ShowNotification), [CloseNotification](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.CloseNotification) | `new Notification()`, `registration.showNotification()` | The notifications are displayed nowhere |
| [Badging](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Badging) | `navigator.setAppBadge()`, `navigator.clearAppBadge()` | The badge is shown nowhere |
| [BatteryStatus](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.BatteryStatus) | `navigator.getBattery()` | A fully charged battery that is plugged in |
| [WakeLock](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.WakeLock) | `navigator.wakeLock.request()` | Nothing is kept on, but the page gets its locks once the permission is granted |
| [KeyboardLock](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.KeyboardLock) | `navigator.keyboard.lock()`, `navigator.keyboard.unlock()` | No key is captured |
| [NetworkInformation](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.NetworkInformation) | `navigator.connection` | An unknown connection as fast as a 4G network |
| [ClientHints](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ClientHints) | `navigator.deviceMemory`, `navigator.connection.saveData` and the `Save-Data` header | 8 GiB of memory, and no data saving |
| [GlobalPrivacyControl](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.GlobalPrivacyControl) | `navigator.globalPrivacyControl` and the `Sec-GPC` header | The user expresses no preference |
| [ScreenOrientation](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ScreenOrientation) | `screen.orientation` | A desktop screen, which cannot be locked |
| [VirtualKeyboard](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.VirtualKeyboard) | `navigator.virtualKeyboard`, the `keyboard-inset-*` environment variables | There is no on-screen keyboard |
| [ViewportSegments](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ViewportSegments) | `window.viewport.segments`, the `*-viewport-segments` media features | The viewport has a single segment |
| [ComputePressure](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ComputePressure) | `PressureObserver` | The `cpu` source is supported, but reports no state |
| [IdleDetection](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.IdleDetection) | `IdleDetector` | The user is active and the screen is unlocked |
| [DevicePosture](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.DevicePosture) | `navigator.devicePosture`, the `device-posture` media feature | The posture is `continuous` |
| [WebAuthentication](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.WebAuthentication) | `navigator.credentials.create()`, `navigator.credentials.get()` and `PublicKeyCredential` (passkeys and security keys) | There is no authenticator: the ceremonies fail with a `NotAllowedError` |
| [KeyboardMap](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.KeyboardMap) | `navigator.keyboard.getLayoutMap()` | A US QWERTY keyboard |
| [MediaSession](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaSession) | `navigator.mediaSession` | The media session is shown nowhere |
| [Sensors](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Sensors) | `Accelerometer`, `Gyroscope`, `AmbientLightSensor` and the other sensors, the `deviceorientation` and `devicemotion` events | The device has no sensor: the sensors fail to start, and the events report null values |
| [ProtocolHandlers](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ProtocolHandlers) | `navigator.registerProtocolHandler()`, `navigator.unregisterProtocolHandler()` | The handlers are not registered |

To remove an API completely, so that the pages see it as unsupported, disable it with
[Features](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Features): for instance
`new WebPlatformFeatures { GeolocationEnabled = false }` removes `navigator.geolocation`. See
[WebPlatformFeatures](cref:Meziantou.Framework.WebPlatform.Dom.WebPlatformFeatures) for the list of features.

## Permissions

A [PermissionStore](cref:Meziantou.Framework.WebPlatform.Dom.PermissionStore) holds the permissions the user gave to the
origins: `navigator.permissions.query()` reads it, and so do the APIs that need a permission (`geolocation`,
`notifications`, `screen-wake-lock`). Grant or deny a permission in advance with
[SetState](cref:Meziantou.Framework.WebPlatform.Dom.PermissionStore.SetState), for every origin or for one top-level
origin.

When a page asks for a permission whose state is `prompt`,
[RequestPermission](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.RequestPermission) plays the role of
the permission prompt: it gets the name of the permission and returns
[PermissionState.Granted](cref:Meziantou.Framework.WebPlatform.Dom.PermissionState.Granted) or
[PermissionState.Denied](cref:Meziantou.Framework.WebPlatform.Dom.PermissionState.Denied). The answer is stored in the
permission store, so the user is asked once per origin. Without the callback, the request is denied and nothing is
stored, as when the user dismisses the prompt.

The insecure contexts (`http:` pages) and the frames that their permissions policy does not allow get `denied`, whatever
the store says.

## Share

`navigator.share()` calls [Share](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Share) with the data the
page shares, once the library has checked it: the page needs a user activation (a click), and the URL is resolved
against the URL of the document. Return true when the data is shared, and false when the user cancels:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_Share;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The callback can be asynchronous: the promise of the page settles when the returned task completes.

## Vibration

`navigator.vibrate()` calls [Vibrate](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Vibrate) with the
pattern of the page: the durations alternate between the vibrations and the pauses. An empty pattern stops the
vibration, which also happens when the page becomes hidden. A page that the user has never activated cannot vibrate:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_Vibrate;format=md-fenced-code-block;dedent=true -->
```csharp
var patterns = new List<string>();
var options = new BrowsingContextOptions
{
    // The durations alternate between the vibrations and the pauses; an empty pattern cancels the vibration
    Vibrate = (callbackContext, pattern) => patterns.Add(string.Join(',', pattern.Select(duration => duration.TotalMilliseconds))),
};
```
<!-- endref -->

## Geolocation

`navigator.geolocation` reads the position of a
[GeolocationProvider](cref:Meziantou.Framework.WebPlatform.Dom.GeolocationProvider). Set its
[Position](cref:Meziantou.Framework.WebPlatform.Dom.GeolocationProvider.Position) to simulate a location, and grant the
`geolocation` permission:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_Geolocation;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

Changing `Position` updates the pages that watch the position (`watchPosition()`); set it to null to make the position
unavailable. To read the position from another source, such as the operating system, derive from `GeolocationProvider`,
override [GetPositionAsync](cref:Meziantou.Framework.WebPlatform.Dom.GeolocationProvider.GetPositionAsync), and call
[OnPositionChanged](cref:Meziantou.Framework.WebPlatform.Dom.GeolocationProvider.OnPositionChanged) when the device
moves. The library handles the `timeout` and `maximumAge` options of the pages: the cancellation token passed to
`GetPositionAsync` is canceled when the timeout elapses. Once it elapsed, the page gets a `TIMEOUT` error, even when
the provider fails because of the canceled token, or before it started. Other failures of the provider are reported as
`POSITION_UNAVAILABLE`.

## Camera, microphone and screen capture

`navigator.mediaDevices` (Media Capture and Streams) captures the devices of a
[MediaCaptureBackend](cref:Meziantou.Framework.WebPlatform.Media.MediaCaptureBackend). The backend lists the devices and
produces their media; the library implements the rest: the `camera` and `microphone` permissions and permissions
policies, the identifiers of the devices (different for each origin), the constraints (`getUserMedia({ video: { width:
320 } })`, `applyConstraints()`), the tracks and the streams. Plug a backend and answer the permission prompt:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_MediaCapture;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new BrowsingContextOptions
{
    MediaCapture = new SolidColorCamera(),

    // Plays the role of the permission prompt: the user allows the camera
    RequestPermission = (callbackContext, name) => name == "camera" ? PermissionState.Granted : PermissionState.Denied,
};
```
<!-- endref -->

A backend describes each device with a
[MediaCaptureDevice](cref:Meziantou.Framework.WebPlatform.Media.MediaCaptureDevice): its kind, its label, its
capabilities (the values of its constrainable properties, such as `facingMode` or `echoCancellation`), its default
settings, and for a camera or a screen, its native video modes. The library selects the settings that satisfy the
constraints of the page best (the SelectSettings algorithm), offering the smaller sizes and frame rates that can be
cropped and scaled down from a native mode, then calls
[StartCapture](cref:Meziantou.Framework.WebPlatform.Media.MediaCaptureBackend.StartCapture). The
[MediaCaptureSource](cref:Meziantou.Framework.WebPlatform.Media.MediaCaptureSource) it returns produces the frames
([GetVideoFrame](cref:Meziantou.Framework.WebPlatform.Media.MediaCaptureSource.GetVideoFrame)) and the audio samples
([ReadAudio](cref:Meziantou.Framework.WebPlatform.Media.MediaCaptureSource.ReadAudio)) when a sink needs them: a `video`
element whose `srcObject` is the stream, `ImageCapture.grabFrame()`, a Web Audio graph. A frame of another size is
scaled to the size of the settings.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_MediaCaptureBackend;format=md-fenced-code-block;dedent=true -->
```csharp
// A camera whose frames are filled with a color, for the tests of a page that uses getUserMedia()
private sealed class SolidColorCamera : MediaCaptureBackend
{
    private static readonly MediaCaptureDevice Camera = new()
    {
        Id = "camera-1",
        Kind = MediaDeviceKind.VideoInput,
        Label = "Test camera",
        Capabilities = new MediaTrackCapabilities { FacingMode = ["user"] },
        DefaultSettings = new MediaTrackSettings { Width = 640, Height = 480, FacingMode = "user" },

        // The native modes; smaller sizes and frame rates are cropped and scaled down from them
        VideoModes = [new MediaCaptureVideoMode(640, 480, 30), new MediaCaptureVideoMode(1280, 720, 30)],
    };

    public override IReadOnlyList<MediaCaptureDevice> GetDevices() => [Camera];

    public override MediaCaptureSource StartCapture(MediaCaptureRequest request) => new Source(request);

    private sealed class Source(MediaCaptureRequest request) : MediaCaptureSource(request)
    {
        public override MediaVideoFrame? GetVideoFrame(TimeSpan timestamp)
        {
            var width = (int)Settings.Width!.Value;
            var height = (int)Settings.Height!.Value;
            var pixels = new byte[width * height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i + 1] = 128; // green
                pixels[i + 3] = 255;
            }

            return new MediaVideoFrame(width, height, pixels);
        }
    }
}
```
<!-- endref -->

- The first device of each kind is the default device. Until a page captured a device of a kind, `enumerateDevices()`
  lists at most one device of the kind, without its identifier and its label.
- Override [SelectDisplaySurface](cref:Meziantou.Framework.WebPlatform.Media.MediaCaptureBackend.SelectDisplaySurface)
  to support `getDisplayMedia()`: it plays the role of the picker of the browser, and returns the device of the screen,
  the window or the tab to capture (and its audio), or null when the user cancels. The page needs a user activation.
- Call [NotifyDevicesChanged](cref:Meziantou.Framework.WebPlatform.Media.MediaCaptureBackend.NotifyDevicesChanged) when
  a device is plugged in or removed: the pages that can see the change get a `devicechange` event. A source calls
  `SetMuted` while it cannot produce media, and `End` when its device is removed: its tracks fire `mute`, `unmute` and
  `ended`. A source that is already muted or ended when `StartCapture` returns gives muted or ended tracks.
- The clones of a track share its device, and each one gets the media at its own settings. When the tracks have
  different sizes or frame rates, the device runs in the smallest native mode that covers all of them (the source gets
  the new settings in `OnSettingsChanged`). `applyConstraints()` on a track only selects settings that a native mode
  can give together with the other live tracks of the device: constraints that only another mode satisfies reject the
  call with an `OverconstrainedError`, and neither the track nor the device change.
- The capture stops when the last track of a device stops, or when the document is unloaded: the source is disposed.

The streams also come from the page itself: `canvas.captureStream()` captures a canvas when it changes, at most
`frameRate` times per second when the page gives a frame rate (only on `requestFrame()` for a rate of 0). Once the
canvas is not origin-clean (a cross-origin image was drawn on it), its tracks are muted and get no frame, until
`reset()` or a new size clears the bitmap: the tracks are unmuted and capture the canvas again. `video.captureStream()`
captures the frames of a media element, and `AudioContext.createMediaStreamDestination()` captures a Web Audio graph,
with the channel count of the node. `createMediaStreamSource()` and `createMediaStreamTrackSource()` play the audio of a
track in a Web Audio graph. The media elements play a stream assigned to `srcObject` as a live timeline that cannot be
seeked, once a [MediaBackend](cref:Meziantou.Framework.WebPlatform.Media.MediaBackend) enables their processing model.
`MediaRecorder` records the streams with the codecs of a codec backend (see
[Audio and video codecs](10-AudioAndVideoCodecs.md#record-a-stream)). The audio of the media elements is not decoded (the
media backend gives no audio), so `createMediaElementSource()` outputs silence.

### Processing the frames of a track

The pages process the media of the tracks with the insertable streams (MediaStreamTrack Insertable Media Processing
using Streams). They need no callback of the host:

- `new MediaStreamTrackProcessor({ track })` reads a track as a `ReadableStream` of `VideoFrame` objects (or of
  `AudioData` objects for an audio track). The frames that arrive while no read is pending are kept in a buffer of
  `maxBufferSize` frames (1 by default, 64 at most): a new frame closes and discards the oldest one, which
  `discardedFrames` counts. The stream is closed when the track ends.
- `new VideoTrackGenerator()` creates a video track from the frames written to its `writable` stream: each frame is
  delivered to the sinks of the track and of its clones, then closed. A frame written while `muted` is true is
  discarded. Stopping the track and all its clones closes the stream, and closing or aborting the stream ends the
  tracks.
- The tracks of a camera, a canvas or a media element are read at the frame rate of the track (30 frames per second
  when it has none, 60 at most) on the clock of the event loop, and the audio by blocks of 10 ms. The frames of a
  `VideoTrackGenerator` are read as they are written.
- A track can be transferred to a dedicated worker, or from a dedicated worker to its window:
  `worker.postMessage(track, [track])` ends the track, and the worker gets a track of the same source. The source keeps
  running on the thread where it was created, and the received track gets its frames (the last frame replaces a frame
  that the receiver did not handle yet) and its state; the source stops when the received track stops or when the
  worker is terminated. A `MediaStreamTrackHandle` transfers a reference to a track that stays live. The tracks and the
  handles cannot leave their agent cluster: a cross-site frame, a shared worker or a service worker gets a
  `messageerror` event.
- `MediaStreamTrackProcessor` and `VideoTrackGenerator` are exposed in the windows too, and the legacy API of Chromium
  is available: `new MediaStreamTrackGenerator({ kind })` is a track (audio or video) with a `writable` stream, and
  `new MediaStreamTrackProcessor(track)` takes the track itself.

`video.requestVideoFrameCallback()` runs a callback when the element presents a new frame: in the next rendering update,
before the animation frame callbacks and with the same time. A frame is new when the element shows a frame of another
media resource, or the frame at another position of the media timeline; the metadata gives the size of the frame, its
position (`mediaTime`) and the number of frames presented by the element. The frames are the ones the
[MediaBackend](cref:Meziantou.Framework.WebPlatform.Media.MediaBackend) gives, or the frames of the `srcObject` stream.

## Notifications

Once the `notifications` permission is granted,
[ShowNotification](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ShowNotification) displays the
notifications the page creates. Keep the [Notification](cref:Meziantou.Framework.WebPlatform.Dom.Notification) to
report what the user does: [Click](cref:Meziantou.Framework.WebPlatform.Dom.Notification.Click) when the user activates
it, and [Dismiss](cref:Meziantou.Framework.WebPlatform.Dom.Notification.Dismiss) when the user closes it.
[CloseNotification](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.CloseNotification) removes a
notification that the page closed, or that a new notification with the same tag replaced:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_Notifications;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

Without `ShowNotification`, the notifications are displayed nowhere, but the page sees them as shown. The frames that
are not same origin with the page cannot show notifications, as in the browsers.

### Persistent notifications

A service worker registration shows persistent notifications with `registration.showNotification()`, from a page or from
the service worker, and lists them with `registration.getNotifications()`. They belong to the registration, not to the
page: they stay when the page is reloaded or closed, and they are closed when the registration is unregistered.
`ShowNotification` and `CloseNotification` display and remove them as the other notifications;
[IsPersistent](cref:Meziantou.Framework.WebPlatform.Dom.Notification.IsPersistent) tells them apart. Only a persistent
notification has [Actions](cref:Meziantou.Framework.WebPlatform.Dom.Notification.Actions), the buttons of the
notification (at most two, `Notification.maxActions`).

What the user does goes to the active service worker of the registration, which is started if needed:
[Click()](cref:Meziantou.Framework.WebPlatform.Dom.Notification.Click) fires the `notificationclick` event,
[Click(action)](cref:Meziantou.Framework.WebPlatform.Dom.Notification.Click(System.String)) fires it with the name of the
action button, and [Dismiss](cref:Meziantou.Framework.WebPlatform.Dom.Notification.Dismiss) fires the
`notificationclose` event:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/ServiceWorkerTests.cs;region=WebPlatform_PersistentNotifications;format=md-fenced-code-block;dedent=true -->
```csharp
var permissions = new PermissionStore();
permissions.SetState("notifications", PermissionState.Granted);
var shown = new List<Notification>();
var options = new BrowsingContextOptions
{
    Permissions = permissions,

    // A persistent notification (registration.showNotification()) has actions: clicking the notification or one
    // of its actions fires the notificationclick event in the service worker, dismissing it the notificationclose event
    ShowNotification = (callbackContext, notification) =>
    {
        lock (shown)
        {
            shown.Add(notification);
        }
    },
};
```
<!-- endref -->

The callbacks of a persistent notification run on the thread of the page or of the service worker that shows or closes
it: synchronize the list of the notifications that you display. As in the browsers, closing a notification from a script
(`notification.close()`) fires no `notificationclose` event, and a service worker registered by a third-party frame
cannot show notifications. The library does not fetch the icon, the image and the badge of the notifications: fetch
them if you display them.

## Badging

`navigator.setAppBadge()` and `navigator.clearAppBadge()` call
[SetBadge](cref:Meziantou.Framework.WebPlatform.Dom.BadgingOptions.SetBadge) with the badge to show on the icon of the
application: a number, a flag (`setAppBadge()` without argument), or nothing (`clearAppBadge()`, or the number 0). The
pages and their workers can set it, but not the frames that are not same origin with the page:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_Badging;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

Set [RequiresNotificationsPermission](cref:Meziantou.Framework.WebPlatform.Dom.BadgingOptions.RequiresNotificationsPermission)
to require the `notifications` permission, as Safari does.

## Battery

`navigator.getBattery()` reads the state of a
[BatteryStatusProvider](cref:Meziantou.Framework.WebPlatform.Dom.BatteryStatusProvider). Set its
[Status](cref:Meziantou.Framework.WebPlatform.Dom.BatteryStatusProvider.Status) to simulate a battery:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_BatteryStatus;format=md-fenced-code-block;dedent=true -->
```csharp
var battery = new BatteryStatusProvider
{
    // The battery of the device: setting the status updates the BatteryManager objects, which fire their change events
    Status = new BatteryStatus { Charging = false, Level = 0.5, ChargingTime = null, DischargingTime = TimeSpan.FromHours(2) },
};

var options = new BrowsingContextOptions
{
    BatteryStatus = new BatteryStatusOptions { Provider = battery },
};
```
<!-- endref -->

Changing `Status` updates the `BatteryManager` of the pages, which fire their `chargingchange`, `chargingtimechange`,
`dischargingtimechange` and `levelchange` events. A hidden page gets the events when it becomes visible. The pages see
the level rounded to two decimals, as in the browsers. To read the battery of the operating system, derive from
`BatteryStatusProvider`, override [GetStatus](cref:Meziantou.Framework.WebPlatform.Dom.BatteryStatusProvider.GetStatus),
and call [OnStatusChanged](cref:Meziantou.Framework.WebPlatform.Dom.BatteryStatusProvider.OnStatusChanged) when it
changes. The frames that their permissions policy does not allow (`battery`) get a `NotAllowedError`.

## Screen wake lock

`navigator.wakeLock.request()` keeps the screen on while the page is visible. Once the `screen-wake-lock` permission is
granted, [Acquire](cref:Meziantou.Framework.WebPlatform.Dom.WakeLockOptions.Acquire) is called when the page gets its
first lock, and [Release](cref:Meziantou.Framework.WebPlatform.Dom.WakeLockOptions.Release) when its last lock is
released:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_WakeLock;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The locks are released, and their `release` event is fired, when the page releases them, becomes hidden, or is
unloaded. A hidden page cannot get a lock. Chromium grants the permission without asking the user: to do the same,
grant it in the permission store or answer `granted` in `RequestPermission`.

## Keyboard lock

`navigator.keyboard.lock()` asks to capture the keys that the system or the browser handle, such as `Alt+Tab` or
`Escape`, while the page is fullscreen. [Lock](cref:Meziantou.Framework.WebPlatform.Dom.KeyboardLockOptions.Lock) gets
the `code` values of the keys (an empty list captures all the keys), and
[Unlock](cref:Meziantou.Framework.WebPlatform.Dom.KeyboardLockOptions.Unlock) is called by `navigator.keyboard.unlock()`
and when the page is unloaded:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_KeyboardLock;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

Only the page of a tab can lock the keyboard, not its frames. As in Chromium, a page whose keyboard is locked is not
kept in the back/forward cache.

## Network information

`navigator.connection` describes the network of the device: its type (`wifi`, `cellular`...), its effective type
(`4g`, `3g`, `2g` or `slow-2g`), its bandwidth (`downlink`, in Mbit/s) and its round-trip time (`rtt`, in ms). Its
values come from a [NetworkInformationProvider](cref:Meziantou.Framework.WebPlatform.Dom.NetworkInformationProvider).
Set its [Status](cref:Meziantou.Framework.WebPlatform.Dom.NetworkInformationProvider.Status) to simulate a network:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_NetworkInformation;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

Setting the status fires the `change` event in the pages and the workers that see a different value. As in Chromium,
the pages see rounded values: the bandwidth is a multiple of 50 kbit/s and at most 10 Mbit/s, and the round-trip time
is a multiple of 50 ms and at most 3 seconds. To read the network of the machine, derive from the provider, override
[GetStatus](cref:Meziantou.Framework.WebPlatform.Dom.NetworkInformationProvider.GetStatus), and call
[OnStatusChanged](cref:Meziantou.Framework.WebPlatform.Dom.NetworkInformationProvider.OnStatusChanged) when the network
changes.

The provider also gives the values of the `RTT`, `Downlink` and `ECT` client hints that
[ClientHintsOptions](cref:Meziantou.Framework.WebPlatform.Dom.ClientHintsOptions) does not set. Without provider, the
connection has an unknown type, the performance of a 4G network (10 Mbit/s, 50 ms), and the network values of
`ClientHintsOptions` when you set them.

`navigator.connection.saveData` is true when the user wants to reduce the data usage:
[SaveData](cref:Meziantou.Framework.WebPlatform.Dom.ClientHintsOptions.SaveData) also sends the `Save-Data: on` header
with the requests to the secure origins.

## Device memory

`navigator.deviceMemory` is the memory of the device in GiB, in the secure pages and workers. It reports
[DeviceMemory](cref:Meziantou.Framework.WebPlatform.Dom.ClientHintsOptions.DeviceMemory), rounded down to a power of two
between 0.25 and 8 to make the users harder to fingerprint. The library does not read the memory of the machine: without
value, the pages see 8, but the `Sec-CH-Device-Memory` client hint is not sent.

## Global Privacy Control

`navigator.globalPrivacyControl` tells the pages that the user does not want their data sold or shared, and the
requests then have the `Sec-GPC: 1` header. Set
[Enabled](cref:Meziantou.Framework.WebPlatform.Dom.GlobalPrivacyControlOptions.Enabled) for a fixed preference, or
[IsEnabled](cref:Meziantou.Framework.WebPlatform.Dom.GlobalPrivacyControlOptions.IsEnabled) for a setting the user can
change, which is read at each request:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_GlobalPrivacyControl;format=md-fenced-code-block;dedent=true -->
```csharp
var enabled = true;
var options = new BrowsingContextOptions
{
    // The preference of the user, read at each request (the Sec-GPC header) and each read of navigator.globalPrivacyControl
    GlobalPrivacyControl = new GlobalPrivacyControlOptions { IsEnabled = _ => enabled },
};
```
<!-- endref -->

## Screen orientation

`screen.orientation` reads the screen of a
[ScreenOrientationProvider](cref:Meziantou.Framework.WebPlatform.Dom.ScreenOrientationProvider). By default, it
simulates a screen that rotates to the orientation of each lock:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_ScreenOrientation;format=md-fenced-code-block;dedent=true -->
```csharp
// The screen of a phone, held in portrait: lock() rotates it to the orientation of the lock
var screen = new ScreenOrientationProvider(OrientationType.PortraitPrimary);
var options = new BrowsingContextOptions
{
    ScreenOrientation = new ScreenOrientationOptions { Provider = screen },
};
```
<!-- endref -->

As in the browsers, a page can lock the orientation only while it is fullscreen (`NotAllowedError` otherwise), and not
from a frame sandboxed without `allow-orientation-lock` or from a hidden page (`SecurityError`). The lock completes after
the screen has rotated, after the `change` event of the documents, from the page down to its frames. A lock request
aborts the pending request of the other documents of the tab (`AbortError`), and the screen is unlocked when the page
exits fullscreen or is unloaded. Set
[DeviceOrientation](cref:Meziantou.Framework.WebPlatform.Dom.ScreenOrientationProvider.DeviceOrientation) to simulate
the user rotating the device. To lock the screen of the operating system, derive from `ScreenOrientationProvider`,
override [TryLock](cref:Meziantou.Framework.WebPlatform.Dom.ScreenOrientationProvider.TryLock), and call
[OnOrientationChanged](cref:Meziantou.Framework.WebPlatform.Dom.ScreenOrientationProvider.OnOrientationChanged) when
the orientation changes. The size of the viewport does not change with the orientation.
[SetOrientationOverride](cref:Meziantou.Framework.WebPlatform.Dom.ScreenOrientationProvider.SetOrientationOverride) emulates
a screen with another natural orientation and orientation type (as the `emulation.setScreenOrientationOverride` command of
WebDriver BiDi), whatever the orientation of the device and the lock, until
[ClearOrientationOverride](cref:Meziantou.Framework.WebPlatform.Dom.ScreenOrientationProvider.ClearOrientationOverride):
the angle of the screen and the `referenceFrame: 'screen'` readings of the motion sensors follow it.

## Virtual keyboard

`navigator.virtualKeyboard.show()` and `hide()` call
[Show](cref:Meziantou.Framework.WebPlatform.Dom.VirtualKeyboardOptions.Show) and
[Hide](cref:Meziantou.Framework.WebPlatform.Dom.VirtualKeyboardOptions.Hide), when the page has a user activation and
its focused element has `virtualkeyboardpolicy="manual"`. The host reports the rectangle of its keyboard with the
[BoundingRect](cref:Meziantou.Framework.WebPlatform.Dom.VirtualKeyboardProvider.BoundingRect) of a
[VirtualKeyboardProvider](cref:Meziantou.Framework.WebPlatform.Dom.VirtualKeyboardProvider):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_VirtualKeyboard;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

Setting `BoundingRect` updates `navigator.virtualKeyboard.boundingRect` of the page, which fires its `geometrychange`
event. While the page sets `overlaysContent` to true, the `keyboard-inset-*` environment variables give the rectangle of
the keyboard. Only the page of a tab gets the geometry of the keyboard, not its frames. The library does not resize the
viewport when the keyboard is shown.

## Viewport segments

The display features of a foldable device, such as its fold or the hinge between two screens, split the viewport in
segments. Set the [DisplayFeatures](cref:Meziantou.Framework.WebPlatform.Dom.DisplayFeaturesProvider.DisplayFeatures) of
a [DisplayFeaturesProvider](cref:Meziantou.Framework.WebPlatform.Dom.DisplayFeaturesProvider) to simulate them:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_ViewportSegments;format=md-fenced-code-block;dedent=true -->
```csharp
// A foldable device: a vertical hinge of 20 pixels splits the viewport in two segments, side by side
var displayFeatures = new DisplayFeaturesProvider();
var options = new BrowsingContextOptions
{
    ViewportSegments = new ViewportSegmentsOptions { Provider = displayFeatures },
};
displayFeatures.DisplayFeatures = [new DisplayFeature(DisplayFeatureOrientation.Vertical, Offset: 390, MaskLength: 20)];
```
<!-- endref -->

The page then sees two segments in `window.viewport.segments`, the `horizontal-viewport-segments` and
`vertical-viewport-segments` media features match the number of columns and rows of segments, and the
`viewport-segment-*` environment variables give their edges (`env(viewport-segment-width 1 0)` is the width of the
second column). As in Chromium, the frames have a single segment, and their `viewport.segments` is null.

## Compute pressure

A `PressureObserver` reads the samples of a
[PressureSourceProvider](cref:Meziantou.Framework.WebPlatform.Dom.PressureSourceProvider). Call
[SetSample](cref:Meziantou.Framework.WebPlatform.Dom.PressureSourceProvider.SetSample) to report the pressure state of a
source:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_ComputePressure;format=md-fenced-code-block;dedent=true -->
```csharp
var pressure = new PressureSourceProvider();
pressure.SetSample(PressureSource.Cpu, new PressureSample(PressureState.Nominal));

var options = new BrowsingContextOptions
{
    ComputePressure = new ComputePressureOptions { Provider = pressure },
};
```
<!-- endref -->

An observer is called when the state changes, or at its `sampleInterval` when it sets one, with the latest sample. It
only gets the samples while the tab has the focus and the focused frame is same origin with its page (or the page that
owns its worker). [SetSupported](cref:Meziantou.Framework.WebPlatform.Dom.PressureSourceProvider.SetSupported) makes a
source unsupported: `observe()` is then rejected with a `NotSupportedError`. To read the pressure of the operating system,
derive from `PressureSourceProvider`, override
[GetSample](cref:Meziantou.Framework.WebPlatform.Dom.PressureSourceProvider.GetSample), and call
[OnSampleChanged](cref:Meziantou.Framework.WebPlatform.Dom.PressureSourceProvider.OnSampleChanged) when it changes. The
frames that their permissions policy does not allow (`compute-pressure`) get a `NotAllowedError`. The rate obfuscation of
the specification is not applied.

## Idle detection

An `IdleDetector` reads the state of an [IdleStateProvider](cref:Meziantou.Framework.WebPlatform.Dom.IdleStateProvider).
Its [Status](cref:Meziantou.Framework.WebPlatform.Dom.IdleStateProvider.Status) gives the time of the last interaction of
the user, measured by the clock of the browsing context, and whether the screen is locked:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_IdleDetection;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The user becomes idle for a detector once its `threshold` (one minute at least) elapsed since the last interaction.
Changing `Status` updates the started detectors, which fire their `change` event. `start()` is rejected with a
`NotAllowedError` when the `idle-detection` permission is denied, or when the permissions policy does not allow
`idle-detection`. `IdleDetector.requestPermission()` needs a click of the user, and asks with `RequestPermission`.

## Device posture

`navigator.devicePosture` and the `device-posture` media feature read the posture of a
[DevicePostureProvider](cref:Meziantou.Framework.WebPlatform.Dom.DevicePostureProvider):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_DevicePosture;format=md-fenced-code-block;dedent=true -->
```csharp
var posture = new DevicePostureProvider();
var options = new BrowsingContextOptions
{
    DevicePosture = new DevicePostureOptions { Provider = posture },
};

// Fires the change event of navigator.devicePosture, and updates the device-posture media feature
posture.Posture = DevicePostureType.Folded;
```
<!-- endref -->

Changing [Posture](cref:Meziantou.Framework.WebPlatform.Dom.DevicePostureProvider.Posture) fires the `change` event of
`navigator.devicePosture`, and the `change` event of the media query lists such as `matchMedia('(device-posture: folded)')`.
A hidden page gets the event when it becomes visible. All the frames of a tab have the posture of the tab.

## Web Authentication

`navigator.credentials.create()` and `navigator.credentials.get()` create and use the public key credentials of
[Web Authentication](https://w3c.github.io/webauthn/) (passkeys and security keys) with the authenticators of a
[VirtualAuthenticatorEnvironment](cref:Meziantou.Framework.WebPlatform.Dom.VirtualAuthenticatorEnvironment): software
authenticators, which create real key pairs and sign real assertions, as the virtual authenticators of WebDriver. Add an
authenticator with the capabilities of the device you want to simulate:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_WebAuthentication;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The options of [VirtualAuthenticatorOptions](cref:Meziantou.Framework.WebPlatform.Dom.VirtualAuthenticatorOptions)
are the authenticator configuration of WebDriver:

- `Transport` is a value of `AuthenticatorTransport`: an `internal` authenticator is a platform authenticator (its
  credentials have the `platform` attachment, and `PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable()`
  is true when it can verify the user), the others are roaming authenticators.
- `HasResidentKey` allows the discoverable credentials (passkeys), and `HasUserVerification` the user verification, whose
  result is `IsUserVerified`. `IsUserConsenting` is the answer of the user to the tests of user presence: when it is
  false, the ceremonies fail.
- A `Ctap1U2f` authenticator has neither discoverable credentials, user verification nor extensions, and only creates
  ES256 keys. The others create ES256, EdDSA (Ed25519) and RS256 keys, in the order of preference of the page.
- `Extensions` lists the authenticator extensions: `prf` (the pseudo-random function of the `prf` extension) and
  `largeBlob` (the blobs of the `largeBlob` extension). The client also implements the `credProps` extension, and the
  `payment` extension of [Secure Payment Confirmation](09-UserAgentUI.md#secure-payment-confirmation) (a credential for the
  payments, whose authentications only the `secure-payment-confirmation` payment method can request).

The credentials of an authenticator can be listed with
[GetCredentials](cref:Meziantou.Framework.WebPlatform.Dom.VirtualAuthenticator.GetCredentials), added with
[AddCredential](cref:Meziantou.Framework.WebPlatform.Dom.VirtualAuthenticator.AddCredential) (for instance a passkey
whose private key is a PKCS #8 package), and removed with
[RemoveCredential](cref:Meziantou.Framework.WebPlatform.Dom.VirtualAuthenticator.RemoveCredential). The environment can be
shared by several browsing contexts, and used from any thread.

The client checks the origin and the RP ID, and the permissions policies: `publickey-credentials-create` and
`publickey-credentials-get` (allowed in the same-origin frames by default; `payment` also allows to create a credential). A
cross-origin frame must have a transient activation to create a credential. A ceremony is performed by a task of the event loop: the first authenticator that can
complete it is used, without user interface. When no authenticator can (there is none, none satisfies the options, or
none has an eligible credential), the ceremony fails with a `NotAllowedError`, as when a user cancels the dialog of a
browser, without waiting for the end of the timeout. A request with the `conditional` mediation waits for the user to
select a passkey in an autofill field, which never happens without a user: it is pending until its signal is aborted.
The attestation is the `none` attestation, or a packed self attestation when the page asks for an attestation. The
`appid`, `appidExclude` and `remoteClientDataJSON` extensions and the related origins are not supported.

## Keyboard layout

`navigator.keyboard.getLayoutMap()` tells the page which character each key of the keyboard produces, for instance to
display the shortcuts of a game. It reads the layout of a
[KeyboardLayoutProvider](cref:Meziantou.Framework.WebPlatform.Dom.KeyboardLayoutProvider): the key value of each `code`
value. Setting its [Layout](cref:Meziantou.Framework.WebPlatform.Dom.KeyboardLayoutProvider.Layout) fires the
`layoutchange` event of `navigator.keyboard`:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_KeyboardMap;format=md-fenced-code-block;dedent=true -->
```csharp
var keyboard = new KeyboardLayoutProvider
{
    // The key produced by each key of the keyboard, by code: setting the layout fires the layoutchange event
    Layout = new Dictionary<string, string>(KeyboardLayoutProvider.UsQwerty) { ["KeyQ"] = "a", ["KeyA"] = "q", ["Digit1"] = "&" },
};

var options = new BrowsingContextOptions
{
    KeyboardMap = new KeyboardMapOptions { Provider = keyboard },
};
```
<!-- endref -->

Without provider, the keyboard is a US QWERTY keyboard
([UsQwerty](cref:Meziantou.Framework.WebPlatform.Dom.KeyboardLayoutProvider.UsQwerty)). Only the writing system keys (the
letters, the digits and the punctuation) are given to the pages. The frames that their permissions policy does not allow
(`keyboard-map`) get a `SecurityError`.

## Encrypted media

Encrypted Media Extensions (`navigator.requestMediaKeySystemAccess()`, `MediaKeys`, `MediaKeySession`,
`video.setMediaKeys()`) implement the Clear Key key system (`org.w3.clearkey`): the license request lists the key IDs of the
Initialization Data (`keyids`, `cenc` and `webm`), and the license is a JSON Web Key Set that the page gives to
`update()`. The commercial key systems (Widevine, PlayReady, FairPlay) are not supported. A configuration is supported
when the [MediaBackend](cref:Meziantou.Framework.WebPlatform.Media.MediaBackend) of the browsing context plays its MP4 or
WebM types; Clear Key supports the `cenc`, `cbcs` and `cbcs-1-9` encryption schemes, the empty robustness, no
distinctive identifier, and the `temporary` and `persistent-license` sessions.

The media data comes from the Media Source Extensions: the segment parser of the backend reports the Initialization Data
of the initialization segments (each one fires an `encrypted` event at the media element) and how each coded frame is
encrypted. The library decrypts the frames with the usable keys of the sessions of the `MediaKeys` of the element
(AES-CTR for `cenc`, AES-CBC with its pattern for `cbcs`); the playback stops before a frame whose key no session has, fires
`waitingforkey`, and resumes when `update()` provides the key. A frame whose subsamples do not match its data is a decode
error. For instance, the parser of an MP4 byte stream reads the `tenc`, `senc` (or `saiz` and `saio`) and `seig` sample
group boxes of the fragments:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/MediaSourceTests.cs;region=WebPlatform_EncryptedMediaFrames;format=md-fenced-code-block;dedent=true -->
```csharp
// A frame of a media segment encrypted with the Common Encryption (cenc): AES-CTR on the bytes after the clear header of
// each subsample. The library decrypts it with the keys of the sessions of the MediaKeys of the media element, and the
// playback waits until a session has the key.
byte[] keyId = [0xad, 0x13, 0xf9, 0xea, 0x2b, 0xe6, 0x98, 0xb8, 0x75, 0xf5, 0x04, 0xa8, 0xe3, 0xcc, 0xea, 0x64];
byte[] initializationVector = [0, 0, 0, 0, 0, 0, 0, 1];
byte[] data = new byte[100];
var frame = new MediaCodedFrame(TrackId: 1, PresentationTimestamp: 0, DecodeTimestamp: 0, Duration: 0.04, IsRandomAccessPoint: true)
{
    Encryption = new MediaSampleEncryption(MediaEncryptionScheme.Cenc, keyId, initializationVector, [new MediaSubsample(ClearBytes: 5, ProtectedBytes: 95)]),
    Data = data,
};
```
<!-- endref -->

The `persistent-license` sessions need a configuration whose `persistentState` is `required`. `update()` stores their keys
in [MediaKeysStorage](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaKeysStorage), `load()` reads them
back in another document, and `remove()` replaces them with a record of license destruction, sent as a `license-release`
message until the page gives its acknowledgement to `update()`. The sessions are partitioned by storage key: a document
only loads the sessions of its origin, in the same top-level site. A stored session is loaded by one document at a time,
until the session is closed or its document is destroyed (a document that holds one is not kept in the back/forward
cache). The data contains the keys: a persistent
[MediaKeysStorageBackend](cref:Meziantou.Framework.WebPlatform.Dom.MediaKeysStorageBackend) should protect it.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/MediaSourceTests.cs;region=WebPlatform_EncryptedMediaStorage;format=md-fenced-code-block;dedent=true -->
```csharp
// The persistent-license sessions of Clear Key, shared by the browsing contexts (e.g. a database to keep them between runs)
var storage = new InMemoryMediaKeysStorageBackend();
var options = new BrowsingContextOptions
{
    MediaKeysStorage = storage,
};
```
<!-- endref -->

- `requestMediaKeySystemAccess()` needs a secure context and the `encrypted-media` permissions policy feature (allowed to
  the same-origin frames by default); a document whose origin is opaque gets a `NotSupportedError`. A prerendering page
  waits for its activation.
- The Initialization Data and the responses of `update()` are limited to 64 KiB, a key ID to 512 bytes, and the keys to
  16 bytes (AES-128). A session has at most 1,024 keys: the keys that several `update()` calls add to a persistent session
  are stored in a record of up to 1 MiB, which `load()` reads back. The keys never leave the library: the page only sees the key IDs and their statuses.
- The media resources played by URL (without Media Source Extensions) are not decrypted: the backend that plays them has
  no access to the keys.

## Media session

A page that plays media describes it with `navigator.mediaSession`: the title, the artist, the album and the artwork of
the media, whether it plays, its position, and the actions that the media controls of the system can trigger (play,
pause, next track...). [SessionChanged](cref:Meziantou.Framework.WebPlatform.Dom.MediaSessionOptions.SessionChanged) gets
the [MediaSession](cref:Meziantou.Framework.WebPlatform.Dom.MediaSession) of the page each time it changes:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_MediaSession;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

Call [HandleAction](cref:Meziantou.Framework.WebPlatform.Dom.MediaSession.HandleAction) when the user presses a media
control: the handler that the page registered with `setActionHandler()` runs from a task, and the method returns false
when the page has no handler for the action. The URLs of the artwork are absolute; the library does not fetch the images.

## Media capabilities

A page asks `navigator.mediaCapabilities.decodingInfo()` and `encodingInfo()` (in a window or a worker) whether it can
play, receive, record or send media with a configuration: a content type, the size and frame rate of the video, the
channels of the audio. The library validates the configurations (the promise is rejected with a `TypeError` for a
content type that is not valid, a container without a single codec, a codec of the other kind, a member that does not
apply to the type...), and the backends of the browsing context answer:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_MediaCapabilities;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new BrowsingContextOptions
{
    // Answers navigator.mediaCapabilities.decodingInfo() for the "file" and "media-source" types
    MediaBackend = new WebMMediaBackend(),

    // Answers the "webrtc" types, and the "record" type (what MediaRecorder records)
    MediaCodecs = new Vp8OpusCodecs(),
};
```
<!-- endref -->

- `file` asks [CanPlayType](cref:Meziantou.Framework.WebPlatform.Media.MediaBackend.CanPlayType) of the
  [MediaBackend](cref:Meziantou.Framework.WebPlatform.Media.MediaBackend): a container with a codec is supported when the
  answer is [MediaTypeSupport.Probably](cref:Meziantou.Framework.WebPlatform.Media.MediaTypeSupport.Probably), a type of a
  single codec such as `audio/mpeg` when it is not
  [MediaTypeSupport.NotSupported](cref:Meziantou.Framework.WebPlatform.Media.MediaTypeSupport.NotSupported).
  `media-source` also needs a byte stream parser from
  [CreateSegmentParser](cref:Meziantou.Framework.WebPlatform.Media.MediaBackend.CreateSegmentParser).
- `webrtc` takes the RTP payload formats (`audio/opus`, `audio/PCMU`, `audio/PCMA`, `video/VP8`, `video/VP9`,
  `video/H264`, `video/AV1`, `video/H265`, with their format parameters such as `profile-id`), converts them to the
  configurations of the WebCodecs decoders and encoders, and asks the
  [MediaCodecBackend](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecBackend).
- `record` is what `MediaRecorder` records: VP8 and Opus in WebM or Matroska, when the encoders of the
  [MediaCodecBackend](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecBackend) support the size, the frame rate
  and the bitrate.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_MediaCapabilitiesBackend;format=md-fenced-code-block;dedent=true -->
```csharp
// Plays WebM files with VP8 and Opus, and MP3 files
private sealed class WebMMediaBackend : MediaBackend
{
    public override MediaTypeSupport CanPlayType(string mimeType, IReadOnlyList<string> codecs) => mimeType switch
    {
        "video/webm" or "audio/webm" when codecs.Count == 0 => MediaTypeSupport.Maybe,
        "video/webm" or "audio/webm" => codecs.All(codec => codec is "vp8" or "opus" || codec.StartsWith("vp09.", StringComparison.Ordinal)) ? MediaTypeSupport.Probably : MediaTypeSupport.NotSupported,
        "audio/mpeg" => MediaTypeSupport.Probably,
        _ => MediaTypeSupport.NotSupported,
    };

    public override MediaPlayer? CreatePlayer(MediaResource resource) => null;
}
```
<!-- endref -->

Without a backend, nothing is supported. The library has no performance model: a supported configuration is `smooth`,
and it is not `powerEfficient`, since the library cannot tell whether a backend uses hardware codecs. A `colorGamut` or
a `transferFunction` must match the color space of a VP9 or AV1 codec string (BT.709 when the string does not give it),
and a video with HDR metadata (`hdrMetadataType`) is not supported, as the rendering is in sRGB. A configuration with
a `keySystemConfiguration` is rejected with a `SecurityError` in a context that is not secure, and with an
`InvalidStateError` in a worker; otherwise it is checked by Encrypted Media Extensions (the Clear Key key system, with
the media types that the media backend plays): a supported configuration has a `keySystemAccess`, the
`MediaKeySystemAccess` that `requestMediaKeySystemAccess()` would give. .NET code calls
[DecodingInfo](cref:Meziantou.Framework.WebPlatform.Dom.MediaCapabilities.DecodingInfo) and
[EncodingInfo](cref:Meziantou.Framework.WebPlatform.Dom.MediaCapabilities.EncodingInfo) on
[Navigator.MediaCapabilities](cref:Meziantou.Framework.WebPlatform.Dom.Navigator.MediaCapabilities).

## Sensors

The sensor objects of the pages (`Accelerometer`, `LinearAccelerationSensor`, `GravitySensor`, `Gyroscope`,
`Magnetometer`, `UncalibratedMagnetometer`, `AmbientLightSensor`, `ProximitySensor`, `AbsoluteOrientationSensor` and
`RelativeOrientationSensor`) read the sensors of a
[DeviceSensorProvider](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensorProvider). Add a sensor with
[AddSensor](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensorProvider.AddSensor), then push its readings with
[Update](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensor.Update):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_Sensors;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The readings are created with the factory method of the shape of the sensor:
[FromXyz](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensorReading.FromXyz) for the motion sensors and the
magnetometer, [FromIlluminance](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensorReading.FromIlluminance),
[FromProximity](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensorReading.FromProximity),
[FromUncalibratedMagnetometer](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensorReading.FromUncalibratedMagnetometer)
and [FromEulerAngles](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensorReading.FromEulerAngles) for the orientation
sensors. A provider can be shared by several browsing contexts, and `Update` can be called from any thread.

`start()` asks for the permissions of the sensor (`accelerometer`, `gyroscope`, `magnetometer` or
`ambient-light-sensor`) and fires the `error` event with a `NotAllowedError` when one is denied, or with a
`NotReadableError` when the device has no working sensor of the type. Chromium grants these permissions without asking
the user: to do the same, grant them in the permission store. The constructors throw a `SecurityError` in the frames
that their permissions policy does not allow.

The pages get the readings as in a browser:

- The `reading` event is fired at most at the frequency the page asked for (5 Hz by default), clamped to the bounds of
  the sensor (1 Hz to 60 Hz by default). [RequestedSamplingFrequency](cref:Meziantou.Framework.WebPlatform.Dom.DeviceSensor.RequestedSamplingFrequency)
  is the highest frequency the started sensors ask for.
- The values are rounded to limit the fingerprinting: the accelerations to 0.1 m/s², the angular velocities to
  0.1 deg/s (as Chromium), and the illuminance to 50 lux, whose changes of less than 25 lux are discarded.
- A hidden page, or a frame that is not same origin with the focused frame, gets no reading: the latest one is
  delivered when it becomes visible or focused.

## Device orientation and motion

The `deviceorientation`, `deviceorientationabsolute` and `devicemotion` events read the same provider: the relative
orientation sensor (or the absolute one when the device has no relative orientation sensor), the absolute orientation
sensor, and the linear acceleration sensor, the accelerometer and the gyroscope:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_DeviceOrientation;format=md-fenced-code-block;dedent=true -->
```csharp
var sensors = new DeviceSensorProvider();

// The deviceorientation event reads the relative orientation sensor, as Euler angles in degrees
var orientation = sensors.AddSensor(DeviceSensorType.RelativeOrientation);
```
<!-- endref -->

As in Chromium, the first event is fired when the page adds a listener, then the orientation events are fired when the
orientation changes and the motion events every 16 ms while the sensors get new readings. The angles are rounded to 0.1
degree, the accelerations to 0.1 m/s² and the rotation rates to 0.1 deg/s. Without a working sensor, or without the
`accelerometer` and `gyroscope` permissions, the first event is fired with null values. A hidden page gets no event.
`DeviceOrientationEvent.requestPermission()` and `DeviceMotionEvent.requestPermission()` resolve with the state of the
permissions, and need a user activation when a permission would prompt the user.

## Protocol handlers

`navigator.registerProtocolHandler()` asks the user to register a page as the handler of a scheme (e.g. `mailto` or
`web+soup`). [RequestRegistration](cref:Meziantou.Framework.WebPlatform.Dom.ProtocolHandlerOptions.RequestRegistration)
answers for the user, and the accepted handlers are stored in
[Registry](cref:Meziantou.Framework.WebPlatform.Dom.ProtocolHandlerOptions.Registry), which the browsing contexts that
share the options share:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_ProtocolHandlers;format=md-fenced-code-block;dedent=true -->
```csharp
// The handlers registered by navigator.registerProtocolHandler() when the user accepts them
var protocolHandlers = new ProtocolHandlerOptions
{
    RequestRegistration = registration => registration.Scheme == "web+soup",
};
var options = new BrowsingContextOptions { ProtocolHandlers = protocolHandlers };
```
<!-- endref -->

A navigation to a URL of a registered scheme navigates to the URL of the last registered handler, in which the first
`%s` is replaced with the percent-encoded URL (`web+soup:chicken` navigates to `soup?url=web%2Bsoup%3Achicken`), and
`navigator.unregisterProtocolHandler()` removes the handler. As the `Set RPH Registration Mode` WebDriver command,
[ProtocolHandlerAutomationMode](cref:Meziantou.Framework.WebPlatform.Dom.Document.ProtocolHandlerAutomationMode) makes a
document accept or reject its registrations without asking. Without the option, the handlers are not registered, and the
URLs whose scheme is not a fetch scheme are handed off to external software: the navigation does nothing.
