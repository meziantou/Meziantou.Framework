---
title: Dialogs
description: Accept or dismiss the alert, confirm and prompt dialogs of a page, answer the confirmation of leaving a page, and check the dialogs it showed.
---

The scripts of a page show dialogs with `alert()`, `confirm()` and `prompt()`, and ask to confirm leaving a page with
unsaved changes (a `beforeunload` event). The [DialogController](cref:Meziantou.AspNetCore.Testing.DialogController) of
the page ([Dialogs](cref:Meziantou.AspNetCore.Testing.TestPage.Dialogs)) answers them and records them in
[History](cref:Meziantou.AspNetCore.Testing.DialogController.History). The script waits for the answer, so the test sets
it up before the action that shows the dialog:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=DialogController_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server);
var page = await browser.OpenAsync("/dialogs");

// Answer the next confirm() with Cancel: the item is kept
page.Dialogs.DismissNext();
await page.GetByRole("button", "Delete").ClickAsync();
await Expect(page.Locator("output")).ToHaveTextAsync("Kept");

// Answer the next prompt() with a text
page.Dialogs.AcceptNext("Invoice.pdf");
await page.GetByRole("button", "Rename").ClickAsync();
await Expect(page.GetByRole("listitem")).ToHaveTextAsync("Invoice.pdf");

// Answer the next confirm() with OK, then the alert() that it shows: the answers are used in order
page.Dialogs.AcceptNext();
page.Dialogs.AcceptNext();
await page.GetByRole("button", "Delete").ClickAsync();
await Expect(page.GetByRole("listitem")).ToHaveCountAsync(0);

// The controller records the dialogs and their answers
var history = page.Dialogs.History;
Assert.Equal(["Delete Report.pdf?", "New name", "Delete Invoice.pdf?", "Deleted"], history.Select(dialog => dialog.Message));
Assert.Equal([UserPromptType.Confirm, UserPromptType.Prompt, UserPromptType.Confirm, UserPromptType.Alert], history.Select(dialog => dialog.Type));
Assert.Equal([false, true, true, true], history.Select(dialog => dialog.Accepted));
Assert.Equal("Invoice.pdf", history[1].Text);
```
<!-- endref -->

[AcceptNext](cref:Meziantou.AspNetCore.Testing.DialogController.AcceptNext) clicks OK: `confirm()` returns true,
`prompt()` returns the text (its default value when the text is null), and the page is left.
[DismissNext](cref:Meziantou.AspNetCore.Testing.DialogController.DismissNext) clicks Cancel: `confirm()` returns false,
`prompt()` returns null, and the navigation or the closing of the page is canceled. Each answer answers one dialog, in the
order they are queued, whatever its kind. [PendingCount](cref:Meziantou.AspNetCore.Testing.DialogController.PendingCount)
is the number of answers that no dialog used yet.

## Unexpected dialogs fail the test

A dialog that nothing answers gets the answer of a browser that shows no dialog: `confirm()` returns false, `prompt()`
returns null, and the page is left. In [DialogMode.Strict](cref:Meziantou.AspNetCore.Testing.DialogMode.Strict) mode, the
default, the action that showed it then fails with an
[UnexpectedDialogException](cref:Meziantou.AspNetCore.Testing.UnexpectedDialogException), so a dialog that the test does
not expect does not go unnoticed:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=DialogController_StrictMode;format=md-fenced-code-block;dedent=true -->
```csharp
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server);
var page = await browser.OpenAsync("/dialogs");

// No answer: confirm() returns false, and the click fails
var exception = await Assert.ThrowsAsync<UnexpectedDialogException>(() => page.GetByRole("button", "Delete").ClickAsync());

