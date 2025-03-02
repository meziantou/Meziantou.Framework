namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class PackageIdAvailableOnNuGetOrgValidationRule : NuGetPackageValidationRule
{
    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var packageIdentity = await context.Package.GetIdentityAsync(context.CancellationToken).ConfigureAwait(false);
        var packageId = packageIdentity.Id;

        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://www.nuget.org/packages/" + Uri.EscapeDataString(packageId));
            using var response = await context.SendHttpRequestAsync(request, context.CancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 200 and <= 400)
            {
                // The package exists
                context.ReportError(ErrorCodes.PackageIdExistsOnNuGetOrg, $"The package '{packageId}' already exists on nuget.org");
                return;
            }

            if ((int)response.StatusCode >= 500)
                continue;

            context.ReportError(ErrorCodes.CannotCheckPackageIdExistsOnNuGetOrg, $"Cannot check if the package '{packageId}' exists on nuget.org");
            return;
        }
    }
}
