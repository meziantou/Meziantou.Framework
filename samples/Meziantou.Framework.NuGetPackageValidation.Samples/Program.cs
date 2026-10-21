using Meziantou.Framework.NuGetPackageValidation.Samples;

// The examples of the readme and of the XML documentation are compiled in CI to keep them in sync with the API. They
// validate MyPackage.1.0.0.nupkg from the current directory and send HTTP requests, so the tests do not run them.
// Run with: dotnet run --project samples/Meziantou.Framework.NuGetPackageValidation.Samples
await Examples.ValidatePackage();
await ReadmeExamples.BasicValidationAsync();
await ReadmeExamples.CustomOptionsAsync();
