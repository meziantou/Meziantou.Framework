---
title: Overview
description: Install Meziantou.AspNetCore.Testing, and choose between testing a Blazor component alone and testing the pages of a whole application.
---

`Meziantou.AspNetCore.Testing` runs your tests in a browser engine written in .NET
([Meziantou.Framework.WebPlatform](../../WebPlatform/01-Introduction.md)): the pages have a real DOM, the CSS applies,
the layout computes the boxes, the scripts run, and the input of the tests goes through the hit testing of the page, as
the input of a user does.

<InstallNuGetPackage Id="Meziantou.AspNetCore.Testing" />

The package tests two things:

| You test | Use | Page |
| --- | --- | --- |
| A Blazor component alone, with fake services | [ComponentTestHost](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHost) | [Blazor components](02-BlazorComponents.md) |
| The pages of an application (MVC, Razor Pages, Blazor, minimal APIs...), in memory or on a server | [WebAppBrowser](cref:Meziantou.AspNetCore.Testing.WebAppBrowser) | [Web applications](07-WebApplications.md), [Blazor applications](08-BlazorApplications.md) |

Both pages are a [TestPage](cref:Meziantou.AspNetCore.Testing.TestPage): they share the
[locators and the assertions](03-Locators.md) (find the elements by role, label or text as Playwright does, wait until
they are actionable, retry the assertions), the [user input](04-UserInput.md) and the [snapshots](05-Snapshots.md).
