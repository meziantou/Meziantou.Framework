---
title: Command-line tool
description: Run the DevTools protocol server from the command line, and connect Playwright, Puppeteer or any CDP client of any language to it.
---

The `Meziantou.Framework.WebPlatform.DevTools.Tool` .NET tool runs a DevTools protocol server whose browser is this engine,
without writing code. Install it:

<InstallDotnetTool Id="Meziantou.Framework.WebPlatform.DevTools.Tool" />

Start a server on port 9222 (the default) whose pages can load any URL:

```bash
Meziantou.Framework.WebPlatform.DevTools.Tool --allow-network
```

The server prints its WebSocket endpoint (`DevTools listening on ws://127.0.0.1:9222/devtools/browser/<id>`), as Chromium
does, and runs until Ctrl+C. Connect a client to it:

- Playwright, in any language: `ConnectOverCDPAsync("http://127.0.0.1:9222/")` in .NET, `chromium.connectOverCDP()` in
  Node.js, Python and Java. The clients read the WebSocket endpoint from `/json/version`; any version of Playwright can
  connect.
- Puppeteer: `puppeteer.connect({ browserURL: "http://127.0.0.1:9222" })`, or `browserWSEndpoint` with the printed endpoint.

See [Hosting the server](02-HostingTheServer.md) for what the server does with the connections.

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `--host` | `127.0.0.1` | The address to listen on (`0.0.0.0` accepts remote clients) |
| `--port` | `9222` | The port to listen on (`0` chooses a free port) |
| `--allow-network` | off | The pages can load the documents and the resources of any URL |
| `--system-fonts` | off | The pages can use the fonts installed on the machine |
| `--max-connections` | none | The number of clients connected at the same time (the others wait) |

The [command reference](04-CommandReference.md) lists them with the help of the tool.

Without `--allow-network`, the pages only use the content they are given (`page.setContent()`) and the responses of the
intercepted requests (`page.route()`). The scripts of the pages run in the process of the server: connect trusted clients
only, and load trusted pages only.
