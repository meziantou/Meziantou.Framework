namespace Meziantou.Framework.SimpleQueryLanguage.Benchmarks;

public sealed record Issue(int Id, string Title, string Author, IssueState State, int Comments, DateTime CreatedAt, Guid ProjectId, bool IsPinned);
