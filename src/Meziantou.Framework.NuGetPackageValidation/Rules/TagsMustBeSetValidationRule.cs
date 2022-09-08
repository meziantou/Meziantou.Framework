namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class TagsMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var tags = context.Package.NuspecReader.GetTags();
        if (string.IsNullOrEmpty(tags))
        {
            context.ReportError(ErrorCodes.TagsNotSet, "The package tags are not set");
        }
        else if (tags.Length > 4000)
        {
            context.ReportError(ErrorCodes.TagsTooLong, "Tags are limited to 4000 characters");
        }

        return Task.CompletedTask;
    }
}
