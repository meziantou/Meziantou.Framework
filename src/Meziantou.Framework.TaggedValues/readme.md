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
- **Casing** does not matter: tags are compared ignoring case, so `"OrderId"` and `"orderId"` are the same tag.
- **Members of types you do not own**: `[assembly: ValueTag(typeof(Process), nameof(Process.Id), "ProcessId")]`. The tags also apply when the member is accessed through a derived type. Assembly attributes are read from referenced assemblies too, so a shared project can declare them once. On a generic type, `typeof(Box<>)` tags the member of every `Box<T>`, while `typeof(Box<int>)` only tags the member of `Box<int>`.

Tags are inherited from overridden members, from implemented interface members (including static abstract members, and members that a base type implements for a derived type), and from the parameters of a record primary constructor. An explicit cast through `object` drops the tag: `(Guid)(object)value`.

## Tagging local variables

C# does not allow attributes on local variables, so a local is tagged with a comment, either `/* ... */` or `// ...`. The comment accepts three forms:

| Comment | Equivalent attribute |
| -- | -- |
| `/* ValueTag=OrderId */` or `// ValueTag=OrderId` | `[ValueTag("OrderId")]` |
| `/* ValueTag=OrderId, ProjectId */` or `// ValueTag=OrderId, ProjectId` | `[ValueTag("OrderId", "ProjectId")]` |
| `/* ValueTag Key=OrderId Value=ProjectId */` or `// ValueTag Key=OrderId Value=ProjectId` | `[ValueTag(Key = "OrderId", Value = "ProjectId")]` |

Spaces around `=` and `,` are allowed. A tag cannot contain spaces, `=`, or `,`, so a `//` comment cannot continue with other text after the tags. In the dictionary form, `Key` and `Value` can be separated by a space or a comma, either one can be omitted, and each accepts a single tag.

A `/* ... */` comment must be part of the declaration: before the type, after the type, or next to the name. A comment before or after the type tags every variable of the declaration, and a comment next to a name, or right after the comma that precedes it, tags only that variable:

````c#
/* ValueTag=OrderId */ Guid id1 = ...;
Guid /* ValueTag=OrderId */ id2 = ...;
var id3 /* ValueTag=OrderId */ = ...;
using var /* ValueTag=OrderId */ id4 = ...;

Guid /* ValueTag=OrderId */ a = ..., b = ...;       // a and b are OrderIds
Guid c = ..., d /* ValueTag=ProjectId */ = ...;     // only d is a ProjectId
Guid e = ..., /* ValueTag=ProjectId */ f = ...;     // only f is a ProjectId

var /* ValueTag Key=OrderId Value=ProjectId */ projectIdByOrderId = new Dictionary<Guid, Guid>();
````

A `//` comment must be on a line before the statement, and tags every variable the statement declares. Other comments can sit between the `// ValueTag` comment and the statement, but a comment at the end of the previous line does not count. When a variable also has a `/* ... */` comment, that comment wins. A comment that another comment overrides for every variable it could tag, with other tags, is reported (MFTV0005):

````c#
// ValueTag=OrderId
Guid id1 = ...;

// ValueTag=OrderId
// The order being processed
Guid a = ..., b /* ValueTag=ProjectId */ = ...; // a is an OrderId, b is a ProjectId

// ValueTag=OrderId
Guid c /* ValueTag=ProjectId */ = ...; // MFTV0005 on the // comment, which does not tag anything

// ValueTag Key=OrderId Value=ProjectId
var projectIdByOrderId = new Dictionary<Guid, Guid>();
````

It also works for the variables of `foreach`, `for`, `out var`, patterns, and deconstructions. A `//` comment works for `foreach` and `for`, but not for `out var`, patterns, and deconstructions, whose statement is not a declaration:

````c#
// ValueTag=OrderId
foreach (var id in ids) { }
foreach (Guid /* ValueTag=OrderId */ id in ids) { }
for (int /* ValueTag=OrderIndex */ i = 0; i < count; i++) { }
if (TryGetId(out var /* ValueTag=OrderId */ id)) { }
if (value is Guid /* ValueTag=OrderId */ id) { }
var (orderId /* ValueTag=OrderId */, projectId) = GetIds();
````

