using Meziantou.Framework.NuGetPackageValidation.Samples;

// The readme examples are compiled in CI to keep the documentation in sync with the API. They validate
// MyPackage.1.0.0.nupkg from the current directory and send HTTP requests, so the tests do not run them.
await ReadmeExamples.BasicValidationAsync();
await ReadmeExamples.CustomOptionsAsync();
