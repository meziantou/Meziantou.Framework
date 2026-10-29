---
title: Tagging local variables
description: Tag local variables and type arguments with a comment, and let locals take the tag of their initializer.
---

C# does not allow attributes on local variables. Most locals need no tag: they take the tag of the value they are
initialized with. When you want to declare the tag of a local, write a `ValueTag` comment.

## Let the local take the tag of its value

A local without a comment takes the tag of its initializer:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/LocalVariables.cs;region=TaggedValues_LocalInference;format=md-fenced-code-block;dedent=true -->
```csharp
var id = order.Id;    // id is an OrderId
Load(id);             // ok
id = order.ProjectId; // MFTV0002

var pair = (order.Id, order.ProjectId);
Load(pair.Item1);     // ok
Load(pair.Item2);     // MFTV0002
```
<!-- endref -->

The same goes for the variable of a `foreach` (the tag of the collection), of an `out var` (the tag of the parameter),
and of a pattern (the tag of the value matched). In a property pattern, a list pattern, a positional pattern, or a
deconstruction, the variable takes the tag of the part it matches:

- `order is { Id: var id }`: `id` is an OrderId.
- `foreach (var (id, projectId) in projectIdByOrderId)`: `id` is an OrderId when the dictionary maps OrderIds to
  ProjectIds.
- `var (id, projectId) = (order.Id, order.ProjectId)`: `id` is an OrderId.
- `var (id, projectId) = order`: `id` is an OrderId when `Order` is a record, or has a `Deconstruct` method with tagged
  `out` parameters.

The element of a tuple, such as `pair.Item1`, takes the tag of the element of the tuple literal it was created with.

## Tag a local with a comment

The comment accepts three forms, written as `/* ... */` or `// ...`:

| Comment | Equivalent attribute |
| -- | -- |
| `/* ValueTag=OrderId */` or `// ValueTag=OrderId` | `[ValueTag("OrderId")]` |
| `/* ValueTag=OrderId, ProjectId */` or `// ValueTag=OrderId, ProjectId` | `[ValueTag("OrderId", "ProjectId")]` |
| `/* ValueTag Key=OrderId Value=ProjectId */` or `// ValueTag Key=OrderId Value=ProjectId` | `[ValueTag(Key = "OrderId", Value = "ProjectId")]` |

- Spaces around `=` and `,` are allowed.
- A tag cannot contain spaces, `=`, or `,`, so a `//` comment cannot continue with other text after the tags.
- In the dictionary form, `Key` and `Value` can be separated by a space or a comma, either one can be omitted, and each
  accepts a single tag.

### Block comments

A `/* ... */` comment must be part of the declaration: before the type, after the type, or next to the name. A comment
before or after the type tags every variable of the declaration. A comment next to a name, or right after the comma
that precedes it, tags only that variable:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/LocalVariables.cs;region=TaggedValues_LocalBlockComments;format=md-fenced-code-block;dedent=true -->
```csharp
/* ValueTag=OrderId */ Guid id1 = Guid.NewGuid();
Guid /* ValueTag=OrderId */ id2 = Guid.NewGuid();
var id3 /* ValueTag=OrderId */ = Guid.NewGuid();
using var /* ValueTag=OrderFile */ stream = File.OpenRead(path);

Guid /* ValueTag=OrderId */ a = Guid.NewGuid(), b = Guid.NewGuid();   // a and b are OrderIds
Guid c = Guid.NewGuid(), d /* ValueTag=ProjectId */ = Guid.NewGuid(); // only d is a ProjectId
Guid e = Guid.NewGuid(), /* ValueTag=ProjectId */ f = Guid.NewGuid(); // only f is a ProjectId

var /* ValueTag Key=OrderId Value=ProjectId */ projectIdByOrderId = new Dictionary<Guid, Guid>();
```
<!-- endref -->

### Line comments

A `//` comment must be on a line before the statement, and tags every variable the statement declares. Other comments
can sit between the `// ValueTag` comment and the statement, but a comment at the end of the previous line does not
count. When a variable also has a `/* ... */` comment, that comment wins:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/LocalVariables.cs;region=TaggedValues_LocalLineComments;format=md-fenced-code-block;dedent=true -->
```csharp
// ValueTag=OrderId
Guid id1 = Guid.NewGuid();

// ValueTag=OrderId
// The order being processed
Guid a = Guid.NewGuid(), b /* ValueTag=ProjectId */ = Guid.NewGuid(); // a is an OrderId, b is a ProjectId

// ValueTag=OrderId
Guid c /* ValueTag=ProjectId */ = Guid.NewGuid(); // MFTV0005 on the // comment, which does not tag anything

// ValueTag Key=OrderId Value=ProjectId
var projectIdByOrderId = new Dictionary<Guid, Guid>();
```
<!-- endref -->

