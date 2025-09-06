using LibGit2Sharp;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class GitSubmoduleDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.GitReference];

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
                context.ReportDependency(this, module.Url, module.WorkDirCommitId.Sha, DependencyType.GitReference,
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
