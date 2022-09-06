namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class RepositoryInfoMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var repo = context.Package.NuspecReader.GetRepositoryMetadata();
        if (repo == null)
        {
            context.ReportError(ErrorCodes.RepositoryNotSet, "Repository is not set");
            return Task.CompletedTask;
        }

        else if (string.IsNullOrEmpty(repo.Type))
        {
            context.ReportError(ErrorCodes.RepositoryTypeNotSet, "Repository type is not set");
        }

        if (string.IsNullOrEmpty(repo.Url))
        {
            context.ReportError(ErrorCodes.RepositoryUrlNotSet, "Repository URL is not set");
        }

        if (string.IsNullOrEmpty(repo.Commit))
        {
            context.ReportError(ErrorCodes.RepositoryCommitNotSet, "Repository commit is not set");
        }

        if (string.IsNullOrEmpty(repo.Branch))
        {
            context.ReportError(ErrorCodes.RepositoryBranchNotSet, "Repository branch is not set");
        }

        return Task.CompletedTask;
    }
}
