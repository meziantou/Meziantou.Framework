---
title: Permissions and devices
description: Answer the permission prompts, and decide what the clipboard, sharing, vibration, geolocation, notifications, badging, battery, wake lock, keyboard lock, screen orientation, virtual keyboard, viewport segments, network information, device memory and Global Privacy Control APIs do in your pages.
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
| [ShowNotification](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ShowNotification), [CloseNotification](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.CloseNotification) | `new Notification()` | The notifications are displayed nowhere |
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
`GetPositionAsync` is canceled when the timeout elapses.

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
are not same origin with the page cannot show notifications, as in the browsers. The persistent notifications of the
service workers (`showNotification()`) are not supported.

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
