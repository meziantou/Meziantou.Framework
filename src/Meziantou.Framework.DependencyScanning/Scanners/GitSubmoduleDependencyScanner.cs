using System;
using System.IO;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning
{
    public sealed class GitSubmoduleDependencyScanner : DependencyScanner
    {
        public override bool ShouldScanFile(CandidateFileContext context)
        {
            return context.FileName.Equals(".gitmodules", StringComparison.Ordinal);
        }

        public override async ValueTask ScanAsync(ScanFileContext context)
        {
            using var repository = new LibGit2Sharp.Repository(Path.GetDirectoryName(context.FullPath));
            foreach (var module in repository.Submodules)
            {
                await context.ReportDependency(new Dependency(module.Url, module.WorkDirCommitId.Sha, DependencyType.GitSubmodule, new NonUpdatableLocation(context.FullPath))).ConfigureAwait(false);
            }
        }
    }
}
