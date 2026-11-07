---
title: Getting started
description: Install the package, register handlers for the keys of your search, and filter a list with a query.
---

## Install the package

Add the `Meziantou.Framework.SimpleQueryLanguage` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.SimpleQueryLanguage" />

The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework.SimpleQueryLanguage;`.

## Filter a list

The examples search issues of this type:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_Issue;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed record Issue(string Title, string Author, bool IsOpen, int Comments, DateTime CreatedAt);
```
<!-- endref -->

Create a [QueryBuilder\<Issue\>](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}), register what each key means, and build the query the user typed:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new QueryBuilder<Issue>();
queryBuilder.AddHandler("author", (issue, value) => issue.Author.Equals(value, StringComparison.OrdinalIgnoreCase));
queryBuilder.AddHandler("is", "open", issue => issue.IsOpen);
queryBuilder.AddHandler("is", "closed", issue => !issue.IsOpen);
queryBuilder.AddRangeHandler<int>("comments", (issue, range) => range.IsInRange(issue.Comments));
queryBuilder.SetTextFilterHandler((issue, text) => issue.Title.Contains(text, StringComparison.OrdinalIgnoreCase));

var issues = new[]
{
    new Issue("Crash on startup", "alice", IsOpen: true, Comments: 5, new DateTime(2026, 1, 10)),
    new Issue("Crash when saving", "bob", IsOpen: true, Comments: 0, new DateTime(2026, 2, 3)),
    new Issue("Typo in the readme", "alice", IsOpen: false, Comments: 1, new DateTime(2026, 3, 15)),
};

var query = queryBuilder.Build("is:open author:alice crash");
Assert.Equal(["Crash on startup"], issues.Where(query.Evaluate).Select(issue => issue.Title));
```
<!-- endref -->

- `author:alice` calls the `author` handler with `alice`.
- `is:open` matches the handler registered for the value `open` of the key `is`.
- `comments` is a range handler, so it also accepts `comments>=2` or `comments:1..5`.
- `crash` has no key: it is free text, passed to the text filter.
- Terms separated by spaces must all match.

[Build](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.Build) parses the query once. Keep the [Query\<Issue\>](cref:Meziantou.Framework.SimpleQueryLanguage.Query{T}) and call [Evaluate](cref:Meziantou.Framework.SimpleQueryLanguage.Query{T}.Evaluate) for each object.

## Filter a database query

To filter an Entity Framework Core `DbSet`, use [ExpressionQueryBuilder\<T\>](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQueryBuilder{T}): its handlers are expressions that the
database can run. See [Database queries](05-DatabaseQueries.md).

## Next steps

- [Query syntax](03-QuerySyntax.md): everything users can type.
- [In-memory queries](04-InMemoryQueries.md): the kinds of handlers, free text and unknown keys.
- [Values and dates](06-ValuesAndDates.md): how values are parsed, and the date keywords.
