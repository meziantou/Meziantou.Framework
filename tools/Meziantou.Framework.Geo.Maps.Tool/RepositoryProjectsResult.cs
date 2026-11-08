namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>The keys of the regions whose project <see cref="RepositoryProjects.UpdateAsync"/> added, updated, removed or left as it was.</summary>
internal sealed record RepositoryProjectsResult(IReadOnlyList<string> Added, IReadOnlyList<string> Updated, IReadOnlyList<string> Removed, IReadOnlyList<string> Unchanged);