Assert.Equal("Delete Report.pdf?", exception.Dialog?.Message);
Assert.Equal("Kept", page.Find("output").TextContent);
```
<!-- endref -->

The exception is thrown by the action (a click, a navigation, a wait), not in the script: a `try`/`catch` of the page
does not hide it. Set [Mode](cref:Meziantou.AspNetCore.Testing.DialogController.Mode) to
[DialogMode.Loose](cref:Meziantou.AspNetCore.Testing.DialogMode.Loose) to give the unexpected dialogs this answer
silently; [IsUnexpected](cref:Meziantou.AspNetCore.Testing.PageDialog.IsUnexpected) still tells them in the history.

## Check a dialog

[HandleNext](cref:Meziantou.AspNetCore.Testing.DialogController.HandleNext) queues a handler that gets the dialog
([PageDialog](cref:Meziantou.AspNetCore.Testing.PageDialog)), can check it, and answers it with
[Accept](cref:Meziantou.AspNetCore.Testing.PageDialog.Accept) or [Dismiss](cref:Meziantou.AspNetCore.Testing.PageDialog.Dismiss).
`AcceptNext` and `DismissNext` are handlers that answer at once:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=DialogController_HandleNext;format=md-fenced-code-block;dedent=true -->
```csharp
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server);
var page = await browser.OpenAsync("/dialogs");

// Check the dialog, then answer it
page.Dialogs.HandleNext(dialog =>
{
    Assert.Equal(UserPromptType.Prompt, dialog.Type);
    Assert.Equal("Report.pdf", dialog.DefaultValue);
    dialog.Accept(dialog.DefaultValue.Replace(".pdf", ".txt", StringComparison.Ordinal));
});
await page.GetByRole("button", "Rename").ClickAsync();

await Expect(page.GetByRole("listitem")).ToHaveTextAsync("Report.txt");
```
<!-- endref -->

A dialog is answered once, while its handler runs: a second answer throws an `InvalidOperationException`, and so does an
answer after the handler returned. An exception of a handler, such as a failed assertion, fails the action that showed
the dialog with this exception. A handler that answers nothing lets the next handler answer.

## Answer every dialog

[HandleAll](cref:Meziantou.AspNetCore.Testing.DialogController.HandleAll) sets the handler of the dialogs that no queued
handler answers; [AcceptAll](cref:Meziantou.AspNetCore.Testing.DialogController.AcceptAll) and
[DismissAll](cref:Meziantou.AspNetCore.Testing.DialogController.DismissAll) answer them all the same way, and
`HandleAll(null)` removes the handler:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=DialogController_HandleAll;format=md-fenced-code-block;dedent=true -->
```csharp
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server);
var page = await browser.OpenAsync("/dialogs");

// Answer the dialogs that no queued handler answers
page.Dialogs.HandleAll(dialog =>
{
    if (dialog.Type is UserPromptType.Prompt)
    {
        dialog.Accept(dialog.DefaultValue.ToUpperInvariant());
    }
    else
    {
        dialog.Accept();
    }
});

await page.GetByRole("button", "Rename").ClickAsync();
await Expect(page.GetByRole("listitem")).ToHaveTextAsync("REPORT.PDF");

await page.GetByRole("button", "Delete").ClickAsync();
await Expect(page.GetByRole("listitem")).ToHaveCountAsync(0);
```
<!-- endref -->

The answer of a dialog is, in this order:

1. the next handler queued with `HandleNext`, `AcceptNext` or `DismissNext`,
2. the handler set with `HandleAll`, `AcceptAll` or `DismissAll`,
3. the [UserPromptHandler](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.UserPromptHandler) of the
   options of the page, set with [ConfigureBrowsingContext](cref:Meziantou.AspNetCore.Testing.TestPageSettings.ConfigureBrowsingContext),
4. the answer of a browser that shows no dialog, which makes the dialog unexpected.

## Share a controller between pages

Each page has its own controller. To answer the dialogs of all the pages of a browser in one place, set
[WebAppBrowserOptions.Dialogs](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.Dialogs) (or
[ComponentTestHostOptions.Dialogs](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.Dialogs),
[TestPageSettings.Dialogs](cref:Meziantou.AspNetCore.Testing.TestPageSettings.Dialogs)). The pages then share its
handlers and its history, and [Page](cref:Meziantou.AspNetCore.Testing.PageDialog.Page) tells the page of a dialog.
[DialogController.AcceptingAll](cref:Meziantou.AspNetCore.Testing.DialogController.AcceptingAll) and
[DialogController.DismissingAll](cref:Meziantou.AspNetCore.Testing.DialogController.DismissingAll) create a new controller
that answers every dialog:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_Dialogs;format=md-fenced-code-block;dedent=true -->
```csharp
// The pages of the browser share the controller: dismiss every dialog
var dialogs = DialogController.DismissingAll();
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { Dialogs = dialogs });
var page = await browser.OpenAsync("/dialogs");
var otherPage = await browser.OpenAsync("/dialogs");

