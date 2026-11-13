// The examples of src/Meziantou.Framework.WebPlatform.DevTools/readme.md that load pages from the network or host the
// server in ASP.NET Core. CI compiles them but does not run them; the other examples of the readme are tests of
// Meziantou.Framework.WebPlatform.DevTools.Tests.
//   dotnet run --project samples/Meziantou.Framework.WebPlatform.DevTools.Samples
using Meziantou.Framework.WebPlatform.DevTools.Samples;

await ReadmeExamples.AllowNetworkRequests();
