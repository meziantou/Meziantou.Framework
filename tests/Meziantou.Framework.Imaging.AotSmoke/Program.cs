using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.AotSmoke;

// NativeAOT/trimming smoke test: every first-release codec through the public API (eager, streaming, identification,
// processing, metadata, errors). Exit code 0 when everything matches, 1 otherwise.
var failures = 0;
var runtime = string.Create(CultureInfo.InvariantCulture, $"{Environment.Version} {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} dynamic-code={RuntimeFeature.IsDynamicCodeSupported}");
Console.WriteLine("Meziantou.Framework.Imaging smoke test on .NET " + runtime);

Run("png", SmokeTests.Png);
Run("apng", SmokeTests.Apng);
Run("gif", SmokeTests.Gif);
Run("jpeg", SmokeTests.Jpeg);
Run("webp", SmokeTests.WebP);
Run("qoi", SmokeTests.Qoi);
Run("bmp", SmokeTests.Bmp);
Run("tga", SmokeTests.Tga);
Run("pnm", SmokeTests.Pnm);
Run("tiff", SmokeTests.Tiff);
Run("icon", SmokeTests.Icon);
Run("processing", SmokeTests.Processing);
Run("streaming", SmokeTests.Streaming);
Run("errors", SmokeTests.Errors);

Console.WriteLine(failures == 0 ? "All smoke tests passed." : $"{failures} smoke test(s) failed.");
return failures == 0 ? 0 : 1;

void Run(string name, Action test)
{
    var stopwatch = Stopwatch.StartNew();
    try
    {
        test();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  ok   {name} ({stopwatch.ElapsedMilliseconds} ms)"));
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"  FAIL {name}: {exception}");
    }
}