A comment that another comment overrides for every variable it could tag, with other tags, does not tag anything and is
reported ([MFTV0005](09-Rules/06-MFTV0005.md)).

### Other declarations

Comments also work for the variables of `foreach`, `for`, `out var`, patterns, and deconstructions. A `//` comment works
for `foreach` and `for`, but not for `out var`, patterns, and deconstructions, whose statement is not a declaration:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/LocalVariables.cs;region=TaggedValues_LocalOtherDeclarations;format=md-fenced-code-block;dedent=true -->
```csharp
// ValueTag=OrderId
foreach (var id in ids) { }
foreach (Guid /* ValueTag=OrderId */ id in ids) { }
for (int /* ValueTag=OrderIndex */ i = 0; i < count; i++) { }
if (TryGetId(out var /* ValueTag=OrderId */ parsedId)) { }
if (value is Guid /* ValueTag=OrderId */ matchedId) { }
var (orderId /* ValueTag=OrderId */, projectId) = GetIds();
```
<!-- endref -->

### When the comment disagrees with the value

When the comment disagrees with the initializer, the collection of a `foreach`, the parameter of an `out var`, the
value matched by a pattern, or the deconstructed value, the comment wins and the value is reported
([MFTV0002](09-Rules/03-MFTV0002.md)). The code fix changes the tag in the comment. When the comment also tags other
variables of the declaration, the code fix adds a comment next to the name of the variable instead.

## Tag type arguments

A `/* ValueTag=... */` comment can also be written in a type argument, to tag the elements of a collection, or the keys
and the values of a dictionary, without naming them:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/TypeArguments.cs;region=TaggedValues_TypeArguments;format=md-fenced-code-block;dedent=true -->
```csharp
var projectIdByOrderId = new Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid>();
var orderIds = new List</* ValueTag=OrderId */ Guid>();
Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid> map = GetMap();
```
<!-- endref -->

- The comment can be placed before or after the type argument: `</* ValueTag=OrderId */ Guid, ...>` and
  `<Guid /* ValueTag=OrderId */, ...>` are equivalent.
- The type argument of a collection, a `Nullable<T>`, a `Task<T>`, a `ValueTask<T>`, or a `Lazy<T>` tags its elements
  or its value. The type arguments of a dictionary or a `KeyValuePair<TKey, TValue>` tag its keys and its values. Type
  arguments can be nested: `new Dictionary</* ValueTag=OrderId */ Guid, List</* ValueTag=ProjectId */ Guid>>()`.
- Only the single-tag and union forms are accepted, as the position of the argument already selects the key or the
  value. `/* ValueTag Key=... */` in a type argument is reported ([MFTV0005](09-Rules/06-MFTV0005.md)).
- The comment applies to the type of a `new` expression, and to the type of a local variable, including `foreach`,
  `for`, `using`, `out` and pattern variables. The tag of a `new` expression flows to a `var` local like any initializer.
- The elements of a collection initializer and the arguments of the constructor are checked:
  `new List</* ValueTag=OrderId */ Guid> { projectId }` is reported ([MFTV0002](09-Rules/03-MFTV0002.md)).
- A comment on the variable itself wins over the comments in the type arguments of its declared type.

## Comments that are ignored or reported

These comments are ignored:

- comments that do not start with `ValueTag`,
- prose that starts with `ValueTag` but contains several words and no `=`, such as `// ValueTag is not used here`,
- `///` documentation comments.

A `ValueTag` comment that cannot be parsed, or that does not tag a local variable, does not tag anything and is
reported ([MFTV0005](09-Rules/06-MFTV0005.md)). This includes a comment on a field, inside an initializer, at the end of
a line, before a statement that declares no variable, in the type arguments of a field, a parameter, or a generic
method, and in the type arguments of a type that is neither a collection, a dictionary, nor a wrapper, such as
`Tuple<Guid, Guid>`. Use the `[ValueTag]` attribute on fields, properties, parameters, and return values.
