# Meziantou.Framework.WebPlatform.Playwright.Tool

`Meziantou.Framework.WebPlatform.Playwright.Tool` runs a Playwright server whose browser is the managed browser engine of
[Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md) (see
[Meziantou.Framework.WebPlatform.Playwright](../Meziantou.Framework.WebPlatform.Playwright/readme.md)). The Playwright
clients (.NET, Node.js, Python, Java) connect to it with `BrowserType.ConnectAsync` (`browserType.connect()` in Node.js). The
clients must use the version of Playwright of the server (shown in the help).

## Install

```bash
dotnet tool install --global Meziantou.Framework.WebPlatform.Playwright.Tool
```

## Example

Start a server that lets the pages load any URL, on port 3000:

```bash
Meziantou.Framework.WebPlatform.Playwright.Tool --port 3000 --allow-network
```

The server prints its endpoint (`Listening on ws://127.0.0.1:3000/`), and runs until Ctrl+C. Connect to it from a
Playwright client:

```csharp
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectAsync("ws://127.0.0.1:3000/");
var page = await browser.NewPageAsync();
await page.GotoAsync("https://example.com/");
```

Without `--allow-network`, the pages can only use the content of `page.setContent()` and the responses of the routes
(`page.route()`). The scripts of the pages run in the process of the server: only connect trusted clients, and only load
trusted pages.

<!-- help -->
## Help

```
Description:
  Run a Playwright 1.64.1 server whose browser is Meziantou.Framework.WebPlatform

Usage:
  Meziantou.Framework.WebPlatform.Playwright.Tool [options]

Options:
  --host <host>                  Address to listen on (use 0.0.0.0 to accept the remote clients) [default: 127.0.0.1]
  --port <port>                  Port to listen on (0 chooses a free port) [default: 0]
  --path <path>                  Path of the WebSocket endpoint [default: /]
  --allow-network                Let the pages load the documents and the resources of any URL (without it, the pages can only use page.setContent() and the routes)
  --system-fonts                 Let the pages use the fonts installed on the machine
  --browser-name <browser-name>  Name of the browser reported to the clients (chromium, firefox or webkit) [default: chromium]
  --allow-any-client-version     Accept the clients of another version of Playwright (the protocol may differ)
  -?, -h, --help                 Show help and usage information
  --version                      Show version information
```
<!-- help -->