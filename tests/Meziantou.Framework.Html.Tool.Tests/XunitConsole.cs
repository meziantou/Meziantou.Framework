using System.CommandLine;
using System.CommandLine.IO;
using Xunit.Abstractions;

namespace Meziantou.Framework.Html.Tool.Tests;

internal sealed class XunitConsole : IConsole
{
    public XunitConsole(ITestOutputHelper testOutputHelper)
    {
        Out = new XunitStandardStreamWriter(testOutputHelper);
        Error = new XunitStandardStreamWriter(testOutputHelper);
    }

    public IStandardStreamWriter Out { get; }

    public bool IsOutputRedirected => false;

    public IStandardStreamWriter Error { get; }

    public bool IsErrorRedirected => false;

    public bool IsInputRedirected => false;

    private sealed class XunitStandardStreamWriter : IStandardStreamWriter
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public XunitStandardStreamWriter(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        public void Write(string value) => _testOutputHelper.WriteLine(value);
    }
}
