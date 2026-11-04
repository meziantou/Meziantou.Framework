---
title: Permissions and devices
description: Answer the permission prompts, and decide what the clipboard, sharing, vibration, geolocation, notifications, badging, battery, wake lock and keyboard lock APIs do in your pages.
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
