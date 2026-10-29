---
title: Introduction
description: Tag ids, units and keys with an attribute, and let a Roslyn analyzer report the values that get mixed up.
---

`Meziantou.Framework.TaggedValues` catches a common family of bugs at compile time: an order id passed where a project
id is expected, a customer id compared with an invoice id, meters added to feet. You add `[ValueTag("OrderId")]` to the
declarations that hold such values, and the analyzer bundled with the package reports when values with different tags
are compared, assigned, passed, returned, or combined.

## Why tags instead of wrapper types

A common fix for these bugs is to wrap each kind of id in its own type, such as `readonly record struct OrderId(Guid Value)`.
It works, but every serializer, ORM mapping, model binder and API contract then has to learn about the new types.

A tag does not change the type. The value stays a `Guid`, an `int` or a `string`, so there is nothing to change in
serializers, ORMs, or APIs. The attribute exists only for the analyzer: it does not change what the code does at run
time.

## What you can do with it

- **Tag declarations**: fields, properties, parameters and return values, with one tag or a union of tags. See
  [Tagging values](03-TaggingValues.md).
- **Tag members you do not own**, such as `Process.Id`, with an assembly attribute. See
  [Tagging values](03-TaggingValues.md#tag-members-of-types-you-do-not-own).
- **Tag local variables** with a comment, as C# does not allow attributes on them. See
  [Tagging local variables](04-LocalVariables.md).
- **Follow tags through collections**, dictionaries, tasks, LINQ and lambdas. See
  [Collections, dictionaries and wrappers](05-CollectionsAndWrappers.md).
- **Follow tags through arithmetic**, so `orderNumber + 1` is still an order number. See
  [Arithmetic and operators](06-ArithmeticAndOperators.md).
- **Infer tags from names**, so a property `Id` of `Order` is an order id without an attribute. See
  [Naming conventions](07-NamingConventions.md).
- **Require tags everywhere** a tagged value goes. See [Strict mode](08-StrictMode.md).
- **Fix the reports** with code fixes that change, add, or remove tags. See [Analyzer rules](09-Rules/01-Overview.md).

## Principles

- **Untagged values are never reported**, unless you enable [strict mode](08-StrictMode.md). You can adopt the package
  one declaration at a time, starting with the ids that matter most.
- **Two values are compatible when they share at least one tag.** Tags are compared ignoring case.
- **Tags flow.** A local variable initialized with a tagged value, the element read from a tagged list, or the result
  of `orderNumber + 1` keeps the tag, so you tag the declarations and the analyzer follows the values.
- **Messages are complete.** Every message names both declarations and their tags, so a build log is enough to act on.

## Namespaces

`ValueTagAttribute` is in the `Meziantou.Framework.TaggedValues` namespace. It is the only type of the package.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, tag your first ids, and fix the first warnings.
- [Analyzer rules](09-Rules/01-Overview.md): what each rule reports and how to fix it.
