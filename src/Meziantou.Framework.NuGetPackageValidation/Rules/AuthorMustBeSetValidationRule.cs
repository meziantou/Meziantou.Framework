namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class AuthorMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var id = context.Package.NuspecReader.GetId();
        var authors = context.Package.NuspecReader.GetAuthors();

        if (string.IsNullOrWhiteSpace(authors))
        {
            context.ReportError(ErrorCodes.AuthorNotSet, "<authors> element is not set");
        }
        else if (string.Equals(authors, id, StringComparison.OrdinalIgnoreCase))
        {
            context.ReportError(ErrorCodes.DefaultAuthorSet, "<authors> element is not set explicitly (same as <id>)");
        }

        return Task.CompletedTask;
    }
}
