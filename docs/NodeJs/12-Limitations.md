---
title: Limitations
description: What the library does not do, and how to work around it.
---

- **Node.js is not included.** Node.js 16.9 or later must be installed, and Node.js 21 or later is needed to return
  `BigInt` values. See [Configuring the Node.js process](07-ConfiguringTheProcess.md#locate-nodejs).
- **Arguments and results are serialized as JSON.** Use `JSValue` to pass values that JSON cannot represent, and
  `JSReference` to keep values in the Node.js process. See [Arguments and results](05-ArgumentsAndResults.md) and
  [Keeping values in Node.js](06-References.md).
- **A message is limited** to the maximum length of a JavaScript string (about 512 million UTF-16 characters). A larger
  message fails its call.
- **`JSValue` and `JSReference` can only be used in the arguments of a call.** A `JsonNode` created from them cannot be
  serialized or cloned, and they cannot be members of objects serialized using reflection.
- **JavaScript code cannot call back into .NET.**
- **Canceling a call does not stop its JavaScript code.** Set `UnresponsiveTimeout` to kill the process when the code
  blocks its event loop. See [Cancellation and timeouts](09-CancellationAndTimeouts.md).
- **A synchronous infinite loop blocks all the other calls**, or all the other calls of its worker thread with
  `WorkerThreads`. Disposing the host kills the process.
- **A module is loaded once per process.** A change to its file has no effect until a new host is started. See
  [Calling modules](04-CallingModules.md#modules-are-loaded-once).
- **JavaScript code must not read from `process.stdin`**, which is closed once the process has started.
- **The library is not a sandbox.** See [Security](11-Security.md).
