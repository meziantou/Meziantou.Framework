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

        if (!string.IsNullOrWhiteSpace(projectUrl))
        {
            if (!Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri))
            {
                context.ReportError(ErrorCodes.ProjectUrlNotAccessible, $"Project url '{projectUrl}' is not valid");
            }
            else if (!await ShareHttpClient.Instance.IsUrlAccessible(uri, context.CancellationToken).ConfigureAwait(false))
            {
                context.ReportError(ErrorCodes.ProjectUrlNotAccessible, $"Project url '{projectUrl}' is not accessible");
            }
        }

        if (!string.IsNullOrWhiteSpace(repositoryUrl))
        {
            if (Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                if (!await ShareHttpClient.Instance.IsUrlAccessible(uri, context.CancellationToken).ConfigureAwait(false))
                {
                    context.ReportError(ErrorCodes.ProjectUrlNotAccessible, $"Repository url '{projectUrl}' is not accessible");
                }
            }
        }
    }
}
