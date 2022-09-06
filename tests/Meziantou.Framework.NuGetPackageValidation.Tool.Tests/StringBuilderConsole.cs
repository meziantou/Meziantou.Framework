using System.CommandLine;
using System.CommandLine.IO;
using System.Text;
using Xunit.Abstractions;

namespace Meziantou.Framework.NuGetPackageValidation.Tool.Tests;

internal sealed class StringBuilderConsole : IConsole
{
    private readonly StringBuilder _output = new();

    public StringBuilderConsole()
    {
        Out = new StringBuilderStreamWriter(_output);
        Error = new StringBuilderStreamWriter(_output);
    }

    public string Output => _output.ToString();

    public IStandardStreamWriter Out { get; }

    public bool IsOutputRedirected => false;

    public IStandardStreamWriter Error { get; }

    public bool IsErrorRedirected => false;

    public bool IsInputRedirected => false;

    private sealed class StringBuilderStreamWriter : IStandardStreamWriter
    {
        private readonly StringBuilder _stringBuilder;

        public StringBuilderStreamWriter(StringBuilder stringBuilder)
        {
            _stringBuilder = stringBuilder;
        }

        public void Write(string value) => _stringBuilder.Append(value);
    }
}
