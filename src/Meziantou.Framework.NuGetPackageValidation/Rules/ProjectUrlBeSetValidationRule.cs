namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class ProjectUrlBeSetValidationRule : NuGetPackageValidationRule
{
    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var projectUrl = context.Package.NuspecReader.GetProjectUrl();
        var repositoryUrl = context.Package.NuspecReader.GetRepositoryMetadata()?.Url;

        if (string.IsNullOrWhiteSpace(projectUrl))
        {
            // A missing repository url is reported by RepositoryInfoMustBeSetValidationRule
            context.ReportError(ErrorCodes.ProjectUrlNotSet, "Project url is not set");
        }
        else if (!Uri.TryCreate(projectUrl, UriKind.Absolute, out var projectUri) || !IsHttpUri(projectUri))
        {
            context.ReportError(ErrorCodes.ProjectUrlNotAccessible, $"Project url '{projectUrl}' is not valid");
        }
        else if (!await context.IsUrlAccessible(projectUri, context.CancellationToken).ConfigureAwait(false))
        {
            context.ReportError(ErrorCodes.ProjectUrlNotAccessible, $"Project url '{projectUrl}' is not accessible");
        }

        if (!string.IsNullOrWhiteSpace(repositoryUrl) && Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri) && IsHttpUri(repositoryUri))
        {
            if (!await context.IsUrlAccessible(repositoryUri, context.CancellationToken).ConfigureAwait(false))
            {
                context.ReportError(ErrorCodes.ProjectUrlNotAccessible, $"Repository url '{repositoryUrl}' is not accessible");
            }
        }

        static bool IsHttpUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
