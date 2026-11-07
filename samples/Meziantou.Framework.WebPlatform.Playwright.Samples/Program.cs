// The examples of src/Meziantou.Framework.WebPlatform.Playwright/readme.md that load pages from the network or host the
// server in ASP.NET Core. CI compiles them but does not run them; the other examples of the readme are tests of
// Meziantou.Framework.WebPlatform.Playwright.Tests.
//   dotnet run --project samples/Meziantou.Framework.WebPlatform.Playwright.Samples
using Meziantou.Framework.WebPlatform.Playwright.Samples;

await ReadmeExamples.AllowNetworkRequests();
