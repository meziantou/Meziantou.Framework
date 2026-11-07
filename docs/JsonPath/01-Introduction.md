---
title: Introduction
description: Query System.Text.Json documents and your own object models with standard JSONPath (RFC 9535).
---

`Meziantou.Framework.JsonPath` finds values in JSON with [JSONPath](https://datatracker.ietf.org/doc/html/rfc9535)
queries such as `$.store.book[?@.price < 10].title`. It implements the JSONPath standard, RFC 9535, for the
`System.Text.Json` types and for any object model you expose to it.

## What you can do with it

- **Query JSON**: select values by member name, index, slice, wildcard or at any depth, and filter them with
  comparisons, logical operators and functions. See [Query syntax](03-QuerySyntax.md) and [Filters](04-Filters.md).
- **Use the standard functions**: `length()`, `count()`, `match()`, `search()` and `value()`, with
  [I-Regexp (RFC 9485)](https://www.rfc-editor.org/rfc/rfc9485) regular expressions. See [Functions](05-Functions.md).
- **Opt in to domain-specific functions** using an immutable typed registry. See
  [Register custom functions](05-Functions.md#register-custom-functions).
- **Evaluate on the type you already have**: `JsonNode`, `JsonDocument` or `JsonElement`. Every match comes with its
  normalized path, such as `$['store']['book'][0]`. See [Evaluating queries](06-EvaluatingQueries.md).
- **Choose how missing data is reported**: by default a member or an element that does not exist selects nothing; in
  strict mode it throws. See [Errors and evaluation modes](07-ErrorsAndEvaluationModes.md).
- **Query your own object model** without converting it to JSON first. See
  [Custom object models](08-CustomObjectModels.md).

## Principles

- **Standard JSONPath.** The library follows RFC 9535 and is tested against the
  [JSONPath Compliance Test Suite](https://github.com/jsonpath-standard/jsonpath-compliance-test-suite). Queries written
  for other JSONPath dialects may need changes; see [Limitations](09-Limitations.md).
- **Invalid queries are rejected.** [JsonPath.Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) throws on a query that is not well-formed and valid, instead of
  guessing what it means.
- **Parse once, evaluate many times.** A [JsonPath](cref:Meziantou.Framework.Json.JsonPath) is immutable and thread-safe: keep it and reuse it for any number of
  documents, from any thread.
- **Results keep the document order.** The matches are returned in the order the query selects them, each with the
  normalized path that leads to it.

## Namespaces

| Namespace | Contains |
| --- | --- |
| [Meziantou.Framework.Json](cref:Meziantou.Framework.Json) | [JsonPath](cref:Meziantou.Framework.Json.JsonPath), the results, the evaluation modes, the extension methods and the navigator for custom object models |
| `System.Text.Json` and `System.Text.Json.Nodes` | The JSON types the queries are evaluated on: `JsonDocument`, `JsonElement`, `JsonNode` |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and run your first query.
- [Query syntax](03-QuerySyntax.md): the parts of a query.
- [Limitations](09-Limitations.md): what the library does not do.
