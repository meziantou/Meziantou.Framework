---
title: Declaring FullPath instead of string
description: The rules that find the methods, properties, variables and parameters declared as string that only ever hold a FullPath.
---

These rules find the declarations that use `string` for a value that is always a `FullPath`. Declaring them as
`FullPath` keeps the guarantee of the type, and lets the [other rules](02-UsingFullPathMembers.md) find the string
operations to replace. They are Info rules without a code fix: changing a declaration can affect its callers, so you
make the change yourself.

The rules recognize a `FullPath` in all its string forms: `fullPath`, `(string)fullPath`, `fullPath.Value`,
`fullPath.RawValue` and `fullPath.ToString()`.

## MFFP0011: Return FullPath instead of string

The rule reports a method or a local function that returns `string` when every value it returns is a `FullPath`, as in
`string GetOutputPath() => root / "output";`. The `return` statements of the lambdas and local functions it contains are
not taken into account.

It does not report a method that is `override`, `virtual` or `abstract`, or that implements an interface: its return
type is not yours to change.

## MFFP0012: Declare the property as FullPath instead of string

The rule reports a `string` property whose getter only returns `FullPath` values, such as
`public string Path => _path;` where `_path` is a `FullPath`.

It does not report:

- a property that has a setter, because changing its type also changes what callers can assign;
- an indexer;
- a property that is `override`, `virtual` or `abstract`, or that implements an interface.

## MFFP0013: Declare the variable as FullPath instead of string

The rule reports a local variable declared as `string` whose initializer and assignments are all `FullPath` values, such
as `string path = fullPath;`.

It does not report:

- a variable declared with `var`: it already has the type of its initializer;
- a variable passed by `ref`, `out` or `in`, or aliased by a `ref` local;
- a variable used with a compound assignment, such as `path += ".bak"` or `path ??= other`;
- a variable assigned by a deconstruction.

## MFFP0014: Declare the parameter as FullPath instead of string

The rule reports a `string` parameter when every call of the method, in the project, passes a `FullPath`.

It only analyzes the methods whose callers are all in the project: it does not report a method visible outside the
assembly, such as a public or protected method of a public type. It also does not report:

- a method that is never called, or that is used as a delegate;
- a `string?` parameter, because a `FullPath` cannot be `null`;
- an optional, `params`, `ref`, `out` or `in` parameter;
- a method that is `override`, `virtual` or `abstract`, or that implements an interface;
- a constructor or an operator.

The rule looks at all the calls of the project, so it is reported when the whole project has been analyzed. Depending
on the settings of your IDE, it may only appear in the build output.
