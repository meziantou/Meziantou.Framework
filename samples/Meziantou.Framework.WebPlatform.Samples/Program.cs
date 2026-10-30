// The examples of src/Meziantou.Framework.WebPlatform/readme.md that load pages from the network, and the examples of
// docs/WebPlatform that cannot be tests (custom elements: the test project sees the internal members of the library;
// system fonts: the result depends on the machine). CI compiles them but does not run them; the other examples are tests
// of Meziantou.Framework.WebPlatform.Tests.
//   dotnet run --project samples/Meziantou.Framework.WebPlatform.Samples
using Meziantou.Framework.WebPlatform.Samples;

ReadmeExamples.DefineCustomElement();
ReadmeExamples.UseSystemFonts();
await ReadmeExamples.AllowNetworkRequests();
await ReadmeExamples.FilterRequests();
await ReadmeExamples.TakeScreenshots();
Console.WriteLine($"Screenshot saved to {Path.GetFullPath("page.png")}");
