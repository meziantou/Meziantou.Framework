namespace Meziantou.Framework.DependencyScanning.Tool;

/// <summary>A GitHub Actions lock file to regenerate, with the workflows it locks.</summary>
internal sealed record ActionsLockFileUpdate(FullPath LockFile, FullPath RepositoryRoot, string[] Workflows);
