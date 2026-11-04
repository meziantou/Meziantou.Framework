---
title: Permissions and devices
description: Answer the permission prompts, and decide what the clipboard, sharing, vibration, geolocation and notifications APIs do in your pages.
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

To remove an API completely, so that the pages see it as unsupported, disable it with
[Features](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Features): for instance
`new WebPlatformFeatures { GeolocationEnabled = false }` removes `navigator.geolocation`. See
[WebPlatformFeatures](cref:Meziantou.Framework.WebPlatform.Dom.WebPlatformFeatures) for the list of features.

## Permissions

A [PermissionStore](cref:Meziantou.Framework.WebPlatform.Dom.PermissionStore) holds the permissions the user gave to the
origins: `navigator.permissions.query()` reads it, and so do the APIs that need a permission (`geolocation`,
`notifications`). Grant or deny a permission in advance with
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
