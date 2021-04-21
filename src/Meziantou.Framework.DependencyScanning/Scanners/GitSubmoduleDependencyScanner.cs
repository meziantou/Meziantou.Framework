using System;
using System.IO;
using System.Threading.Tasks;
using LibGit2Sharp;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners
{
    public sealed class GitSubmoduleDependencyScanner : DependencyScanner
    {
        protected override bool ShouldScanFileCore(CandidateFileContext context)
        {
            return context.FileName.Equals(".gitmodules", StringComparison.Ordinal);
        }

        public override async ValueTask ScanAsync(ScanFileContext context)
        {
            try
            {
                using var repository = new Repository(Path.GetDirectoryName(context.FullPath));
                foreach (var module in repository.Submodules)
                {
                    await context.ReportDependency(new Dependency(module.Url, module.WorkDirCommitId.Sha, DependencyType.GitSubmodule, new NonUpdatableLocation(context.FullPath))).ConfigureAwait(false);
                }
            }
            catch (LibGit2SharpException)
            {
            }
        }
    }
}
