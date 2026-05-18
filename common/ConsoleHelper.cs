#pragma warning disable CA1001
using System.CommandLine;

namespace Meziantou.Framework;

internal sealed class ConsoleHelper
{
    private readonly StringWriter _outputWriter = new();
    private readonly StringWriter _errorWriter = new();

    private readonly TextWriter _teeOutputWriter;
    private readonly TextWriter _teeErrorWriter;

    public ConsoleHelper(ITestOutputHelper testOutputHelper)
    {
        _teeOutputWriter = TextWriter.CreateBroadcasting(new XunitTextWriter(testOutputHelper), _outputWriter);
        _teeErrorWriter = TextWriter.CreateBroadcasting(new XunitTextWriter(testOutputHelper), _errorWriter);
    }

    public string Output => _outputWriter.ToString();
    public string Error => _errorWriter.ToString();

    public void ConfigureConsole(InvocationConfiguration configuration)
    {
        configuration.Output = _teeOutputWriter;
        configuration.Error = _teeErrorWriter;
    }

    private sealed class XunitTextWriter : TextWriter
    {
        private readonly ITestOutputHelper _testOutputHelper;
        public XunitTextWriter(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value)
        {
            if (value is not null)
            {
                _testOutputHelper.WriteLine(value);
            }
        }
        public override void Write(string? value)
        {
            if (value is not null)
            {
                _testOutputHelper.Write(value);
            }
        }
    }
}
