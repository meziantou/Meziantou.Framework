using Meziantou.Framework.NuGetPackageValidation.Internal;

namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class ProjectUrlBeSetValidationRule : NuGetPackageValidationRule
{
    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var projectUrl = context.Package.NuspecReader.GetProjectUrl();
        var repositoryUrl = context.Package.NuspecReader.GetRepositoryMetadata()?.Url;
        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(repositoryUrl))
        {
            context.ReportError(ErrorCodes.ProjectUrlNotSet, "Project url is not set");
        }
        else if (!await ShareHttpClient.Instance.IsUrlAccessible(projectUrl, context.CancellationToken).ConfigureAwait(false))
        {
            context.ReportError(ErrorCodes.ProjectUrlNotAccessible, $"Project url '{projectUrl}' is not accessible");
        }
    }
}
