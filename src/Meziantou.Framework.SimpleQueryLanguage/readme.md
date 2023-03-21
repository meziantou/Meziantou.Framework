# Meziantou.Framework.SimpleQueryLanguage

Syntax inspired from [KQL](https://docs.microsoft.com/en-us/sharepoint/dev/general-development/keyword-query-language-kql-syntax-reference?WT.mc_id=DT-MVP-5003978) ([specification](https://docs.microsoft.com/en-us/openspecs/sharepoint_protocols/ms-kql/51e2c39a-b6ad-44a7-a4bf-a04ea03ffafd))

# Usage

````c#
record Person(string FullName, DateTime DateOfBirth);

var collection = new List<Person>(');

var queryBuilder = new QueryBuilder<Person>();

queryBuilder.AddHandler<string>("name", (obj, value) => obj.FullName.Contains(value, StringComparison.OrdinalIgnoreCase));
queryBuilder.AddRangeHandler<int>("age", (obj, value) => (DateTime.UtcNow - obj.DateOfBirth)..Contains(value, StringComparison.OrdinalIgnoreCase));

var query = queryBuilder.Build("name:sample query");
query.Evaluate(new() { StringValue = "sample" })
````

# Supported syntax

- Logical operators `NOT`, `AND`, `OR`
- Priority using `(`, `)`
- `AddHandler`: supported operators: `:`
- `AddRangeHandler`: supported operators: `:`, `<`, `<=`, `>`, `>=`, `..` (range)
- `SetTextFilterHandler` matches all non-bound filters
- Special values: `today`, `yesterday`, `this week`, `this month`, `last month`, `this year`, `last year`

Examples:
- `name:john` or `name=john`
- `name:"john doe"`
- `name<>john` or `-name:john` or `NOT name:john`
- `(name:"john doe" OR name:jane) AND age>21`
- `created:"this week"`
- `age:13..19` (lower and upper bound are included)
- `age>=21`
- `is_open:true free form text`
- `is_open:true AND NOT "free form text"`

