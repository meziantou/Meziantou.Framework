using LibGit2Sharp;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class GitSubmoduleDependencyScanner : DependencyScanner
{
    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName(".gitmodules", ignoreCase: false);
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            using var repository = new Repository(Path.GetDirectoryName(context.FullPath));
            foreach (var module in repository.Submodules)
            {
                context.ReportDependency<GitSubmoduleDependencyScanner>(module.Url, module.WorkDirCommitId.Sha, DependencyType.GitSubmodule,
                    nameLocation: new NonUpdatableLocation(context),
                    versionLocation: new NonUpdatableLocation(context));
            }
        }
        catch (LibGit2SharpException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
