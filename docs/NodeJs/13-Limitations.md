---
title: Limitations
description: What the library does not do, and how to work around it.
---

- **Node.js is not included.** Node.js 16.9 or later must be installed, and Node.js 21 or later is needed to return
  `BigInt` values. See [Configuring the Node.js process](08-ConfiguringTheProcess.md#locate-nodejs).
- **Arguments and results are serialized as JSON.** Use [JSValue](cref:Meziantou.Framework.NodeJs.JSValue) to pass values that JSON cannot represent, and
  [JSReference](cref:Meziantou.Framework.NodeJs.JSReference) to keep values in the Node.js process. See [Arguments and results](05-ArgumentsAndResults.md) and
  [Keeping values in Node.js](06-References.md).
- **A message is limited** to the maximum length of a JavaScript string (about 512 million UTF-16 characters). A larger
  message fails its call.
- **[JSValue](cref:Meziantou.Framework.NodeJs.JSValue), [JSReference](cref:Meziantou.Framework.NodeJs.JSReference) and [JSCallback](cref:Meziantou.Framework.NodeJs.JSCallback) can only be used in the arguments of a call**, or in the result of a
  [JSCallback](cref:Meziantou.Framework.NodeJs.JSCallback). A `JsonNode` created from them cannot be serialized or cloned, and they cannot be members of objects
  serialized using reflection.
- **JavaScript code calls .NET through the delegates it receives.** It cannot call other .NET code, and the arguments
  and results of the delegates are serialized as JSON. See [Calling .NET from JavaScript](07-CallingDotNet.md).
- **Canceling a call does not stop its JavaScript code.** Set [UnresponsiveTimeout](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.UnresponsiveTimeout) to kill the process when the code
  blocks its event loop. See [Cancellation and timeouts](10-CancellationAndTimeouts.md).
- **A synchronous infinite loop blocks all the other calls**, or all the other calls of its worker thread with
  [WorkerThreads](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.WorkerThreads). Disposing the host kills the process.
- **A module is loaded once per process.** A change to its file has no effect until a new host is started. See
  [Calling modules](04-CallingModules.md#modules-are-loaded-once).
- **JavaScript code must not read from `process.stdin`**, which is closed once the process has started.
- **The library is not a sandbox.** See [Security](12-Security.md).