A local without a comment takes the tag of its initializer, of the collection of a `foreach`, of the parameter of an `out var`, or of the value matched by a pattern. In a property pattern, a list pattern, a positional pattern, or a deconstruction, it takes the tag of the part it matches: `order is { Id: var id }` is an OrderId, and so is `id` in `foreach (var (id, projectId) in projectIdByOrderId)`, in `var (id, projectId) = (order.Id, order.ProjectId)`, or in `var (id, projectId) = order` when `Order` is a record or has a `Deconstruct` method with tagged `out` parameters. The element of a tuple, such as `pair.Item1`, takes the tag of the element of the tuple literal it was created with:

````c#
var id = order.Id;    // id is an OrderId
Load(id);             // ok
id = order.ProjectId; // MFTV0002

var pair = (order.Id, order.ProjectId);
Load(pair.Item1);     // ok
Load(pair.Item2);     // MFTV0002
````

When the comment disagrees with the initializer, the collection of a `foreach`, the parameter of an `out var`, the value matched by a pattern, or the deconstructed value, the comment wins and the value is reported (MFTV0002). The code fix changes the tag in the comment. When the comment also tags other variables of the declaration, the code fix adds a comment next to the name of the variable instead.

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

Comments that do not start with `ValueTag`, prose that starts with `ValueTag` but contains several words and no `=`, such as `// ValueTag is not used here`, and `///` documentation comments, are ignored. A `ValueTag` comment that cannot be parsed, or that does not tag a local variable, is reported (MFTV0005) and does not tag anything. This includes a comment on a field, inside an initializer, at the end of a line, before a statement that declares no variable, in the type arguments of a field, a parameter, or a generic method, and in the type arguments of a type that is neither a collection, a dictionary, nor a wrapper, such as `Tuple<Guid, Guid>`. The code fix removes it.

## Collections, dictionaries, and wrappers

On a collection, a `Nullable<T>`, a `Task<T>`, a `ValueTask<T>`, a `Lazy<T>`, a span, or a memory, the tag describes the element or the value. It flows through indexers, `foreach`, LINQ, lambda parameters (including `IQueryable` expression trees), `ConfigureAwait`, `WithCancellation`, and any generic method whose signature preserves the element type. A string is not a collection: the tag of a string describes the string, not its characters:

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

Anonymous type properties take the tag of their initializer, so projections keep their tags. A collection initializer takes the tags of the elements it adds: `new List<Guid> { order.Id }` holds OrderIds, and `new Dictionary<Guid, Guid> { [order.Id] = order.ProjectId }` maps OrderIds to ProjectIds.

## Arithmetic and operators

Built-in arithmetic on numbers, including the numbers of the framework such as `Int128`, `Half`, and `BigInteger`, keeps the tag, so `orderId + 1 == projectId` is still reported:

- `+` and `-` keep the tag of their operands. Adding values with different tags is reported (MFTV0003): `orderId + projectId`.
- `*` and `/` keep the tag when the other operand is not tagged: `price * 2` is a price, but `meters / seconds` is not tagged.
- Unary `+` and `-`, `++`, `--`, `+=`, and `-=` keep the tag.
- `%`, bitwise operators, shifts, and string concatenations create another kind of value, so the result is not tagged.

A user-defined operator or conversion creates a new value, so it does not keep the tag of its operands: `dueDate - startDate` is a `TimeSpan`, not a due date. Tag the return value of the operator to tag its result, and its parameters to check its operands, including the operands of a comparison operator and the target of a compound assignment such as `+=`:

````c#
[return: ValueTag("Meters")]
public static Distance operator +([ValueTag("Meters")] Distance left, [ValueTag("Meters")] Distance right) => ...;
````

## Naming conventions (opt-in)

When enabled, the analyzer infers tags from names, so most ids need no attribute:

````editorconfig
[*.cs]
taggedvalues.infer_tags_from_names = true
````

