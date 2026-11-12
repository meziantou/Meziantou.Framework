# Meziantou.Framework.WebPlatform.DevTools.Tool

`Meziantou.Framework.WebPlatform.DevTools.Tool` runs a Chrome DevTools Protocol (CDP) server whose browser is the managed
browser engine of [Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md) (see
[Meziantou.Framework.WebPlatform.DevTools](../Meziantou.Framework.WebPlatform.DevTools/readme.md)). The CDP clients see a
headless Chromium: the Playwright clients (.NET, Node.js, Python, Java) connect to it with `connectOverCDP`, Puppeteer with
`puppeteer.connect()`. Any version of Playwright can connect.

## Install

```bash
dotnet tool install --global Meziantou.Framework.WebPlatform.DevTools.Tool
```

## Example

Start a server that lets the pages load any URL, on port 9222 (the default):

```bash
Meziantou.Framework.WebPlatform.DevTools.Tool --allow-network
```

The server prints its WebSocket endpoint (`DevTools listening on ws://127.0.0.1:9222/devtools/browser/<id>`), and runs until
Ctrl+C. Connect to it from a Playwright client with the HTTP endpoint, which serves `/json/version`:

```csharp
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectOverCDPAsync("http://127.0.0.1:9222/");
var context = await browser.NewContextAsync();
var page = await context.NewPageAsync();
await page.GotoAsync("https://example.com/");
```

Without `--allow-network`, the pages can only use the content they are given (e.g. `page.setContent()`) and the responses of
the intercepted requests (`page.route()`). The scripts of the pages run in the process of the server: only connect trusted
clients, and only load trusted pages.

<!-- help -->
## Help

```
Description:
  Run a Chrome DevTools Protocol server whose browser is Meziantou.Framework.WebPlatform (as a headless Chromium 146.0.0.0)

Usage:
  Meziantou.Framework.WebPlatform.DevTools.Tool [options]

Options:
  --host <host>                        Address to listen on (use 0.0.0.0 to accept the remote clients) [default: 127.0.0.1]
  --port <port>                        Port to listen on (0 chooses a free port) [default: 9222]
  --allow-network                      Let the pages load the documents and the resources of any URL (without it, the pages can only use the content they are given and the intercepted requests)
  --system-fonts                       Let the pages use the fonts installed on the machine
  --max-connections <max-connections>  Maximum number of clients connected at the same time (the others wait)
  -?, -h, --help                       Show help and usage information
  --version                            Show version information
```
<!-- help -->