# Meziantou.Framework.TaggedValues

Tag primitive values with `[ValueTag]`, and the bundled Roslyn analyzer reports when values with different tags are mixed. The value stays a `Guid`, an `int` or a `string`, so there is nothing to change in serializers, ORMs, or APIs.

````c#
using Meziantou.Framework.TaggedValues;

class Order
{
    [ValueTag("OrderId")] public Guid Id { get; set; }
    [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
}

class OrderService
{
    public Order Load([ValueTag("OrderId")] Guid orderId) => ...;

    public void M(Order order)
    {
        _ = order.Id == order.ProjectId; // MFTV0001: values with different tags are compared
        Load(order.ProjectId);           // MFTV0002: a ProjectId flows to a parameter that expects an OrderId
    }
}
````

Values that are not tagged are never reported, so you can adopt the package one declaration at a time.

## Tagging values

- **Fields, properties, and parameters**: `[ValueTag("OrderId")]`
- **Return values**: `[return: ValueTag("OrderId")]`. This also works for async methods and iterators, where the tag describes the result or the elements.
- **Local variables** cannot have attributes, so use a comment: `Guid /* ValueTag=OrderId */ id = ...;`. See [Tagging local variables](#tagging-local-variables).
- **Unions**: `[ValueTag("OrderId", "ProjectId")]` accepts both. Two values are compatible when they share at least one tag.
- **Members of types you do not own**: `[assembly: ValueTag(typeof(Process), nameof(Process.Id), "ProcessId")]`. The tags also apply when the member is accessed through a derived type. Assembly attributes are read from referenced assemblies too, so a shared project can declare them once.

Tags are inherited from overridden members, from implemented interface members, and from the parameters of a record primary constructor. An explicit cast through `object` drops the tag: `(Guid)(object)value`.

## Tagging local variables

C# does not allow attributes on local variables, so a local is tagged with a comment, either `/* ... */` or `// ...`. The comment accepts three forms:

| Comment | Equivalent attribute |
| -- | -- |
| `/* ValueTag=OrderId */` or `// ValueTag=OrderId` | `[ValueTag("OrderId")]` |
| `/* ValueTag=OrderId, ProjectId */` or `// ValueTag=OrderId, ProjectId` | `[ValueTag("OrderId", "ProjectId")]` |
| `/* ValueTag Key=OrderId Value=ProjectId */` or `// ValueTag Key=OrderId Value=ProjectId` | `[ValueTag(Key = "OrderId", Value = "ProjectId")]` |

Spaces around `=` and `,` are allowed. A tag cannot contain spaces, `=`, or `,`, so a `//` comment cannot continue with other text after the tags. In the dictionary form, `Key` and `Value` can be separated by a space or a comma, either one can be omitted, and each accepts a single tag.

A `/* ... */` comment must be part of the declaration: before the type, after the type, or next to the name. A comment before or after the type tags every variable of the declaration, and a comment next to a name tags only that variable:

````c#
/* ValueTag=OrderId */ Guid id1 = ...;
Guid /* ValueTag=OrderId */ id2 = ...;
var id3 /* ValueTag=OrderId */ = ...;
using var /* ValueTag=OrderId */ id4 = ...;

Guid /* ValueTag=OrderId */ a = ..., b = ...;       // a and b are OrderIds
Guid c = ..., d /* ValueTag=ProjectId */ = ...;     // only d is a ProjectId

var /* ValueTag Key=OrderId Value=ProjectId */ projectIdByOrderId = new Dictionary<Guid, Guid>();
````

A `//` comment must be on a line before the statement, and tags every variable the statement declares. Other comments can sit between the `// ValueTag` comment and the statement, but a comment at the end of the previous line does not count. When a variable also has a `/* ... */` comment, that comment wins:

````c#
// ValueTag=OrderId
Guid id1 = ...;

// ValueTag=OrderId
// The order being processed
Guid a = ..., b /* ValueTag=ProjectId */ = ...; // a is an OrderId, b is a ProjectId

// ValueTag Key=OrderId Value=ProjectId
var projectIdByOrderId = new Dictionary<Guid, Guid>();
````

It also works for the variables of `foreach`, `for`, `out var`, and patterns. A `//` comment works for `foreach` and `for`, but not for `out var` and patterns, whose statement is not a declaration:

````c#
// ValueTag=OrderId
foreach (var id in ids) { }
foreach (Guid /* ValueTag=OrderId */ id in ids) { }
for (int /* ValueTag=OrderIndex */ i = 0; i < count; i++) { }
if (TryGetId(out var /* ValueTag=OrderId */ id)) { }
if (value is Guid /* ValueTag=OrderId */ id) { }
````

A local without a comment takes the tag of its initializer, of the collection of a `foreach`, of the parameter of an `out var`, or of the value matched by a pattern:

````c#
var id = order.Id;    // id is an OrderId
Load(id);             // ok
id = order.ProjectId; // MFTV0002
````

When the comment and the initializer disagree, the comment wins and the initializer is reported (MFTV0002). The code fix changes the tag in the comment.

### Tagging type arguments

A `/* ValueTag=... */` comment can also be written in a type argument, to tag the elements of a collection or the keys and the values of a dictionary without naming them:

````c#
var projectIdByOrderId = new Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid>();
var orderIds = new List</* ValueTag=OrderId */ Guid>();
Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid> map = GetMap();
````

- The comment can be placed before or after the type argument: `</* ValueTag=OrderId */ Guid, ...>` and `<Guid /* ValueTag=OrderId */, ...>` are equivalent.
- The type argument of a collection, a `Nullable<T>`, a `Task<T>`, a `ValueTask<T>`, or a `Lazy<T>` tags its elements or its value. The type arguments of a dictionary or a `KeyValuePair<TKey, TValue>` tag its keys and its values. Type arguments can be nested: `new Dictionary</* ValueTag=OrderId */ Guid, List</* ValueTag=ProjectId */ Guid>>()`.
- Only the single-tag and union forms are accepted, as the position of the argument already selects the key or the value. `/* ValueTag Key=... */` is reported (MFTV0005).
- The comment applies to the type of a `new` expression, and to the type of a local variable, including `foreach`, `for`, `using`, `out` and pattern variables. The tag of a `new` expression flows to a `var` local like any initializer.
- The elements of a collection initializer and the arguments of the constructor are checked: `new List</* ValueTag=OrderId */ Guid> { projectId }` is reported (MFTV0002).
- A comment on the variable itself wins over the comments in the type arguments of its declared type.

Comments that do not start with `ValueTag`, and `///` documentation comments, are ignored. A `ValueTag` comment that cannot be parsed, or that does not tag a local variable, is reported (MFTV0005) and does not tag anything. This includes a comment on a field, inside an initializer, at the end of a line, before a statement that declares no variable, in the type arguments of a field, a parameter, or a generic method, and in the type arguments of a type that is neither a collection, a dictionary, nor a wrapper, such as `Tuple<Guid, Guid>`. The code fix removes it.

## Collections, dictionaries, and wrappers

On a collection, a `Nullable<T>`, a `Task<T>`, a `ValueTask<T>`, a `Lazy<T>`, a span, or a memory, the tag describes the element or the value. It flows through indexers, `foreach`, LINQ, lambda parameters (including `IQueryable` expression trees), and any generic method whose signature preserves the element type:

````c#
[ValueTag("OrderId")] public List<Guid> OrderIds { get; }

OrderIds.Add(order.ProjectId);                            // MFTV0002
OrderIds.Where(id => id == order.ProjectId);              // MFTV0001
Load(OrderIds.Where(id => id != Guid.Empty).First());     // ok
````

Use `Key` and `Value` for dictionaries:

````c#
[ValueTag(Key = "OrderId", Value = "ProjectId")]
public Dictionary<Guid, Guid> ProjectIdByOrderId { get; }
````

Anonymous type properties take the tag of their initializer, so projections keep their tags.

## Naming conventions (opt-in)

When enabled, the analyzer infers tags from names, so most ids need no attribute:

````editorconfig
[*.cs]
taggedvalues.infer_tags_from_names = true
````

- A property or a field named `Id` takes the name of its type: `Id` on `Order` is an `"OrderId"`. When the member is inherited, it is also an id of every type between the receiver and the declaring type.
- A property, a field, or a parameter named `xxxId` takes its name with the first letter in upper case: `projectId` is a `"ProjectId"`. The `s_` prefix and leading underscores of fields are ignored: `s_projectId` and `_projectId` are both `"ProjectId"`.
- Explicit tags always win. Collections, indexers, and parameters named exactly `id` are not tagged by convention.

## What is reported

- Comparisons: `==`, `!=`, `<`, `<=`, `>`, `>=`, tuple equality, `Equals`, `CompareTo`, `EqualityComparer<T>.Equals`, `Comparer<T>.Compare`, `string.Equals(a, b, comparison)`
- Flows: assignments, object initializers, `with` expressions, field and property initializers, arguments (including `ref`, `out`, and generic arguments that must share a type), `return` and `yield return`
- Combined values: branches of `?:`, `??`, and switch expressions, and the elements of arrays and collection expressions
- Overrides and interface implementations whose tags differ from the base member
- Suggestions to tag a return value: when every value returned by a method, a local function, or a property getter has the same tag, but the return value is not tagged, so the callers lose the tag (only for tags written by the user, not inferred from a naming convention)
- Invalid annotations: empty tags, malformed comments, comments on something other than a local variable, `Key` and `Value` on something other than a dictionary, and assembly attributes that name a missing member

Every message names both declarations and their tags, so a build log is enough to act on. Code fixes change the tag of the target of a flow or of an override, add the suggested tag to a return value, and remove invalid or redundant annotations.

## Analyzer rules

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFTV0001` | TaggedValues | Do not compare values with different tags | Warning | ✔️ |
| `MFTV0002` | TaggedValues | Use a value with the tag the target expects | Warning | ✔️ |
| `MFTV0003` | TaggedValues | Combine only values with the same tag | Warning | ✔️ |
| `MFTV0004` | TaggedValues | Use the tags of the overridden or implemented member | Warning | ✔️ |
| `MFTV0005` | TaggedValues | Fix or remove the invalid value tag annotation | Warning | ✔️ |
| `MFTV0006` | TaggedValues | Add an explicit tag to disambiguate the conventional tag | Warning | ✔️ |
| `MFTV0007` | TaggedValues | Remove the redundant value tag | Info | ✔️ |
| `MFTV0008` | TaggedValues | Tag the return value with the tag of the returned values | Info | ✔️ |
<!-- analyzer-rules -->
