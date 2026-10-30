---
title: Security
description: The JavaScript code runs with the permissions of your application; keep untrusted input out of the code you run.
---

The JavaScript code runs with the permissions of your application, and the Node.js process inherits its environment
variables. Only run code you trust: the library is not a sandbox.

## Pass untrusted values as arguments

- Never insert an untrusted value in the code passed to `EvaluateAsync`. Pass it as an argument, in the `args` array
  (see [Running JavaScript code](03-RunningCode.md)).
- Never build a module specifier, an export name, or the member name of a `JSReference` call from untrusted input: they
  select the code to run. For instance, `InvokeAsync("node:child_process", "execSync", ["..."])` runs a command.

## What the library checks

- `InvokeAsync` only accepts `file:` and `node:` URLs, as a `data:` URL contains the code to run.
- The inherited `constructor` and `__proto__` members cannot be accessed.
- Calling `Function` or `eval` directly, or creating an instance of `Function` using `CreateInstanceAsync`, is rejected.

These checks only close the most direct ways of running code. Built-in modules run code by design, such as
`InvokeAsync("node:vm", "runInThisContext", [code])`, so a module specifier, an export name or a member name built from
untrusted input can still run arbitrary code.

## The connection with the process

The socket used to communicate with the Node.js process is only accessible by the current user, and the process must
prove that it was started by the host.

To keep secrets out of the JavaScript code, remove the environment variables it does not need with
`EnvironmentVariables` (a `null` value removes a variable). See
[Configuring the Node.js process](07-ConfiguringTheProcess.md).