- A property or a field named `Id` takes the name of its declaring type: `Id` on `Order` is an `"OrderId"`, and `Id` on the interface `IEntity` is an `"EntityId"`. When the member is inherited, it is also an id of every type between the receiver and the declaring type, and when it overrides or implements an `Id`, it is also an id of the base type or of the interface.
- A property, a field, or a parameter named `xxxId` takes its name: `projectId` is a `"projectId"`, which is the same tag as `"ProjectId"` as tags are compared ignoring case. The `s_`, `t_`, and `m_` prefixes and leading underscores of fields are ignored: `s_projectId` and `_projectId` are both `"projectId"`.
- A parameter named `id` takes the name of its type: `void Load(Order id)` is an `"OrderId"`, and `void Load(OrderId id)` is an `"OrderId"` too. A parameter named `id` whose type is a primitive, a `string`, a `Guid`, an enum, or another type of the `System` namespace such as `Int128` or `DateTimeOffset` is not tagged, as its type does not say what it identifies.
- Explicit tags always win. Collections and indexers are not tagged by convention. Conventions only apply to the declarations of the project, not to the declarations of referenced projects or assemblies.

## Strict mode (opt-in)

By default, untagged values are never reported. Strict mode also reports a tagged value mixed with an untagged value (MFTV0009), so every value that interacts with a tagged value must be tagged too:

````editorconfig
[*.cs]
taggedvalues.strict = true
````

````c#
[ValueTag("OrderId")] public Guid Id { get; set; } = Guid.NewGuid(); // ok, a new value

void Load(Guid id) { }
Guid _other;

_ = order.Id == _other;     // MFTV0009, compared with an untagged value
order.Id = _other;          // MFTV0009, an untagged value flows to a tagged declaration
Load(order.Id);             // MFTV0009, a tagged value flows to an untagged declaration
_ = order.Id == Guid.Empty; // ok
````

- Default values, `null`, constants, `Guid.Empty`, `string.Empty`, and `new Guid()` are always allowed.
- New values, such as `Guid.NewGuid()`, `new Guid(bytes)`, or `Guid.Parse(text)`, can flow to a tagged declaration. Values read from an untagged field, property, parameter, local, or array element, or returned by an untagged method of your code, cannot. A `?:`, `??`, or switch expression is a new value only when each of its branches is a new value or an allowed value.
- A tagged value can flow to a declaration of a referenced assembly or project, or typed `object`, `dynamic`, a type parameter, or a collection of them such as `params object[]`, as they cannot be tagged. A local initialized with a tagged value takes its tag, so it is not reported. The elements of a tuple cannot be tagged either, so an untagged element of a tuple is not reported.
- The code fix adds the tag to the untagged declaration.

## What is reported

- Comparisons: `==`, `!=`, `<`, `<=`, `>`, `>=`, tuple equality, `Equals`, `CompareTo`, `EqualityComparer<T>.Equals`, `Comparer<T>.Compare`, `string.Equals(a, b, comparison)`, `a.Equals(b, comparison)`
- Flows: assignments, deconstructions, object initializers, `with` expressions, field and property initializers, arguments (including `ref`, `out`, each argument of `params`, and generic arguments that must share a type), `return` and `yield return`. An `out` argument flows from the parameter to the variable.
- Combined values: operands of `+` and `-`, branches of `?:`, `??`, and switch expressions, and the elements of arrays, collection expressions, and collection initializers. The arguments of `params` and the elements of a collection of `object` are not combined, as they hold values of different kinds, such as the arguments of a log message.
- Overrides and interface implementations whose tags differ from the base member
- In strict mode, tagged values mixed with untagged values
- Suggestions to tag a return value: when every value returned by a method, a local function, or a property getter has the same tag, but the return value is not tagged, so the callers lose the tag (only for tags written by the user, not inferred from a naming convention, and not for overrides and implementations such as `ToString`, which get their tags from the base member)
- Invalid annotations: empty tags, malformed comments, comments on something other than a local variable, `Key` and `Value` on something other than a dictionary, `[field: ValueTag]` on a property, whose backing field is not analyzed, and assembly attributes that name a missing member

Every message names both declarations and their tags, so a build log is enough to act on. Code fixes change the tag of the target of a flow or of an override, add the suggested tag to a return value, and remove invalid or redundant annotations. The code fix does not change a tag inherited from a base member or declared by an `[assembly: ValueTag]` attribute, and it changes every part of a partial member.

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
| `MFTV0009` | TaggedValues | Do not mix tagged values with untagged values | Warning | ✔️ |
<!-- analyzer-rules -->
