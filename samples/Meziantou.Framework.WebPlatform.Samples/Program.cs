// The examples of src/Meziantou.Framework.WebPlatform/readme.md that load pages from the network. CI compiles them but
// does not run them; the other examples of the readme are tests of Meziantou.Framework.WebPlatform.Tests.
//   dotnet run --project samples/Meziantou.Framework.WebPlatform.Samples
using Meziantou.Framework.WebPlatform.Samples;

await ReadmeExamples.AllowNetworkRequests();
await ReadmeExamples.FilterRequests();
await ReadmeExamples.TakeScreenshots();
Console.WriteLine($"Screenshot saved to {Path.GetFullPath("page.png")}");
