namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class RepositoryBranchMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var repo = context.Package.NuspecReader.GetRepositoryMetadata();
        if (repo == null || string.IsNullOrEmpty(repo.Branch))
        {
            context.ReportError(ErrorCodes.RepositoryBranchNotSet, "Repository branch is not set");
        }

        return Task.CompletedTask;
    }
}