await page.GetByRole("button", "Delete").ClickAsync();
await otherPage.GetByRole("button", "Rename").ClickAsync();

Assert.Same(dialogs, page.Dialogs);
Assert.Equal([page, otherPage], dialogs.History.Select(dialog => dialog.Page));
```
<!-- endref -->

## Wait for a dialog

A dialog shown later, by a timer or after a request, is answered by the handlers when it is shown.
[WaitForDialogAsync](cref:Meziantou.AspNetCore.Testing.TestPage.WaitForDialogAsync) runs the page until it shows a dialog,
then returns it with its answer. Each call returns the next dialog of the page that it did not return yet:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=TestPage_WaitForDialog;format=md-fenced-code-block;dedent=true -->
```csharp
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server);
var page = await browser.OpenAsync("/dialogs");

// The answer is set up before the dialog: the script waits for it
page.Dialogs.AcceptNext();
await page.GetByRole("button", "Save").ClickAsync(new ClickOptions { NoWaitAfter = true });

// The alert() is shown by a timer: wait for it, and get it with its answer
var dialog = await page.WaitForDialogAsync();
Assert.Equal("Saved", dialog.Message);
Assert.True(dialog.Accepted);
```
<!-- endref -->

The answer cannot be given after the wait: the script stops in `confirm()` until it gets its answer, and the page runs on
the thread of the test, so the action that shows a dialog returns only once the dialog is answered.

## Leave a page with unsaved changes

A page that cancels its `beforeunload` event asks to confirm leaving it, once the user interacted with it (it has sticky
activation, as after a click). The dialog is a
[UserPromptType.BeforeUnload](cref:Meziantou.Framework.WebPlatform.Dom.UserPromptType.BeforeUnload) without message:
dismiss it to stay on the page, accept it to leave.

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=DialogController_BeforeUnload;format=md-fenced-code-block;dedent=true -->
```csharp
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server);
var page = await browser.OpenAsync("/dialogs");

// The click gives the page the sticky activation that the confirmation requires
page.Dialogs.DismissNext();
await page.GetByRole("button", "Delete").ClickAsync();

// Stay on the page that has unsaved changes
page.Dialogs.DismissNext();
await page.GetByRole("link", "Home").ClickAsync();
await Expect(page).ToHaveUrlAsync("/dialogs");
```
<!-- endref -->

## Components

The calls of the components to `confirm()`, `prompt()` or `alert()` through `IJSRuntime` are dialogs of the page when it
runs its scripts ([EnableJavaScript](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.EnableJavaScript)).
Without it, set up their results with the [JavaScript interop](07-JavaScriptInterop.md).

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_Dialogs;format=md-fenced-code-block;dedent=true -->
```csharp
// The calls of the components to confirm() go to the window of the page
await using var host = new ComponentTestHost(new ComponentTestHostOptions { EnableJavaScript = true });
var component = await host.RenderAsync<DeleteButton>();

host.Dialogs.AcceptNext();
await component.GetByRole("button", "Delete").ClickAsync();

Assert.Equal("Deleted", component.Find("#status").TextContent);
Assert.Equal("Delete the item?", Assert.Single(host.Dialogs.History).Message);
```
<!-- endref -->

## What is recorded

Each [PageDialog](cref:Meziantou.AspNetCore.Testing.PageDialog) of the history has its kind
([Type](cref:Meziantou.AspNetCore.Testing.PageDialog.Type)), its [Message](cref:Meziantou.AspNetCore.Testing.PageDialog.Message)
with the newlines normalized to `\n`, the [DefaultValue](cref:Meziantou.AspNetCore.Testing.PageDialog.DefaultValue) of a
`prompt()`, whether it was [Accepted](cref:Meziantou.AspNetCore.Testing.PageDialog.Accepted), the
[Text](cref:Meziantou.AspNetCore.Testing.PageDialog.Text) of an accepted `prompt()`, and whether it was unexpected.
[ClearHistory](cref:Meziantou.AspNetCore.Testing.DialogController.ClearHistory) empties the history. A browser does not
show a dialog of a document sandboxed without `allow-modals`, nor of a document being unloaded: those dialogs are not
recorded, and the scripts get the answer of a browser that shows no dialog.
