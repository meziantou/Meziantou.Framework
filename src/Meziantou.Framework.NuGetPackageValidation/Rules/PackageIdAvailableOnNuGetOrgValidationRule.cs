using System.Net;

namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class PackageIdAvailableOnNuGetOrgValidationRule : NuGetPackageValidationRule
{
    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var packageIdentity = await context.Package.GetIdentityAsync(context.CancellationToken).ConfigureAwait(false);
        var packageId = packageIdentity.Id;

        // The registration index is the documented way to check if a package id is registered.
        // The package page (https://www.nuget.org/packages/<id>) answers 404 to HEAD requests whether or not the package exists.
        // https://learn.microsoft.com/en-us/nuget/api/registration-base-url-resource?WT.mc_id=DT-MVP-5003978
        var url = "https://api.nuget.org/v3/registration5-semver1/" + Uri.EscapeDataString(packageId.ToLowerInvariant()) + "/index.json";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await context.SendHttpRequestAsync(request, context.CancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                context.ReportError(ErrorCodes.PackageIdExistsOnNuGetOrg, $"The package '{packageId}' already exists on nuget.org");
            }
            else if (response.StatusCode is not HttpStatusCode.NotFound)
            {
                context.ReportError(ErrorCodes.CannotCheckPackageIdExistsOnNuGetOrg, $"Cannot check if the package '{packageId}' exists on nuget.org (HTTP status code = {(int)response.StatusCode})");
            }
        }
        catch (HttpRequestException ex)
        {
            context.ReportError(ErrorCodes.CannotCheckPackageIdExistsOnNuGetOrg, $"Cannot check if the package '{packageId}' exists on nuget.org: {ex.Message}");
        }
    }
}
