#pragma warning disable IDE1006 // Naming Styles
using Xunit;

namespace Meziantou.Extensions.Logging.Xunit.v3.Tests;

internal sealed class InMemoryTestOutputHelper : ITestOutputHelper
{
    private readonly List<string> _logs = new();

    public IEnumerable<string> Logs => _logs;

    public string Output { get; }

    public void Write(string message)
    {
        lock (_logs)
        {
            _logs.Add(message);
        }
    }

    public void Write(string format, params object[] args)
    {
        lock (_logs)
        {
            _logs.Add(string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }

    public void WriteLine(string message)
    {
        lock (_logs)
        {
            _logs.Add(message + Environment.NewLine);
        }
    }

    public void WriteLine(string format, params object[] args)
    {
        lock (_logs)
        {
            _logs.Add(string.Format(CultureInfo.InvariantCulture, format, args) + Environment.NewLine);
        }
    }
}
