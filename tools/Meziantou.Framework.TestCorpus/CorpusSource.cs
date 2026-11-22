namespace Meziantou.Framework.TestCorpus;

/// <summary>Where a corpus is fetched from: a commit of its upstream repository, or its snapshot in a mirror.</summary>
/// <param name="Name">The name of the repository, for the messages.</param>
/// <param name="Url">The URL of the repository.</param>
/// <param name="Reference">What is fetched: the commit, or the tag of the snapshot.</param>
/// <param name="Commit">The commit that <paramref name="Reference"/> must be.</param>
/// <param name="IsSnapshot">Whether the commit is a snapshot, which contains only the files of the pin.</param>
public sealed record CorpusSource(string Name, string Url, string Reference, string Commit, bool IsSnapshot)
{
    /// <summary>Gets a commit of a repository of GitHub.</summary>
    /// <param name="repository">The repository (owner/name).</param>
    /// <param name="commit">The commit.</param>
    public static CorpusSource Upstream(string repository, string commit) => new(repository, ToUrl(repository), commit, commit, IsSnapshot: false);

    /// <summary>Gets the URL of a repository: a repository of GitHub (owner/name), else a URL or a path that git understands.</summary>
    public static string ToUrl(string repository)
    {
        if (repository.Contains("://", StringComparison.Ordinal) || Path.IsPathRooted(repository))
            return repository;

        return $"https://github.com/{repository}.git";
    }
}
