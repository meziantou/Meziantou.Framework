using System.Linq.Expressions;

#pragma warning disable CA5394 // The data of the benchmarks is not security sensitive

namespace Meziantou.Framework.SimpleQueryLanguage.Benchmarks;

internal static class BenchmarkData
{
    public static readonly Guid ProjectId = new("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    public static IReadOnlyDictionary<string, string> Queries { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Single"] = "author:meziantou",
        ["Typical"] = "author:meziantou is:open comments>=2 performance",
        ["Mixed"] = "(author:meziantou OR author:john) AND -state:closed created:2024-01-01..2024-12-31 project:3f2504e0-4f89-11d3-9a0c-0305e82c3301 \"memory leak\" pinned:true",
        ["Long"] = string.Join(' ', Enumerable.Range(0, 40).Select(i => (i % 4) switch
        {
            0 => "author:user" + i,
            1 => "comments>" + i,
            2 => "-state:draft",
            _ => "word" + i,
        })),
        ["Or"] = string.Join(" OR ", Enumerable.Range(0, 20).Select(i => "author:user" + i)),
        ["Range"] = "comments:2..5 created>=2024-01-01",
    };

    public static Issue[] CreateIssues(int count)
    {
        var random = new Random(42);
        var authors = new[] { "meziantou", "john", "jane", "user1", "user5", "user12" };
        var words = new[] { "performance", "memory leak", "crash", "documentation", "feature", "bug" };
        var issues = new Issue[count];
        for (var i = 0; i < issues.Length; i++)
        {
            issues[i] = new Issue(
                i,
                "Issue " + i + " about " + words[random.Next(words.Length)] + " and " + words[random.Next(words.Length)],
                authors[random.Next(authors.Length)],
                (IssueState)random.Next(3),
                random.Next(10),
                new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(random.Next(800)),
                random.Next(2) == 0 ? ProjectId : Guid.Empty,
                random.Next(5) == 0);
        }

        return issues;
    }

    public static QueryBuilder<Issue> CreateQueryBuilder()
    {
        var builder = new QueryBuilder<Issue>();
        builder.AddHandler<string>("author", (issue, value) => issue.Author.Equals(value, StringComparison.OrdinalIgnoreCase));
        builder.AddHandler("is", "open", issue => issue.State is IssueState.Open);
        builder.AddHandler("is", "closed", issue => issue.State is IssueState.Closed);
        builder.AddHandler<IssueState>("state", (issue, value) => issue.State == value);
        builder.AddRangeHandler<int>("comments", (issue, range) => range.IsInRange(issue.Comments));
        builder.AddRangeHandler<DateTime>("created", (issue, range) => range.IsInRange(issue.CreatedAt));
        builder.AddHandler<Guid>("project", (issue, value) => issue.ProjectId == value);
        builder.AddHandler<bool>("pinned", (issue, value) => issue.IsPinned == value);
        builder.SetTextFilterHandler((issue, text) => issue.Title.Contains(text, StringComparison.OrdinalIgnoreCase));
        return builder;
    }

    public static ExpressionQueryBuilder<Issue> CreateExpressionQueryBuilder()
    {
        var builder = new ExpressionQueryBuilder<Issue>();
        builder.AddHandler("author", issue => issue.Author);
        builder.AddHandler("is", value => value.Equals("open", StringComparison.OrdinalIgnoreCase) ? issue => issue.State == IssueState.Open : issue => issue.State == IssueState.Closed);
        builder.AddHandler("state", issue => issue.State);
        builder.AddHandler("comments", issue => issue.Comments);
        builder.AddHandler("created", issue => issue.CreatedAt);
        builder.AddHandler("project", issue => issue.ProjectId);
        builder.AddHandler("pinned", issue => issue.IsPinned);
        builder.SetFreeTextHandler(CreateFreeTextExpression);
        return builder;

        static Expression<Func<Issue, bool>> CreateFreeTextExpression(string text) => issue => issue.Title.Contains(text);
    }
}
