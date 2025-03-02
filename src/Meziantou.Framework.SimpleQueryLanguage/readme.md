# Meziantou.Framework.SimpleQueryLanguage

Syntax inspired from [KQL](https://docs.microsoft.com/en-us/sharepoint/dev/general-development/keyword-query-language-kql-syntax-reference?WT.mc_id=DT-MVP-5003978) ([specification](https://docs.microsoft.com/en-us/openspecs/sharepoint_protocols/ms-kql/51e2c39a-b6ad-44a7-a4bf-a04ea03ffafd))

# Usage

````c#
var collection = new List<Person>();

var queryBuilder = new QueryBuilder<Person>();
queryBuilder.AddHandler<string>("name", (obj, value) => obj.FullName.Contains(value, StringComparison.OrdinalIgnoreCase));
queryBuilder.AddRangeHandler<int>("age", (obj, value) => value.IsInRange((int)(DateTime.UtcNow - obj.DateOfBirth).TotalDays / 365));

var query = queryBuilder.Build("name:sample query");
query.Evaluate(new Person("John Doe", new DateTime(2000, 1, 1)));

record Person(string FullName, DateTime DateOfBirth);
````

# Syntax

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
