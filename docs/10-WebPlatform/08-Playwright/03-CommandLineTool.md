---
title: Command-line tool
description: Run the Playwright server from the command line, and connect the Playwright clients of any language to it.
---

The `Meziantou.Framework.WebPlatform.Playwright.Tool` .NET tool runs a Playwright server whose browser is this engine,
without writing code. Install it:

```bash
dotnet tool install --global Meziantou.Framework.WebPlatform.Playwright.Tool
```

Start a server on port 3000 whose pages can load any URL:

```bash
Meziantou.Framework.WebPlatform.Playwright.Tool --port 3000 --allow-network
```

The server prints its endpoint (`Listening on ws://127.0.0.1:3000/`) and runs until Ctrl+C. Connect a Playwright client
to it with `BrowserType.ConnectAsync("ws://127.0.0.1:3000/")` in .NET, or `browserType.connect()` in Node.js, as in
[Hosting the server](02-HostingTheServer.md). The clients must use the version of Playwright of the server, which the
help shows.

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `--host` | `127.0.0.1` | The address to listen on (`0.0.0.0` accepts remote clients) |
| `--port` | `0` (a free port) | The port to listen on |
| `--path` | `/` | The path of the WebSocket endpoint |
| `--allow-network` | off | The pages can load the documents and the resources of any URL |
| `--system-fonts` | off | The pages can use the fonts installed on the machine |
| `--browser-name` | `chromium` | The name of the browser reported to the clients (`chromium`, `firefox` or `webkit`) |
| `--allow-any-client-version` | off | Accepts the clients of another version of Playwright |

Without `--allow-network`, the pages only use the content of `page.setContent()` and the responses of the routes
(`page.route()`). The scripts of the pages run in the process of the server: connect trusted clients only, and load
trusted pages only.
