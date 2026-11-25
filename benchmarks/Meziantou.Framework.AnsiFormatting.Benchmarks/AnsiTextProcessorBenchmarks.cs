using System.Text;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.AnsiFormatting.Benchmarks;

[MemoryDiagnoser]
public class AnsiTextProcessorBenchmarks
{
    private string _input = "";

    [Params("PlainLine", "ColoredLine", "HyperlinkLine", "TrueColorLine", "LargePlain", "LargeColored")]
    public string Scenario { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _input = Scenario switch
        {
            "PlainLine" => PlainLine,
            "ColoredLine" => ColoredLine,
            "HyperlinkLine" => HyperlinkLine,
            "TrueColorLine" => TrueColorLine,
            "LargePlain" => Repeat(PlainLine + "\n", 64 * 1024),
            "LargeColored" => Repeat(ColoredLine + "\n" + HyperlinkLine + "\n" + TrueColorLine + "\n" + PlainLine + "\n", 64 * 1024),
            _ => throw new InvalidOperationException(),
        };
    }

    // A line of a plain logger
    private const string PlainLine = "2026-11-25T10:15:42.123Z info: Microsoft.Hosting.Lifetime[14] Now listening on: http://localhost:5000 (pid 12345)";

    // A line of the console logger of ASP.NET Core and of a test runner
    private const string ColoredLine = "\u001b[40m\u001b[32minfo\u001b[39m\u001b[22m\u001b[49m: Microsoft.Hosting.Lifetime[14]\u001b[0m Now listening on: \u001b[1mhttp://localhost:5000\u001b[22m \u001b[90m(12 ms)\u001b[0m \u001b[1;31mfailed\u001b[0m";

    // A line with OSC 8 hyperlinks, as written by recent command-line tools
    private const string HyperlinkLine = "\u001b[32m✔\u001b[0m Build succeeded\u001b[K See \u001b]8;;https://example.com/build/42/report.html\u001b\\the report\u001b]8;;\u001b\\ and \u001b]8;;https://example.com/build/42/logs\u0007the logs\u001b]8;;\u0007";

    // A line of syntax-highlighted code, as written by bat or delta
    private const string TrueColorLine = "\u001b[38;2;249;38;114mpublic\u001b[0m\u001b[38;2;248;248;242m \u001b[0m\u001b[38;2;102;217;239mstatic\u001b[0m\u001b[38;2;248;248;242m \u001b[0m\u001b[38;2;102;217;239mstring\u001b[0m\u001b[38;2;248;248;242m \u001b[0m\u001b[38;2;166;226;46mRemoveAnsiSequences\u001b[0m\u001b[38;2;248;248;242m(\u001b[0m\u001b[38;2;102;217;239mstring\u001b[0m\u001b[38;2;248;248;242m \u001b[0m\u001b[3;38;2;253;151;31mvalue\u001b[0m\u001b[38;2;248;248;242m)\u001b[0m";

    private static string Repeat(string value, int minLength)
    {
        var sb = new StringBuilder(minLength + value.Length);
        while (sb.Length < minLength)
        {
            sb.Append(value);
        }

        return sb.ToString();
    }

    [Benchmark, BenchmarkCategory("Remove")]
    public string Remove() => AnsiTextProcessor.RemoveAnsiSequences(_input);

    [Benchmark, BenchmarkCategory("Contains")]
    public bool Contains() => AnsiTextProcessor.ContainsAnsiSequences(_input);

    [Benchmark, BenchmarkCategory("Parse")]
    public object Parse() => AnsiTextProcessor.ParseTextWithAnsiStyles(_input);
}
