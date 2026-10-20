# Meziantou.Framework.SimpleQueryLanguage

Syntax inspired from [KQL](https://docs.microsoft.com/en-us/sharepoint/dev/general-development/keyword-query-language-kql-syntax-reference?WT.mc_id=DT-MVP-5003978) ([specification](https://docs.microsoft.com/en-us/openspecs/sharepoint_protocols/ms-kql/51e2c39a-b6ad-44a7-a4bf-a04ea03ffafd))

# Usage

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new QueryBuilder<Person>();
queryBuilder.AddHandler<string>("name", (obj, value) => obj.FullName.Contains(value, StringComparison.OrdinalIgnoreCase));
queryBuilder.AddRangeHandler<int>("age", (obj, value) => value.IsInRange((int)(DateTime.UtcNow - obj.DateOfBirth).TotalDays / 365));

var query = queryBuilder.Build("name:john AND age>=18");
Assert.True(query.Evaluate(new Person("John Doe", new DateTime(2000, 1, 1))));
Assert.False(query.Evaluate(new Person("Jane Doe", new DateTime(2000, 1, 1))));
Assert.False(query.Evaluate(new Person("Johnny Doe", DateTime.UtcNow.AddYears(-10))));
```
<!-- endref -->

with the following type:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_Person;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed record Person(string FullName, DateTime DateOfBirth);
```
<!-- endref -->

# Syntax

- Logical operators `NOT`, `AND`, `OR`
- Priority using `(`, `)`
- `AddHandler`: supported operators: `:`
- `AddRangeHandler`: supported operators: `:`, `<`, `<=`, `>`, `>=`, `..` (range). Use `*` for an open bound: `10..*` is `>=10` and `*..10` is `<=10`
- `SetTextFilterHandler` matches all non-bound filters
- Special values: `today`, `yesterday`, `this week`, `this month`, `last month`, `this year`, `last year`. The two-word values also accept an underscore, which needs no quotes: `this_week`, `this_month`, `last_month`, `this_year`, `last_year`.
- Relative dates: `@today`, optionally followed by an offset in days (`d`) or weeks (`w`): `@today-1w`, `@today+3d`. The value is the start of that day in UTC and can be used wherever a date is expected, including range bounds.
- Special values and relative dates are resolved when the query is built, so rebuild a query that is kept across days.

Examples:
- `name:john` or `name=john`
- `name:"john doe"`
- `name<>john` or `-name:john` or `NOT name:john`
- `(name:"john doe" OR name:jane) AND age>21`
- `created:"this week"` or `created:this_week`
- `age:13..19` (lower and upper bound are included)
- `stars:10..*` or `created:*..2024-12-31` (open bound)
- `created>@today-1w` or `created:@today-2w..@today`
- `age>=21`
- `is_open:true free form text`
- `is_open:true AND NOT "free form text"`

# Expression-based queries for EF Core

The `ExpressionQueryBuilder<T>` class creates `Expression<Func<T, bool>>` objects that can be translated to SQL by Entity Framework Core, allowing you to apply query language filters directly to database queries.

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

`Apply` works the same way on an EF Core `DbSet`: `await query.Apply(dbContext.Customers).ToListAsync()`.

The `ExpressionQueryBuilder<T>` supports the same query syntax as `QueryBuilder<T>`, including:
- String matching with `:` operator
- Comparison operators: `<`, `<=`, `>`, `>=`
- Range syntax: `age:18..25`
- Logical operators: `AND`, `OR`, `NOT`
- Parentheses for grouping
