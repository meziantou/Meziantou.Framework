---
title: Database queries
description: Build expression trees from queries so Entity Framework Core and other LINQ providers filter in the database.
---

[ExpressionQueryBuilder\<T\>](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQueryBuilder{T}) reads the same syntax as [QueryBuilder\<T\>](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}), but its handlers are expressions. The query
becomes an `Expression<Func<T, bool>>` that Entity Framework Core translates to SQL, so the database does the
filtering:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/ExpressionQueryBuilderTests.cs;region=ExpressionQueryBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Define a query builder for your entity
var queryBuilder = new ExpressionQueryBuilder<Customer>();

// Add handlers for string properties (uses Contains for partial matching).
// Case sensitivity follows the provider: the database collation for EF Core, ordinal for LINQ to Objects.
// Pass a StringComparison to force one, but note that EF Core cannot translate it.
queryBuilder.AddHandler("name", customer => customer.FullName);

// Add handlers for comparable types (supports range and comparison operators)
queryBuilder.AddHandler<int>("age", customer => customer.Age);
queryBuilder.AddHandler<DateTime>("created", customer => customer.CreatedAt);

// Optional: handle free-text search across multiple fields
queryBuilder.SetFreeTextHandler(text =>
    customer => customer.FullName.Contains(text) || customer.Email.Contains(text));

// Build the query
var query = queryBuilder.Build("name:John AND age>=21");

// Apply to an IQueryable (e.g., an EF Core DbSet)
var customers = new[]
{
    new Customer("John Doe", "john@example.com", 30, new DateTime(2024, 1, 1)),
    new Customer("Johnny Smith", "johnny@example.com", 18, new DateTime(2024, 1, 1)),
    new Customer("Jane Doe", "jane@example.com", 40, new DateTime(2024, 1, 1)),
};
var results = query.Apply(customers.AsQueryable()).ToList();

Assert.Equal(["John Doe"], results.Select(customer => customer.FullName));
```
<!-- endref -->

with the following entity type:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/ExpressionQueryBuilderTests.cs;region=ExpressionQueryBuilder_Customer;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed record Customer(string FullName, string Email, int Age, DateTime CreatedAt);
```
<!-- endref -->

[Apply](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQuery{T}.Apply) adds a `Where` to any `IQueryable<T>`. On an Entity Framework Core `DbSet`, run the result as usual, for
instance with `await query.Apply(dbContext.Customers).ToListAsync()`.

## Kinds of handlers

| Registration | Generates | Operators |
| --- | --- | --- |
| `AddHandler(key, x => x.Text)` | `x.Text != null && x.Text.Contains(value)` | `:`, `=`, `<>` |
| `AddHandler<TValue>(key, x => x.Value)` for a type that can be ordered | Comparisons with the parsed value | All, and ranges |
| `AddHandler<TValue>(key, x => x.Value)` for other types | `x.Value == value` | `:`, `=`, `<>` |
| `AddHandler(key, value => …)` | The expression your function returns for the value | `:`, `=`, `<>` |
| `AddHandler(key, op, value => …)` | The expression your function returns, for one operator | The one you give |

- A string handler uses `string.Contains(string)`, so its case sensitivity is the provider's: the collation of the
  column for Entity Framework Core, ordinal in memory. Passing a `StringComparison` makes the query run in memory
  only: Entity Framework Core cannot translate that overload.
- Numbers, dates (`DateTime`, `DateTimeOffset`, `DateOnly`), `TimeOnly`, `TimeSpan`, `decimal` and the other types
  that define `<` can be ordered, and their nullable versions. Enums, `bool` and `Guid` support equality only.
- `<>` negates the `:` handler of the key when no handler is registered for `<>` itself.
- A value that cannot be parsed generates a condition that is always false.

The values of the query are not inserted in the expression as constants. They are read from an object, like a captured
variable, which LINQ providers such as Entity Framework Core turn into query parameters.

## Custom expressions

For a key that does not map to one property, return the expression yourself. The function runs when the query is
built, with the value as written in the query:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/ExpressionQueryBuilderTests.cs;region=ExpressionQueryBuilder_CustomHandler;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new ExpressionQueryBuilder<Customer>();
queryBuilder.AddHandler("is", value => value.Equals("adult", StringComparison.OrdinalIgnoreCase)
    ? customer => customer.Age >= 18
    : customer => false);

var customers = new[]
{
    new Customer("John Doe", "john@example.com", 30, new DateTime(2024, 1, 1)),
    new Customer("Johnny Smith", "johnny@example.com", 16, new DateTime(2024, 1, 1)),
};

var query = queryBuilder.Build("is:adult");
Assert.Equal(["John Doe"], query.Apply(customers.AsQueryable()).Select(customer => customer.FullName));

// <> negates the handler of ':'
query = queryBuilder.Build("is<>adult");
Assert.Equal(["Johnny Smith"], query.Apply(customers.AsQueryable()).Select(customer => customer.FullName));
```
<!-- endref -->

Write expressions the provider can translate: Entity Framework Core cannot translate a call to a method of your own.

## Free text and unknown keys

- [SetFreeTextHandler](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQueryBuilder{T}.SetFreeTextHandler) receives the text of a term without a key and returns an expression, such as a `Contains` on
  several columns. Without it, free text matches nothing.
- [SetUnhandledPropertyHandler](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQueryBuilder{T}.SetUnhandledPropertyHandler) receives the key, the operator and the value of a term whose key has no handler. Return
  an expression, or `null` to search the term as free text, which is also what happens without this handler.

## The predicate

[Predicate](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQuery{T}.Predicate) returns the expression the query built, to combine it with your own filters or to compile it for objects
in memory. It is `null` for an empty query, which matches everything:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/ExpressionQueryBuilderTests.cs;region=ExpressionQuery_Predicate;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new ExpressionQueryBuilder<Customer>();
queryBuilder.AddHandler<int>("age", customer => customer.Age);

// An empty query has no predicate: Apply returns the source unchanged
Assert.Null(queryBuilder.Build("").Predicate);

var predicate = queryBuilder.Build("age:18..65").Predicate;
Assert.NotNull(predicate);

var isWorkingAge = predicate.Compile();
Assert.True(isWorkingAge(new Customer("John Doe", "john@example.com", 30, new DateTime(2024, 1, 1))));
```
<!-- endref -->

[Build](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQueryBuilder{T}.Build) throws a [QueryTooComplexException](cref:Meziantou.Framework.SimpleQueryLanguage.QueryTooComplexException) for a query too complex to evaluate, as [QueryBuilder\<T\>.Build](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.Build) does; see
[Queries from users](04-InMemoryQueries.md#queries-from-users).
