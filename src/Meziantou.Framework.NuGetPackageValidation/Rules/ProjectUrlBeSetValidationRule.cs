namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class ProjectUrlBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var projectUrl = context.Package.NuspecReader.GetProjectUrl();
        var repositoryUrl = context.Package.NuspecReader.GetRepositoryMetadata()?.Url;
        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(repositoryUrl))
        {
            context.ReportError(ErrorCodes.ProjectUrlNotSet, "Project url is not set");
        }

        return Task.CompletedTask;
    }
}
